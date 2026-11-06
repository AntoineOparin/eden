using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class WorldSnapshotTests
{
    [Fact]
    public void Snapshot_publishes_living_npcs_on_the_terrain()
    {
        using var host = new SimHost(new SimConfig { Seed = 7 });
        host.StepSync(500);
        var s = host.Latest;
        Assert.Equal(2, s.Population);
        var info = s.World;
        foreach (var npc in s.Npcs.Span)
        {
            Assert.InRange(npc.X, 0, (info.Width - 1) * info.CellSize);
            Assert.InRange(npc.Z, 0, (info.Height - 1) * info.CellSize);
            int cx = (int)MathF.Round(npc.X / info.CellSize), cz = (int)MathF.Round(npc.Z / info.CellSize);
            float h = s.Heights.Span[cz * info.Width + cx];
            Assert.Equal(MathF.Max(h, info.SeaLevel - 0.8f), npc.Y);
            Assert.Equal(LifeStage.Adult, npc.Stage);
            Assert.True(npc.Health > 200);
        }
    }

    [Fact]
    public void Snapshots_are_immutable_once_published()
    {
        using var host = new SimHost(new SimConfig { Seed = 7 });
        var first = host.Latest;
        float x = first.Npcs.Span[0].X;
        host.StepSync(200);
        Assert.Equal(x, first.Npcs.Span[0].X);
        Assert.NotEqual(x, host.Latest.Npcs.Span[0].X);
    }

    [Fact]
    public void Npc_detail_is_available_from_the_snapshot_after_one_request()
    {
        using var host = new SimHost(new SimConfig { Seed = 7 });
        int id = host.Latest.Npcs.Span[0].Id;
        Assert.False(host.Latest.TryGetNpcDetail(id, out _));
        host.StepSync(1);
        Assert.True(host.Latest.TryGetNpcDetail(id, out var detail));
        Assert.Equal(7, detail.Needs.Count);
        Assert.Equal(12, detail.Traits.Count);
        Assert.True(detail.AgeYears >= 17);
        Assert.False(string.IsNullOrEmpty(detail.Name));

        host.StepSync(1);
        Assert.True(host.Latest.TryGetNpcDetail(id, out _), "watched npcs stay in later snapshots");
        Assert.False(host.Latest.TryGetNpcDetail(999_999, out _));
    }

    [Fact]
    public void Possession_is_visible_and_moves_the_npc()
    {
        using var host = new SimHost(new SimConfig { Seed = 7 });
        int id = host.Latest.Npcs.Span[1].Id;
        float x0 = host.Latest.Npcs.Span[1].X;
        host.Enqueue(new PossessCommand(id));
        host.Enqueue(new PossessedMoveCommand(1, 0, false));
        host.StepSync(5);
        var view = host.Latest.Npcs.Span[1];
        Assert.True(view.IsPossessed);
        Assert.NotEqual(x0, view.X);
        host.Enqueue(new ReleasePossessionCommand());
        host.StepSync(1);
        Assert.False(host.Latest.Npcs.Span[1].IsPossessed);
    }

    [Fact]
    public void Unknown_commands_and_bad_targets_are_logged_not_thrown()
    {
        using var host = new SimHost(new SimConfig { Seed = 7 });
        host.Enqueue(new BlessCommand(424242, false, 100));
        host.Enqueue(new SpawnCommand("dragon", 1, 1));
        host.Enqueue(new WeatherCommand("meteor", 10));
        host.StepSync(1);
        Assert.Equal(3, host.Diagnostics.Count);
    }
}
