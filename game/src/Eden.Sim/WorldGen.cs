using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>Deterministic island generation: fBm simplex heightmap with radial falloff, biomes, resources.</summary>
public static class WorldGen
{
    public const float HeightScale = 50f;

    public static (Terrain terrain, ResourceNodes resources) Generate(SimConfig config, ref Rng rng)
    {
        var terrain = new Terrain(config.WorldWidth, config.WorldHeight, config.CellSize, config.SeaLevel);
        var heightNoise = new SimplexNoise(ref rng);
        var moistNoise = new SimplexNoise(ref rng);
        float ox = rng.NextFloat() * 1000f, oz = rng.NextFloat() * 1000f;

        int w = config.WorldWidth, h = config.WorldHeight;
        float cx = w * 0.5f, cz = h * 0.5f, radius = Math.Min(w, h) * 0.5f;
        float freq = 4f / Math.Min(w, h);

        // Raise the base until at least ~10% of the map is land, so every seed gets an island.
        float baseLift = 0.3f;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int land = 0;
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                float e = heightNoise.Fbm(x * freq + ox, z * freq + oz, 5) * 0.5f + 0.5f;
                float dx = (x - cx) / radius, dz = (z - cz) / radius;
                float d2 = dx * dx + dz * dz;
                float v = e + baseLift - 1.1f * d2;
                terrain.Heights[i] = config.SeaLevel + (v - 0.5f) * HeightScale;
                terrain.Moisture[i] = moistNoise.Fbm(x * freq * 1.5f + oz, z * freq * 1.5f + ox, 3) * 0.5f + 0.5f;
                if (terrain.Heights[i] > config.SeaLevel) land++;
            }
            if (land >= w * h / 10) break;
            baseLift += 0.05f;
        }
        terrain.RecomputeDerived();

        var resources = new ResourceNodes(terrain);
        float density = config.ResourceDensity;
        if (density > 0)
        {
            for (int i = 0; i < w * h; i++)
            {
                var biome = (BiomeId)terrain.Biomes[i];
                float px = terrain.CellX(i), pz = terrain.CellZ(i);
                float roll = rng.NextFloat() / density;
                switch (biome)
                {
                    case BiomeId.Forest:
                        if (roll < 0.06f) resources.Add(ResourceKind.Tree, px, pz, 20f);
                        else if (roll < 0.075f) resources.Add(ResourceKind.Berry, px, pz, 12f);
                        else if (roll < 0.078f) resources.Add(ResourceKind.Game, px, pz, 6f);
                        break;
                    case BiomeId.Grassland:
                        if (roll < 0.010f * (0.5f + terrain.Moisture[i])) resources.Add(ResourceKind.Berry, px, pz, 12f);
                        else if (roll < 0.014f) resources.Add(ResourceKind.Tree, px, pz, 20f);
                        else if (roll < 0.0165f) resources.Add(ResourceKind.Game, px, pz, 6f);
                        break;
                    case BiomeId.Mountain:
                        if (roll < 0.04f) resources.Add(ResourceKind.Stone, px, pz, 40f);
                        else if (roll < 0.052f) resources.Add(ResourceKind.Ore, px, pz, 15f);
                        else if (roll < 0.055f) resources.Add(ResourceKind.Game, px, pz, 6f);
                        break;
                }
            }
        }
        return (terrain, resources);
    }

    /// <summary>Pick a starting spot: walkable lowland with the most food in reach and water nearby.</summary>
    public static (float x, float z) PickStart(Terrain terrain, ResourceNodes resources, ref Rng rng)
    {
        float bestScore = float.NegativeInfinity, bx = terrain.MaxX * 0.5f, bz = terrain.MaxZ * 0.5f;
        for (int attempt = 0; attempt < 400; attempt++)
        {
            int cell = rng.NextInt(terrain.Width * terrain.Height);
            var b = (BiomeId)terrain.Biomes[cell];
            if (b is not (BiomeId.Grassland or BiomeId.Forest or BiomeId.Beach)) continue;
            float x = terrain.CellX(cell), z = terrain.CellZ(cell);
            int water = terrain.NearestWater[cell];
            if (water < 0) continue;
            float wd = Dist(x, z, terrain.CellX(water), terrain.CellZ(water));
            int food = 0;
            for (int i = 0; i < resources.Count; i++)
                if (ResourceNodes.IsFood(resources.Kind[i]) && Dist(x, z, resources.X[i], resources.Z[i]) < 60f) food++;
            float score = Math.Min(food, 12) - wd / 15f;
            if (score > bestScore) { bestScore = score; bx = x; bz = z; }
        }
        return (bx, bz);
    }

    private static float Dist(float ax, float az, float bx, float bz)
    {
        float dx = ax - bx, dz = az - bz;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
