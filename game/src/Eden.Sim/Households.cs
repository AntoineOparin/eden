using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// A household: one home site (later a hut), up to two heads (a couple) and whoever lives with them
/// (children, unpaired adult children, lodgers). Struct-of-arrays with a free list; index = household id.
/// </summary>
public sealed class Households
{
    public int HighWater { get; private set; }
    public int Count { get; private set; }

    public bool[] Alive = new bool[32];
    public float[] HomeX = new float[32], HomeZ = new float[32];
    public int[] Settlement = new int[32];
    public int[] Hut = new int[32];
    public int[] HeadA = new int[32], HeadB = new int[32];
    public int[] Members = new int[32];
    public long[] Founded = new long[32];

    /// <summary>Settlement the household left in a migration (-1 if none); a new settlement it founds is a colony of that one.</summary>
    public int[] MigratedFrom = new int[32];

    private int[] _free = new int[16];
    private int _freeCount;

    public bool IsHead(int h, int npcId) => h >= 0 && Alive[h] && (HeadA[h] == npcId || HeadB[h] == npcId);

    public int Add(float x, float z, int headA, long tick)
    {
        int h;
        if (_freeCount > 0) h = _free[--_freeCount];
        else
        {
            if (HighWater == Alive.Length) Grow();
            h = HighWater++;
        }
        Alive[h] = true;
        HomeX[h] = x; HomeZ[h] = z;
        Settlement[h] = -1;
        Hut[h] = -1;
        HeadA[h] = headA; HeadB[h] = Npcs.None;
        Members[h] = 1;
        Founded[h] = tick;
        MigratedFrom[h] = -1;
        Count++;
        return h;
    }

    public void Remove(int h)
    {
        if (!Alive[h]) return;
        Alive[h] = false;
        Count--;
        if (_freeCount == _free.Length) Array.Resize(ref _free, _free.Length * 2);
        _free[_freeCount++] = h;
    }

    private void Grow()
    {
        int n = Alive.Length * 2;
        Array.Resize(ref Alive, n);
        Array.Resize(ref HomeX, n); Array.Resize(ref HomeZ, n);
        Array.Resize(ref Settlement, n); Array.Resize(ref Hut, n);
        Array.Resize(ref HeadA, n); Array.Resize(ref HeadB, n);
        Array.Resize(ref Members, n); Array.Resize(ref Founded, n);
        Array.Resize(ref MigratedFrom, n);
    }

