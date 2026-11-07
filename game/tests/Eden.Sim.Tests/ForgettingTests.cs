namespace Eden.Sim.Tests;

/// <summary>K1 forgetting (docs/specs/51-knowledge-roadmap.md item 1): unpractised knowledge fades toward the written floor.</summary>
public class ForgettingTests
{
    /// <summary>One person alone, so nothing they know is refreshed by chatter; no discovery to muddy the dictionary.</summary>
    private static World Hermit(float ratePerYear = 0.05f, float graceYears = 2f, float recordRadius = 64f, int seed = 3)
        => new(new SimConfig
        {
            Seed = seed, StartingNpcs = 1, DiscoveryRate = 0,
            ForgetRatePerYear = ratePerYear, ForgetGraceYears = graceYears, ForgetRecordRadius = recordRadius,
        });

    private static void Years(World w, float years)
    {
        long ticks = (long)(years * w.Config.TicksPerYear);
        for (long t = 0; t < ticks; t++) w.Step();
    }

    [Fact]
    public void Daily_decay_follows_the_formula_and_never_passes_the_floor()
    {
        // Δp per day = −rate / DaysPerYear × (p − floor)
        Assert.Equal(0.8f - 0.005f * 0.8f, KnowledgeRules.Forget(0.8f, 0f, 0.05f, 10), 6);
        Assert.Equal(0.8f - 0.005f * 0.3f, KnowledgeRules.Forget(0.8f, 0.5f, 0.05f, 10), 6);
        Assert.Equal(0.4f, KnowledgeRules.Forget(0.4f, 0.5f, 0.05f, 10));
        Assert.Equal(0.5f, KnowledgeRules.Forget(0.9f, 0.5f, 100f, 1)); // a silly rate still stops at the floor
    }

    [Fact]
    public void Knowledge_not_practised_fades_after_the_grace_period()
    {
        var w = Hermit();
        w.SetKnowledge(0, "smithing", 0.8f);
        Years(w, 1.9f);
        Assert.True(w.Npcs.Alive[0]);
        Assert.Equal(0.8f, w.Proficiency(0, "smithing")); // still within the two-year grace

        Years(w, 1.1f);
        // About one year of decay at 5 %/year: 0.8 × 0.995^10 ≈ 0.761.
        Assert.InRange(w.Proficiency(0, "smithing"), 0.755f, 0.775f);
    }

    [Fact]
    public void Practised_knowledge_does_not_fade()
    {
        // Brutal forgetting: anything unpractised for a year loses most of itself in another.
        var w = Hermit(ratePerYear: 3f, graceYears: 1f);
        w.SetKnowledge(0, "smithing", 0.8f);
        Years(w, 4f);
        Assert.True(w.Npcs.Alive[0]);
        // Foraging (gathering) and fire (resting) are practised every day; smithing never is.
        Assert.True(w.Proficiency(0, "foraging") >= 0.5f, $"foraging {w.Proficiency(0, "foraging")}");
        Assert.True(w.Proficiency(0, "fire") >= 0.5f, $"fire {w.Proficiency(0, "fire")}");
        Assert.Equal(0f, w.Proficiency(0, "smithing"));
    }

    [Fact]
    public void Effects_switch_off_below_the_unlock_level()
    {
        var w = Hermit(ratePerYear: 1f, graceYears: 0.5f);
        w.SetKnowledge(0, "metalworking", 0.4f);
        Assert.True(w.CanBuild(0, "workshop"));
        Assert.NotEqual(0UL, w.KnowledgeState.Craftable[0]);
        Years(w, 1.5f);
        Assert.InRange(w.Proficiency(0, "metalworking"), 0.01f, 0.299f);
        Assert.False(w.CanBuild(0, "workshop"));
        Assert.Equal(0UL, w.KnowledgeState.Craftable[0]);
    }

