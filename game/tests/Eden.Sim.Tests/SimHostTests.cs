using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class SimHostTests
{
    [Fact]
    public void Same_seed_same_hash_after_10k_ticks()
    {
        using var a = new SimHost(new SimConfig { Seed = 11 });
        using var b = new SimHost(new SimConfig { Seed = 11 });
        a.StepSync(10_000);
        b.StepSync(10_000);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(10_000, a.Latest.Tick);
    }

    [Fact]
    public void Different_seed_different_hash()
    {
        using var a = new SimHost(new SimConfig { Seed = 1 });
        using var b = new SimHost(new SimConfig { Seed = 2 });
        a.StepSync(10);
        b.StepSync(10);
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Clock_follows_config()
    {
        var cfg = new SimConfig { TicksPerHour = 10, HoursPerDay = 24, DaysPerYear = 10 };
        using var host = new SimHost(cfg);
        host.StepSync(cfg.TicksPerYear * 2 + cfg.TicksPerDay * 3 + 15);
        Assert.Equal(2, host.Latest.Year);
        Assert.Equal(3, host.Latest.DayOfYear);
        Assert.Equal(1.5f, host.Latest.HourOfDay);
    }

    [Fact]
    public void Genesis_chronicle_is_published_once()
    {
        using var host = new SimHost(new SimConfig());
        Assert.Single(host.Latest.NewChronicle);
        host.StepSync(1);
        Assert.Empty(host.Latest.NewChronicle);
    }

    [Fact]
    public void Intentions_never_block_and_are_rejected_until_m1()
    {
        using var host = new SimHost(new SimConfig());
        Assert.True(host.Intentions.TryPush(new GoalIntention(1, 0, IntentionSource.Llm, ActionKind.Eat, null, null, 1)));
        host.StepSync(1);
        Assert.Contains(host.Diagnostics, d => d.Contains("rejected"));
    }

    [Fact]
    public void Thread_runs_and_stops()
    {
        using var host = new SimHost(new SimConfig { TicksPerRealSecond = 2000 });
        host.Enqueue(new SetTimeScaleCommand(TimeScale.X10));
        host.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (host.Latest.Tick < 100 && DateTime.UtcNow < deadline) Thread.Sleep(5);
        host.Stop();
        Assert.True(host.Latest.Tick >= 100, $"only reached tick {host.Latest.Tick}");
    }
}
