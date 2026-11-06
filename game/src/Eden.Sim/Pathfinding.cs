namespace Eden.Sim;

/// <summary>
/// Grid A* on <see cref="Terrain.MoveCost"/> (8 neighbours, no corner cutting, so connectivity equals the
/// 4-neighbour walkable regions in <see cref="Region"/>). All buffers are allocated once per terrain;
/// a search allocates nothing. Results are string-pulled into a few waypoints with
/// <see cref="Terrain.LineWalkable"/>. This is sim truth; Godot's navigation is never consulted.
/// </summary>
public sealed class Pathfinder
{
    private const float Sqrt2 = 1.41421356f;

    private readonly Terrain _t;
    private readonly float[] _g;
    private readonly int[] _parent;
    private readonly int[] _stamp;
    private readonly bool[] _closed;
    private int[] _heapCell;
    private float[] _heapF;
    private readonly int[] _cells;
    private int _heapCount;
    private int _search;

    /// <summary>Connected walkable region per cell (-1 = impassable).</summary>
    public int[] Region { get; }

    public int RegionCount { get; private set; }

    /// <summary>Upper bound on expanded cells per search; a search that hits it fails.</summary>
    public int MaxExpansions { get; set; } = 6000;

    public Pathfinder(Terrain terrain)
    {
        _t = terrain;
        int n = terrain.Width * terrain.Height;
        _g = new float[n];
        _parent = new int[n];
        _stamp = new int[n];
        _closed = new bool[n];
        _heapCell = new int[n];
        _heapF = new float[n];
        _cells = new int[n];
        Region = new int[n];
        ComputeRegions();
    }

    private bool Passable(int cell) => !float.IsInfinity(_t.MoveCost[cell]);

    /// <summary>Flood-fill walkable regions. Call after terraforming.</summary>
    public void ComputeRegions()
    {
        int w = _t.Width, n = Region.Length;
        Array.Fill(Region, -1);
        int region = 0;
        for (int start = 0; start < n; start++)
        {
            if (Region[start] >= 0 || !Passable(start)) continue;
            int head = 0, tail = 0;
            _cells[tail++] = start;
            Region[start] = region;
            while (head < tail)
            {
                int c = _cells[head++];
                int cx = c % w, cz = c / w;
                if (cx > 0) Visit(c - 1);
                if (cx < w - 1) Visit(c + 1);
                if (cz > 0) Visit(c - w);
                if (cz < _t.Height - 1) Visit(c + w);
            }
            region++;

            void Visit(int nb)
            {
                if (Region[nb] >= 0 || !Passable(nb)) return;
                Region[nb] = region;
                _cells[tail++] = nb;
            }
        }
        RegionCount = region;
    }

    public bool SameRegion(float ax, float az, float bx, float bz)
    {
        int ra = Region[_t.CellIndex(ax, az)];
        return ra >= 0 && ra == Region[_t.CellIndex(bx, bz)];
    }

    /// <summary>
    /// Path from (sx, sz) toward (tx, tz). Writes up to <paramref name="maxWaypoints"/> intermediate waypoints
    /// (world x, z pairs, excluding the target itself) into <paramref name="xs"/>/<paramref name="zs"/>.
    /// Returns the waypoint count, or -1 if there is no path within the expansion budget.
    /// <paramref name="truncated"/> is set when the path had more waypoints than fit.
    /// </summary>
    public int Find(float sx, float sz, float tx, float tz, Span<float> xs, Span<float> zs, int maxWaypoints,
        out float length, out bool truncated)
    {
        length = 0;
        truncated = false;
        int w = _t.Width, h = _t.Height;
        int start = _t.CellIndex(sx, sz), goal = _t.CellIndex(tx, tz);
        if (!Passable(goal)) goal = NearestPassableNeighbour(goal);
        if (goal < 0 || !Passable(start) || Region[start] != Region[goal]) return -1;
        if (start == goal) return 0;

        if (++_search == int.MaxValue) { Array.Clear(_stamp); _search = 1; }
        _heapCount = 0;
        Open(start, 0f, Heuristic(start, goal, w), -1);
        int expanded = 0;
        bool found = false;
        while (_heapCount > 0)
        {
            int c = Pop();
            if (_closed[c]) continue;
            _closed[c] = true;
            if (c == goal) { found = true; break; }
            if (++expanded > MaxExpansions) break;
            int cx = c % w, cz = c / w;
            float gc = _g[c];
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = cx + dx, nz = cz + dz;
                if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                int nb = nz * w + nx;
                if (!Passable(nb)) continue;
                bool diagonal = dx != 0 && dz != 0;
                if (diagonal && (!Passable(cz * w + nx) || !Passable(nz * w + cx))) continue;
                if (_stamp[nb] == _search && _closed[nb]) continue;
                float step = (diagonal ? Sqrt2 : 1f) * 0.5f * (_t.MoveCost[c] + _t.MoveCost[nb]);
                float g = gc + step;
                if (_stamp[nb] == _search && g >= _g[nb]) continue;
                Open(nb, g, g + Heuristic(nb, goal, w), c);
            }
        }
        if (!found) return -1;

