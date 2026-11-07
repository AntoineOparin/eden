using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

/// <summary>M7 UI contract fill-in: <see cref="IWorldSnapshot.Settlements"/> and <see cref="MemoryView.Generation"/>.</summary>
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
}
