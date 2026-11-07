using Eden.Mind;
using Eden.Sim.Contracts;

namespace Eden.Sim.Tests.Mind;

public class SpotlightTests
{
    internal static int Id(int slot) => (1 << Npcs.SlotBits) | slot;

    internal static NpcView View(int slot, float x, float z, LifeStage stage = LifeStage.Adult, bool possessed = false, int settlement = 0)
        => new(Id(slot), x, 0, z, 0, Sex.Female, stage, NpcPose.Idle, 255, settlement, false, possessed);

    /// <summary>A snapshot built by hand: slots 0..n-1 alive with version 1.</summary>
    internal static WorldSnapshot Snapshot(NpcView[] views, long tick, int[]? leaders = null, long[]? drama = null,
        IReadOnlyDictionary<int, NpcDetail>? details = null)
    {
        int n = views.Max(v => v.Id & Npcs.SlotMask) + 1;
        var versions = Enumerable.Repeat(1, n).ToArray();
        var extras = new MindExtras(10, 24, leaders ?? [], ["Ashford"], drama ?? Enumerable.Repeat(-1L, n).ToArray(), []);
        return new WorldSnapshot(tick, 0, 0, 12f, new WorldInfo(1, 4, 4, 2f, 0f, 32), new float[16], new byte[16],
            views, [], [], details, versions, null, extras);
    }

    private static SpotlightContext Ctx(float x = 0, float z = 0, bool focus = false, Dictionary<int, long>? player = null)
        => new(x, z, focus, player ?? new Dictionary<int, long>());

    [Fact]
    public void Struck_leader_outranks_an_idle_farmer()
    {
        long now = 1000;
        // Both equally far from the camera; the leader remembers a lightning strike an hour ago.
        var snap = Snapshot([View(0, 100, 100), View(1, 100, 100)], now, leaders: [Id(0)], drama: [now - 10, -1]);
        var ranked = Spotlight.Rank(snap, Ctx(0, 0, focus: true));
        Assert.Equal(Id(0), ranked[0].Id);
        Assert.True(ranked[0].Score > ranked[1].Score + 4.5, $"leader {ranked[0].Score} vs farmer {ranked[1].Score}");
    }

    [Fact]
    public void Score_follows_the_spec_weights()
    {
        Assert.Equal(2 + 3 + 2 + 4 + 1, Spotlight.Score(1, true, 1, 1, true));
        Assert.Equal(0.5, Spotlight.Decay(now: 150, then: 100, window: 100));
        Assert.Equal(0, Spotlight.Decay(now: 300, then: 100, window: 100));
        Assert.Equal(0, Spotlight.Decay(now: 300, then: -1, window: 100));
    }

    [Fact]
    public void Proximity_player_interaction_and_possession_count()
    {
        var views = new[]
        {
            View(0, 0, 0),                    // under the camera
            View(1, 500, 500),                // far, but the god just talked to them
            View(2, 300, 300, possessed: true),
            View(3, 305, 300),                // next to the possessed NPC
            View(4, 1, 1, LifeStage.Infant),  // infants never think
        };
        var snap = Snapshot(views, 240);
        var ranked = Spotlight.Rank(snap, Ctx(0, 0, true, new Dictionary<int, long> { [Id(1)] = 240 }));
        Assert.Equal([Id(1), Id(0), Id(3)], ranked.Select(r => r.Id).ToArray());
        Assert.Equal([Id(0), Id(1)], Spotlight.Pick(snap, Ctx(0, 0, true, new Dictionary<int, long> { [Id(1)] = 240 }), 2).Order().ToArray());
    }

    [Fact]
    public void SetThinkingCommand_flags_npcs_in_the_snapshot_without_touching_the_hash()
    {
        var w = new World(new SimConfig { Seed = 12 });
        ulong before = w.StateHash();
        int a = w.Npcs.IdOf(0);
        w.Apply(new SetThinkingCommand([a, 12345]));
        var snap = w.Snapshot();
        Assert.True(snap.Npcs.Span.ToArray().Single(v => v.Id == a).IsThinking);
        Assert.False(snap.Npcs.Span.ToArray().Single(v => v.Id == w.Npcs.IdOf(1)).IsThinking);
        Assert.Equal(before, w.StateHash());
        w.Apply(new SetThinkingCommand([]));
        Assert.DoesNotContain(w.Snapshot().Npcs.Span.ToArray(), v => v.IsThinking);
    }

    [Fact]
    public void Lightning_shows_up_as_drama_in_the_mind_snapshot()
    {
        var w = new World(new SimConfig { Seed = 12 });
        int a = w.Npcs.IdOf(0);
        Assert.Equal(-1, w.Snapshot().LastDramaTick(a));
        w.Apply(new LightningCommand(w.Npcs.X[0] + 15, w.Npcs.Z[0], 2));
        var snap = w.Snapshot();
        Assert.Equal(w.Tick, snap.LastDramaTick(a));
        Assert.Equal(10, snap.TicksPerHour);
        Assert.NotEmpty(snap.RecentChronicle);
    }
}
