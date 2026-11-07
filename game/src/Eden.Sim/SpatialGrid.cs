namespace Eden.Sim;

/// <summary>
/// Uniform bin grid over NPC slots, rebuilt once per tick in O(n) with intrusive linked lists
/// (no allocation unless the NPC capacity grew). Queries walk the bins overlapping a square.
/// </summary>
public sealed class SpatialGrid
{
    private readonly float _binSize;
    private readonly int _binsX, _binsZ;
    private readonly int[] _head;
    private int[] _next = [];
    private int[] _bin = [];

    public SpatialGrid(float worldWidth, float worldDepth, float binSize)
    {
        _binSize = binSize;
        _binsX = Math.Max(1, (int)MathF.Ceiling(worldWidth / binSize));
        _binsZ = Math.Max(1, (int)MathF.Ceiling(worldDepth / binSize));
        _head = new int[_binsX * _binsZ];
    }

    public void Rebuild(Npcs npcs)
    {
        if (_next.Length < npcs.Capacity) { _next = new int[npcs.Capacity]; _bin = new int[npcs.Capacity]; }
        Array.Fill(_head, -1);
        for (int s = npcs.HighWater - 1; s >= 0; s--)
        {
            if (!npcs.Alive[s]) continue;
            int b = BinOf(npcs.X[s], npcs.Z[s]);
            _bin[s] = b;
            _next[s] = _head[b];
            _head[b] = s;
        }
    }

    public int BinOf(float x, float z)
    {
        int bx = Math.Clamp((int)(x / _binSize), 0, _binsX - 1);
        int bz = Math.Clamp((int)(z / _binSize), 0, _binsZ - 1);
        return bz * _binsX + bx;
    }

    /// <summary>Bin range covering a square of half-size <paramref name="radius"/>.</summary>
    public void Range(float x, float z, float radius, out int bx0, out int bx1, out int bz0, out int bz1)
    {
        bx0 = Math.Clamp((int)((x - radius) / _binSize), 0, _binsX - 1);
        bx1 = Math.Clamp((int)((x + radius) / _binSize), 0, _binsX - 1);
        bz0 = Math.Clamp((int)((z - radius) / _binSize), 0, _binsZ - 1);
        bz1 = Math.Clamp((int)((z + radius) / _binSize), 0, _binsZ - 1);
    }

    public int Head(int bx, int bz) => _head[bz * _binsX + bx];

    public int Next(int slot) => _next[slot];

    /// <summary>
    /// M8: bin of a slot as of the last <see cref="Rebuild"/>. A range walk visits bins in ascending index order and each
    /// bin in ascending slot order, so (bin, slot) is the order in which <see cref="Range"/> scans meet NPCs; searches
    /// that find candidates another way use it to break distance ties exactly as a scan would.
    /// </summary>
    public int BinIndexOf(int slot) => _bin[slot];

    /// <summary>True when the slot's bin (as of the last rebuild) lies inside a <see cref="Range"/> rectangle.</summary>
    public bool InRange(int slot, int bx0, int bx1, int bz0, int bz1)
    {
        int b = _bin[slot], bx = b % _binsX, bz = b / _binsX;
        return bx >= bx0 && bx <= bx1 && bz >= bz0 && bz <= bz1;
    }
}
