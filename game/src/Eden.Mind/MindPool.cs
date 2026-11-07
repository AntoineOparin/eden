using System.Collections.Concurrent;
using Eden.Mind.Contracts;
using Eden.Sim;
using Eden.Sim.Contracts;

namespace Eden.Mind;

/// <summary>Tuning for <see cref="MindPool"/>. Defaults follow docs/specs/20-mind.md; tests shrink the times.</summary>
public sealed record MindOptions
{
    /// <summary>Per-task timeout (provider call plus its embedding lookup).</summary>
    public TimeSpan TaskTimeout { get; init; } = TimeSpan.FromSeconds(20);
    /// <summary>How often the background loop looks at the latest snapshot.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    /// <summary>Real-time floor between two background thoughts (or two reflections) of one NPC, so fast-forward cannot burn the budget.</summary>
    public TimeSpan MinThoughtInterval { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan BackoffMin { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan BackoffMax { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>In-game hours between thoughts of one thinking NPC (staggered by id).</summary>
    public int ThoughtEveryHours { get; init; } = 4;
    public int EmbedBatch { get; init; } = 64;
    /// <summary>At this fraction of the daily budget only dialogue runs.</summary>
    public double DialogueOnlyFraction { get; init; } = 0.9;
    public int MaxQueue { get; init; } = 256;
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
}

public enum MindTaskKind { Dialogue = 0, Reflection = 1, Thought = 2, Embed = 3 }

public sealed record DialogueReply(int NpcId, string Reply, string? Mood, BrainUsage Usage);

/// <summary>A belief about the god from a reflection (religion seed, consumed in M7).</summary>
public sealed record ReligionSeed(int NpcId, long Tick, string Text);

/// <summary>
/// The cognition scheduler (docs/specs/20-mind.md). Runs on its own background loop, never on the sim thread:
/// it reads <see cref="ISimHost.Latest"/>, picks the spotlight every in-game hour, queues thoughts (every 4 in-game
/// hours, staggered by id), reflections (at day end) and embeddings, and runs them on a priority queue
/// (dialogue &gt; reflection &gt; thought &gt; embeddings) with bounded concurrency, a per-task timeout, a daily Neuron
/// budget and exponential backoff. Results reach the sim only as intentions (<see cref="ISimHost.Intentions"/>)
/// and commands (<see cref="SetThinkingCommand"/>, <see cref="WhisperCommand"/>). With the "none" provider, an
/// exhausted budget or a failing provider, the sim simply runs on rules.
/// </summary>
public sealed class MindPool : IDisposable
{
    private readonly ISimHost _host;
    private readonly Func<BrainSettings, BudgetLedger, IBrainProvider> _factory;
    private readonly MindOptions _o;
    private readonly object _gate = new();
    private readonly PriorityQueue<(MindTaskKind Kind, int NpcId), (int, long)> _queue = new();
    private readonly HashSet<(MindTaskKind, int)> _pending = new();
    private readonly Dictionary<int, long> _playerTicks = new();
    private readonly Dictionary<(MindTaskKind, int), DateTimeOffset> _lastRun = new();
    private readonly ConcurrentDictionary<int, string> _thoughts = new();
    private readonly ConcurrentDictionary<int, List<ChatTurn>> _history = new();
    private readonly ConcurrentQueue<string> _log = new();
    private readonly List<ReligionSeed> _godBeliefs = new();
    private readonly EmbeddingCache _cache = new();

    private volatile IBrainProvider _provider;
    private volatile BrainSettings _settings;
    private volatile bool _embedSupported = true;
    private int[] _thinking = [];
    private long _seq, _lastHour = -1, _completed, _failures;
    private int _inFlight, _dialogueInFlight;
    private TimeSpan _backoff;
    private DateTimeOffset _backoffUntil;
    private string? _lastError;
    private float _focusX, _focusZ;
    private volatile bool _hasFocus;
    private CancellationTokenSource _stop = new();
    private Task? _loop;

    public MindPool(ISimHost host, BrainSettings settings, Func<BrainSettings, BudgetLedger, IBrainProvider>? providerFactory = null,
        BudgetLedger? ledger = null, MindOptions? options = null)
    {
        _host = host;
        _factory = providerFactory ?? ((s, l) => ProviderFactory.Create(s, l));
        _o = options ?? new MindOptions();
        Budget = ledger ?? new BudgetLedger();
        _settings = settings;
        _provider = _factory(settings, Budget);
    }

    public BrainSettings Settings => _settings;
    public BudgetLedger Budget { get; }
    public IBrainProvider Provider => _provider;
    public EmbeddingCache Embeddings => _cache;
    public bool IsRunning => _loop is { IsCompleted: false };

    /// <summary>NPC ids currently in the spotlight (sorted).</summary>
    public IReadOnlyList<int> Thinking { get { lock (_gate) return _thinking; } }

    public IReadOnlyList<ReligionSeed> GodBeliefs { get { lock (_gate) return _godBeliefs.ToArray(); } }

    /// <summary>Raised for every log line (rejections, failures). Any thread.</summary>
    public event Action<string>? Logged;

    public bool TryDequeueLog(out string line) => _log.TryDequeue(out line!);

    public MindStatus Status
    {
        get
        {
            var s = _settings;
            var (_, neurons, usd) = Budget.Snapshot();
            lock (_gate)
            {
                var now = _o.Now();
                var state = _provider is NullBrainProvider ? MindState.Off
                    : neurons >= s.DailyNeuronBudget ? MindState.Exhausted
                    : neurons >= s.DailyNeuronBudget * _o.DialogueOnlyFraction ? MindState.DialogueOnly
                    : now < _backoffUntil ? MindState.Backoff
                    : MindState.Ok;
                string model = _provider is Providers.DirectProvider d ? d.Model : s.Model;
                return new MindStatus(_provider.Id, model, state, neurons, s.DailyNeuronBudget, usd, _thinking.Length,
                    _queue.Count, _inFlight + Volatile.Read(ref _dialogueInFlight), Interlocked.Read(ref _completed),
                    Interlocked.Read(ref _failures), _lastError, now < _backoffUntil ? _backoffUntil : null);
            }
        }
    }

    // ---- lifecycle -------------------------------------------------------------------------------

    public void Start()
    {
        if (IsRunning) return;
        _stop = new CancellationTokenSource();
        var token = _stop.Token;
        _loop = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { PollOnce(); }
                catch (Exception e) { Log($"mind loop: {e.GetType().Name}: {e.Message}"); }
                try { await Task.Delay(_o.PollInterval, token); }
                catch (OperationCanceledException) { break; }
            }
        });
    }

