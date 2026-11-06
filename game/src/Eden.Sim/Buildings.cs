using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>Static description of one building kind. Ids are kebab-case and shared with game/data.</summary>
/// <param name="Id">Kebab-case id, also the <see cref="BuildingView.Kind"/> string.</param>
/// <param name="Wood">Wood delivered before labour can start.</param>
/// <param name="Stone">Stone delivered before labour can start.</param>
/// <param name="Work">Labour in NPC-ticks (scaled by strength) to finish it.</param>
/// <param name="Footprint">Radius in metres kept clear of other buildings.</param>
/// <param name="Capacity">Kind-specific: hut = beds, storehouse = food units stored, others 0.</param>
/// <param name="ConstructibleByDefault">True for the M3 set; the rest needs an unlockBuilding effect (M4).</param>
public sealed record BuildingKind(string Id, int Wood, int Stone, int Work, float Footprint, int Capacity, bool ConstructibleByDefault);

/// <summary>
/// Registry of building kinds plus the per-settlement unlock state. Definitions are static; the instance
/// (one per <see cref="World"/>, <see cref="World.BuildingKinds"/>) records which kinds each settlement may build.
/// M4's <c>EffectApplier</c> calls <see cref="Unlock"/> for <c>unlockBuilding</c> effects.
/// </summary>
public sealed class BuildingKinds
{
    public const int Hut = 0, Storehouse = 1, FarmPlot = 2, Well = 3, Kiln = 4, Workshop = 5, Shrine = 6, Archive = 7, Wall = 8, Dock = 9;

    public static readonly BuildingKind[] All =
    [
        new("hut", Wood: 6, Stone: 0, Work: 60, Footprint: 3f, Capacity: 5, ConstructibleByDefault: true),
        new("storehouse", Wood: 12, Stone: 0, Work: 100, Footprint: 4f, Capacity: 150, ConstructibleByDefault: true),
        new("farm-plot", Wood: 0, Stone: 0, Work: 40, Footprint: 4f, Capacity: 0, ConstructibleByDefault: true),
        new("well", Wood: 6, Stone: 0, Work: 80, Footprint: 2f, Capacity: 0, ConstructibleByDefault: true),
        new("kiln", Wood: 8, Stone: 10, Work: 120, Footprint: 3f, Capacity: 0, ConstructibleByDefault: false),
        new("workshop", Wood: 16, Stone: 4, Work: 140, Footprint: 4f, Capacity: 0, ConstructibleByDefault: false),
        new("shrine", Wood: 6, Stone: 12, Work: 140, Footprint: 3f, Capacity: 0, ConstructibleByDefault: false),
        new("archive", Wood: 12, Stone: 12, Work: 180, Footprint: 4f, Capacity: 0, ConstructibleByDefault: false),
        new("wall", Wood: 0, Stone: 20, Work: 160, Footprint: 3f, Capacity: 0, ConstructibleByDefault: false),
        new("dock", Wood: 20, Stone: 0, Work: 160, Footprint: 4f, Capacity: 0, ConstructibleByDefault: false),
    ];

    /// <summary>Bit per kind index; the M3 constructible set.</summary>
    public static readonly ushort DefaultMask = ComputeDefaultMask();

    private readonly World _world;

    internal BuildingKinds(World world) => _world = world;

    /// <summary>Kind index for a kebab-case id, or -1.</summary>
    public static int IndexOf(string? id)
    {
        if (id is null) return -1;
        for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return i;
        return -1;
    }

    public static BuildingKind? Find(string? id) => IndexOf(id) is int i and >= 0 ? All[i] : null;

    /// <summary>
    /// Let an NPC (id ≥ 2^20, its settlement now and any it joins later) or a settlement (small id) build
    /// <paramref name="buildingId"/>. Unknown building ids and unknown targets are a no-op.
    /// Same shape as M4's <c>IBuildingUnlocks.Unlock</c>.
    /// </summary>
    public void Unlock(int id, string buildingId) => TryUnlock(id, buildingId);

    /// <summary><see cref="Unlock"/>, returning true when something changed.</summary>
    public bool TryUnlock(int id, string buildingId) => _world.UnlockBuilding(id, IndexOf(buildingId));

