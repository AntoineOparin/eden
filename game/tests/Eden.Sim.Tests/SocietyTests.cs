using Eden.Sim.Contracts;
using Xunit.Abstractions;

namespace Eden.Sim.Tests;

/// <summary>Builds villages out of god-spawned strangers so society tests do not have to wait for generations.</summary>
internal static class Villages
{
    /// <summary>A 2-person world and an inland spot near the genesis pair with room for a village.</summary>
    public static (World w, float x, float z) World(int seed, SimConfig? config = null)
    {
        // Mechanics tests exercise society without the M4 knowledge gate; the Slow acceptance test keeps the gate on.
        var w = new World((config ?? new SimConfig()) with { Seed = seed, KnowledgeGatesActions = false });
        var (x, z) = SpotAway(w, w.Npcs.X[0], w.Npcs.Z[0], 0f, 80f);
        return (w, x, z);
    }

    /// <summary>
    /// Two groups of four adults 20 m apart: two households in one cluster, so the next daily update founds a
    /// settlement there. Returns that settlement's id after running the update.
    /// </summary>
    public static int Found(World w, float x, float z)
    {
        int before = w.Settlements.Count;
        w.Apply(new SpawnCommand("human", x, z, 4));
        w.Apply(new SpawnCommand("human", x + 20f, z, 4));
        w.RunSocietyDayForTests();
        Assert.True(w.Settlements.Count > before, "no settlement was founded");
        return w.Settlements.Count - 1;
    }

    /// <summary>A buildable spot <paramref name="min"/>-<paramref name="max"/> m from (x, z) on the same landmass with room for a 20 m village.</summary>
    public static (float x, float z) SpotAway(World w, float x, float z, float min = 120f, float max = 200f)
    {
        var t = w.Terrain;
        for (float r = min; r <= max; r += 10f)
        for (int a = 0; a < 36; a++)
        {
            float px = x + MathF.Cos(a * MathF.Tau / 36) * r, pz = z + MathF.Sin(a * MathF.Tau / 36) * r;
            if (Ok(px, pz) && Ok(px + 20f, pz) && Ok(px + 10f, pz) && w.Pathing.SameRegion(x, z, px, pz)) return (px, pz);
        }
        throw new InvalidOperationException("no spot found");

        bool Ok(float px, float pz)
        {
            if (px < 4 || pz < 4 || px > t.MaxX - 4 || pz > t.MaxZ - 4) return false;
            int c = t.CellIndex(px, pz);
            return t.IsLand(c) && t.MoveCost[c] < 2f && t.Heights[c] > 1f;
        }
    }

    public static IEnumerable<ChronicleEntry> Chronicle(World w, string kind) => w.ChronicleTail(512).Where(e => e.Kind == kind);

    public static void Days(World w, int days)
    {
        for (int i = 0; i < days * w.Config.TicksPerDay; i++) w.Step();
    }

    /// <summary>Run to the next day boundary, calling <paramref name="beforeUpdate"/> just before the daily update.</summary>
    public static void Day(World w, Action beforeUpdate)
    {
        while ((w.Tick + 1) % w.Config.TicksPerDay != 0) w.Step();
        beforeUpdate();
        w.Step();
    }
}

public class SocietyTests(ITestOutputHelper output)
{
    [Fact]
    public void Genesis_pair_shares_one_household_and_children_join_their_mother()
    {
        var (w, _, _) = Villages.World(5);
        int h = w.HouseholdOfSlot(0);
        Assert.True(h >= 0);
        Assert.Equal(h, w.HouseholdOfSlot(1));
        Assert.Equal(1, w.Households.Count);

        // A newborn joins its mother's household on creation.
        w.Npcs.Partner[0] = w.Npcs.IdOf(1);
        w.Npcs.Partner[1] = w.Npcs.IdOf(0);
        int mother = w.Npcs.Sex[0] == Sex.Female ? 0 : 1;
        w.Npcs.PregnantUntil[mother] = w.Tick + 1;
        w.Npcs.UnbornFather[mother] = w.Npcs.IdOf(1 - mother);
        w.Step();
        w.Step();
        int child = Enumerable.Range(0, w.Npcs.HighWater).First(s => w.Npcs.Alive[s] && w.Npcs.Mother[s] == w.Npcs.IdOf(mother));
        Assert.Equal(w.HouseholdOfSlot(mother), w.HouseholdOfSlot(child));
    }