    public void Stop()
    {
        _stop.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        _loop = null;
        ClearThinking();
    }

    public void Dispose() => Stop();

    /// <summary>Swap provider/limits at runtime (settings panel). Resets backoff and re-runs the spotlight.</summary>
    public void ApplySettings(BrainSettings settings)
    {
        var provider = _factory(settings, Budget);
        lock (_gate)
        {
            _settings = settings;
            _provider = provider;
            _embedSupported = true;
            _backoff = TimeSpan.Zero;
            _backoffUntil = default;
            _lastError = null;
            _lastHour = -1;
        }
        if (provider is NullBrainProvider) ClearThinking();
        Log($"brain provider: {provider.Id}");
    }

    /// <summary>Camera focus on the ground (world metres), from the Godot main thread every frame.</summary>
    public void SetFocus(float x, float z)
    {
        _focusX = x;
        _focusZ = z;
        _hasFocus = true;
    }

    /// <summary>The player talked to, whispered to, blessed or selected this NPC (spotlight term, 1 day decay).</summary>
    public void NotePlayerInteraction(int npcId)
    {
        long tick = _host.Latest.Tick;
        lock (_gate) _playerTicks[npcId] = tick;
    }

    public string? CurrentThought(int npcId) => _thoughts.TryGetValue(npcId, out var t) ? t : null;

    public IReadOnlyList<ChatTurn> History(int npcId)
    {
        if (!_history.TryGetValue(npcId, out var list)) return [];
        lock (list) return list.ToArray();
    }

    // ---- scheduling (background loop, or tests calling PollOnce directly) -----------------------

