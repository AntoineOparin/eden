using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class DeterminismTests
{
    /// <summary>The same scripted god commands and intentions, issued at fixed ticks.</summary>
    private static void Run(World w, int ticks, bool extraSpawn = false)
    {
        int first = w.Npcs.IdOf(0);
        float x = w.Npcs.X[0], z = w.Npcs.Z[0];
        for (int t = 0; t < ticks; t++)
        {
            switch (t)
            {
                case 500: w.Apply(new SpawnCommand("human", x, z, 3)); break;
                case 1500: w.Apply(new WeatherCommand("rain", 2000)); break;
                case 2500: w.Apply(new WhisperCommand(first, "Go north.")); break;
                case 3000: w.Apply(new BlessCommand(first, false, 5000)); break;
                case 4000: w.Apply(new GoalIntention(first, w.Tick, IntentionSource.Llm, ActionKind.Explore, null, "curious", 5)); break;
                case 6000: w.Apply(new LightningCommand(x + 40, z + 40, 6)); break;
                case 7000 when extraSpawn: w.Apply(new SpawnCommand("human", x, z)); break;
            }
            w.Step();
        }
    }

    [Fact]
    public void Same_seed_and_commands_give_equal_hash_after_10k_ticks()
    {
        var a = new World(new SimConfig { Seed = 21 });
        var b = new World(new SimConfig { Seed = 21 });
        Run(a, 10_000);
        Run(b, 10_000);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(a.Population, b.Population);
        Assert.Equal(a.TotalBirths, b.TotalBirths);
    }

    [Fact]
    public void A_different_command_stream_diverges()
    {
        var a = new World(new SimConfig { Seed = 21 });
        var b = new World(new SimConfig { Seed = 21 });
        Run(a, 8_000);
        Run(b, 8_000, extraSpawn: true);
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Hash_changes_as_the_world_evolves()
    {
        var w = new World(new SimConfig { Seed = 4 });
        ulong h0 = w.StateHash();
        w.Step();
        Assert.NotEqual(h0, w.StateHash());
    }
}
