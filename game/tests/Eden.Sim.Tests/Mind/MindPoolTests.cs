using Eden.Mind;
using Eden.Mind.Contracts;
using Eden.Sim.Contracts;

namespace Eden.Sim.Tests.Mind;

/// <summary>
/// MindPool against a real SimHost and a fake provider whose thoughts stay "in flight" (a gate the test opens,
/// standing in for a 10 s model) — so the tests prove non-blocking behaviour without sleeping.
/// </summary>
/// <summary>
/// Runs alone (after the parallel collections): the pool dispatches on the thread pool, which the Slow
/// whole-world tests saturate when everything runs at once.
/// </summary>
[CollectionDefinition(nameof(MindPoolTests), DisableParallelization = true)]
public sealed class MindPoolCollection;

[Collection(nameof(MindPoolTests))]
public class MindPoolTests
{
    private sealed class SlowProvider : IBrainProvider
    {
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        public int MaxActive, ThinkCalls, ChatCalls;
        public ProposedBelief? Belief;

        public string Id => "fake";
        public bool SupportsBatching => false;
        public int Active => Volatile.Read(ref _active);

        public async Task<ThinkResult> ThinkAsync(ThinkRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref ThinkCalls);
            int now = Interlocked.Increment(ref _active);
            lock (this) MaxActive = Math.Max(MaxActive, now);
            try { await Gate.Task.WaitAsync(ct); }
            finally { Interlocked.Decrement(ref _active); }
            return new ThinkResult(new ThinkResponse([new PlannedGoal(ActionKind.Rest, null, "tired")], null, Belief, null),
                new BrainUsage(300, 50, 5, "fake"));
        }