    [Fact]
    public void A_couple_living_with_parents_founds_its_own_household_nearby()
    {
        var (w, x, z) = Villages.World(5);
        w.Apply(new SpawnCommand("human", x, z, 3));
        int head = w.Npcs.HighWater - 3, a = w.Npcs.HighWater - 2, b = w.Npcs.HighWater - 1;
        Assert.Equal(w.HouseholdOfSlot(head), w.HouseholdOfSlot(a)); // lodgers in the first stranger's household
        Assert.Equal(w.HouseholdOfSlot(head), w.HouseholdOfSlot(b));
        w.Npcs.Partner[a] = w.Npcs.IdOf(b);
        w.Npcs.Partner[b] = w.Npcs.IdOf(a);
        w.RunSocietyDayForTests();

        int ha = w.HouseholdOfSlot(a), h0 = w.HouseholdOfSlot(head);
        Assert.Equal(ha, w.HouseholdOfSlot(b));
        Assert.NotEqual(h0, ha);
        Assert.True(w.Households.IsHead(ha, w.Npcs.IdOf(a)) && w.Households.IsHead(ha, w.Npcs.IdOf(b)));
        float d = MathF.Sqrt(MathF.Pow(w.Households.HomeX[ha] - w.Households.HomeX[h0], 2) + MathF.Pow(w.Households.HomeZ[ha] - w.Households.HomeZ[h0], 2));
        Assert.InRange(d, 0f, 20f);
    }

    [Fact]
    public void Clustered_households_found_a_settlement_with_a_leader_faction_and_chronicle()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        Assert.True(w.Settlements.IsAlive(set));
        Assert.True(w.Settlements.Members[set] >= 8);
        Assert.True(w.Factions.IsAlive(w.Settlements.Faction[set]));
        Assert.True(w.Npcs.IsAlive(w.Settlements.Leader[set]));
        Assert.Single(Villages.Chronicle(w, "settlement"));
        Assert.Single(Villages.Chronicle(w, "leader"));

