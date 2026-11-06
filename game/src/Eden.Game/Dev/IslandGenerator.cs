namespace Eden.Game.Dev;

/// <summary>
/// Stand-in world generator for <see cref="FakeSimHost"/>: fBm value noise + radial falloff so the
/// border is sea, biomes from height + a moisture field. Pure C#, no Godot types, deterministic per seed.
/// The real generator lives in Eden.Sim (M1); this one only has to look plausible.
/// </summary>
public static class IslandGenerator
{
    public static (float[] Heights, byte[] Biomes) Generate(int seed, int width, int height, float cellSize, float seaLevel)
    {
        var heights = new float[width * height];
        var biomes = new byte[width * height];
        float cx = (width - 1) * 0.5f, cz = (height - 1) * 0.5f;
        float radius = MathF.Min(width, height) * 0.5f;
        float sizeMetres = MathF.Min(width, height) * cellSize;
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float fx = x / (float)width, fz = z / (float)height;
                float n = Math.Clamp((Fbm(seed, fx * 4f, fz * 4f, 5) - 0.5f) * 1.6f + 0.5f, 0f, 1f); // contrast-stretched 0..1
                float dx = (x - cx) / radius, dz = (z - cz) / radius;
                float r = MathF.Sqrt(dx * dx + dz * dz);
                float falloff = SmoothStep(0.35f, 0.95f, r);
                float h01 = n * 0.85f + 0.25f - falloff * 1.0f; // >0.25 = land roughly
                float metres = (h01 - 0.22f) * sizeMetres * 0.065f;
                // Flatten the lowlands a little so there is walkable grass.
                if (metres > 0) metres = MathF.Pow(metres / 30f, 1.35f) * 30f;
                heights[z * width + x] = seaLevel + metres;

                float moisture = Fbm(seed + 7919, fx * 6f, fz * 6f, 3);
                biomes[z * width + x] = ClassifyBiome(metres, moisture);
            }
        }
        return (heights, biomes);
    }

    public static byte ClassifyBiome(float metresAboveSea, float moisture)
    {
        if (metresAboveSea < 0f) return Biome.Ocean;
        if (metresAboveSea < 1.2f) return Biome.Beach;
        if (metresAboveSea > 24f) return Biome.Snow;
        if (metresAboveSea > 15f) return Biome.Mountain;
        return moisture > 0.52f ? Biome.Forest : Biome.Grassland;
    }

    /// <summary>Fractal value noise in [0,1].</summary>
    public static float Fbm(int seed, float x, float z, int octaves)
    {
        float sum = 0, amp = 0.5f, norm = 0, freq = 1;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * ValueNoise(seed + o * 1013, x * freq, z * freq);
            norm += amp;
            amp *= 0.5f;
            freq *= 2.03f;
        }
        return sum / norm;
    }

    private static float ValueNoise(int seed, float x, float z)
    {
        int x0 = (int)MathF.Floor(x), z0 = (int)MathF.Floor(z);
        float tx = x - x0, tz = z - z0;
        tx = tx * tx * (3 - 2 * tx);
        tz = tz * tz * (3 - 2 * tz);
        float a = Hash01(seed, x0, z0), b = Hash01(seed, x0 + 1, z0);
        float c = Hash01(seed, x0, z0 + 1), d = Hash01(seed, x0 + 1, z0 + 1);
        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), tz);
    }

    internal static float Hash01(int seed, int x, int z)
    {
        uint h = (uint)seed * 0x9E3779B1u;
        h ^= (uint)x * 0x85EBCA77u;
        h = (h << 13) | (h >> 19);
        h ^= (uint)z * 0xC2B2AE3Du;
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float SmoothStep(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
