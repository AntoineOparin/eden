namespace Eden.Game.Tests;

using Eden.Game.Dev;
using Eden.Sim.Contracts;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
public class FakeSimHostTests
{
    [TestCase]
    public void Generates_an_island_with_sea_on_the_border_and_land_in_the_middle()
    {
        using var host = new FakeSimHost(0);
        var s = host.Latest;
        var h = s.Heights.Span;
        int w = s.World.Width;
        AssertThat(h.Length).IsEqual(w * s.World.Height);
        AssertThat(h[0]).IsLess(s.World.SeaLevel);
        AssertThat(h[w * s.World.Height - 1]).IsLess(s.World.SeaLevel);
        int land = 0;
        foreach (var v in h) if (v > s.World.SeaLevel) land++;
        AssertThat(land).IsGreater(h.Length / 6);
    }

    [TestCase]
    public void Npcs_spawn_and_stay_on_land()
    {
        using var host = new FakeSimHost(1000);
        host.StepSync(200);
        var s = host.Latest;
        AssertThat(s.Population).IsEqual(1000);
        AssertThat(s.Tick).IsEqual(200L);
        foreach (var n in s.Npcs.Span)
            AssertThat(n.Y).IsGreater(s.World.SeaLevel);
    }

    [TestCase]
    public void Honours_set_time_scale_command()
    {
        using var host = new FakeSimHost(10);
        AssertThat(host.Scale).IsEqual(TimeScale.X1);
        host.Enqueue(new SetTimeScaleCommand(TimeScale.X500));
        host.StepSync(0);
        AssertThat(host.Scale).IsEqual(TimeScale.X500);
        host.Enqueue(new SetTimeScaleCommand(TimeScale.Paused));
        host.StepSync(0);
        AssertThat(host.Scale).IsEqual(TimeScale.Paused);
    }

    [TestCase]
    public async Task Paused_background_thread_publishes_no_new_ticks()
    {
        using var host = new FakeSimHost(10);
        host.Enqueue(new SetTimeScaleCommand(TimeScale.Paused));
        host.Start();
        await Task.Delay(50);
        long tick = host.Latest.Tick;
        await Task.Delay(100);
        AssertThat(host.Latest.Tick).IsEqual(tick);
        host.Enqueue(new SetTimeScaleCommand(TimeScale.X100));
        await Task.Delay(150);
        AssertThat(host.Latest.Tick).IsGreater(tick);
        host.Stop();
    }

    [TestCase]
    public void Snapshots_are_immutable_and_details_resolve()
    {
        using var host = new FakeSimHost(50);
        var first = host.Latest;
        var x0 = first.Npcs.Span[3].X;
        host.StepSync(100);
        AssertThat(first.Npcs.Span[3].X).IsEqual(x0);
        AssertThat(ReferenceEquals(first, host.Latest)).IsFalse();
        AssertThat(host.Latest.TryGetNpcDetail(3, out var d)).IsTrue();
        AssertThat(d.Needs.Count).IsGreater(0);
        AssertThat(host.Latest.TryGetNpcDetail(999, out _)).IsFalse();
    }

    [TestCase]
    public void Same_seed_same_world()
    {
        using var a = new FakeSimHost(100, seed: 3);
        using var b = new FakeSimHost(100, seed: 3);
        a.StepSync(50);
        b.StepSync(50);
        AssertThat(a.StateHash()).IsEqual(b.StateHash());
    }
}
