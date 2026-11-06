using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// The island grid: heights (absolute metres), <see cref="BiomeId"/> bytes, moisture, fertility, movement cost and
/// a nearest-water field. Layout follows the <see cref="WorldInfo"/> contract: row-major, cell (x, z) at index
/// <c>z * Width + x</c>, sitting at world position <c>(x * CellSize, z * CellSize)</c>; a world point belongs to
/// the nearest cell. Cells at or below <see cref="SeaLevel"/> are water.
/// </summary>
public sealed class Terrain
{
    public const float Impassable = float.PositiveInfinity;

    /// <summary>Water deeper than this below sea level cannot be waded.</summary>
    public const float WadeDepth = 3f;

    public int Width { get; }
    public int Height { get; }
    public float CellSize { get; }
    public float SeaLevel { get; }
    public float[] Heights { get; }
    public byte[] Biomes { get; }
    public float[] Moisture { get; }
    public float[] Fertility { get; }

    /// <summary>Movement cost multiplier per cell (1 = grass). <see cref="Impassable"/> for deep water.</summary>
    public float[] MoveCost { get; }

    /// <summary>Index of the closest water cell (4-neighbour BFS distance), or -1 if the map has no water.</summary>
    public int[] NearestWater { get; }

    public Terrain(int width, int height, float cellSize, float seaLevel)
    {
        Width = width;
        Height = height;
        CellSize = cellSize;
        SeaLevel = seaLevel;
        int n = width * height;
        Heights = new float[n];
        Biomes = new byte[n];
        Moisture = new float[n];
        Fertility = new float[n];
        MoveCost = new float[n];
        NearestWater = new int[n];
    }

    /// <summary>World-space extent: positions run from 0 to (Width - 1) * CellSize.</summary>
    public float MaxX => (Width - 1) * CellSize;
    public float MaxZ => (Height - 1) * CellSize;

    public int CellIndex(float x, float z)
    {
        int cx = Math.Clamp((int)MathF.Floor(x / CellSize + 0.5f), 0, Width - 1);
        int cz = Math.Clamp((int)MathF.Floor(z / CellSize + 0.5f), 0, Height - 1);
        return cz * Width + cx;
    }

    public float CellX(int cell) => cell % Width * CellSize;
    public float CellZ(int cell) => cell / Width * CellSize;

    public float HeightAt(float x, float z) => Heights[CellIndex(x, z)];
    public BiomeId BiomeAt(float x, float z) => (BiomeId)Biomes[CellIndex(x, z)];

    public bool IsWater(int cell) => Heights[cell] <= SeaLevel;
    public bool IsLand(int cell) => !IsWater(cell);
    public bool IsWalkable(float x, float z)
        => x >= 0 && z >= 0 && x <= MaxX && z <= MaxZ && !float.IsInfinity(MoveCost[CellIndex(x, z)]);

    /// <summary>True if every point along the straight segment (sampled each half cell) is walkable.</summary>
    public bool LineWalkable(float x0, float z0, float x1, float z1)
    {
        float dx = x1 - x0, dz = z1 - z0;
        float len = MathF.Sqrt(dx * dx + dz * dz);
        int steps = (int)(len / (CellSize * 0.5f)) + 1;
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            if (!IsWalkable(x0 + dx * t, z0 + dz * t)) return false;
        }
        return true;
    }

    /// <summary>Classify one cell from its height above sea level and moisture.</summary>
    public static BiomeId Classify(float heightAboveSea, float moisture) => heightAboveSea switch
    {
        <= 0f => BiomeId.Ocean,
        < 1.5f => BiomeId.Beach,
        > 28f => BiomeId.Snow,
        > 16f => BiomeId.Mountain,
        _ => moisture > 0.55f ? BiomeId.Forest : BiomeId.Grassland,
    };

    /// <summary>Recompute biomes, fertility, move cost and the water field from heights + moisture.</summary>
    public void RecomputeDerived()
    {
        for (int i = 0; i < Heights.Length; i++)
        {
            float h = Heights[i] - SeaLevel;
            var b = Classify(h, Moisture[i]);
            Biomes[i] = (byte)b;
            Fertility[i] = b switch
            {
                BiomeId.Grassland => 0.5f + 0.5f * Moisture[i],
                BiomeId.Forest => 0.5f,
                BiomeId.Beach => 0.1f,
                _ => 0f,
            };
            float slope = h > 10f ? 1f + (h - 10f) / 12f : 1f; // uplands are slow going
            MoveCost[i] = b switch
            {
                BiomeId.Ocean => h > -WadeDepth ? 3f : Impassable,
                BiomeId.Beach => 1.1f,
                BiomeId.Grassland => 1f * slope,
                BiomeId.Forest => 1.4f * slope,
                BiomeId.Mountain => 2.5f,
                _ => 3f,
            };
        }
        ComputeWaterField();
    }

    private void ComputeWaterField()
    {
        int n = Heights.Length;
        var queue = new int[n];
        int head = 0, tail = 0;
        for (int i = 0; i < n; i++)
        {
            if (IsWater(i)) { NearestWater[i] = i; queue[tail++] = i; }
            else NearestWater[i] = -1;
        }
        while (head < tail)
        {
            int c = queue[head++];
            int cx = c % Width, cz = c / Width;
            int src = NearestWater[c];
            if (cx > 0) Visit(c - 1);
            if (cx < Width - 1) Visit(c + 1);
            if (cz > 0) Visit(c - Width);
            if (cz < Height - 1) Visit(c + Width);

            void Visit(int nb)
            {
                if (NearestWater[nb] != -1) return;
                NearestWater[nb] = src;
                queue[tail++] = nb;
            }
        }
    }
}
