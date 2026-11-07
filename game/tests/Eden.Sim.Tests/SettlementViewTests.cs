using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

/// <summary>
/// M7 UI contract fill-in (<see cref="IWorldSnapshot.Settlements"/>, <see cref="MemoryView.Generation"/>) and C1:
/// <see cref="SettlementView.Food"/>, <see cref="SettlementView.TerritoryRadius"/> and the on-request
/// <see cref="SettlementDetail"/> with its watch list.
/// </summary>
public class SettlementViewTests
{
    [Fact]
    public void Snapshot_lists_living_settlements_with_the_gods_standing()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        int leader = w.Settlements.Leader[set];
        for (int i = 0; i < 10; i++) w.Apply(new BlessCommand(leader, false, w.Config.TicksPerDay));

        var snap = w.Snapshot();
        var v = Assert.Single(snap.Settlements.ToArray(), s => s.Id == set);
        Assert.Equal(w.Settlements.Name[set], v.Name);
        Assert.Equal(w.Settlements.CenterX[set], v.X);
        Assert.Equal(w.Settlements.CenterZ[set], v.Z);
        Assert.Equal(w.Settlements.Members[set], v.Members);
        Assert.Equal(leader, v.LeaderId);
        Assert.Equal(w.Standing.Favour[set], v.Favour);
        Assert.Equal(w.Standing.Awe[set], v.Awe);
        Assert.Equal(global::Eden.Sim.Contracts.GodStance.Favourable, v.Stance);
        Assert.Equal(-1, v.EnviedId);

