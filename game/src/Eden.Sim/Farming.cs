using Eden.Sim.Contracts;

namespace Eden.Sim;

public enum SeasonKind : byte { Spring, Summer, Autumn, Winter }

/// <summary>
/// Farm plots: planted in spring and summer, grow daily with soil fertility, weather and tending, ripen,
/// and are harvested into the farmer's hands and the settlement's stock. Ripe crops left too long rot.
/// </summary>
public sealed partial class World
{
    public SeasonKind Season(int dayOfYear)
    {
        float f = dayOfYear / (float)Config.DaysPerYear;
        return f < 0.3f ? SeasonKind.Spring : f < 0.6f ? SeasonKind.Summer : f < 0.8f ? SeasonKind.Autumn : SeasonKind.Winter;
    }

    private long DayIndex => Tick / Config.TicksPerDay;

    /// <summary>
    /// Multiplier on derived goods an NPC slot produces (resource id, e.g. "grain"). Defaults to 1; M4 points it at
    /// <c>EffectApplier.YieldFactor</c>. Called on harvest only, never per tick.
    /// </summary>
    public Func<int, string, float> YieldFactor { get; set; } = static (_, _) => 1f;

    private bool CanPlant => Season(Clock.DayOfYear) is SeasonKind.Spring or SeasonKind.Summer;

    /// <summary>Daily crop growth for every growing plot; ripe crops rot after a few days.</summary>
    private void GrowCrops()
    {
        var b = Buildings;
        var season = Season(Clock.DayOfYear);
        float weather = Weather switch { WeatherKind.Rain => 1.2f, WeatherKind.Drought => 0.35f, _ => 1f };
        float seasonal = season == SeasonKind.Winter ? 0f : season == SeasonKind.Autumn ? 0.6f : 1f;
        long today = DayIndex;
        for (int i = 0; i < b.HighWater; i++)
        {
            if (!b.Alive[i] || b.Kind[i] != BuildingKinds.FarmPlot || b.Progress[i] < 1f) continue;
            if (b.Crop[i] == CropStage.Ripe)
            {
                if (today - b.RipeDay[i] > 3) { b.Crop[i] = CropStage.Fallow; b.Growth[i] = 0; }
                continue;
            }
            if (b.Crop[i] != CropStage.Growing) continue;
            float fert = Terrain.Fertility[Terrain.CellIndex(b.X[i], b.Z[i])];
            bool tended = b.TendedDay[i] >= today - 1;
            b.Growth[i] += Config.CropGrowthPerDay * (0.6f + fert) * weather * seasonal * (tended ? 1.3f : 1f);
            if (b.Growth[i] >= 1f) { b.Growth[i] = 1f; b.Crop[i] = CropStage.Ripe; b.RipeDay[i] = today; }
        }
    }

    /// <summary>True if the plot has something for a farmer to do today.</summary>
    private bool PlotNeedsWork(int i)
    {
        var b = Buildings;
        if (!b.Alive[i] || b.Kind[i] != BuildingKinds.FarmPlot || b.Progress[i] < 1f) return false;
        return b.Crop[i] switch
        {
            CropStage.Fallow => CanPlant,
            CropStage.Growing => b.TendedDay[i] < DayIndex,
            _ => true,
        };
    }

    /// <summary>Nearest plot of the settlement needing work (ripe first), or -1.</summary>
    private int FindFarmWork(int set, float x, float z)
    {
        var b = Buildings;
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < b.HighWater; i++)
        {
            if (!b.Alive[i] || b.Settlement[i] != set || !PlotNeedsWork(i)) continue;
            float d = Sq(b.X[i] - x) + Sq(b.Z[i] - z);
            if (b.Crop[i] == CropStage.Ripe) d *= 0.25f;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private void WorkPlot(int s, int plot)
    {
        var b = Buildings;
        var n = Npcs;
        if (plot < 0 || !PlotNeedsWork(plot)) return;
        n.NeedRef(s, Need.Purpose) = Math.Min(1f, n.Need(s, Need.Purpose) + 0.2f);
        switch (b.Crop[plot])
        {
            case CropStage.Fallow:
                b.Crop[plot] = CropStage.Growing;
                b.Growth[plot] = 0f;
                b.TendedDay[plot] = DayIndex;
                break;
            case CropStage.Growing:
                b.TendedDay[plot] = DayIndex;
                b.Growth[plot] = Math.Min(0.99f, b.Growth[plot] + 0.05f);
                break;
            case CropStage.Ripe:
            {
                float fert = Terrain.Fertility[Terrain.CellIndex(b.X[plot], b.Z[plot])];
                float yield = Config.FarmYield * (0.6f + fert) / 1.2f * (0.9f + 0.2f * n.GatherSkill[s]) * YieldFactor(s, "grain");
                float carry = Math.Min(yield, Config.MaxCarriedFood - n.Food[s]);
                n.Food[s] += carry;
                int set = b.Settlement[plot];
                if (Settlements.IsAlive(set)) AddFood(set, yield - carry);
                n.GatherSkill[s] += 0.02f * (1f - n.GatherSkill[s]);
                b.Crop[plot] = CropStage.Fallow;
                b.Growth[plot] = 0f;
                TotalHarvested += yield;
                break;
            }
        }
    }
}
