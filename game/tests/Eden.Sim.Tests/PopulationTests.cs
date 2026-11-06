using Xunit.Abstractions;

namespace Eden.Sim.Tests;

public class PopulationTests(ITestOutputHelper output)
{
    private enum Outcome { Reached50, DiedOut, Stalled }

    /// <summary>Run until 50+ people, extinction, or more than 10 generations (or 300 years) without reaching 50.</summary>
    private static (Outcome outcome, int year, int generation) Run(int seed)
    {
        var w = new World(new SimConfig { Seed = seed });
        int ticksPerYear = w.Config.TicksPerYear;
        for (int year = 1; year <= 300; year++)
        {
            for (int t = 0; t < ticksPerYear; t++) w.Step();
            if (w.Population >= 50) return (Outcome.Reached50, year, w.MaxLineage);
            if (w.Population == 0) return (Outcome.DiedOut, year, w.MaxLineage);
            if (w.MaxLineage > 10) return (Outcome.Stalled, year, w.MaxLineage);
        }
        return (Outcome.Stalled, 300, w.MaxLineage);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Two_people_grow_to_fifty_on_most_seeds_and_some_seeds_die_out()
    {
        var results = new (Outcome outcome, int year, int generation)[20];
        Parallel.For(1, 21, seed => results[seed - 1] = Run(seed));
        for (int i = 0; i < results.Length; i++)
            output.WriteLine($"seed {i + 1}: {results[i].outcome} in year {results[i].year}, generation {results[i].generation}");

        int grew = results.Count(r => r.outcome == Outcome.Reached50);
        int diedOut = results.Count(r => r.outcome == Outcome.DiedOut);
        Assert.True(grew >= 14, $"only {grew}/20 seeds reached 50 within 10 generations");
        Assert.True(diedOut >= 1, "no seed died out; growth looks scripted");
    }
}