    /// <summary>One scheduling step: spotlight on a new in-game hour, queue due work, dispatch.</summary>
    public void PollOnce()
    {
        var snap = _host.Latest;
        if (_provider is NullBrainProvider) { ClearThinking(); return; }
        var extras = snap as IMindSnapshot;
        int tph = Math.Max(1, extras?.TicksPerHour ?? 10), hpd = Math.Max(1, extras?.HoursPerDay ?? 24);
        long hour = snap.Tick / tph;
        List<(MindTaskKind, int)>? due = null;
        lock (_gate)
        {
            if (hour != _lastHour)
            {
                long from = _lastHour < 0 || hour < _lastHour ? hour : Math.Max(_lastHour + 1, hour - hpd);
                _lastHour = hour;
                UpdateSpotlight(snap);
                due = new();
                int every = Math.Max(1, _o.ThoughtEveryHours);
                bool dayEnd = false;
                for (long h = from; h <= hour; h++)
                {
                    if (h > 0 && h % hpd == 0) dayEnd = true;
                    foreach (int id in _thinking)
                        if ((h + id) % every == 0) due.Add((MindTaskKind.Thought, id));
                }
                if (dayEnd)
                    foreach (int id in _thinking) due.Add((MindTaskKind.Reflection, id));
            }
            if (due is not null) foreach (var (k, id) in due) EnqueueLocked(k, id);
        }
        Dispatch();
    }

    private void UpdateSpotlight(IWorldSnapshot snap)
    {
        var ctx = new SpotlightContext(_focusX, _focusZ, _hasFocus, new Dictionary<int, long>(_playerTicks));
        var ids = Spotlight.Pick(snap, ctx, Math.Max(0, _settings.MaxThinkingNpcs));
        Array.Sort(ids);
        foreach (int id in ids) snap.TryGetNpcDetail(id, out _); // keep them on the sim's detail watch list
        if (ids.AsSpan().SequenceEqual(_thinking)) return;
        var old = new HashSet<int>(_thinking);
        _thinking = ids;
        _host.Enqueue(new SetThinkingCommand(ids));
        foreach (int id in ids)
            if (!old.Contains(id) && _embedSupported) EnqueueLocked(MindTaskKind.Embed, id); // promotion
    }

    private void ClearThinking()
    {
        lock (_gate)
        {
            if (_thinking.Length == 0) return;
            _thinking = [];
        }
        try { _host.Enqueue(new SetThinkingCommand([])); } catch (ObjectDisposedException) { }
    }

    /// <summary>Queue background work unless it is pending already, too soon, or over budget. Caller holds _gate.</summary>
    private bool EnqueueLocked(MindTaskKind kind, int npcId)
    {
        if (kind == MindTaskKind.Dialogue || _pending.Contains((kind, npcId)) || _queue.Count >= _o.MaxQueue) return false;
        if (OverBackgroundBudget()) return false;
        if (kind is MindTaskKind.Thought or MindTaskKind.Reflection
            && _lastRun.TryGetValue((kind, npcId), out var last) && _o.Now() - last < _o.MinThoughtInterval) return false;
        _pending.Add((kind, npcId));
        _queue.Enqueue((kind, npcId), ((int)kind, _seq++));
        return true;
    }

    private bool OverBackgroundBudget() => Budget.NeuronsToday >= _settings.DailyNeuronBudget * _o.DialogueOnlyFraction;

    private void Dispatch()
    {
        lock (_gate)
        {
            int limit = Math.Clamp(_settings.Concurrency, 1, 16);
            while (_inFlight < limit && _queue.Count > 0)
            {
                if (_o.Now() < _backoffUntil) return;
                if (OverBackgroundBudget())
                {
                    _queue.Clear();
                    _pending.Clear();
                    return;
                }
                var task = _queue.Dequeue();
                _inFlight++;
                if (task.Kind is MindTaskKind.Thought or MindTaskKind.Reflection) _lastRun[task] = _o.Now();
                _ = Task.Run(() => RunAsync(task.Kind, task.NpcId));
            }
        }
    }