    /// <summary>True if the settlement may construct this kind.</summary>
    public bool IsUnlocked(int settlementId, string id) => _world.IsBuildingUnlocked(settlementId, IndexOf(id));

    private static ushort ComputeDefaultMask()
    {
        int m = 0;
        for (int i = 0; i < All.Length; i++) if (All[i].ConstructibleByDefault) m |= 1 << i;
        return (ushort)m;
    }
}

public enum CropStage : byte { Fallow, Growing, Ripe }

/// <summary>
/// Struct-of-arrays building store with a free list. A building with <see cref="Progress"/> &lt; 1 is a
/// construction site. Farm-plot crop state lives here too. Indices are the public building ids.
/// </summary>
public sealed class Buildings
{
    public int HighWater { get; private set; }
    public int Count { get; private set; }

    public bool[] Alive = new bool[32];
    public byte[] Kind = new byte[32];
    public float[] X = new float[32], Z = new float[32], Rotation = new float[32];
    public int[] Settlement = new int[32];
    public int[] Household = new int[32];
    public float[] Progress = new float[32];
    public float[] WorkDone = new float[32];
    public float[] NeedWood = new float[32], NeedStone = new float[32];
    public CropStage[] Crop = new CropStage[32];
    public float[] Growth = new float[32];
    public long[] TendedDay = new long[32];
    public long[] RipeDay = new long[32];

    private int[] _free = new int[16];
    private int _freeCount;

    public bool IsComplete(int b) => Alive[b] && Progress[b] >= 1f;

    public int Add(int kind, float x, float z, float rotation, int settlement, int household)
    {
        int b;
        if (_freeCount > 0) b = _free[--_freeCount];
        else
        {
            if (HighWater == Alive.Length) Grow();
            b = HighWater++;
        }
        var def = BuildingKinds.All[kind];
        Alive[b] = true;
        Kind[b] = (byte)kind;
        X[b] = x; Z[b] = z; Rotation[b] = rotation;
        Settlement[b] = settlement;
        Household[b] = household;
        Progress[b] = 0f;
        WorkDone[b] = 0f;
        NeedWood[b] = def.Wood;
        NeedStone[b] = def.Stone;
        Crop[b] = CropStage.Fallow;
        Growth[b] = 0f;
        TendedDay[b] = -1;
        RipeDay[b] = -1;
        Count++;
        return b;
    }

    /// <summary>Marks a building gone. Indices are never reused, so a building id stays stable for its lifetime and after.</summary>
    public void Remove(int b)
    {
        if (!Alive[b]) return;
        Alive[b] = false;
        Count--;
    }

    private void Grow()
    {
        int n = Alive.Length * 2;
        Array.Resize(ref Alive, n); Array.Resize(ref Kind, n);
        Array.Resize(ref X, n); Array.Resize(ref Z, n); Array.Resize(ref Rotation, n);
        Array.Resize(ref Settlement, n); Array.Resize(ref Household, n);
        Array.Resize(ref Progress, n); Array.Resize(ref WorkDone, n);
        Array.Resize(ref NeedWood, n); Array.Resize(ref NeedStone, n);
        Array.Resize(ref Crop, n); Array.Resize(ref Growth, n);
        Array.Resize(ref TendedDay, n); Array.Resize(ref RipeDay, n);
    }

    public ulong Hash(ulong h)
    {
        h = Eden.Sim.Hash.Mix(h, (ulong)HighWater);
        h = Eden.Sim.Hash.Mix(h, (ulong)Count);
        for (int i = 0; i < _freeCount; i++) h = Eden.Sim.Hash.Mix(h, (ulong)_free[i]);
        for (int b = 0; b < HighWater; b++)
        {
            if (!Alive[b]) continue;
            h = Eden.Sim.Hash.Mix(h, (ulong)b);
            h = Eden.Sim.Hash.Mix(h, Kind[b]);
            h = Eden.Sim.Hash.Mix(h, X[b]); h = Eden.Sim.Hash.Mix(h, Z[b]); h = Eden.Sim.Hash.Mix(h, Rotation[b]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Settlement[b]); h = Eden.Sim.Hash.Mix(h, (ulong)Household[b]);
            h = Eden.Sim.Hash.Mix(h, Progress[b]); h = Eden.Sim.Hash.Mix(h, WorkDone[b]);
            h = Eden.Sim.Hash.Mix(h, NeedWood[b]); h = Eden.Sim.Hash.Mix(h, NeedStone[b]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Crop[b]); h = Eden.Sim.Hash.Mix(h, Growth[b]);
            h = Eden.Sim.Hash.Mix(h, (ulong)TendedDay[b]); h = Eden.Sim.Hash.Mix(h, (ulong)RipeDay[b]);
        }
        return h;
    }

