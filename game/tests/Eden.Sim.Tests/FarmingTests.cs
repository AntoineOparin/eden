namespace Eden.Sim.Tests;

public class FarmingTests
{
    [Fact]
    public void Seasons_follow_the_day_of_year()
    {
        var w = new World(new SimConfig { Seed = 1, StartingNpcs = 0, KnowledgeGatesActions = false }); // farming mechanics only, no knowledge gate
        var seasons = Enumerable.Range(0, 10).Select(w.Season).ToArray();
        Assert.Equal(
            [SeasonKind.Spring, SeasonKind.Spring, SeasonKind.Spring, SeasonKind.Summer, SeasonKind.Summer, SeasonKind.Summer,
             SeasonKind.Autumn, SeasonKind.Autumn, SeasonKind.Winter, SeasonKind.Winter], seasons);
    }

    private static (World w, int set, int plot) VillageWithPlot(int seed = 5)
    {
        var (w, x, z) = Villages.World(seed);
        int set = Villages.Found(w, x, z);
        // A finished plot on the best soil near the village.
        float px = x, pz = z, best = -1;
        for (int dx = -12; dx <= 12; dx += 3)
        for (int dz = -12; dz <= 12; dz += 3)
        {
            int c = w.Terrain.CellIndex(x + 10 + dx, z + dz);
            if (w.Terrain.IsLand(c) && w.Terrain.Fertility[c] > best) { best = w.Terrain.Fertility[c]; px = x + 10 + dx; pz = z + dz; }
        }
        int plot = w.Buildings.Add(BuildingKinds.FarmPlot, px, pz, 0f, set, -1);
        w.Buildings.Progress[plot] = 1f;
        return (w, set, plot);
    }

    [Fact]
    public void Crops_grow_in_spring_ripen_and_stand_still_in_winter()
    {
        var (w, _, plot) = VillageWithPlot();
        var b = w.Buildings;
        b.Crop[plot] = CropStage.Growing;
        b.Growth[plot] = 0f;
        Assert.NotEqual(SeasonKind.Winter, w.Season(w.Clock.DayOfYear));
        for (int d = 0; d < 6 && b.Crop[plot] == CropStage.Growing; d++) w.RunSocietyDayForTests();
        Assert.Equal(CropStage.Ripe, b.Crop[plot]);

        // Jump to winter: growth stops.
        while (w.Season(w.Clock.DayOfYear) != SeasonKind.Winter) w.Step();
        b.Crop[plot] = CropStage.Growing;
        b.Growth[plot] = 0.2f;
        w.RunSocietyDayForTests();
        Assert.Equal(0.2f, b.Growth[plot]);
    }

    [Fact]
    public void A_farmer_harvests_a_ripe_plot_into_hands_and_the_shared_store()
    {
        var (w, set, plot) = VillageWithPlot();
        w.RunSocietyDayForTests(); // assigns a farmer now that a plot exists
        Assert.Contains(Enumerable.Range(0, w.Npcs.HighWater), s => w.Npcs.Alive[s] && w.JobOfSlot(s) == Job.Farmer);
        w.Buildings.Crop[plot] = CropStage.Ripe;
        w.Buildings.RipeDay[plot] = w.Tick / w.Config.TicksPerDay;
        float before = w.Settlements.Food[set];
        for (int t = 0; t < w.Config.TicksPerDay && w.TotalHarvested == 0; t++) w.Step();
        Assert.True(w.TotalHarvested > 10f, "nobody harvested the ripe plot within a day");
        Assert.NotEqual(CropStage.Ripe, w.Buildings.Crop[plot]);
        Assert.True(w.Settlements.Food[set] > before);
    }

    [Fact]
    public void A_storehouse_raises_capacity_and_slows_spoilage()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        float pile = w.FoodCapacity(set);
        w.Settlements.Food[set] = pile;
        w.RunSocietyDayForTests();
        float lostPiled = pile - w.Settlements.Food[set];

        int store = w.Buildings.Add(BuildingKinds.Storehouse, x + 6, z + 6, 0f, set, -1);
        w.Buildings.Progress[store] = 1f;
        w.RunSocietyDayForTests();
        Assert.Equal(pile + BuildingKinds.All[BuildingKinds.Storehouse].Capacity, w.FoodCapacity(set));
        w.Settlements.Food[set] = pile;
        w.RunSocietyDayForTests();
        float lostStored = pile - w.Settlements.Food[set];
        Assert.True(lostStored < lostPiled, $"stored food spoiled as fast as piled food ({lostStored} vs {lostPiled})");
    }
}
