using Eden.Sim.Contracts;
using Xunit.Abstractions;

namespace Eden.Sim.Tests;

/// <summary>M7: the god's standing per settlement (Favour, Awe), neighbours' Envy/Fear, determinism and saves.</summary>
public class MythTests(ITestOutputHelper output)
{
    private static void Bless(World w, int set, bool curse = false)
        => w.Apply(new BlessCommand(w.Settlements.Leader[set], curse, w.Config.TicksPerDay));

    private static void Strike(World w, int set)
        => w.Apply(new LightningCommand(w.Settlements.CenterX[set] + 30f, w.Settlements.CenterZ[set], 1f));

    [Fact]
    public void Blessings_raise_favour_and_curses_lower_it()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        Assert.Equal(0f, w.Standing.Favour[set]);
        for (int i = 0; i < 10; i++) Bless(w, set);
        Assert.True(w.Standing.Favour[set] > 0.6f, $"favour {w.Standing.Favour[set]}");
        Assert.True(w.Standing.Awe[set] > 0.2f);
        Assert.Equal(GodStance.Favourable, w.StanceOf(set));

        var (w2, x2, z2) = Villages.World(5);
        int s2 = Villages.Found(w2, x2, z2);
        for (int i = 0; i < 5; i++) Bless(w2, s2, curse: true);
        Assert.True(w2.Standing.Favour[s2] < -0.4f, $"favour {w2.Standing.Favour[s2]}");
    }

    [Fact]
    public void Favour_and_awe_decay_slowly()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        Bless(w, set);
        float f0 = w.Standing.Favour[set], a0 = w.Standing.Awe[set];
        for (int d = 0; d < 10; d++) w.RunMythDayForTests();
        Assert.InRange(w.Standing.Favour[set], f0 * 0.9f, f0 * 0.99f);
        Assert.InRange(w.Standing.Awe[set], a0 * 0.85f, a0 * 0.95f);
    }

    [Fact]
    public void Lightning_costs_favour_only_after_the_grace_period_and_rain_forgives_it()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        Strike(w, set);
        Assert.True(w.Standing.Awe[set] > 0.1f);
        Assert.Equal(0f, w.Standing.Favour[set]);
        Villages.Days(w, w.Config.WrathGraceDays + 1);
        Assert.True(w.Standing.Favour[set] < -0.05f, $"favour {w.Standing.Favour[set]}");

        var (w2, x2, z2) = Villages.World(5);
        int s2 = Villages.Found(w2, x2, z2);
        Strike(w2, s2);
        Villages.Days(w2, 1);
        w2.Apply(new WeatherCommand("rain", w2.Config.TicksPerDay));
        Villages.Days(w2, w2.Config.WrathGraceDays + 1);
        Assert.True(w2.Standing.Favour[s2] > 0f, $"forgiven favour {w2.Standing.Favour[s2]}");
    }

    [Fact]
    public void Ten_lightning_strikes_without_blessing_make_the_stance_fearful()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        for (int i = 0; i < 10; i++) { Strike(w, set); Villages.Days(w, 1); }
        Villages.Days(w, w.Config.WrathGraceDays + 1);
        output.WriteLine($"favour {w.Standing.Favour[set]:0.00} awe {w.Standing.Awe[set]:0.00}");
        Assert.Equal(GodStance.Fearful, w.StanceOf(set));
        int r = w.ReligionOfSettlement(set);
        if (r >= 0) Assert.Equal(GodStance.Fearful, w.Religions.Stance[r]);
    }

    [Fact]
    public void Weather_touches_every_settlement_and_bless_only_the_target()
    {
        var (w, x, z) = Villages.World(5);
        int a = Villages.Found(w, x, z);
        var (bx, bz) = Villages.SpotAway(w, x, z);
        int b = Villages.Found(w, bx, bz);
        Bless(w, a);
        Assert.True(w.Standing.Favour[a] > 0f);
        Assert.Equal(0f, w.Standing.Favour[b]);
        w.Apply(new WeatherCommand("drought", 100));
        Assert.True(w.Standing.Awe[b] > 0f);
        Assert.True(w.Standing.Wrath[a] > 0f && w.Standing.Wrath[b] > 0f);
    }

    [Fact]
    public void A_neighbour_in_contact_envies_or_fears_the_blessed_settlement()
    {
        var (w, x, z) = Villages.World(5);
        int a = Villages.Found(w, x, z);
        var (bx, bz) = Villages.SpotAway(w, x, z);
        int b = Villages.Found(w, bx, bz);
        int sa = Member(w, a), sb = Member(w, b);
        for (int d = 0; d < 10; d++)
        {
            Bless(w, a);
            w.ConverseForTests(sb, sa); // a visit: news of the god travels
            Villages.Days(w, 1);
        }
        var g = w.Standing;
        output.WriteLine($"favour a {g.Favour[a]:0.00} b {g.Favour[b]:0.00}; b envy {g.Envy[b]:0.00} fear {g.Fear[b]:0.00}");
        Assert.True(g.LastContact(a, b) >= 0);
        Assert.True(g.Envy[b] > 0.3f || g.Fear[b] > 0.3f);
        Assert.Equal(a, g.EnvyOf[b]);
        Assert.True(g.Envy[a] < 0.05f, "the favoured side envies nobody");
        Assert.True(w.Settlements.Grievance(b, a) > 0f, "envy breeds grievance");
    }

    [Fact]
    public void Without_contact_neighbours_feel_nothing()
    {
        var (w, x, z) = Villages.World(5);
        int a = Villages.Found(w, x, z);
        var (bx, bz) = Villages.SpotAway(w, x, z, 200f, 400f);
        int b = Villages.Found(w, bx, bz);
        for (int i = 0; i < 10; i++) Bless(w, a);
        for (int d = 0; d < 5; d++) w.RunMythDayForTests();
        if (w.Standing.LastContact(a, b) < 0) Assert.Equal(0f, w.Standing.Envy[b]);
    }

    /// <summary>
    /// M7 acceptance: 10 blessings on one settlement over an in-game year (one a day) leave Favour above 0.5, and a
    /// religion with a favourable stance exists within 5 years, on at least 70 % of seeds 1..10.
    /// </summary>
    [Fact] // ~0.25 s for ten seeds (villages of 8-10 people), so not tagged Slow
    public void Repeated_blessing_founds_a_favourable_religion_on_most_seeds()
    {
        var results = new (float favour, int religionDay, string stance)[10];
        Parallel.For(1, 11, seed =>
        {
            var (w, x, z) = Villages.World(seed);
            int set = FoundSomewhere(w, x, z);
            var cfg = w.Config;
            float favour = 0f;
            int found = -1;
            string stance = "-";
            for (int day = 0; day < 5 * cfg.DaysPerYear && (found < 0 || day < cfg.DaysPerYear); day++)
            {
                if (day < cfg.DaysPerYear && w.Npcs.IsAlive(w.Settlements.Leader[set])) Bless(w, set);
                Villages.Days(w, 1);
                if (day == cfg.DaysPerYear - 1) favour = w.Standing.Favour[set];
                int r = w.ReligionOfSettlement(set);
                if (r >= 0 && found < 0) { found = day + 1; stance = GodStanding.StanceName(w.Religions.Stance[r]); }
            }
            results[seed - 1] = (favour, found, stance);
        });
        for (int i = 0; i < results.Length; i++)
            output.WriteLine($"seed {i + 1}: favour after a year {results[i].favour:0.00}, religion on day {results[i].religionDay} ({results[i].stance})");
        int ok = results.Count(r => r.favour > 0.5f && r.religionDay > 0 && r.stance == "favourable");
        output.WriteLine($"{ok}/10 seeds pass");
        Assert.True(ok >= 7, $"only {ok}/10 seeds have favour > 0.5 and a favourable religion within 5 years");
    }

    [Fact]
    public void Same_seed_and_god_acts_give_the_same_myth()
    {
        ulong Run()
        {
            var (w, x, z) = Villages.World(9);
            int set = Villages.Found(w, x, z);
            for (int d = 0; d < 20; d++)
            {
                if (d % 2 == 0) Bless(w, set); else Strike(w, set);
                Villages.Days(w, 1);
            }
            return w.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Myth_state_survives_a_save_round_trip()
    {
        var (w, x, z) = Villages.World(5);
        int a = Villages.Found(w, x, z);
        var (bx, bz) = Villages.SpotAway(w, x, z);
        int b = Villages.Found(w, bx, bz);
        for (int d = 0; d < 25; d++)
        {
            if (d < 10) Bless(w, a);
            if (d == 3) Strike(w, b);
            w.ConverseForTests(Member(w, b), Member(w, a));
            Villages.Days(w, 1);
        }
        // Force a religion and some retold gossip so every part of the MYTH section has content.
        if (w.ReligionOfSettlement(a) < 0) Believe(w, a, 1f);
        w.RunMythDayForTests();
        Assert.True(w.ReligionOfSettlement(a) >= 0);
        Assert.Contains(Enumerable.Range(0, w.Npcs.HighWater).Where(s => w.Npcs.Alive[s]).SelectMany(s => w.Memories.Of(s)), m => m.Generation > 0);

        using var ms = new MemoryStream();
        SaveWriter.Write(w, ms);
        ms.Position = 0;
        var c = SaveReader.Load(ms);
        Assert.Equal(w.MythHashForTests(), c.MythHashForTests());
        Assert.Equal(w.StateHash(), c.StateHash());
        Assert.Equal(w.Standing.Favour[a], c.Standing.Favour[a]);
        Assert.Equal(w.Religions.Name[0], c.Religions.Name[0]);
        Assert.Equal(w.ReligionOfSettlement(a), c.ReligionOfSettlement(a));
        for (int t = 0; t < 3 * w.Config.TicksPerDay; t++) { w.Step(); c.Step(); }
        Assert.Equal(w.StateHash(), c.StateHash());
    }

    [Fact]
    public void Snapshot_carries_the_god_standing_for_the_mind()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        for (int i = 0; i < 5; i++) Bless(w, set);
        var snap = w.Snapshot();
        var g = ((IMindSnapshot)snap).GodStanding(set);
        Assert.NotNull(g);
        Assert.Equal("favourable", g!.Value.Stance);
        Assert.Equal(w.Standing.Favour[set], g.Value.Favour);
        Assert.Null(((IMindSnapshot)snap).GodStanding(99));
    }

    /// <summary>Villages.Found at (x, z), or at a spot further away when that one does not cluster into a settlement.</summary>
    private static int FoundSomewhere(World w, float x, float z)
    {
        try { return Villages.Found(w, x, z); }
        catch (Xunit.Sdk.XunitException)
        {
            var (x2, z2) = Villages.SpotAway(w, x, z, 100f, 220f);
            return Villages.Found(w, x2, z2);
        }
    }

    internal static int Member(World w, int set)
    {
        for (int s = 0; s < w.Npcs.HighWater; s++)
            if (w.Npcs.Alive[s] && w.SettlementOfSlot(s) == set && w.StageOf(s) is LifeStage.Adult or LifeStage.Elder) return s;
        throw new InvalidOperationException("no adult member");
    }

    /// <summary>Give a share of the settlement's adults a belief about the god (the rest none).</summary>
    internal static int Believe(World w, int set, float share, string text = "The sky watches and judges.")
    {
        var adults = Enumerable.Range(0, w.Npcs.HighWater)
            .Where(s => w.Npcs.Alive[s] && w.SettlementOfSlot(s) == set && w.StageOf(s) is LifeStage.Adult or LifeStage.Elder).ToList();
        int n = (int)MathF.Ceiling(share * adults.Count);
        for (int i = 0; i < n; i++)
            w.Memories.Add(adults[i], new Memory(w.Tick, MemoryKind.Belief, [w.Npcs.IdOf(adults[i])], text, 6), w.Tick);
        return n;
    }
}