        // Walk back to the start, then string-pull: keep only cells where line of sight breaks.
        int count = 0;
        for (int c = goal; c >= 0; c = _parent[c]) _cells[count++] = c;
        length = _g[goal] * _t.CellSize;
        float ax = sx, az = sz;
        int written = 0;
        for (int i = count - 2; i >= 1; i--) // _cells[count-1] is the start
        {
            int next = _cells[i - 1];
            if (_t.LineWalkable(ax, az, _t.CellX(next), _t.CellZ(next))) continue;
            if (written == maxWaypoints) { truncated = true; break; }
            ax = _t.CellX(_cells[i]);
            az = _t.CellZ(_cells[i]);
            xs[written] = ax;
            zs[written] = az;
            written++;
        }
        return written;
    }

    private int NearestPassableNeighbour(int cell)
    {
        int w = _t.Width, cx = cell % w, cz = cell / w;
        for (int r = 1; r <= 2; r++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            int nx = cx + dx, nz = cz + dz;
            if (nx < 0 || nz < 0 || nx >= w || nz >= _t.Height) continue;
            if (Passable(nz * w + nx)) return nz * w + nx;
        }
        return -1;
    }

    private static float Heuristic(int a, int b, int w)
    {
        int dx = Math.Abs(a % w - b % w), dz = Math.Abs(a / w - b / w);
        int lo = Math.Min(dx, dz), hi = Math.Max(dx, dz);
        return (hi - lo + Sqrt2 * lo) * 1.1f; // slightly inflated: faster, still near-optimal
    }

    private void Open(int cell, float g, float f, int parent)
    {
        if (_stamp[cell] != _search) { _stamp[cell] = _search; _closed[cell] = false; }
        _g[cell] = g;
        _parent[cell] = parent;
        // Binary min-heap on (f, cell) so ties break deterministically. Stale entries are skipped on pop.
        if (_heapCount == _heapCell.Length)
        {
            Array.Resize(ref _heapCell, _heapCell.Length * 2);
            Array.Resize(ref _heapF, _heapF.Length * 2);
        }
        int i = _heapCount++;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (Less(_heapF[p], _heapCell[p], f, cell)) break;
            _heapF[i] = _heapF[p];
            _heapCell[i] = _heapCell[p];
            i = p;
        }
        _heapF[i] = f;
        _heapCell[i] = cell;
    }

    private int Pop()
    {
        int top = _heapCell[0];
        int last = --_heapCount;
        float f = _heapF[last];
        int cell = _heapCell[last];
        int i = 0;
        while (true)
        {
            int l = 2 * i + 1;
            if (l >= last) break;
            int r = l + 1;
            int m = r < last && Less(_heapF[r], _heapCell[r], _heapF[l], _heapCell[l]) ? r : l;
            if (Less(f, cell, _heapF[m], _heapCell[m])) break;
            _heapF[i] = _heapF[m];
            _heapCell[i] = _heapCell[m];
            i = m;
        }
        _heapF[i] = f;
        _heapCell[i] = cell;
        return top;
    }

    private static bool Less(float fa, int ca, float fb, int cb) => fa < fb || (fa == fb && ca < cb);
}