    private async Task RunAsync(MindTaskKind kind, int npcId)
    {
        var provider = _provider;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        cts.CancelAfter(_o.TaskTimeout);
        try
        {
            bool called = kind switch
            {
                MindTaskKind.Embed => await EmbedNpcAsync(provider, npcId, cts.Token),
                _ => await ThinkAsync(provider, npcId, kind == MindTaskKind.Reflection ? ThinkMode.Reflection : ThinkMode.Thought, cts.Token),
            };
            if (called) Succeeded();
        }
        catch (BrainException e) { Failed(kind, npcId, e); }
        catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
        {
            Failed(kind, npcId, new BrainException(BrainFailure.Network, $"timed out after {_o.TaskTimeout.TotalSeconds:0.#} s"));
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Failed(kind, npcId, new BrainException(BrainFailure.InvalidOutput, $"{e.GetType().Name}: {e.Message}", e)); }
        finally
        {
            lock (_gate)
            {
                _inFlight--;
                _pending.Remove((kind, npcId));
            }
            if (!_stop.IsCancellationRequested) Dispatch();
        }
    }

    private void Succeeded()
    {
        Interlocked.Increment(ref _completed);
        lock (_gate)
        {
            _backoff = TimeSpan.Zero;
            _backoffUntil = default;
        }
    }

    private void Failed(MindTaskKind kind, int npcId, BrainException e)
    {
        Interlocked.Increment(ref _failures);
        if (e.Failure == BrainFailure.Unsupported && kind == MindTaskKind.Embed) _embedSupported = false;
        bool backOff = e.Failure is BrainFailure.Network or BrainFailure.Auth or BrainFailure.Budget;
        lock (_gate)
        {
            _lastError = $"{kind.ToString().ToLowerInvariant()} {npcId}: {e.Failure}: {e.Message}";
            if (backOff)
            {
                var next = _backoff == TimeSpan.Zero ? _o.BackoffMin : _backoff * 2;
                _backoff = next > _o.BackoffMax ? _o.BackoffMax : next;
                _backoffUntil = _o.Now() + _backoff;
            }
        }
        Log(_lastError + (backOff ? $" (backing off {_backoff.TotalSeconds:0} s)" : ""));
    }

    private void Log(string line)
    {
        while (_log.Count >= 200) _log.TryDequeue(out _);
        _log.Enqueue(line);
        Logged?.Invoke(line);
    }

    private void Book(BrainUsage usage) => Budget.AddNeurons(usage.Neurons);

    // ---- work ------------------------------------------------------------------------------------

    private static NpcView? ViewOf(IWorldSnapshot snap, int id)
    {
        foreach (var v in snap.Npcs.Span) if (v.Id == id) return v;
        return null;
    }

    /// <summary>Thought or reflection for one NPC. False when there was nothing to do (no detail yet).</summary>
    private async Task<bool> ThinkAsync(IBrainProvider provider, int npcId, ThinkMode mode, CancellationToken ct)
    {
        var snap = _host.Latest;
        if (!snap.TryGetNpcDetail(npcId, out var d))
        {
            lock (_gate) _lastRun.Remove((mode == ThinkMode.Reflection ? MindTaskKind.Reflection : MindTaskKind.Thought, npcId));
            return false; // now on the watch list; the next due hour has the detail
        }
        var view = ViewOf(snap, npcId);
        string situation = PromptContext.Situation(d, view, snap);
        var memories = await RetrieveAsync(provider, d, snap, situation, ct);
        bool allowInvention = mode == ThinkMode.Thought && PromptContext.AllowInvention(d);
        var request = new ThinkRequest(PromptContext.Npc(d, view, snap), Lines(memories, snap), PromptContext.WorldRules,
            mode == ThinkMode.Reflection ? PromptContext.ReflectionSituation : situation, 3, allowInvention);
        var result = await provider.ThinkAsync(request, ct);
        Book(result.Usage);

        var mapped = IntentionMapper.FromThink(result.Response, npcId, snap.Tick, mode, allowInvention);
        if (mapped.Rejected is { } why) { Log($"{mode.ToString().ToLowerInvariant()} {npcId} rejected: {why}"); return true; }
        Push(mapped);
        var r = result.Response;
        string? thought = mode == ThinkMode.Reflection
            ? r.Belief?.Text
            : r.Say ?? r.Belief?.Text ?? (r.Plan.Count > 0 ? r.Plan[0].Reason ?? $"{r.Plan[0].Action.ToString().ToLowerInvariant()} {r.Plan[0].Target}".Trim() : null);
        if (!string.IsNullOrWhiteSpace(thought)) _thoughts[npcId] = thought;
        if (mode == ThinkMode.Reflection && r.Belief is { } b && IntentionMapper.MentionsGod(b.Text))
            lock (_gate) { if (_godBeliefs.Count < 1000) _godBeliefs.Add(new ReligionSeed(npcId, snap.Tick, b.Text)); }
        return true;
    }

