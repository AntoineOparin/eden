using Xunit.Abstractions;

namespace Eden.Sim.Tests;

/// <summary>
/// K1 pace (docs/specs/51-knowledge-roadmap.md item 2, targets in DECISIONS 2026-11-06 "K1: era targets"):
/// writing on a median seed in years 150–250, bronze casting in years 300–450.
/// </summary>
public class PacingTests(ITestOutputHelper output)
{
    public const int WritingBandLow = 150, WritingBandHigh = 250;
    public const int BronzeBandLow = 300, BronzeBandHigh = 450;
    private const int EarlyYears = 40, EarlyMedianConcepts = 40;

    /// <summary>First year anyone knew writing / bronze casting (-1: not by <paramref name="years"/>), and concepts ever known.</summary>
    private static (int writing, int bronze, int known, int pop, World world) Run(int seed, int years, bool stopAtBronze)
    {
        var w = new World(new SimConfig { Seed = seed });
        w.Graph.TryIndex("writing", out int wi);
        w.Graph.TryIndex("bronze-casting", out int bi);
        int writing = -1, bronze = -1;
        for (int y = 1; y <= years; y++)
        {
            for (int t = 0; t < w.Config.TicksPerYear; t++) w.Step();
            if (writing < 0 && w.Graph.EverKnown(wi)) writing = y;
            if (bronze < 0 && w.Graph.EverKnown(bi)) bronze = y;
            if (w.Population == 0 || (stopAtBronze && bronze > 0)) break;
        }
        int known = 0;
        for (int i = 0; i < w.Graph.Count; i++) if (w.Graph.EverKnown(i)) known++;
        return (writing, bronze, known, w.Population, w);
    }

    /// <summary>
    /// The half check.sh runs (must stay under 30 s in Debug, so 40 years: the population boom that makes later
    /// years expensive starts after that). On seeds 1..20 the early game is wide (the median living seed has found
    /// a good share of the graph) while no seed is anywhere near writing or metal. The median-in-band half needs
    /// 250–500 simulated years per seed (many minutes in Release), so it runs only with EDEN_PACE_FULL=1; the
    /// runner sweep recorded in STATUS.md is the evidence for it.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void Early_game_is_wide_and_writing_and_metal_are_far_off()
    {
        const int years = EarlyYears;
        var r = new (int writing, int bronze, int known, int pop, bool pictographs, bool copper)[20];
        Parallel.For(1, 21, seed =>
        {
            var (writing, bronze, known, pop, w) = Run(seed, years, stopAtBronze: false);
            r[seed - 1] = (writing, bronze, known, pop, w.Graph.KnownAnywhere("pictographs"), w.Graph.KnownAnywhere("copper-smelting"));
        });
        for (int i = 0; i < r.Length; i++)
            output.WriteLine($"seed {i + 1}: year {years}, pop {r[i].pop}, concepts {r[i].known}, pictographs {r[i].pictographs}, copper {r[i].copper}");
        Assert.All(r, x => Assert.Equal(-1, x.writing));
        Assert.All(r, x => Assert.Equal(-1, x.bronze));
        Assert.All(r, x => Assert.False(x.copper, "copper smelting in the early game"));
        var known = r.Where(x => x.pop > 0).Select(x => x.known).Order().ToArray();
        Assert.True(known.Length >= 14, $"only {known.Length} seeds alive at year {years}");
        Assert.True(known[known.Length / 2] >= EarlyMedianConcepts, $"median seed knows only {known[known.Length / 2]} concepts by year {years}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Median_seed_writes_and_casts_bronze_inside_the_bands()
    {
        if (Environment.GetEnvironmentVariable("EDEN_PACE_FULL") != "1")
        {
            output.WriteLine("skipped: set EDEN_PACE_FULL=1 (takes many minutes; run in Release)");
            return;
        }
        var r = new (int writing, int bronze, int pop)[20];
        Parallel.For(1, 21, seed =>
        {
            var x = Run(seed, BronzeBandHigh + 50, stopAtBronze: true);
            r[seed - 1] = (x.writing, x.bronze, x.pop);
        });
        for (int i = 0; i < r.Length; i++)
            output.WriteLine($"seed {i + 1}: writing@ {r[i].writing}, bronze@ {r[i].bronze}, pop {r[i].pop}");
        static int Median(IEnumerable<int> years)
        {
            var a = years.Select(y => y < 0 ? int.MaxValue : y).Order().ToArray();
            return a[a.Length / 2];
        }
        // Seeds that died out before getting there have no era to measure; living seeds that never got there count as late.
        Assert.InRange(Median(r.Where(x => x.pop > 0 || x.writing > 0).Select(x => x.writing)), WritingBandLow, WritingBandHigh);
        Assert.InRange(Median(r.Where(x => x.pop > 0 || x.bronze > 0).Select(x => x.bronze)), BronzeBandLow, BronzeBandHigh);
        Assert.All(r, x => Assert.True(x.writing < 0 || x.writing >= WritingBandLow / 2, $"writing in year {x.writing}"));
        Assert.All(r, x => Assert.True(x.bronze < 0 || x.bronze >= BronzeBandLow / 2, $"bronze in year {x.bronze}"));
    }
}
