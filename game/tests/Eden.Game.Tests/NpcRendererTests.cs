namespace Eden.Game.Tests;

using Eden.Game.Dev;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
public class NpcRendererTests
{
    private static NpcRenderer NewRenderer() => new()
    {
        HiScene = GD.Load<PackedScene>("res://scenes/npc/npc_hi.tscn"),
    };

    [TestCase]
    [RequireGodotRuntime]
    public void Assigns_exactly_50_hi_lod_slots_for_1000_npcs()
    {
        using var host = new FakeSimHost(1000);
        var renderer = NewRenderer();
        renderer.Render(host.Latest, new Vector3(256, 100, 256), 0);

        AssertThat(renderer.HiLodCount).IsEqual(50);
        AssertThat(renderer.LowLodCount).IsEqual(950);
        AssertThat(renderer.HiLodNpcIds.Distinct().Count()).IsEqual(50);
        renderer.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Hi_lod_slots_go_to_the_nearest_npcs()
    {
        using var host = new FakeSimHost(1000);
        var renderer = NewRenderer();
        var viewer = new Vector3(200, 60, 300);
        renderer.Render(host.Latest, viewer, 0);

        var npcs = host.Latest.Npcs.ToArray();
        var hi = renderer.HiLodNpcIds.ToHashSet();
        float farthestHi = npcs.Where(n => hi.Contains(n.Id)).Max(n => new Vector3(n.X, n.Y, n.Z).DistanceSquaredTo(viewer));
        float nearestLow = npcs.Where(n => !hi.Contains(n.Id)).Min(n => new Vector3(n.X, n.Y, n.Z).DistanceSquaredTo(viewer));
        AssertThat(farthestHi).IsLessEqual(nearestLow);
        renderer.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Hysteresis_keeps_slots_when_the_viewer_barely_moves()
    {
        using var host = new FakeSimHost(1000);
        var renderer = NewRenderer();
        renderer.Render(host.Latest, new Vector3(256, 60, 256), 0);
        var before = renderer.HiLodNpcIds.ToHashSet();
        renderer.Render(host.Latest, new Vector3(256.5f, 60, 256), 0.016);
        AssertThat(renderer.HiLodNpcIds.ToHashSet().SetEquals(before)).IsTrue();
        AssertThat(renderer.HiLodCount).IsEqual(50);
        renderer.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Fewer_npcs_than_slots_are_all_hi_lod()
    {
        using var host = new FakeSimHost(12);
        var renderer = NewRenderer();
        renderer.Render(host.Latest, Vector3.Zero, 0);
        AssertThat(renderer.HiLodCount).IsEqual(12);
        AssertThat(renderer.LowLodCount).IsEqual(0);
        renderer.Free();
    }
}