    private static IReadOnlyList<MemoryLine> Lines(IReadOnlyList<MemoryView> memories, IWorldSnapshot snap)
    {
        var x = snap as IMindSnapshot;
        return PromptContext.Lines(memories, snap.Tick, x?.TicksPerHour ?? 10, x?.HoursPerDay ?? 24);
    }

    private void Push(MappedResponse mapped)
    {
        foreach (var i in mapped.Intentions)
            if (!_host.Intentions.TryPush(i)) Log($"intention for {i.NpcId} refused by the sim queue (npc died or queue full)");
    }

    /// <summary>On promotion: vectors for every memory of the NPC that has none yet, in batches of ≤ 64.</summary>
    private async Task<bool> EmbedNpcAsync(IBrainProvider provider, int npcId, CancellationToken ct)
    {
        if (!_embedSupported || !_host.Latest.TryGetNpcDetail(npcId, out var d)) return false;
        var missing = _cache.Missing(MemoryTexts(d));
        if (missing.Count == 0) return false;
        int batch = Math.Clamp(_o.EmbedBatch, 1, 64);
        for (int i = 0; i < missing.Count; i += batch)
        {
            var texts = missing.GetRange(i, Math.Min(batch, missing.Count - i));
            var r = await provider.EmbedAsync(new EmbedRequest(texts.Select(t => PromptContext.Clip(t, 2000)).ToList()), ct);
            Book(r.Usage);
            for (int k = 0; k < texts.Count && k < r.Response.Vectors.Count; k++) _cache.Put(texts[k], r.Response.Vectors[k]);
        }
        return true;
    }

    private static IEnumerable<string> MemoryTexts(NpcDetail d)
    {
        for (int i = d.Memories.Count - 1, n = 0; i >= 0 && n < Retrieval.MaxCandidates; i--, n++)
            if (d.Memories[i].Kind != "summary") yield return d.Memories[i].Text;
    }

    /// <summary>Top 12 memories for <paramref name="query"/>. One /embed call carries the query plus up to 63 uncached memories.</summary>
    private async Task<IReadOnlyList<MemoryView>> RetrieveAsync(IBrainProvider provider, NpcDetail d, IWorldSnapshot snap, string query, CancellationToken ct)
    {
        float[]? queryVector = null;
        if (_embedSupported)
        {
            var missing = _cache.Missing(MemoryTexts(d));
            if (missing.Count > 63) missing.RemoveRange(63, missing.Count - 63);
            var texts = new List<string>(missing.Count + 1) { PromptContext.Clip(query, 2000) };
            texts.AddRange(missing.Select(t => PromptContext.Clip(t, 2000)));
            try
            {
                var r = await provider.EmbedAsync(new EmbedRequest(texts), ct);
                Book(r.Usage);
                if (r.Response.Vectors.Count == texts.Count)
                {
                    queryVector = r.Response.Vectors[0];
                    for (int k = 0; k < missing.Count; k++) _cache.Put(missing[k], r.Response.Vectors[k + 1]);
                }
            }
            catch (BrainException e) when (e.Failure == BrainFailure.Unsupported) { _embedSupported = false; }
            catch (BrainException e) { Log($"embed for {d.Id} failed, using word overlap: {e.Message}"); }
        }
        int tph = (snap as IMindSnapshot)?.TicksPerHour ?? 10;
        return Retrieval.Top(d.Memories, snap.Tick, tph, Retrieval.Relevance(query, queryVector, _cache));
    }

    // ---- dialogue (bypasses the queue) ------------------------------------------------------------