        // Same numbers as the Mind's view of the standing.
        var mind = ((IMindSnapshot)snap).GodStanding(set)!.Value;
        Assert.Equal(mind.Favour, v.Favour);
        Assert.Equal("favourable", mind.Stance);
        Assert.Equal(mind.Religion, v.ReligionName);
    }

    [Fact]
    public void Building_the_settlement_views_does_not_change_the_state_hash()
    {
        var (w, x, z) = Villages.World(5);
        Villages.Found(w, x, z);
        ulong before = w.StateHash();
        _ = w.Snapshot();
        _ = w.Snapshot();
        Assert.Equal(before, w.StateHash());
    }

    [Fact]
    public void Npc_detail_memories_carry_their_retelling_generation()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        int id = w.Settlements.Leader[set];
        Assert.True(w.Npcs.TryGetSlot(id, out int slot));
        w.Memories.Add(slot, new Memory(w.Tick, MemoryKind.Gossip, [MemoryStore.GodId], "Bo told me: the god walked among us.", 8) { Generation = 2 }, w.Tick);

        Assert.True(w.TryBuildDetail(id, out var d));
        var heard = Assert.Single(d.Memories, m => m.Text.Contains("walked among us"));
        Assert.Equal(2, heard.Generation);
        Assert.All(d.Memories.Where(m => m.Kind == "summary"), m => Assert.Equal(0, m.Generation));
    }

    // ---- C1 ---------------------------------------------------------------------------------

    [Fact]
    public void Views_carry_food_and_the_sims_territory_radius()
    {
        var (w, x, z) = Villages.World(5, new SimConfig { SettlementRadius = 30f, TerritoryFactor = 2f });
        int set = Villages.Found(w, x, z);
        w.Settlements.Food[set] = 12.5f;
        float carried = 0;
        for (int s = 0; s < w.Npcs.HighWater; s++)
            if (w.Npcs.Alive[s] && w.SettlementOfSlot(s) == set) { w.Npcs.Food[s] = 2f; carried += 2f; }
        Assert.True(carried > 0);

        var v = Assert.Single(w.Snapshot().Settlements.ToArray(), s => s.Id == set);
        Assert.Equal(12.5f + carried, v.Food, 3);
        Assert.Equal(60f, v.TerritoryRadius); // 30 m × 2, the circle trespass uses
    }

    [Fact]
    public void Detail_is_requested_then_present_on_a_later_snapshot()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        var first = w.Snapshot();
        Assert.False(first.TryGetSettlementDetail(set, out _)); // asks the sim to watch it
        Assert.False(first.TryGetSettlementDetail(999, out _));
        var next = w.Snapshot();
        Assert.True(next.TryGetSettlementDetail(set, out var d));
        Assert.Equal(set, d.Id);
        Assert.Equal(w.Settlements.Name[set], d.Name);
        Assert.Equal(w.Settlements.Members[set], d.Members);
        Assert.Equal(w.WatchedSettlements, [set]);
        Assert.False(next.TryGetSettlementDetail(999, out _)); // unknown ids are never watched
        Assert.Equal(w.WatchedSettlements, [set]);
    }

    [Fact]
    public void Watch_command_and_detail_contents_match_the_sim()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        var members = new List<int>();
        for (int s = 0; s < w.Npcs.HighWater; s++)
            if (w.Npcs.Alive[s] && w.SettlementOfSlot(s) == set) members.Add(s);
        Assert.True(members.Count >= 4);
        foreach (int s in members) w.SetKnowledge(s, "pottery", 0f);
        w.SetKnowledge(members[0], "pottery", 0.9f);
        w.SetKnowledge(members[1], "pottery", 0.4f);
        w.SetKnowledge(members[2], "weaving", 0.3f);
        var rec = w.AddRecordSite(w.Settlements.CenterX[set] + 5f, w.Settlements.CenterZ[set]);
        rec.Entries["weaving"] = 0.3f;
        int hut = w.Buildings.Add(BuildingKinds.Hut, x, z, 0, set, -1);
        w.Buildings.Progress[hut] = 1f;
        int site = w.Buildings.Add(BuildingKinds.Shrine, x + 3, z, 0, set, -1); // unfinished: not counted
        w.Settlements.Food[set] = 30f;

        w.Apply(new WatchSettlementCommand(set));
        Assert.True(w.Snapshot().TryGetSettlementDetail(set, out var d));

        var pottery = Assert.Single(d.Concepts, c => c.ConceptId == "pottery");
        Assert.Equal(2, pottery.Holders);
        Assert.Equal(0.9f, pottery.BestProficiency);
        Assert.False(pottery.HasRecord);
        Assert.True(Assert.Single(d.Concepts, c => c.ConceptId == "weaving").HasRecord);
        for (int i = 1; i < d.Concepts.Count; i++) Assert.True(d.Concepts[i - 1].Holders >= d.Concepts[i].Holders);
        Assert.Contains(new BuildingCount("hut", CountFinished(w, set, BuildingKinds.Hut)), d.Buildings);
        Assert.Equal(0f, w.Buildings.Progress[site]);
        Assert.DoesNotContain(d.Buildings, b => b.Kind == "shrine");
        int adults = members.Count(s => w.StageOf(s) is LifeStage.Adult or LifeStage.Elder);
        Assert.Equal(adults, d.Adults);
        float carried = members.Sum(s => w.Npcs.Food[s]);
        Assert.Equal(30f + carried, d.Food, 3);
        Assert.Equal(members.Count * w.Config.FoodDecayPerDay / w.Config.FoodPerUnit, d.FoodPerDay, 2); // all adults here
    }

    private static int CountFinished(World w, int set, int kind)
    {
        int n = 0;
        for (int i = 0; i < w.Buildings.HighWater; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Settlement[i] == set && w.Buildings.Kind[i] == kind && w.Buildings.Progress[i] >= 1f) n++;
        return n;
    }

    [Fact]
    public void Watch_list_is_bounded_and_drops_the_oldest_request()
    {
        var (w, _, _) = Villages.World(5);
        // Settlements are cheap to add directly: ten names around the genesis pair.
        int first = w.Settlements.Count;
        for (int i = 0; i < World.MaxWatchedSettlements + 2; i++) w.Settlements.Add($"Test{i}", 50 + i * 10, 50, w.Tick);
        for (int i = 0; i < World.MaxWatchedSettlements + 2; i++) w.Apply(new WatchSettlementCommand(first + i));
        var snap = w.Snapshot();
        Assert.Equal(World.MaxWatchedSettlements, w.WatchedSettlements.Count);
        Assert.False(snap.TryGetSettlementDetail(first, out _));
        Assert.False(snap.TryGetSettlementDetail(first + 1, out _));
        Assert.True(snap.TryGetSettlementDetail(first + 2, out _));
        Assert.True(snap.TryGetSettlementDetail(first + World.MaxWatchedSettlements + 1, out _));
    }

    [Fact]
    public void Detail_requests_and_history_reads_do_not_change_the_state_hash_over_10k_ticks()
    {
        static (ulong Hash, int Details) Run(bool ask)
        {
            var (w, x, z) = Villages.World(5);
            Villages.Found(w, x, z);
            int details = 0;
            for (int t = 0; t < 10_000; t++)
            {
                w.Step();
                if (t % 50 != 0) continue;
                var snap = w.Snapshot(); // both runs publish snapshots; only one asks for details
                if (!ask) continue;
                _ = snap.ChronicleSince(-1);
                foreach (var v in snap.Settlements.Span)
                {
                    if (snap.TryGetSettlementDetail(v.Id, out _)) details++;
                    w.Apply(new WatchSettlementCommand(v.Id));
                }
            }
            return (w.StateHash(), details);
        }
        var plain = Run(false);
        var asked = Run(true);
        Assert.True(asked.Details > 0);
        Assert.Equal(plain.Hash, asked.Hash);
    }
}
