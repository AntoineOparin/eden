using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

/// <summary>M7: religion emergence, shrines, prayer, spread and schism.</summary>
public class ReligionTests
{
    /// <summary>Give the village a finished storehouse (and refresh the store cache with one society day).</summary>
    private static void Establish(World w, int set)
    {
        int b = w.Buildings.Add(BuildingKinds.Storehouse, w.Settlements.CenterX[set] + 3f, w.Settlements.CenterZ[set], 0f, set, -1);
        w.Buildings.Progress[b] = 1f;
        w.RunSocietyDayForTests();
    }

    private static int Adults(World w, int set) => Enumerable.Range(0, w.Npcs.HighWater)
        .Count(s => w.Npcs.Alive[s] && w.SettlementOfSlot(s) == set && w.StageOf(s) is LifeStage.Adult or LifeStage.Elder);

    [Fact]
    public void A_religion_is_founded_when_forty_percent_of_adults_believe_in_the_god()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        int adults = Adults(w, set);
        // Just under the threshold: nothing.
        int few = (int)MathF.Ceiling(0.4f * adults) - 1;
        MythTests.Believe(w, set, few / (float)adults - 0.001f);
        w.RunMythDayForTests();
        Assert.Equal(-1, w.ReligionOfSettlement(set));
        Assert.False(w.BuildingKinds.IsUnlocked(set, "shrine"));

