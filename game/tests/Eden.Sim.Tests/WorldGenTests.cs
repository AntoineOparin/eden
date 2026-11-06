using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class WorldGenTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(19)]
    public void Island_has_land_in_the_middle_and_ocean_at_the_border(int seed)
    {
        var w = new World(new SimConfig { Seed = seed });
        var t = w.Terrain;
        int land = 0;
        for (int i = 0; i < t.Heights.Length; i++) if (t.IsLand(i)) land++;
        Assert.InRange(land, t.Heights.Length / 10, t.Heights.Length * 7 / 10);
        for (int x = 0; x < t.Width; x++)
        {
            Assert.Equal((byte)BiomeId.Ocean, t.Biomes[x]);
            Assert.Equal((byte)BiomeId.Ocean, t.Biomes[(t.Height - 1) * t.Width + x]);
        }
    }

    [Fact]
    public void Biome_bytes_follow_the_contract()
    {
        var w = new World(new SimConfig { Seed = 3 });
        var t = w.Terrain;
        var seen = new HashSet<BiomeId>();
        for (int i = 0; i < t.Heights.Length; i++)
        {
            var b = (BiomeId)t.Biomes[i];
            Assert.True(Enum.IsDefined(b), $"cell {i} has biome byte {t.Biomes[i]}");
            Assert.Equal(t.Heights[i] <= w.Config.SeaLevel, b == BiomeId.Ocean);
            seen.Add(b);
        }
        Assert.Contains(BiomeId.Beach, seen);
        Assert.Contains(BiomeId.Grassland, seen);
        Assert.Contains(BiomeId.Forest, seen);
    }

    [Fact]
    public void Grid_layout_is_row_major_with_cells_at_multiples_of_cell_size()
    {
        var w = new World(new SimConfig { Seed = 5 });
        var t = w.Terrain;
        int x = 37, z = 201;
        int index = z * t.Width + x;
        Assert.Equal(index, t.CellIndex(x * t.CellSize, z * t.CellSize));
        Assert.Equal(x * t.CellSize, t.CellX(index));
        Assert.Equal(z * t.CellSize, t.CellZ(index));
        // A point belongs to the nearest cell centre.
        Assert.Equal(index, t.CellIndex(x * t.CellSize + 0.9f, z * t.CellSize - 0.9f));
    }

    [Fact]
    public void Generation_is_deterministic_and_seed_dependent()
    {
        var a = new World(new SimConfig { Seed = 42 });
        var b = new World(new SimConfig { Seed = 42 });
        var c = new World(new SimConfig { Seed = 43 });
        Assert.Equal(a.Terrain.Heights, b.Terrain.Heights);
        Assert.Equal(a.Resources.Count, b.Resources.Count);
        Assert.NotEqual(a.Terrain.Heights, c.Terrain.Heights);
    }

    [Fact]
    public void Resources_and_start_are_on_land_with_food_nearby()
    {
        var w = new World(new SimConfig { Seed = 11 });
        var t = w.Terrain;
        Assert.True(w.Resources.Count > 100);
        for (int i = 0; i < w.Resources.Count; i++)
            Assert.True(t.IsLand(t.CellIndex(w.Resources.X[i], w.Resources.Z[i])));
        for (int s = 0; s < w.Npcs.HighWater; s++)
        {
            Assert.True(t.IsWalkable(w.Npcs.X[s], w.Npcs.Z[s]));
            Assert.True(w.Resources.FindNearest(ResourceKind.Berry, w.Npcs.X[s], w.Npcs.Z[s], w.Config.SearchRadius) >= 0);
        }
    }

    [Fact]
    public void Starting_pair_is_a_woman_and_a_man()
    {
        var w = new World(new SimConfig { Seed = 2 });
        Assert.Equal(2, w.Population);
        Assert.Equal(Sex.Female, w.Npcs.Sex[0]);
        Assert.Equal(Sex.Male, w.Npcs.Sex[1]);
        Assert.Equal(LifeStage.Adult, w.StageOf(0));
    }
}