    public ulong Hash(ulong h)
    {
        h = Eden.Sim.Hash.Mix(h, (ulong)HighWater);
        h = Eden.Sim.Hash.Mix(h, (ulong)Count);
        for (int i = 0; i < _freeCount; i++) h = Eden.Sim.Hash.Mix(h, (ulong)_free[i]);
        for (int i = 0; i < HighWater; i++)
        {
            if (!Alive[i]) continue;
            h = Eden.Sim.Hash.Mix(h, (ulong)i);
            h = Eden.Sim.Hash.Mix(h, HomeX[i]); h = Eden.Sim.Hash.Mix(h, HomeZ[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Settlement[i]); h = Eden.Sim.Hash.Mix(h, (ulong)Hut[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)HeadA[i]); h = Eden.Sim.Hash.Mix(h, (ulong)HeadB[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Members[i]); h = Eden.Sim.Hash.Mix(h, (ulong)Founded[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)MigratedFrom[i]);
        }
        return h;
    }

    public void Write(BinaryWriter w)
    {
        w.Write(HighWater); w.Write(Count); w.Write(_freeCount);
        for (int i = 0; i < _freeCount; i++) w.Write(_free[i]);
        for (int i = 0; i < HighWater; i++)
        {
            w.Write(Alive[i]);
            if (!Alive[i]) continue;
            w.Write(HomeX[i]); w.Write(HomeZ[i]); w.Write(Settlement[i]); w.Write(Hut[i]);
            w.Write(HeadA[i]); w.Write(HeadB[i]); w.Write(Members[i]); w.Write(Founded[i]); w.Write(MigratedFrom[i]);
        }
    }

    public void Read(BinaryReader r)
    {
        int hw = r.ReadInt32();
        Count = r.ReadInt32();
        _freeCount = r.ReadInt32();
        _free = new int[Math.Max(16, _freeCount)];
        for (int i = 0; i < _freeCount; i++) _free[i] = r.ReadInt32();
        while (Alive.Length < hw) Grow();
        Array.Clear(Alive);
        HighWater = hw;
        for (int i = 0; i < hw; i++)
        {
            Alive[i] = r.ReadBoolean();
            if (!Alive[i]) continue;
            HomeX[i] = r.ReadSingle(); HomeZ[i] = r.ReadSingle(); Settlement[i] = r.ReadInt32(); Hut[i] = r.ReadInt32();
            HeadA[i] = r.ReadInt32(); HeadB[i] = r.ReadInt32(); Members[i] = r.ReadInt32(); Founded[i] = r.ReadInt64();
            MigratedFrom[i] = r.ReadInt32();
        }
    }
}

public sealed partial class World
{
    public Households Households { get; private set; } = new();

    // Daily-only spatial bins over household homes (rebuilt before use, never saved).
    private int[] _hhHead = [], _hhNext = [];
    private int _hhBinsX, _hhBinsZ;
    private float _hhBin;

    private void RebuildHouseholdBins()
    {
        var hh = Households;
        float bin = Math.Max(8f, Config.SettlementRadius);
        if (_hhHead.Length == 0 || bin != _hhBin)
        {
            _hhBin = bin;
            _hhBinsX = (int)(Terrain.MaxX / bin) + 1;
            _hhBinsZ = (int)(Terrain.MaxZ / bin) + 1;
            _hhHead = new int[_hhBinsX * _hhBinsZ];
        }
        Array.Fill(_hhHead, -1);
        for (int i = hh.HighWater - 1; i >= 0; i--) if (hh.Alive[i]) InsertHouseholdBin(i);
    }

    /// <summary>Only for households created after the last rebuild (a reused index is never in the bins twice).</summary>
    private void InsertHouseholdBin(int h)
    {
        if (_hhHead.Length == 0) return;
        if (_hhNext.Length <= h) Array.Resize(ref _hhNext, Math.Max(64, Households.Alive.Length));
        int b = HouseholdBin(Households.HomeX[h], Households.HomeZ[h]);
        _hhNext[h] = _hhHead[b];
        _hhHead[b] = h;
    }

    private int HouseholdBin(float x, float z)
        => Math.Clamp((int)(z / _hhBin), 0, _hhBinsZ - 1) * _hhBinsX + Math.Clamp((int)(x / _hhBin), 0, _hhBinsX - 1);

    /// <summary>Bin range covering a disc of radius up to one bin around (x, z).</summary>
    private void HouseholdBinRange(float x, float z, out int bx0, out int bx1, out int bz0, out int bz1)
    {
        bx0 = Math.Clamp((int)(x / _hhBin) - 1, 0, _hhBinsX - 1);
        bx1 = Math.Clamp((int)(x / _hhBin) + 1, 0, _hhBinsX - 1);
        bz0 = Math.Clamp((int)(z / _hhBin) - 1, 0, _hhBinsZ - 1);
        bz1 = Math.Clamp((int)(z / _hhBin) + 1, 0, _hhBinsZ - 1);
    }

    /// <summary>Household of a living NPC slot, or -1.</summary>
    public int HouseholdOfSlot(int s) => s < _home.Length ? _home[s] : -1;

    /// <summary>A new NPC joins a household: newborns their mother's, adults the nearest within 15 m, else a new one.</summary>
    private void AssignInitialHousehold(int s)
    {
        var h = Households;
        int home = -1;
        if (Npcs.TryGetSlot(Npcs.Mother[s], out int mother) && _home[mother] >= 0) home = _home[mother];
        else if (Npcs.TryGetSlot(Npcs.Father[s], out int father) && _home[father] >= 0) home = _home[father];
        else
        {
            float best = 15f * 15f;
            for (int i = 0; i < h.HighWater; i++)
            {
                if (!h.Alive[i]) continue;
                float d = Sq(h.HomeX[i] - Npcs.X[s]) + Sq(h.HomeZ[i] - Npcs.Z[s]);
                if (d < best) { best = d; home = i; }
            }
        }
        if (home < 0) home = h.Add(Npcs.X[s], Npcs.Z[s], Npcs.IdOf(s), Tick);
        else h.Members[home]++;
        _home[s] = home;
    }

    /// <summary>
    /// Daily: couples found or merge households, children follow their mother, empty households close.
    /// Member counts are recomputed from scratch, so deaths need no hook.
    /// </summary>
    private void UpdateHouseholds()
    {
        var n = Npcs;
        var h = Households;
        RebuildHouseholdBins();
        for (int s = 0; s < n.HighWater; s++)
        {
            if (!n.Alive[s] || StageOf(s) is not (LifeStage.Adult or LifeStage.Elder)) continue;
            int id = n.IdOf(s);
            if (_home[s] < 0) { _home[s] = h.Add(n.X[s], n.Z[s], id, Tick); InsertHouseholdBin(_home[s]); continue; }
            if (!n.TryGetSlot(n.Partner[s], out int p) || n.Partner[p] != id || n.IdOf(p) < id) continue;
            // Each couple is handled once, from the partner with the lower id.
            int hs = _home[s], hp = _home[p], pid = n.IdOf(p);
            if (hs == hp && h.IsHead(hs, id) && h.IsHead(hs, pid)) continue;
            if (h.IsHead(hs, id)) JoinAsHead(p, hs);
            else if (h.IsHead(hp, pid)) JoinAsHead(s, hp);
            else if (hs >= 0 && h.Alive[hs] && OnlyHead(hs, id)) JoinAsHead(p, hs);
            else FoundHousehold(s, p);
        }
        for (int s = 0; s < n.HighWater; s++)
        {
            if (!n.Alive[s] || StageOf(s) is LifeStage.Adult or LifeStage.Elder) continue;
            if (n.TryGetSlot(n.Mother[s], out int m) && _home[m] >= 0) _home[s] = _home[m];
            else if (n.TryGetSlot(n.Father[s], out int f) && _home[f] >= 0) _home[s] = _home[f];
        }

        for (int i = 0; i < h.HighWater; i++) if (h.Alive[i]) h.Members[i] = 0;
        for (int s = 0; s < n.HighWater; s++)
        {
            if (!n.Alive[s]) continue;
            int home = _home[s];
            if (home < 0 || !h.Alive[home]) { home = h.Add(n.X[s], n.Z[s], n.IdOf(s), Tick); h.Members[home] = 0; _home[s] = home; InsertHouseholdBin(home); }
            h.Members[home]++;
        }
        for (int i = 0; i < h.HighWater; i++)
        {
            if (!h.Alive[i]) continue;
            if (h.Members[i] == 0)
            {
                if (h.Hut[i] >= 0 && Buildings.Alive[h.Hut[i]]) Buildings.Household[h.Hut[i]] = -1;
                h.Remove(i);
                continue;
            }
            if (!n.IsAlive(h.HeadA[i])) h.HeadA[i] = Npcs.None;
            if (!n.IsAlive(h.HeadB[i])) h.HeadB[i] = Npcs.None;
        }
    }

    /// <summary>True if <paramref name="id"/> is a head of <paramref name="h"/> or the household has no living head.</summary>
    private bool OnlyHead(int h, int id)
    {
        var hh = Households;
        return hh.HeadA[h] == id || hh.HeadB[h] == id || (!Npcs.IsAlive(hh.HeadA[h]) && !Npcs.IsAlive(hh.HeadB[h]));
    }

    private void JoinAsHead(int s, int h)
    {
        var hh = Households;
        int id = Npcs.IdOf(s);
        _home[s] = h;
        if (hh.HeadA[h] == Npcs.None || !Npcs.IsAlive(hh.HeadA[h])) hh.HeadA[h] = id;
        else if (hh.HeadA[h] != id) hh.HeadB[h] = id;
    }

    /// <summary>A couple leaves home: a vacant hut in their settlement if any, else a new site near it.</summary>
    private void FoundHousehold(int a, int b)
    {
        var hh = Households;
        int origin = _home[a] >= 0 ? _home[a] : _home[b];
        int set = origin >= 0 ? hh.Settlement[origin] : -1;
        float ox = origin >= 0 ? hh.HomeX[origin] : Npcs.X[a], oz = origin >= 0 ? hh.HomeZ[origin] : Npcs.Z[a];
        int hut = -1;
        float x = ox, z = oz;
        if (set >= 0)
        {
            hut = FindVacantHut(set);
            if (hut >= 0) { x = Buildings.X[hut]; z = Buildings.Z[hut]; }
            else (x, z) = PickHomeSite(Settlements.CenterX[set], Settlements.CenterZ[set], 8f, 12f + 2.5f * MathF.Sqrt(Settlements.Households[set]));
        }
        else (x, z) = PickHomeSite(ox, oz, 7f, 16f);
        int h = hh.Add(x, z, Npcs.IdOf(a), Tick);
        hh.HeadB[h] = Npcs.IdOf(b);
        hh.Settlement[h] = set;
        if (hut >= 0) { hh.Hut[h] = hut; Buildings.Household[hut] = h; }
        _home[a] = _home[b] = h;
        InsertHouseholdBin(h);
    }

    private int FindVacantHut(int set)
    {
        var b = Buildings;
        for (int i = 0; i < b.HighWater; i++)
            if (b.Alive[i] && b.Kind[i] == BuildingKinds.Hut && b.Settlement[i] == set && b.Household[i] < 0) return i;
        return -1;
    }

    /// <summary>A buildable spot in a ring around a point, away from other homes; falls back to the centre.</summary>
    private (float x, float z) PickHomeSite(float cx, float cz, float rMin, float rMax)
    {
        var hh = Households;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float a = Rng.NextFloat() * MathF.Tau, r = rMin + Rng.NextFloat() * (rMax - rMin);
            float x = cx + MathF.Cos(a) * r, z = cz + MathF.Sin(a) * r;
            if (!BuildableAt(x, z) || !SpotFree(x, z, 3f, -1)) continue;
            if (Pathing.Region[Terrain.CellIndex(x, z)] != Pathing.Region[Terrain.CellIndex(cx, cz)]) continue;
            bool clear = true;
            HouseholdBinRange(x, z, out int bx0, out int bx1, out int bz0, out int bz1);
            for (int bz = bz0; bz <= bz1 && clear; bz++)
            for (int bx = bx0; bx <= bx1 && clear; bx++)
            for (int i = _hhHead[bz * _hhBinsX + bx]; i >= 0 && clear; i = _hhNext[i])
                if (hh.Alive[i] && Sq(hh.HomeX[i] - x) + Sq(hh.HomeZ[i] - z) < 36f) clear = false;
            if (clear) return (x, z);
        }
        return (cx, cz);
    }
}