    /// <summary>Low-level: send a prepared request straight to the provider, ignoring queue and concurrency.</summary>
    public Task<ChatResult> AskAsync(ChatRequest request, CancellationToken ct) => _provider.ChatAsync(request, ct);

    /// <summary>
    /// The god speaks to an NPC. Highest priority: never queued behind background work. The NPC answers with the
    /// same retrieval as a thought; the god's line becomes an importance-9 memory (<see cref="WhisperCommand"/>),
    /// the reply is spoken aloud (<see cref="SayIntention"/>, neighbours hear it) and the NPC's own memory of the
    /// exchange becomes a belief of importance ≥ 6, which the sim's gossip spreads.
    /// </summary>
    public async Task<DialogueReply> AskAsync(int npcId, string message, CancellationToken ct = default)
    {
        message = (message ?? "").Trim();
        if (message.Length == 0) throw new ArgumentException("empty message", nameof(message));
        if (message.Length > 1000) message = message[..1000];
        NotePlayerInteraction(npcId);
        var provider = _provider;
        Interlocked.Increment(ref _dialogueInFlight);
        try
        {
            if (provider is NullBrainProvider)
            {
                Heard(npcId, message);
                throw new BrainException(BrainFailure.Unsupported, "no brain provider: the NPC hears you but cannot answer");
            }
            if (Budget.NeuronsToday >= _settings.DailyNeuronBudget)
            {
                Heard(npcId, message);
                throw new BrainException(BrainFailure.Budget, "today's Neuron budget is spent");
            }

            IWorldSnapshot snap = _host.Latest;
            NpcDetail? d = null;
            for (int i = 0; i < 40 && !(snap = _host.Latest).TryGetNpcDetail(npcId, out d); i++)
                await Task.Delay(25, ct);
            if (d is null) throw new BrainException(BrainFailure.Unsupported, $"npc {npcId} is not available (dead?)");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_o.TaskTimeout);
            var view = ViewOf(snap, npcId);
            ChatResult result;
            try
            {
                var memories = await RetrieveAsync(provider, d, snap, message, cts.Token);
                var history = History(npcId);
                if (history.Count > 20) history = history.Skip(history.Count - 20).ToArray();
                var request = new ChatRequest(PromptContext.Npc(d, view, snap), Lines(memories, snap), PromptContext.WorldRules,
                    Speaker.God, PromptContext.GodName, history, message);
                result = await provider.ChatAsync(request, cts.Token);
            }
            catch (BrainException e)
            {
                Failed(MindTaskKind.Dialogue, npcId, e);
                Heard(npcId, message);
                throw;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var e = new BrainException(BrainFailure.Network, $"no answer within {_o.TaskTimeout.TotalSeconds:0} s");
                Failed(MindTaskKind.Dialogue, npcId, e);
                Heard(npcId, message);
                throw e;
            }
            Book(result.Usage);
            Succeeded();

            var mapped = IntentionMapper.FromChat(result.Response, npcId, snap.Tick);
            Heard(npcId, message);
            if (mapped.Rejected is { } why)
            {
                Log($"dialogue {npcId} rejected: {why}");
                throw new BrainException(BrainFailure.InvalidOutput, why);
            }
            Push(mapped);
            AddTurn(npcId, new ChatTurn(Speaker.Npc, result.Response.Reply));
            if (result.Response.Memory is { } m) _thoughts[npcId] = m.Text;
            return new DialogueReply(npcId, result.Response.Reply, result.Response.Mood, result.Usage);
        }
        finally
        {
            Interlocked.Decrement(ref _dialogueInFlight);
        }
    }

    /// <summary>The god's line reaches the NPC whatever the provider does: a whisper memory plus the history turn.</summary>
    private void Heard(int npcId, string message)
    {
        _host.Enqueue(new WhisperCommand(npcId, message));
        AddTurn(npcId, new ChatTurn(Speaker.God, message));
    }

    private void AddTurn(int npcId, ChatTurn turn)
    {
        var list = _history.GetOrAdd(npcId, _ => new List<ChatTurn>());
        lock (list)
        {
            list.Add(turn);
            if (list.Count > 40) list.RemoveRange(0, list.Count - 40);
        }
    }
}
