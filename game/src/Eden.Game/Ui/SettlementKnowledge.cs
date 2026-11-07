using Eden.Sim.Contracts;

namespace Eden.Game;

/// <summary>
/// What a settlement knows, estimated from a sample of its members' <see cref="NpcDetail.Knowledge"/> (M7 UI: the
/// settlement panel and the knowledge-spread overlay). Details are built by the sim only for a bounded watch list, so
/// this samples one settlement at a time (the leader plus up to <see cref="SampleSize"/> − 1 adults spread over the
/// member list), polls <see cref="IWorldSnapshot.TryGetNpcDetail"/> until they answer or <see cref="JobSeconds"/>
/// pass, and caches the result per in-game day, with a real-time floor (<see cref="MinRefreshSeconds"/>) so 500×
/// does not resample forever. Driven by <see cref="Update"/> on a timer, never per frame.
/// </summary>
public sealed class SettlementKnowledge
{
    public sealed class Entry
    {
        public int SettlementId;
        public long Day;
        public double RealTime;
        public int Members;
        public int Sampled;
        /// <summary>Concept id → (members in the sample who know it, best proficiency among them).</summary>
        public readonly Dictionary<string, (int Knowers, float Best)> Concepts = new();

        /// <summary>Concepts by number of knowers, then best proficiency.</summary>
        public List<KeyValuePair<string, (int Knowers, float Best)>> Top(int n)
        {
            var list = Concepts.ToList();
            list.Sort((a, b) => b.Value.Knowers != a.Value.Knowers ? b.Value.Knowers.CompareTo(a.Value.Knowers) : b.Value.Best.CompareTo(a.Value.Best));
            if (list.Count > n) list.RemoveRange(n, list.Count - n);
            return list;
        }
    }

    public int SampleSize { get; set; } = 8;
    public double JobSeconds { get; set; } = 3;
    public double MinRefreshSeconds { get; set; } = 6;
    public int TicksPerDay { get; set; } = 240;

    private readonly Dictionary<int, Entry> _cache = new();
    private readonly List<int> _queue = new();
    private Entry? _job;
    private readonly List<int> _pending = new();
    private double _jobStarted;
    private double _clock;

    /// <summary>Bumped when an entry finishes; consumers redraw only when it changes.</summary>
    public int Version { get; private set; }

    /// <summary>The settlement currently being sampled, or -1.</summary>
    public int Sampling => _job?.SettlementId ?? -1;

    public Entry? Get(int settlementId) => _cache.TryGetValue(settlementId, out var e) ? e : null;

    /// <summary>Ask for a settlement; <paramref name="first"/> puts it at the front of the queue (the open panel).</summary>
    public void Want(int settlementId, bool first = false)
    {
        if (settlementId < 0 || _job?.SettlementId == settlementId) return;
        int at = _queue.IndexOf(settlementId);
        if (at >= 0)
        {
            if (!first || at == 0) return;
            _queue.RemoveAt(at);
        }
        if (first) _queue.Insert(0, settlementId);
        else _queue.Add(settlementId);
    }

    /// <summary>True when the cached entry is missing or older than the current in-game day (and the real-time floor).</summary>
    public bool IsStale(int settlementId, long tick)
    {
        if (!_cache.TryGetValue(settlementId, out var e)) return true;
        return e.Day != tick / Math.Max(1, TicksPerDay) && _clock - e.RealTime >= MinRefreshSeconds;
    }

    /// <summary>Advances sampling. Call a few times per second with the frame's snapshot.</summary>
    public void Update(IWorldSnapshot s, double realSeconds)
    {
        _clock += realSeconds;
        if (_job is null)
        {
            while (_queue.Count > 0)
            {
                int id = _queue[0];
                _queue.RemoveAt(0);
                if (!IsStale(id, s.Tick)) continue;
                Start(s, id);
                break;
            }
            if (_job is null) return;
        }
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (!s.TryGetNpcDetail(_pending[i], out var d)) continue; // asks the sim to watch it
            _job.Sampled++;
            foreach (var (concept, p) in d.Knowledge)
            {
                if (p <= 0f) continue;
                _job.Concepts.TryGetValue(concept, out var c);
                _job.Concepts[concept] = (c.Knowers + 1, MathF.Max(c.Best, p));
            }
            _pending.RemoveAt(i);
        }
        if (_pending.Count == 0 || _clock - _jobStarted >= JobSeconds)
        {
            _job.RealTime = _clock;
            _cache[_job.SettlementId] = _job;
            _job = null;
            _pending.Clear();
            Version++;
        }
    }

    private void Start(IWorldSnapshot s, int settlementId)
    {
        _job = new Entry { SettlementId = settlementId, Day = s.Tick / Math.Max(1, TicksPerDay) };
        _jobStarted = _clock;
        _pending.Clear();
        int leader = -1;
        foreach (var v in s.Settlements.Span)
            if (v.Id == settlementId) { leader = v.LeaderId; break; }
        var npcs = s.Npcs.Span;
        int members = 0;
        foreach (var n in npcs)
            if (n.SettlementId == settlementId && n.Stage is LifeStage.Adult or LifeStage.Elder) members++;
        _job.Members = members;
        if (leader >= 0) _pending.Add(leader);
        int want = Math.Max(1, SampleSize) - _pending.Count;
        if (want <= 0 || members == 0) return;
        int stride = Math.Max(1, members / want), seen = 0;
        foreach (var n in npcs)
        {
            if (n.SettlementId != settlementId || n.Stage is not (LifeStage.Adult or LifeStage.Elder)) continue;
            if (seen++ % stride != 0 || n.Id == leader) continue;
            _pending.Add(n.Id);
            if (_pending.Count >= SampleSize) break;
        }
    }
}