    public void Write(BinaryWriter w)
    {
        w.Write(HighWater); w.Write(Count); w.Write(_freeCount);
        for (int i = 0; i < _freeCount; i++) w.Write(_free[i]);
        for (int b = 0; b < HighWater; b++)
        {
            w.Write(Alive[b]);
            if (!Alive[b]) continue;
            w.Write(Kind[b]); w.Write(X[b]); w.Write(Z[b]); w.Write(Rotation[b]);
            w.Write(Settlement[b]); w.Write(Household[b]);
            w.Write(Progress[b]); w.Write(WorkDone[b]); w.Write(NeedWood[b]); w.Write(NeedStone[b]);
            w.Write((byte)Crop[b]); w.Write(Growth[b]); w.Write(TendedDay[b]); w.Write(RipeDay[b]);
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
        for (int b = 0; b < hw; b++)
        {
            Alive[b] = r.ReadBoolean();
            if (!Alive[b]) continue;
            Kind[b] = r.ReadByte(); X[b] = r.ReadSingle(); Z[b] = r.ReadSingle(); Rotation[b] = r.ReadSingle();
            Settlement[b] = r.ReadInt32(); Household[b] = r.ReadInt32();
            Progress[b] = r.ReadSingle(); WorkDone[b] = r.ReadSingle(); NeedWood[b] = r.ReadSingle(); NeedStone[b] = r.ReadSingle();
            Crop[b] = (CropStage)r.ReadByte(); Growth[b] = r.ReadSingle(); TendedDay[b] = r.ReadInt64(); RipeDay[b] = r.ReadInt64();
        }
    }
}

public sealed partial class World
{
    public Buildings Buildings { get; private set; } = new();

    /// <summary>Building registry and unlocks; M4 calls <c>BuildingKinds.Unlock</c>.</summary>
    public BuildingKinds BuildingKinds => _buildingKinds ??= new BuildingKinds(this);
    private BuildingKinds? _buildingKinds;

    /// <summary>Raised after a building is destroyed (argument: its id). M4 hooks record sites here.</summary>
    public event Action<int>? BuildingDestroyed;

    /// <summary>Destroy a building or site; its id is never reused. Households living in a destroyed hut keep their home spot.</summary>
    public void DestroyBuilding(int id)
    {
        if (id < 0 || id >= Buildings.HighWater || !Buildings.Alive[id]) return;
        for (int h = 0; h < Households.HighWater; h++)
            if (Households.Alive[h] && Households.Hut[h] == id) Households.Hut[h] = -1;
        Buildings.Remove(id);
        RefreshStores();
        BuildingDestroyed?.Invoke(id);
    }

    internal bool UnlockBuilding(int npcOrSettlement, int kind)
    {
        if (kind < 0) return false;
        ushort bit = (ushort)(1 << kind);
        if (npcOrSettlement >= 1 << Npcs.SlotBits)
        {
            if (!Npcs.TryGetSlot(npcOrSettlement, out int s)) return false;
            bool changed = (_unlocks[s] & bit) == 0;
            _unlocks[s] |= bit;
            int set = SettlementOfSlot(s);
            if (set >= 0) Settlements.Unlocked[set] |= bit;
            return changed;
        }
        if (!Settlements.IsAlive(npcOrSettlement)) return false;
        bool ch = (Settlements.Unlocked[npcOrSettlement] & bit) == 0;
        Settlements.Unlocked[npcOrSettlement] |= bit;
        return ch;
    }

    internal bool IsBuildingUnlocked(int settlement, int kind)
        => kind >= 0 && Settlements.IsAlive(settlement) && (Settlements.Unlocked[settlement] & (1 << kind)) != 0;

