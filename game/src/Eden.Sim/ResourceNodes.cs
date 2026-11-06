namespace Eden.Sim;

public enum ResourceKind : byte
{
    Tree,
    Stone,
    Ore,
    Berry,

    /// <summary>A game-animal spawn (herd). Amount = animals available to hunt. M1 keeps animals as nodes.</summary>
    Game,
}

/// <summary>
/// Struct-of-arrays store of resource nodes with a coarse spatial bin index. Nodes are never removed
/// (depleted nodes regrow), so indices are stable and the index is rebuilt only when nodes are added.
/// </summary>
public sealed class ResourceNodes
{
    public const int BinCells = 16;

    private readonly Terrain _terrain;
    private readonly int _binsX, _binsZ;
    private readonly float _binSize;
    private int[] _binHead;
    private int[] _next = new int[256];

    public int Count { get; private set; }
    public ResourceKind[] Kind = new ResourceKind[256];
    public float[] X = new float[256];
    public float[] Z = new float[256];
    public float[] Amount = new float[256];
    public float[] Max = new float[256];
    public float[] RegenPerDay = new float[256];

    /// <summary>
    /// Walkable-region label per terrain cell, set by the world's <see cref="Pathfinder"/> (M3). When set,
    /// "reachable" means "on the same landmass" (A* finds the way around coasts) instead of a straight line.
    /// </summary>
    public int[]? Region;

    public ResourceNodes(Terrain terrain)
    {
        _terrain = terrain;
        _binsX = (terrain.Width + BinCells - 1) / BinCells;
        _binsZ = (terrain.Height + BinCells - 1) / BinCells;
        _binSize = BinCells * terrain.CellSize;
        _binHead = new int[_binsX * _binsZ];
        Array.Fill(_binHead, -1);
    }

    public static bool IsFood(ResourceKind k) => k is ResourceKind.Berry or ResourceKind.Game;

    public static (float max, float regen) Defaults(ResourceKind k) => k switch
    {
        ResourceKind.Berry => (12f, 1.2f),
        ResourceKind.Game => (6f, 0.25f),
        ResourceKind.Tree => (20f, 0.2f),
        ResourceKind.Stone => (40f, 0f),
        _ => (15f, 0f),
    };

    public int Add(ResourceKind kind, float x, float z, float amount)
    {
        if (Count == Kind.Length) Grow();
        int i = Count++;
        var (max, regen) = Defaults(kind);
        Kind[i] = kind;
        X[i] = x;
        Z[i] = z;
        Max[i] = Math.Max(max, amount);
        Amount[i] = amount;
        RegenPerDay[i] = regen;
        int bin = BinOf(x, z);
        _next[i] = _binHead[bin];
        _binHead[bin] = i;
        return i;
    }

    /// <summary>Daily regrowth. <paramref name="foodFactor"/> scales berry/game regen (weather).</summary>
    public void RegenDay(float foodFactor)
    {
        for (int i = 0; i < Count; i++)
        {
            float r = RegenPerDay[i] * (IsFood(Kind[i]) ? foodFactor : 1f);
            if (Amount[i] < Max[i]) Amount[i] = Math.Min(Max[i], Amount[i] + r);
        }
    }

    /// <summary>Nearest node of <paramref name="kind"/> with at least <paramref name="minAmount"/>, or -1. No allocation.</summary>
    public int FindNearest(ResourceKind kind, float x, float z, float radius, float minAmount = 1f, int exclude = -1, bool reachable = false)
    {
        int bx0 = Math.Max(0, (int)((x - radius) / _binSize));
        int bx1 = Math.Min(_binsX - 1, (int)((x + radius) / _binSize));
        int bz0 = Math.Max(0, (int)((z - radius) / _binSize));
        int bz1 = Math.Min(_binsZ - 1, (int)((z + radius) / _binSize));
        float best = radius * radius;
        int bestIdx = -1;
        for (int bz = bz0; bz <= bz1; bz++)
        for (int bx = bx0; bx <= bx1; bx++)
        {
            for (int i = _binHead[bz * _binsX + bx]; i >= 0; i = _next[i])
            {
                if (Kind[i] != kind || Amount[i] < minAmount || i == exclude) continue;
                float dx = X[i] - x, dz = Z[i] - z;
                float d2 = dx * dx + dz * dz;
                if (d2 > best || (d2 == best && i > bestIdx)) continue;
                // Movement is straight-line: a node across water is not an option.
                if (reachable && (Region is { } region
                        ? region[_terrain.CellIndex(X[i], Z[i])] != region[_terrain.CellIndex(x, z)]
                        : !_terrain.LineWalkable(x, z, X[i], Z[i]))) continue;
                best = d2;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    /// <summary>Set every node within <paramref name="radius"/> of a point to zero (lightning fire). Returns count.</summary>
    public int Burn(float x, float z, float radius)
    {
        int n = 0;
        float r2 = radius * radius;
        for (int i = 0; i < Count; i++)
        {
            if (Kind[i] is not (ResourceKind.Tree or ResourceKind.Berry)) continue;
            float dx = X[i] - x, dz = Z[i] - z;
            if (dx * dx + dz * dz <= r2 && Amount[i] > 0) { Amount[i] = 0; n++; }
        }
        return n;
    }

    public ulong Hash(ulong h)
    {
        h = global::Eden.Sim.Hash.Mix(h, (ulong)Count);
        for (int i = 0; i < Count; i++)
        {
            h = global::Eden.Sim.Hash.Mix(h, (ulong)Kind[i]);
            h = global::Eden.Sim.Hash.Mix(h, X[i]);
            h = global::Eden.Sim.Hash.Mix(h, Z[i]);
            h = global::Eden.Sim.Hash.Mix(h, Amount[i]);
        }
        return h;
    }

    public void Write(BinaryWriter w)
    {
        w.Write(Count);
        for (int i = 0; i < Count; i++)
        {
            w.Write((byte)Kind[i]);
            w.Write(X[i]);
            w.Write(Z[i]);
            w.Write(Amount[i]);
            w.Write(Max[i]);
            w.Write(RegenPerDay[i]);
        }
    }

    public void Read(BinaryReader r)
    {
        Count = 0;
        Array.Fill(_binHead, -1);
        int n = r.ReadInt32();
        for (int k = 0; k < n; k++)
        {
            var kind = (ResourceKind)r.ReadByte();
            float x = r.ReadSingle(), z = r.ReadSingle(), amount = r.ReadSingle();
            int i = Add(kind, x, z, amount);
            Max[i] = r.ReadSingle();
            RegenPerDay[i] = r.ReadSingle();
        }
    }

    private int BinOf(float x, float z)
    {
        int bx = Math.Clamp((int)(x / _binSize), 0, _binsX - 1);
        int bz = Math.Clamp((int)(z / _binSize), 0, _binsZ - 1);
        return bz * _binsX + bx;
    }

    private void Grow()
    {
        int n = Kind.Length * 2;
        Array.Resize(ref Kind, n);
        Array.Resize(ref X, n);
        Array.Resize(ref Z, n);
        Array.Resize(ref Amount, n);
        Array.Resize(ref Max, n);
        Array.Resize(ref RegenPerDay, n);
        Array.Resize(ref _next, n);
    }

}