        public Task<ChatResult> ChatAsync(ChatRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref ChatCalls);
            var memory = request.Memories.Any(m => m.Text.Contains("Lightning", StringComparison.Ordinal))
                ? new ProposedBelief("The Voice asked me how we fare after the fire from the sky.", 8) : null;
            return Task.FromResult(new ChatResult(new ChatResponse("We are frightened, Voice.", memory, "afraid"), new BrainUsage(400, 30, 6, "fake")));
        }

        public Task<EmbedResult> EmbedAsync(EmbedRequest request, CancellationToken ct)
            => Task.FromException<EmbedResult>(new BrainException(BrainFailure.Unsupported, "no embeddings"));
    }

    private static readonly MindOptions Fast = new() { ThoughtEveryHours = 1, MinThoughtInterval = TimeSpan.Zero };

    private static void WaitFor(Func<bool> condition, string what)
        => Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(30)), "timed out waiting for " + what);

    /// <summary>Seed 12 plus six spawned adults; spotlight picked and details watched; round one drained.</summary>
    private static (SimHost Host, MindPool Pool, SlowProvider Provider) Setup(int concurrency, double budget = 9000, MindOptions? options = null)
    {
        var host = new SimHost(new SimConfig { Seed = 12 });
        var first = host.Latest.Npcs.Span[0];
        // The start pair may stand on the shore line; try a ring of spots until six strangers are on land.
        for (int i = 0; i < 16 && host.Latest.Population < 8; i++)
        {
            float a = i * MathF.PI / 8, r = 6 + i;
            host.Enqueue(new SpawnCommand("human", first.X + r * MathF.Cos(a), first.Z + r * MathF.Sin(a), 8 - host.Latest.Population));
            host.StepSync(1);
        }
        Assert.Equal(8, host.Latest.Population);
        var provider = new SlowProvider();
        var settings = BrainDefaults.Settings with { Concurrency = concurrency, DailyNeuronBudget = budget };
        var pool = new MindPool(host, settings, (_, _) => provider, options: options ?? Fast);
        pool.PollOnce();      // spotlight + detail requests; thoughts find no detail yet and end at once
        WaitFor(() => pool.Status.InFlight == 0, "round one");
        host.StepSync(10);    // next in-game hour; details are now in the snapshot
        return (host, pool, provider);
    }

    [Fact]
    public void Sim_keeps_ticking_while_thoughts_are_in_flight_and_concurrency_holds()
    {
        var (host, pool, provider) = Setup(concurrency: 2);
        using var _ = host;
        Assert.Equal(8, pool.Thinking.Count);
        pool.PollOnce();
        WaitFor(() => provider.Active == 2, "two thoughts in flight");
        Assert.Equal(6, pool.Status.Queued);

        long before = host.Latest.Tick;
        host.StepSync(240); // a whole in-game day while the "model" is still thinking
        Assert.Equal(before + 240, host.Latest.Tick);

        // And on the real sim thread too.
        host.Enqueue(new SetTimeScaleCommand(TimeScale.X100));
        host.Start();
        long t0 = host.Latest.Tick;
        WaitFor(() => host.Latest.Tick > t0 + 50, "sim thread ticks");
        host.Stop();

        provider.Gate.SetResult();
        WaitFor(() => provider.ThinkCalls >= 8 && pool.Status.InFlight == 0, "all thoughts");
        Assert.Equal(2, provider.MaxActive);
        Assert.Equal(8 * 5.0, pool.Budget.NeuronsToday);
        host.StepSync(1);
        Assert.DoesNotContain(host.Diagnostics, d => d.Contains("rejected", StringComparison.Ordinal));
        Assert.Equal(8, host.Latest.Npcs.ToArray().Count(v => v.IsThinking));
        Assert.Equal("tired", pool.CurrentThought(pool.Thinking[0]));
    }

    [Fact]
    public async Task Dialogue_preempts_saturated_background_work()
    {
        var (host, pool, provider) = Setup(concurrency: 1);
        using var _ = host;
        pool.PollOnce();
        WaitFor(() => provider.Active == 1, "one thought in flight");
        int leader = pool.Thinking[0];

        // Strike near them first so the reply has something to talk about.
        var v = host.Latest.Npcs.ToArray().First(n => n.Id == leader);
        host.Enqueue(new LightningCommand(v.X + 12, v.Z, 2));
        host.StepSync(1);

        var reply = await pool.AskAsync(leader, "How do your people fare today?");
        Assert.Equal("We are frightened, Voice.", reply.Reply);
        Assert.Equal(1, provider.Active); // the background thought is still blocked
        Assert.Equal(2, pool.History(leader).Count);

        host.StepSync(1);
        Assert.True(host.Latest.TryGetNpcDetail(leader, out var d));
        Assert.Contains(d.Memories, m => m.Importance == 9 && m.Text.Contains("How do your people fare today?"));
        Assert.Contains(d.Memories, m => m.Kind == "speech" && m.Text.Contains("We are frightened"));
        Assert.Contains(d.Memories, m => m.Kind == "belief" && m.Importance >= 6 && m.Text.Contains("fire from the sky"));
        provider.Gate.SetResult();
    }

    [Fact]
    public async Task At_ninety_percent_only_dialogue_runs_and_at_the_cap_nothing()
    {
        var (host, pool, provider) = Setup(concurrency: 3, budget: 100);
        using var _ = host;
        pool.Budget.AddNeurons(90);
        pool.PollOnce();
        Assert.Equal(MindState.DialogueOnly, pool.Status.State);
        Assert.Equal(0, pool.Status.Queued);
        Assert.Equal(0, provider.ThinkCalls);

        await pool.AskAsync(pool.Thinking[0], "Are you there?");
        Assert.Equal(1, provider.ChatCalls);
        Assert.Equal(96, pool.Budget.NeuronsToday);

        pool.Budget.AddNeurons(10);
        Assert.Equal(MindState.Exhausted, pool.Status.State);
        var e = await Assert.ThrowsAsync<BrainException>(() => pool.AskAsync(pool.Thinking[0], "Still there?"));
        Assert.Equal(BrainFailure.Budget, e.Failure);
        Assert.Equal(1, provider.ChatCalls);
    }

    [Fact]
    public void Timeouts_back_off_exponentially_and_recover()
    {
        var now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        var options = Fast with { TaskTimeout = TimeSpan.FromMilliseconds(30), Now = () => now };
        var (host, pool, provider) = Setup(concurrency: 1, options: options);
        using var _ = host;
        pool.PollOnce();
        WaitFor(() => pool.Status.Failures >= 1 && pool.Status.InFlight == 0, "a timeout");
        var status = pool.Status;
        Assert.Equal(MindState.Backoff, status.State);
        Assert.Equal(now + TimeSpan.FromSeconds(5), status.BackoffUntil);
        int calls = provider.ThinkCalls;
        pool.PollOnce();
        Assert.Equal(calls, provider.ThinkCalls); // nothing dispatched while backing off

        now += TimeSpan.FromSeconds(6);
        pool.PollOnce();
        WaitFor(() => pool.Status.Failures >= 2 && pool.Status.InFlight == 0, "a second timeout");
        Assert.Equal(now + TimeSpan.FromSeconds(10), pool.Status.BackoffUntil);
        Assert.Contains("timed out", pool.Status.LastError);
    }

    [Fact]
    public void Day_end_reflection_raises_importance_and_seeds_religion()
    {
        var (host, pool, provider) = Setup(concurrency: 8);
        using var _ = host;
        provider.Belief = new ProposedBelief("The voice in the sky watches our village.", 4);
        provider.Gate.SetResult();
        host.StepSync(240 - (int)host.Latest.Tick % 240); // cross midnight
        pool.PollOnce();
        WaitFor(() => pool.Status.InFlight == 0 && pool.Status.Queued == 0, "thoughts and reflections");
        Assert.True(provider.ThinkCalls >= 16, $"thoughts + reflections: {provider.ThinkCalls}");
        Assert.Equal(8, pool.GodBeliefs.Count);
        host.StepSync(1);
        int id = pool.Thinking[0];
        Assert.True(host.Latest.TryGetNpcDetail(id, out var d));
        Assert.Contains(d.Memories, m => m.Kind == "belief" && m.Importance >= 7);
    }

    [Fact]
    public async Task Rules_only_provider_thinks_nothing_but_the_npc_still_hears_the_god()
    {
        using var host = new SimHost(new SimConfig { Seed = 12 });
        var pool = new MindPool(host, BrainDefaults.Settings with { ProviderId = "none" });
        pool.PollOnce();
        Assert.Equal(MindState.Off, pool.Status.State);
        Assert.Empty(pool.Thinking);
        int id = host.Latest.Npcs.Span[0].Id;
        var e = await Assert.ThrowsAsync<BrainException>(() => pool.AskAsync(id, "Hello?"));
        Assert.Equal(BrainFailure.Unsupported, e.Failure);
        host.StepSync(1);
        host.Latest.TryGetNpcDetail(id, out _);
        host.StepSync(1);
        Assert.True(host.Latest.TryGetNpcDetail(id, out var d));
        Assert.Contains(d.Memories, m => m.Text.Contains("Hello?"));
    }

    [Fact]
    public void Ledger_resets_at_local_midnight_and_restores_only_today()
    {
        var clock = new DateTime(2026, 11, 6, 23, 59, 0);
        var ledger = new BudgetLedger(() => clock);
        ledger.AddNeurons(100);
        ledger.AddUsd(0.5);
        ledger.Restore(new DateOnly(2026, 11, 5), 5000, 9);
        Assert.Equal(100, ledger.NeuronsToday);
        clock = clock.AddMinutes(2);
        Assert.Equal(0, ledger.NeuronsToday);
        Assert.Equal(0, ledger.UsdToday);
        ledger.Restore(new DateOnly(2026, 11, 7), 250, 0.1);
        Assert.Equal(250, ledger.NeuronsToday);
    }
}