    /// <summary>Renderer view of every building and construction site. Not on the tick path.</summary>
    private BuildingView[] BuildingViews()
    {
        var b = Buildings;
        if (b.Count == 0) return Array.Empty<BuildingView>();
        var views = new BuildingView[b.Count];
        int k = 0;
        for (int i = 0; i < b.HighWater && k < views.Length; i++)
        {
            if (!b.Alive[i]) continue;
            views[k++] = new BuildingView(i, BuildingKinds.All[b.Kind[i]].Id, b.X[i], Terrain.HeightAt(b.X[i], b.Z[i]), b.Z[i],
                b.Rotation[i], b.Settlement[i], Math.Min(1f, b.Progress[i]));
        }
        return views;
    }

    // ---- construction -----------------------------------------------------------------------

    /// <summary>Daily: keep a few construction sites open per settlement, in priority order.</summary>
    private void PlanConstruction(int set)
    {
        var st = Settlements;
        var b = Buildings;
        int sites = 0, stores = 0, plots = 0, wells = 0;
        for (int i = 0; i < b.HighWater; i++)
        {
            if (!b.Alive[i] || b.Settlement[i] != set) continue;
            if (b.Progress[i] < 1f) sites++;
            switch (b.Kind[i])
            {
                case BuildingKinds.Storehouse: stores++; break;
                case BuildingKinds.FarmPlot: plots++; break;
                case BuildingKinds.Well: wells++; break;
            }
        }
        int maxSites = Math.Min(3, 1 + st.Households[set] / 4);
        if (sites >= maxSites) return;

        if (stores == 0 && TryPlaceNear(set, BuildingKinds.Storehouse, st.CenterX[set], st.CenterZ[set], 2f, 10f)) return;
        int wantPlots = Math.Min(Config.MaxFarmPlotsPerSettlement, (st.Members[set] + Config.PeoplePerFarmPlot - 1) / Config.PeoplePerFarmPlot);
        if (plots < wantPlots && Season(Clock.DayOfYear) != SeasonKind.Winter
            && TryPlaceNear(set, BuildingKinds.FarmPlot, st.CenterX[set], st.CenterZ[set], 12f, 24f + 2f * plots)) return;
        if (TryPlaceHut(set)) return;
        if (wells == 0 && st.Members[set] >= 12 && TryPlaceNear(set, BuildingKinds.Well, st.CenterX[set], st.CenterZ[set], 3f, 10f)) return;
        if (stores > 0 && st.Food[set] > 0.8f * FoodCapacity(set)
            && TryPlaceNear(set, BuildingKinds.Storehouse, st.CenterX[set], st.CenterZ[set], 4f, 14f)) return;
    }

    private bool TryPlaceHut(int set)
    {
        if (!IsBuildingUnlocked(set, BuildingKinds.Hut)) return false;
        var h = Households;
        for (int i = 0; i < h.HighWater; i++)
        {
            if (!h.Alive[i] || h.Settlement[i] != set || h.Hut[i] >= 0 || h.Members[i] == 0) continue;
            if (!SpotFree(h.HomeX[i], h.HomeZ[i], BuildingKinds.All[BuildingKinds.Hut].Footprint, -1)) continue;
            h.Hut[i] = Buildings.Add(BuildingKinds.Hut, h.HomeX[i], h.HomeZ[i], Rng.NextFloat() * MathF.Tau, set, i);
            return true;
        }
        return false;
    }

    private bool TryPlaceNear(int set, int kind, float cx, float cz, float rMin, float rMax)
    {
        if (!IsBuildingUnlocked(set, kind)) return false;
        float foot = BuildingKinds.All[kind].Footprint;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            float a = Rng.NextFloat() * MathF.Tau, r = rMin + Rng.NextFloat() * (rMax - rMin);
            float x = cx + MathF.Cos(a) * r, z = cz + MathF.Sin(a) * r;
            if (!BuildableAt(x, z)) continue;
            if (kind == BuildingKinds.FarmPlot && Terrain.Fertility[Terrain.CellIndex(x, z)] < 0.35f) continue;
            if (!SpotFree(x, z, foot, -1)) continue;
            Buildings.Add(kind, x, z, Rng.NextFloat() * MathF.Tau, set, -1);
            return true;
        }
        return false;
    }

    private bool BuildableAt(float x, float z)
    {
        if (x < 2f || z < 2f || x > Terrain.MaxX - 2f || z > Terrain.MaxZ - 2f) return false;
        int cell = Terrain.CellIndex(x, z);
        return Terrain.IsLand(cell) && Terrain.MoveCost[cell] < 2f && Terrain.Heights[cell] - Terrain.SeaLevel > 0.5f;
    }