    [Fact]
    public void A_readable_record_is_the_floor()
    {
        var w = Hermit(ratePerYear: 3f, graceYears: 0.5f, recordRadius: 1e6f);
        w.SetKnowledge(0, "pictographs", 0.7f); // can read, cannot write
        w.SetKnowledge(0, "smithing", 0.9f);
        var rec = w.AddRecordSite(w.Npcs.X[0], w.Npcs.Z[0]);
        w.WriteRecord(rec, "smithing", 0.5f);
        w.WriteRecord(rec, "pictographs", 0.65f); // a hermit never talks, so the record keeps their literacy up too
        Years(w, 4f);
        Assert.True(w.Npcs.Alive[0]);
        Assert.InRange(w.Proficiency(0, "smithing"), 0.5f, 0.52f); // held up by the record
        Assert.InRange(w.Proficiency(0, "pictographs"), 0.65f, 0.67f);
        // The record is only a floor for the literate: without pictographs nothing holds it up.
        var v = Hermit(ratePerYear: 3f, graceYears: 0.5f, recordRadius: 1e6f);
        v.SetKnowledge(0, "smithing", 0.9f);
        v.WriteRecord(v.AddRecordSite(v.Npcs.X[0], v.Npcs.Z[0]), "smithing", 0.5f);
        Years(v, 4f);
        Assert.Equal(0f, v.Proficiency(0, "smithing"));
        Assert.True(v.Graph.KnownAnywhere("smithing")); // the record still holds it
    }

    [Fact]
    public void Forgetting_the_last_unwritten_knowledge_loses_it_from_the_world()
    {
        var w = Hermit(ratePerYear: 3f, graceYears: 0.5f);
        w.SetKnowledge(0, "smithing", 0.9f);
        Assert.True(w.Graph.KnownAnywhere("smithing"));
        Years(w, 4f);
        Assert.False(w.Graph.KnownAnywhere("smithing"));
        Assert.Contains(w.ChronicleTail(200), e => e.Kind == "knowledge-lost" && e.Text.Contains("forgot smithing"));
    }

    [Fact]
    public void Forgetting_is_deterministic()
    {
        static World Run()
        {
            var w = new World(new SimConfig { Seed = 11, ForgetRatePerYear = 1f, ForgetGraceYears = 0.5f });
            for (int s = 0; s < 2; s++) w.SetKnowledge(s, "smithing", 0.9f);
            for (int t = 0; t < 3 * w.Config.TicksPerYear; t++) w.Step();
            return w;
        }
        var a = Run();
        var b = Run();
        Assert.Equal(a.StateHash(), b.StateHash());
        // Something really was forgotten along the way.
        Assert.True(a.Proficiency(0, "smithing") < 0.9f || !a.Npcs.Alive[0]);
    }

    [Fact]
    public void Last_practised_ticks_survive_a_save_round_trip()
    {
        var a = new World(new SimConfig { Seed = 6, ForgetRatePerYear = 1f, ForgetGraceYears = 0.5f });
        for (int s = 0; s < 2; s++) a.SetKnowledge(s, "smithing", 0.9f);
        for (int t = 0; t < a.Config.TicksPerYear / 3; t++) a.Step();
        a.SetKnowledge(0, "iron-smelting", 0.7f); // a later stamp than everything else

        using var ms = new MemoryStream();
        SaveWriter.Write(a, ms);
        ms.Position = 0;
        var b = SaveReader.Load(ms);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.True(b.Graph.TryIndex("iron-smelting", out int law));
        Assert.Equal(a.KnowledgeState.LastPractised[0]![law], b.KnowledgeState.LastPractised[0]![law]);

        // Past the grace period, the two worlds forget exactly alike.
        for (int t = 0; t < 2 * a.Config.TicksPerYear; t++) { a.Step(); b.Step(); }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(a.Proficiency(0, "iron-smelting"), b.Proficiency(0, "iron-smelting"));
    }
}
