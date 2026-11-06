using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class NeedsTests
{
    /// <summary>One adult on a barren island (water but no food), no random deaths.</summary>
    private static (World world, int id) Castaway(int seed = 5)
    {
        var w = new World(new SimConfig { Seed = seed, StartingNpcs = 1, ResourceDensity = 0, AdultDeathPerYear = 0 });
        return (w, w.Npcs.IdOf(0));
    }

    [Fact]
    public void Npc_with_no_food_dies_within_the_starvation_window()
    {
        var (w, id) = Castaway();
        w.Npcs.Food[0] = 0;
        w.Npcs.NeedRef(0, Need.Food) = 0f;
        int window = w.Config.StarvationTicks;

        for (int t = 0; t < window - 5; t++) w.Step();
        Assert.True(w.Npcs.IsAlive(id), "died before the starvation window ended");

        for (int t = 0; t < 10; t++) w.Step();
        Assert.False(w.Npcs.IsAlive(id), "still alive after the starvation window");
        Assert.Contains(w.ChronicleTail(5), e => e.Kind == "death" && e.Text.Contains("starved"));
    }

    [Fact]
    public void Food_need_decays_to_zero_in_about_two_days()
    {
        var (w, _) = Castaway();
        w.Npcs.Food[0] = 0;
        float decayPerDay = w.Config.FoodDecayPerDay;
        for (int t = 0; t < w.Config.TicksPerDay; t++) w.Step();
        Assert.InRange(w.Npcs.Need(0, Need.Food), 1f - decayPerDay - 0.05f, 1f - decayPerDay * 0.6f + 0.01f);
    }

    [Fact]
    public void Eating_resets_food()
    {
        var (w, id) = Castaway();
        w.Npcs.Food[0] = 4;
        w.Npcs.NeedRef(0, Need.Food) = 0.05f;
        w.Npcs.NeedRef(0, Need.Water) = 1f;
        w.Npcs.Phase[0] = ActionPhase.Done;
        for (int t = 0; t < 20; t++) w.Step();
        Assert.True(w.Npcs.IsAlive(id));
        Assert.True(w.Npcs.Need(0, Need.Food) > 0.8f, $"food need {w.Npcs.Need(0, Need.Food)} after eating");
        Assert.True(w.Npcs.Food[0] < 4, "carried food was not consumed");
    }

    [Fact]
    public void Thirsty_npc_walks_to_water_and_drinks()
    {
        var (w, id) = Castaway();
        w.Npcs.NeedRef(0, Need.Water) = 0.05f;
        w.Npcs.Phase[0] = ActionPhase.Done;
        for (int t = 0; t < w.Config.TicksPerDay / 2; t++) w.Step();
        Assert.True(w.Npcs.IsAlive(id));
        Assert.True(w.Npcs.Need(0, Need.Water) > 0.7f, $"water need {w.Npcs.Need(0, Need.Water)}");
    }

    [Fact]
    public void Needs_stay_in_unit_range_over_a_year()
    {
        var w = new World(new SimConfig { Seed = 8 });
        for (int t = 0; t < w.Config.TicksPerYear; t++)
        {
            w.Step();
            if (t % 97 != 0) continue;
            for (int s = 0; s < w.Npcs.HighWater; s++)
            {
                if (!w.Npcs.Alive[s]) continue;
                for (int n = 0; n < Needs.Count; n++) Assert.InRange(w.Npcs.Needs[s * Needs.Count + n], 0f, 1f);
            }
        }
    }
}