    /// <summary>No building (other than <paramref name="ignore"/>) overlaps a disc of <paramref name="radius"/>.</summary>
    private bool SpotFree(float x, float z, float radius, int ignore)
    {
        var b = Buildings;
        for (int i = 0; i < b.HighWater; i++)
        {
            if (!b.Alive[i] || i == ignore) continue;
            float min = radius + BuildingKinds.All[b.Kind[i]].Footprint;
            if (Sq(b.X[i] - x) + Sq(b.Z[i] - z) < min * min) return false;
        }
        return true;
    }

    /// <summary>Nearest construction site of the settlement, or -1.</summary>
    private int FindSite(int set, float x, float z)
    {
        var b = Buildings;
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < b.HighWater; i++)
        {
            if (!b.Alive[i] || b.Settlement[i] != set || b.Progress[i] >= 1f) continue;
            float d = Sq(b.X[i] - x) + Sq(b.Z[i] - z);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Arrived at a site: unload carried material, draw the rest from stock, then labour if supplied.</summary>
    private void WorkOnSite(int s, int site)
    {
        var b = Buildings;
        if (site < 0 || !b.Alive[site] || b.Progress[site] >= 1f) return;
        int set = b.Settlement[site];
        if (_carryMat[s] > 0)
        {
            ref float need = ref _carryMatKind[s] == (byte)ResourceKind.Stone ? ref b.NeedStone[site] : ref b.NeedWood[site];
            float used = Math.Min(need, _carryMat[s]);
            need -= used;
            _carryMat[s] -= used;
            if (_carryMat[s] > 0 && Settlements.IsAlive(set))
            {
                // Left-overs go into the settlement's stock.
                if (_carryMatKind[s] == (byte)ResourceKind.Stone) Settlements.Stone[set] += _carryMat[s];
                else Settlements.Wood[set] += _carryMat[s];
                _carryMat[s] = 0;
            }
        }
        if (Settlements.IsAlive(set))
        {
            float w = Math.Min(b.NeedWood[site], Settlements.Wood[set]);
            b.NeedWood[site] -= w; Settlements.Wood[set] -= w;
            float st = Math.Min(b.NeedStone[site], Settlements.Stone[set]);
            b.NeedStone[site] -= st; Settlements.Stone[set] -= st;
        }
        if (b.NeedWood[site] > 0.01f || b.NeedStone[site] > 0.01f) return;
        float work = ActDuration(ActionKind.Build) * (0.7f + 0.6f * Npcs.Gene(s, Gene.Strength));
        b.WorkDone[site] += work;
        b.Progress[site] = Math.Min(1f, b.WorkDone[site] / BuildingKinds.All[b.Kind[site]].Work);
        Npcs.NeedRef(s, Need.Purpose) = Math.Min(1f, Npcs.Need(s, Need.Purpose) + 0.2f);
        if (b.Progress[site] >= 1f) OnBuildingCompleted(site, s);
    }

    private void OnBuildingCompleted(int site, int builder)
    {
        var b = Buildings;
        int set = b.Settlement[site];
        int kind = b.Kind[site];
        TotalBuildingsCompleted++;
        RefreshStores();
        if (kind is BuildingKinds.Storehouse or BuildingKinds.Well && Settlements.IsAlive(set))
        {
            string what = kind == BuildingKinds.Storehouse ? "a storehouse" : "a well";
            Chronicle(Tick, "building", $"{Npcs.Name[builder]} finished {what} in {Settlements.Name[set]}.", 3, [Npcs.IdOf(builder)]);
        }
    }

    /// <summary>A builder short of material fetches it from the nearest tree (or stone) and carries it to the site.</summary>
    private void FetchMaterial(int s)
    {
        int node = Npcs.TargetNode[s];
        if (node < 0 || Resources.Amount[node] < 1f) return;
        float take = MathF.Floor(Math.Min(Resources.Amount[node], 4f + 2f * Npcs.Gene(s, Gene.Strength)));
        Resources.Amount[node] -= take;
        _carryMat[s] = take;
        _carryMatKind[s] = (byte)Resources.Kind[node];
    }
}
