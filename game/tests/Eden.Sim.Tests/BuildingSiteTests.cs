using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

/// <summary>M8: building sites prefer flat ground, and a finished building levels the ground under it a little.</summary>
public class BuildingSiteTests
{
    /// <summary>A 32×32 ramp rising 0.5 m per 2 m cell along x, all land.</summary>
    private static Terrain Ramp()
    {
        var t = new Terrain(32, 32, 2f, 0f);
        for (int z = 0; z < 32; z++)
        for (int x = 0; x < 32; x++)
        {
            t.Heights[z * 32 + x] = 2f + 0.5f * x;
            t.Moisture[z * 32 + x] = 0.3f;
        }
        t.RecomputeDerived();
        return t;
    }

    [Fact]
    public void Slope_under_a_footprint_is_rise_over_run()
    {
        var t = Ramp();
        // Radius 4 m reaches cells 2 either side: 2 m of rise over an 8 m diameter.
        Assert.Equal(0.25f, t.SlopeUnder(30f, 30f, 4f), 3);
        var flat = new Terrain(8, 8, 2f, 0f);
        Array.Fill(flat.Heights, 3f);
        Assert.Equal(0f, flat.SlopeUnder(6f, 6f, 3f));
    }

    [Fact]
    public void Flattening_halves_the_slope_under_the_footprint_and_leaves_the_rest_alone()
    {
        var t = Ramp();
        var before = (float[])t.Heights.Clone();
        float slope = t.SlopeUnder(30f, 30f, 4f);
        Assert.True(t.FlattenDisc(30f, 30f, 4f, 0.5f));
        Assert.Equal(slope * 0.5f, t.SlopeUnder(30f, 30f, 4f), 3);
        for (int cell = 0; cell < t.Heights.Length; cell++)
        {
            float dx = t.CellX(cell) - 30f, dz = t.CellZ(cell) - 30f;
            if (MathF.Sqrt(dx * dx + dz * dz) > 6f) Assert.Equal(before[cell], t.Heights[cell]);
            Assert.True(t.IsLand(cell));
        }
        // Deterministic: the same call on the same ground gives the same heights.
        var again = Ramp();
        again.FlattenDisc(30f, 30f, 4f, 0.5f);
        Assert.Equal(t.Heights, again.Heights);
    }

    [Fact]
    public void Flattening_never_turns_land_into_sea()
    {
        var t = new Terrain(16, 16, 2f, 0f);
        for (int i = 0; i < t.Heights.Length; i++) t.Heights[i] = i % 16 < 8 ? -2f : 0.2f;
        t.RecomputeDerived();
        t.FlattenDisc(16f, 16f, 6f, 1f);
        for (int i = 0; i < t.Heights.Length; i++)
            Assert.Equal(i % 16 < 8, t.IsWater(i));
    }

    [Fact]
    public void Settlement_sites_are_flatter_than_the_ground_around_them_and_get_finished()
    {
        var (w, x, z) = Villages.World(5);
        Villages.Found(w, x, z);
        Villages.Days(w, 12);
        var b = w.Buildings;
        var t = w.Terrain;
        double siteSlope = 0, ringSlope = 0;
        int sites = 0, ring = 0, finished = 0;
        for (int i = 0; i < b.HighWater; i++)
        {
            if (!b.Alive[i]) continue;
            float foot = BuildingKinds.All[b.Kind[i]].Footprint;
            siteSlope += t.SlopeUnder(b.X[i], b.Z[i], foot);
            sites++;
            if (b.Progress[i] >= 1f) finished++;
            // The ground the picker could have chosen: land spots 6-24 m around the site.
            for (int a = 0; a < 12; a++)
            for (float r = 6f; r <= 24f; r += 6f)
            {
                float px = b.X[i] + MathF.Cos(a * MathF.Tau / 12) * r, pz = b.Z[i] + MathF.Sin(a * MathF.Tau / 12) * r;
                int c = t.CellIndex(px, pz);
                if (!t.IsLand(c) || t.MoveCost[c] >= 2f) continue;
                ringSlope += t.SlopeUnder(px, pz, foot);
                ring++;
            }
        }
        Assert.True(sites > 0, "no building sites were planned");
        Assert.True(finished > 0, "no building was finished in 12 days");
        Assert.True(siteSlope / sites < ringSlope / ring, $"sites {siteSlope / sites:F3} vs surroundings {ringSlope / ring:F3}");
    }
}