        // Concept beliefs count too ("Great stones make the gods remember us." is masonry's belief effect).
        MythTests.Believe(w, set, 1f, "Great stones make the gods remember us.");
        w.RunMythDayForTests();
        int r = w.ReligionOfSettlement(set);
        Assert.True(r >= 0);
        Assert.Equal(GodStance.Favourable, w.Religions.Stance[r]); // no god acts yet: a gentle faith
        Assert.StartsWith("the Way of ", w.Religions.Name[r]);
        Assert.True(w.Memories.LastGodBeliefTick(w.Npcs.TryGetSlot(w.Religions.Founder[r], out int f) ? f : -1) >= 0);
        Assert.True(w.BuildingKinds.IsUnlocked(set, "shrine"));
        var e = Assert.Single(Villages.Chronicle(w, "religion"));
        Assert.Contains(w.Religions.Name[r], e.Text);
    }

    [Fact]
    public void Non_god_beliefs_do_not_count()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        MythTests.Believe(w, set, 1f, "Walls keep our children safe.");
        w.RunMythDayForTests();
        Assert.Equal(-1, w.ReligionOfSettlement(set));
    }

    [Fact]
    public void Stance_at_founding_follows_favour_and_awe()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        w.Standing.Favour[set] = -0.6f;
        w.Standing.Awe[set] = 0.8f;
        MythTests.Believe(w, set, 1f);
        w.RunMythDayForTests();
        int r = w.ReligionOfSettlement(set);
        Assert.Equal(GodStance.Fearful, w.Religions.Stance[r]);
        Assert.StartsWith("the Dread of ", w.Religions.Name[r]);
        Assert.Contains("fear the sky", Villages.Chronicle(w, "religion").Single().Text);
    }

    [Fact]
    public void God_memories_and_awe_turn_into_beliefs_without_any_llm()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        w.Standing.Favour[set] = 0.8f;
        // Everyone in the village saw the god act.
        foreach (int s in Enumerable.Range(0, w.Npcs.HighWater).Where(s => w.Npcs.Alive[s] && w.SettlementOfSlot(s) == set))
            w.Memories.Add(s, new Memory(w.Tick, MemoryKind.God, [MemoryStore.GodId], "The god blessed us.", 8), w.Tick);
        for (int d = 0; d < 60 && w.ReligionOfSettlement(set) < 0; d++) w.RunMythDayForTests();
        Assert.True(w.TotalGodBeliefsFormed > 0);
        Assert.True(w.ReligionOfSettlement(set) >= 0);
        var belief = w.Memories.Of(w.Npcs.TryGetSlot(w.Religions.Founder[w.ReligionOfSettlement(set)], out int f) ? f : 0)
            .First(m => m.Kind == MemoryKind.Belief && GodStanding.MentionsGod(m.Text));
        Assert.Equal("The sky watches over us and is kind.", belief.Text);
        Assert.Equal(6, belief.Importance);
    }

    [Fact]
    public void A_religious_settlement_plans_a_shrine_and_the_chronicle_marks_it_when_finished()
    {
        var (w, x, z) = Villages.World(5, new SimConfig { ShrineMinMembers = 6 });
        int set = Villages.Found(w, x, z);
        MythTests.Believe(w, set, 1f);
        w.RunMythDayForTests();
        Assert.True(w.ReligionOfSettlement(set) >= 0);
        Assert.DoesNotContain(Enumerable.Range(0, w.Buildings.HighWater), b => w.Buildings.Alive[b] && w.Buildings.Kind[b] == BuildingKinds.Shrine);
        Establish(w, set); // a finished storehouse: now the village can spare builders for a shrine
        w.RunMythDayForTests();
        int shrine = Enumerable.Range(0, w.Buildings.HighWater).Single(b => w.Buildings.Alive[b] && w.Buildings.Kind[b] == BuildingKinds.Shrine);
        Assert.Equal(set, w.Buildings.Settlement[shrine]);
        Assert.True(w.Buildings.Progress[shrine] < 1f);
        w.Buildings.Progress[shrine] = 1f;
        w.RunMythDayForTests();
        Assert.Equal(1, w.TotalShrines);
        Assert.Contains(w.Religions.Name[0], Assert.Single(Villages.Chronicle(w, "shrine")).Text);
        w.RunMythDayForTests();
        Assert.Single(Villages.Chronicle(w, "shrine")); // announced once
    }

    [Fact]
    public void Prayer_is_drawn_to_the_shrine_fulfils_more_and_keeps_the_faith_alive()
    {
        var (w, x, z) = Villages.World(5, new SimConfig { ShrineMinMembers = 6 });
        int set = Villages.Found(w, x, z);
        Establish(w, set);
        int s = MythTests.Member(w, set);
        Assert.Equal(1f, w.PrayBoostForTests(s)); // no shrine: M1 scoring

        MythTests.Believe(w, set, 1f);
        w.RunMythDayForTests();
        int shrine = Enumerable.Range(0, w.Buildings.HighWater).Single(b => w.Buildings.Alive[b] && w.Buildings.Kind[b] == BuildingKinds.Shrine);
        w.Buildings.Progress[shrine] = 1f;
        w.RunMythDayForTests();
        Assert.Equal(w.Config.ShrinePrayBoost + w.Config.FaithPrayBonus * w.Trait(s, Gene.Piety), w.PrayBoostForTests(s), 4);

        // Starting a prayer walks to the shrine.
        w.Npcs.X[s] = w.Buildings.X[shrine] + 20f;
        w.Npcs.Z[s] = w.Buildings.Z[shrine];
        w.BeginPrayForTests(s);
        Assert.Equal(ActionPhase.Moving, w.Npcs.Phase[s]);

        // At the shrine: more purpose than M1's +0.35, the first prayer is chronicled, the belief is renewed.
        w.Npcs.X[s] = w.Buildings.X[shrine] + 1f;
        w.Npcs.Z[s] = w.Buildings.Z[shrine];
        w.Npcs.NeedRef(s, Need.Purpose) = 0.1f;
        w.Memories.Reset(s); // their belief has faded: this prayer renews it
        int beliefsBefore = w.TotalPrayerBeliefs;
        w.CompletePrayForTests(s);
        Assert.True(w.Npcs.Need(s, Need.Purpose) >= 0.1f + 0.35f + w.Config.ShrinePurpose - 1e-4f);
        Assert.Single(Villages.Chronicle(w, "prayer"));
        Assert.Equal(beliefsBefore + 1, w.TotalPrayerBeliefs);
        Assert.Equal(w.Tick, w.Memories.LastGodBeliefTick(s));
        w.CompletePrayForTests(s);
        Assert.Equal(beliefsBefore + 1, w.TotalPrayerBeliefs); // not every prayer: once per PrayerBeliefDays
        Assert.Single(Villages.Chronicle(w, "prayer"));
    }

    [Fact]
    public void A_religion_spreads_to_a_faction_in_contact()
    {
        var (w, x, z) = Villages.World(5);
        int a = Villages.Found(w, x, z);
        var (bx, bz) = Villages.SpotAway(w, x, z);
        int b = Villages.Found(w, bx, bz);
        Assert.NotEqual(w.Settlements.Faction[a], w.Settlements.Faction[b]);
        MythTests.Believe(w, a, 1f);
        w.RunMythDayForTests();
        int r = w.ReligionOfSettlement(a);
        Assert.True(r >= 0);
        for (int d = 0; d < 400 && w.ReligionOfSettlement(b) < 0; d++)
        {
            w.Standing.Touch(a, b, w.Tick);
            w.RunMythDayForTests();
        }
        Assert.Equal(r, w.ReligionOfSettlement(b));
        Assert.Contains(Villages.Chronicle(w, "religion"), e => e.Text.Contains("spread from"));
        Assert.True(w.BuildingKinds.IsUnlocked(b, "shrine"));
    }

    [Fact]
    public void Without_contact_a_religion_stays_home()
    {
        var (w, x, z) = Villages.World(5);
        int a = Villages.Found(w, x, z);
        var (bx, bz) = Villages.SpotAway(w, x, z);
        int b = Villages.Found(w, bx, bz);
        MythTests.Believe(w, a, 1f);
        for (int d = 0; d < 200; d++) w.RunMythDayForTests();
        Assert.True(w.ReligionOfSettlement(a) >= 0);
        Assert.Equal(-1, w.ReligionOfSettlement(b));
    }

    [Fact]
    public void A_schism_follows_when_favour_flips_for_a_generation()
    {
        var (w, x, z) = Villages.World(5, new SimConfig { SchismYears = 0.5f }); // a "generation" of 5 days
        int set = Villages.Found(w, x, z);
        w.Standing.Favour[set] = 0.7f;
        MythTests.Believe(w, set, 1f);
        w.RunMythDayForTests();
        int r = w.ReligionOfSettlement(set);
        Assert.Equal(GodStance.Favourable, w.Religions.Stance[r]);

        w.Standing.Favour[set] = -0.8f; // the sky turned
        for (int d = 0; d < 3; d++) w.RunMythDayForTests();
        Assert.Equal(0, w.TotalSchisms); // not yet a generation
        w.Standing.Favour[set] = 0.7f; // and back: the clock resets
        w.RunMythDayForTests();
        w.Standing.Favour[set] = -0.8f;
        for (int d = 0; d < 8 && w.TotalSchisms == 0; d++)
        {
            for (int t = 0; t < w.Config.TicksPerDay; t++) w.Step();
            w.Standing.Favour[set] = -0.8f;
        }
        Assert.Equal(1, w.TotalSchisms);
        int nr = w.ReligionOfSettlement(set);
        Assert.NotEqual(r, nr);
        Assert.Equal(r, w.Religions.Parent[nr]);
        Assert.NotEqual(GodStance.Favourable, w.Religions.Stance[nr]);
        Assert.Contains(w.Religions.Name[r], Assert.Single(Villages.Chronicle(w, "schism")).Text);
    }
}
