using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class TradeConflictTests
{
    /// <summary>Two villages 120-200 m apart on one landmass; each is its own faction.</summary>
    private static (World w, int a, int b) TwoVillages(SimConfig? config = null, int seed = 5)
    {
        var (w, x, z) = Villages.World(seed, config);
        int a = Villages.Found(w, x, z);
        var (bx, bz) = Villages.SpotAway(w, x, z);
        int b = Villages.Found(w, bx, bz);
        Assert.NotEqual(w.Settlements.Faction[a], w.Settlements.Faction[b]);
        return (w, a, b);
    }

    [Fact]
    public void Surplus_food_is_carried_to_a_neighbour_in_need_and_chronicled()
    {
        var (w, a, b) = TwoVillages();
        Villages.Day(w, () =>
        {
            w.Settlements.Food[a] = w.FoodCapacity(a);
            w.Settlements.Food[b] = 0f;
            w.Settlements.Wood[b] = 10f;
        });
        for (int d = 0; d < 4 && w.TotalTrades == 0; d++) Villages.Days(w, 1);
        Assert.True(w.TotalTrades >= 1, "no trade within four days");
        var e = Assert.Single(Villages.Chronicle(w, "trade"));
        Assert.Contains(w.Settlements.Name[b], e.Text);
        Assert.True(w.Settlements.Wood[a] > 0, "the buyer did not pay in wood");
    }

    [Fact]
    public void No_trade_without_a_surplus()
    {
        var (w, a, b) = TwoVillages();
        for (int d = 0; d < 3; d++) Villages.Day(w, () => w.Settlements.Food[a] = w.Settlements.Food[b] = 0f);
        Assert.Equal(0, w.TotalTrades);
    }

    [Fact]
    public void Outsiders_on_a_settlements_land_raise_its_grievance()
    {
        var (w, a, b) = TwoVillages();
        int outsider = Enumerable.Range(0, w.Npcs.HighWater).First(s => w.Npcs.Alive[s] && w.SettlementOfSlot(s) == b);
        w.Npcs.X[outsider] = w.Settlements.CenterX[a];
        w.Npcs.Z[outsider] = w.Settlements.CenterZ[a];
        float before = w.Settlements.Grievance(a, b);
        w.RunSocietyDayForTests();
        Assert.True(w.Settlements.Grievance(a, b) > before);
        Assert.Equal(0f, w.Settlements.Grievance(b, a));
    }

    [Fact]
    public void Raid_pressure_grows_with_grievance()
    {
        var (w, a, b) = TwoVillages();
        float p0 = w.RaidPressure(a, b);
        w.Settlements.GrievanceRef(a, b) = 2f;
        Assert.Equal(p0 + 2f, w.RaidPressure(a, b), 4);
    }

    [Fact]
    public void A_grievance_past_the_threshold_sends_raiders_who_take_food_and_are_chronicled()
    {
        var (w, a, b) = TwoVillages();
        Villages.Day(w, () => w.Settlements.GrievanceRef(a, b) = 4f);
        for (int d = 0; d < 4 && w.TotalRaids == 0; d++) Villages.Days(w, 1);
        Assert.True(w.TotalRaids >= 1, "no raid within four days");
        var e = Assert.Single(Villages.Chronicle(w, "raid"));
        Assert.Contains(w.Settlements.Name[a], e.Text);
        Assert.True(w.Settlements.Grievance(b, a) > 0.5f, "the victims bear no grudge");
    }

    [Fact]
    public void Settlements_of_one_faction_never_raid_each_other()
    {
        var (w, a, b) = TwoVillages(new SimConfig { RaidThreshold = 0f });
        w.Settlements.Faction[b] = w.Settlements.Faction[a];
        w.Settlements.GrievanceRef(a, b) = 4f;
        w.Settlements.GrievanceRef(b, a) = 4f;
        Villages.Days(w, 3);
        Assert.Equal(0, w.TotalRaids);
    }

    [Fact]
    public void Leaders_who_are_friends_ally_their_factions()
    {
        var (w, a, b) = TwoVillages();
        w.Relationships.Adjust(w.Settlements.Leader[a], w.Settlements.Leader[b], 0.9f, 0.5f);
        w.RunSocietyDayForTests();
        Assert.Equal(w.Settlements.Faction[a], w.Settlements.Faction[b]);
        Assert.Single(Villages.Chronicle(w, "alliance"));
    }
}