        // The snapshot tags members with their settlement.
        var snap = w.Snapshot();
        Assert.Contains(snap.Npcs.ToArray(), v => v.SettlementId == set);
    }

    [Fact]
    public void Leader_is_the_member_with_the_highest_weighted_degree()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        var members = Enumerable.Range(0, w.Npcs.HighWater).Where(s => w.Npcs.Alive[s] && w.SettlementOfSlot(s) == set).ToArray();
        int star = members.First(s => w.Npcs.IdOf(s) != w.Settlements.Leader[set]);
        foreach (int o in members)
            if (o != star) w.Relationships.Adjust(w.Npcs.IdOf(star), w.Npcs.IdOf(o), 0.8f, 0.8f);
        // The seat is reviewed every few days.
        for (int d = 0; d < 6; d++) w.RunSocietyDayForTests();
        Assert.Equal(w.Npcs.IdOf(star), w.Settlements.Leader[set]);
        Assert.Contains(Villages.Chronicle(w, "leader"), e => e.Text.Contains("replaced"));
    }

    [Fact]
    public void Settlements_plan_and_finish_buildings_that_show_up_in_the_snapshot()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        w.Settlements.Wood[set] = 40f;
        Villages.Days(w, 8);
        var views = w.Snapshot().Buildings.ToArray();
        Assert.NotEmpty(views);
        var known = BuildingKinds.All.Select(k => k.Id).ToHashSet();
        Assert.All(views, v => Assert.Contains(v.Kind, known));
        Assert.All(views, v => Assert.InRange(v.Progress, 0f, 1f));
        Assert.Contains(views, v => v.Kind == "storehouse");
        Assert.True(w.TotalBuildingsCompleted >= 1, "nothing was finished in 8 days");
        Assert.Contains(views, v => v.Progress >= 1f && v.SettlementId == set);
    }

    [Fact]
    public void Building_kind_registry_has_the_shared_ids_and_unlocks()
    {
        Assert.Equal(["hut", "storehouse", "farm-plot", "well", "kiln", "workshop", "shrine", "archive", "wall", "dock"],
            BuildingKinds.All.Select(k => k.Id).ToArray());
        Assert.Equal(["hut", "storehouse", "farm-plot", "well"],
            BuildingKinds.All.Where(k => k.ConstructibleByDefault).Select(k => k.Id).ToArray());

        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        Assert.True(w.BuildingKinds.IsUnlocked(set, "hut"));
        Assert.False(w.BuildingKinds.IsUnlocked(set, "kiln"));
        Assert.False(w.BuildingKinds.TryUnlock(set, "no-such-building"));
        Assert.False(w.BuildingKinds.TryUnlock(12345, "kiln"));
        Assert.True(w.BuildingKinds.TryUnlock(set, "kiln"));
        Assert.True(w.BuildingKinds.IsUnlocked(set, "kiln"));

        // Unlocking through an NPC reaches their settlement.
        int member = Enumerable.Range(0, w.Npcs.HighWater).First(s => w.Npcs.Alive[s] && w.SettlementOfSlot(s) == set);
        Assert.True(w.BuildingKinds.TryUnlock(w.Npcs.IdOf(member), "dock"));
        Assert.True(w.BuildingKinds.IsUnlocked(set, "dock"));
    }

    [Fact]
    public void Destroyed_buildings_raise_an_event_and_their_ids_are_never_reused()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        int first = w.Buildings.Add(BuildingKinds.Well, x + 5, z, 0f, set, -1);
        int destroyed = -1;
        w.BuildingDestroyed += id => destroyed = id;
        w.DestroyBuilding(first);
        Assert.Equal(first, destroyed);
        Assert.False(w.Buildings.Alive[first]);
        int next = w.Buildings.Add(BuildingKinds.Well, x + 5, z, 0f, set, -1);
        Assert.NotEqual(first, next);
        Assert.DoesNotContain(w.Snapshot().Buildings.ToArray(), v => v.Id == first);
    }

    [Fact]
    public void M3_actions_unlock_for_settlement_members_only()
    {
        var (w, x, z) = Villages.World(5);
        Assert.False(w.IsActionUnlocked(0, ActionKind.Build));
        Assert.True(w.IsActionUnlocked(0, ActionKind.Settle));
        int set = Villages.Found(w, x, z);
        int member = Enumerable.Range(0, w.Npcs.HighWater).First(s => w.Npcs.Alive[s] && w.SettlementOfSlot(s) == set);
        foreach (var a in new[] { ActionKind.Build, ActionKind.Farm, ActionKind.Trade, ActionKind.Raid })
            Assert.True(w.IsActionUnlocked(member, a));
    }

    [Fact]
    public void Grown_children_blend_genes_with_their_parents_traits()
    {
        var (w, _, _) = Villages.World(8, new SimConfig { AdultAgeYears = 1 });
        int mother = w.Npcs.Sex[0] == Sex.Female ? 0 : 1, father = 1 - mother;
        w.Npcs.Partner[0] = w.Npcs.IdOf(1);
        w.Npcs.Partner[1] = w.Npcs.IdOf(0);
        w.Npcs.PregnantUntil[mother] = w.Tick + 1;
        w.Npcs.UnbornFather[mother] = w.Npcs.IdOf(father);
        w.Npcs.UnbornOf(mother).Fill(0f); // child genes 0, parents' traits are their random genes
        w.Step();
        w.Step();
        int child = Enumerable.Range(0, w.Npcs.HighWater).First(s => w.Npcs.Alive[s] && w.Npcs.Mother[s] == w.Npcs.IdOf(mother));
        Assert.Equal(0f, w.Trait(child, Gene.Curiosity)); // a child is still its genes

        // Age the child past adulthood without simulating a year.
        w.Npcs.BirthTick[child] -= 4L * w.Config.TicksPerYear; // infants are infants until 3
        w.RunSocietyDayForTests();
        float expected = 0.3f * 0.5f * (w.Trait(mother, Gene.Curiosity) + w.Trait(father, Gene.Curiosity));
        Assert.Equal(expected, w.Trait(child, Gene.Curiosity), 4);
        Assert.Equal(0f, w.Trait(child, Gene.Strength)); // physical genes are not taught
    }

    [Fact]
    public void Save_and_load_round_trip_society_state()
    {
        var (w, x, z) = Villages.World(5);
        int a = Villages.Found(w, x, z);
        var (bx, bz) = Villages.SpotAway(w, x, z);
        int b = Villages.Found(w, bx, bz);
        w.Settlements.Wood[a] = 30f;
        w.Settlements.Food[a] = 20f;
        w.BuildingKinds.Unlock(b, "wall");
        Villages.Days(w, 5);
        Assert.True(w.Buildings.Count > 0);

        using var ms = new MemoryStream();
        SaveWriter.Write(w, ms);
        ms.Position = 0;
        var c = SaveReader.Load(ms);
        Assert.Equal(w.StateHash(), c.StateHash());
        Assert.Equal(w.SettlementCount, c.SettlementCount);
        Assert.Equal(w.Buildings.Count, c.Buildings.Count);
        Assert.True(c.BuildingKinds.IsUnlocked(b, "wall"));

        Villages.Days(w, 4);
        Villages.Days(c, 4);
        Assert.Equal(w.StateHash(), c.StateHash());
    }

    [Fact]
    public void Society_state_is_deterministic()
    {
        ulong Run()
        {
            var (w, x, z) = Villages.World(11);
            Villages.Found(w, x, z);
            var (bx, bz) = Villages.SpotAway(w, x, z);
            Villages.Found(w, bx, bz);
            Villages.Days(w, 6);
            return w.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    /// <summary>
    /// M3 acceptance (docs/specs/10-sim-core.md): from a 2-person start, within 20 generations, two or more
    /// settlements emerge on at least half the seeds, and a trade and a raid reach the Chronicle on at least half.
    /// Runs each seed until all three have happened, it dies out, or its deepest lineage passes 20 generations.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void Two_settlements_trade_and_raid_emerge_from_two_people_on_most_seeds()
    {
        var results = new (int maxSettlements, int trades, int raids, int year, int gen, int pop)[20];
        Parallel.For(1, 21, seed =>
        {
            var w = new World(new SimConfig { Seed = seed });
            int maxSet = 0, year, trades = 0, raids = 0;
            // Horizon: 20 generations (the spec's), with a 600-year guard against a stalled lineage.
            for (year = 1; year <= 600; year++)
            {
                for (int t = 0; t < w.Config.TicksPerYear; t++) w.Step();
                maxSet = Math.Max(maxSet, w.SettlementCount);
                foreach (var e in w.Snapshot().NewChronicle)
                {
                    if (e.Kind == "trade") trades++;
                    else if (e.Kind == "raid") raids++;
                }
                if (w.Population == 0 || w.MaxLineage > 20) break;
                if (maxSet >= 2 && trades > 0 && raids > 0) break;
            }
            // Every counted trade/raid wrote exactly one Chronicle entry of that kind.
            Assert.Equal(w.TotalTrades, trades);
            Assert.Equal(w.TotalRaids, raids);
            results[seed - 1] = (maxSet, trades, raids, Math.Min(year, 600), w.MaxLineage, w.Population);
        });
        for (int i = 0; i < results.Length; i++)
        {
            var r = results[i];
            output.WriteLine($"seed {i + 1}: settlements {r.maxSettlements}, trades {r.trades}, raids {r.raids}, stopped year {r.year}, gen {r.gen}, pop {r.pop}");
        }
        Assert.True(results.Count(r => r.maxSettlements >= 2) >= 10, "two settlements on fewer than half the seeds");
        Assert.True(results.Count(r => r.trades > 0) >= 10, "a trade on fewer than half the seeds");
        Assert.True(results.Count(r => r.raids > 0) >= 10, "a raid on fewer than half the seeds");
    }
}
