using System.Runtime.CompilerServices;
using Eden.Sim.Contracts;

[assembly: InternalsVisibleTo("Eden.Sim.Tests")]

namespace Eden.Sim;

/// <summary>A standing instruction from the settlement: carry goods, raid, move to a new home, or go back home.</summary>
public enum Order : byte { None, Trade, Raid, Migrate, Return }

/// <summary>
/// M3 society: per-NPC society state (household, job, traits, orders, path) and the hooks the M1 partials call.
/// Everything structural (households, settlements, factions, buildings, crops, stock, trade, raids) is updated
/// once per in-game day in <see cref="SocietyDaily"/>; per tick only movement along paths and the decision
/// context touch it, allocation-free.
/// </summary>
public sealed partial class World
{
    private const int MaxWaypoints = 24;
    private const float HomeRestRange = 80f;

    public Pathfinder Pathing { get; private set; } = null!;

    // ---- per-NPC society arrays, indexed by slot (sized to Npcs.Capacity) ---------------------
    private int[] _home = [];
    private byte[] _job = [];
    private float[] _traits = [];
    private bool[] _traitsSet = [];
    private int[] _targetBuilding = [];
    private int[] _storeTrip = [];
    private float[] _carryMat = [];
    private byte[] _carryMatKind = [];
    private float[] _cargo = [];
    private byte[] _order = [];
    private int[] _orderTarget = [];
    private long[] _orderUntil = [];
    private ushort[] _unlocks = [];
    private float[] _pathX = [], _pathZ = [];
    private int[] _pathLen = [], _pathPos = [];
    private bool[] _pathTrunc = [];

    // Per-settlement caches, rebuilt daily and when a building finishes (derived, not saved).
    private float[] _storeX = [], _storeZ = [];
    private int[] _storeCount = [], _wellOf = [];

    // ---- totals for the runner and tests (hashed and saved) -----------------------------------
    public int TotalTrades { get; private set; }
    public int TotalRaids { get; private set; }
    public int TotalRaidDeaths { get; private set; }
    public int TotalSettlementsFounded { get; private set; }
    public int LeadersChosen { get; private set; }
    public int TotalBuildingsCompleted { get; private set; }
    public float TotalHarvested { get; private set; }

    public int SettlementCount
    {
        get
        {
            int n = 0;
            for (int k = 0; k < Settlements.Count; k++) if (Settlements.Alive[k]) n++;
            return n;
        }
    }

    public Order OrderOfSlot(int s) => s < _order.Length ? (Order)_order[s] : Order.None;

    /// <summary>Personality as expressed: genes blended with upbringing once the NPC grows up.</summary>
    public float Trait(int s, Gene g) => _traits[s * Genetics.Count + (int)g];

    // ---- lifecycle hooks --------------------------------------------------------------------

    /// <summary>Called from the constructor before genesis spawns anyone.</summary>
    private void InitSociety()
    {
        Pathing = new Pathfinder(Terrain);
        Resources.Region = Pathing.Region;
        EnsureSocietySlots();
    }

    /// <summary>Terraforming changed walkability: recompute regions; stale paths are dropped.</summary>
    private void OnTerrainChanged()
    {
        Pathing.ComputeRegions();
        Array.Clear(_pathLen);
    }

    private void EnsureSocietySlots()
    {
        int n = Npcs.Capacity;
        if (_home.Length >= n) return;
        int old = _home.Length;
        Array.Resize(ref _home, n);
        Array.Resize(ref _job, n);
        Array.Resize(ref _traits, n * Genetics.Count);
        Array.Resize(ref _traitsSet, n);
        Array.Resize(ref _targetBuilding, n);
        Array.Resize(ref _storeTrip, n);
        Array.Resize(ref _carryMat, n);
        Array.Resize(ref _carryMatKind, n);
        Array.Resize(ref _cargo, n);
        Array.Resize(ref _order, n);
        Array.Resize(ref _orderTarget, n);
        Array.Resize(ref _orderUntil, n);
        Array.Resize(ref _unlocks, n);
        Array.Resize(ref _pathX, n * MaxWaypoints);
        Array.Resize(ref _pathZ, n * MaxWaypoints);
        Array.Resize(ref _pathLen, n);
        Array.Resize(ref _pathPos, n);
        Array.Resize(ref _pathTrunc, n);
        for (int s = old; s < n; s++) ResetSocietySlot(s);
    }

    private void ResetSocietySlot(int s)
    {
        _home[s] = -1;
        _job[s] = 0;
        _traitsSet[s] = false;
        _targetBuilding[s] = -1;
        _storeTrip[s] = -1;
        _carryMat[s] = 0;
        _carryMatKind[s] = 0;
        _cargo[s] = 0;
        _order[s] = 0;
        _orderTarget[s] = -1;
        _orderUntil[s] = 0;
        _unlocks[s] = 0;
        _pathLen[s] = _pathPos[s] = 0;
        _pathTrunc[s] = false;
    }

    /// <summary>Called after a spawn or birth has filled the slot (genes, parents, position).</summary>
    private void OnNpcCreated(int s, bool born)
    {
        EnsureSocietySlots();
        ResetSocietySlot(s);
        Npcs.GenesOf(s).CopyTo(_traits.AsSpan(s * Genetics.Count, Genetics.Count));
        _traitsSet[s] = !born;
        AssignInitialHousehold(s);
    }

    /// <summary>Base availability of M3 actions: work, trade and raid need a settlement; settling needs adulthood.</summary>
    private bool IsSocietyActionUnlocked(int slot, ActionKind action, bool adult) => action switch
    {
        ActionKind.Build or ActionKind.Farm or ActionKind.Trade or ActionKind.Raid => adult && SettlementOfSlot(slot) >= 0,
        ActionKind.Settle => adult,
        _ => false,
    };

    // ---- daily driver -----------------------------------------------------------------------

    private void SocietyDaily()
    {
        UpdateHouseholds();
        BlendTraits();
        ClusterSettlements();
        SettlementStats();
        RefreshStores();
        GrowCrops();
        var st = Settlements;
        for (int k = 0; k < st.Count; k++)
        {
            if (!st.Alive[k]) continue;
            SpoilStock(k);
            ChooseLeader(k);
            PlanConstruction(k);
            AssignJobs(k);
        }
        AccrueGrievance();
        for (int k = 0; k < st.Count; k++) if (st.Alive[k]) TryMigration(k);
        for (int k = 0; k < st.Count; k++) if (st.Alive[k]) PlanTrade(k);
        for (int k = 0; k < st.Count; k++) if (st.Alive[k]) PlanRaid(k);
        UpdateAlliances();
        for (int s = 0; s < Npcs.HighWater; s++)
            if (Npcs.Alive[s] && _order[s] != 0 && _orderUntil[s] <= Tick) ClearOrder(s);
        MythDaily(); // M7 (World.Myth.cs)
    }

    private void ClearOrder(int s)
    {
        if (_order[s] == (byte)Order.Trade && _cargo[s] > 0)
        {
            int own = SettlementOfSlot(s);
            if (own >= 0) AddFood(own, _cargo[s]);
        }
        _cargo[s] = 0;
        _order[s] = 0;
        _orderTarget[s] = -1;
    }

    /// <summary>Grown-ups' personality: genes blended with the parents who raised them, and with a hard childhood.</summary>
    private void BlendTraits()
    {
        var n = Npcs;
        float w = Config.UpbringingWeight;
        for (int s = 0; s < n.HighWater; s++)
        {
            if (!n.Alive[s] || _traitsSet[s] || StageOf(s) is not (LifeStage.Adult or LifeStage.Elder)) continue;
            _traitsSet[s] = true;
            bool hasM = n.TryGetSlot(n.Mother[s], out int m), hasF = n.TryGetSlot(n.Father[s], out int f);
            int tb = s * Genetics.Count;
            if (hasM || hasF)
            {
                for (int g = 0; g < Genetics.Count; g++)
                {
                    if (!IsPersonality((Gene)g)) continue;
                    float raised = hasM && hasF ? 0.5f * (_traits[m * Genetics.Count + g] + _traits[f * Genetics.Count + g])
                        : _traits[(hasM ? m : f) * Genetics.Count + g];
                    _traits[tb + g] = (1f - w) * n.Genes[tb + g] + w * raised;
                }
            }
            int set = SettlementOfSlot(s);
            if (set >= 0)
            {
                float grudge = 0f;
                for (int k = 0; k < Settlements.Count; k++) grudge = Math.Max(grudge, Settlements.Grievance(set, k));
                int a = tb + (int)Gene.Aggression;
                _traits[a] = Math.Clamp(_traits[a] + 0.1f * Math.Min(1f, grudge / 3f), 0f, 1f);
            }
        }
    }

    private static bool IsPersonality(Gene g) => g is Gene.Curiosity or Gene.Sociability or Gene.Aggression or Gene.Empathy
        or Gene.Patience or Gene.Ambition or Gene.Piety or Gene.Creativity;

    private void RefreshStores()
    {
        var st = Settlements;
        int n = Math.Max(16, st.Count);
        if (_storeX.Length < n)
        {
            Array.Resize(ref _storeX, n * 2); Array.Resize(ref _storeZ, n * 2);
            Array.Resize(ref _storeCount, n * 2); Array.Resize(ref _wellOf, n * 2);
        }
        for (int k = 0; k < st.Count; k++)
        {
            _storeX[k] = st.CenterX[k]; _storeZ[k] = st.CenterZ[k];
            _storeCount[k] = 0; _wellOf[k] = -1;
        }
        var b = Buildings;
        for (int i = 0; i < b.HighWater; i++)
        {
            int k = b.Settlement[i];
            if (!b.Alive[i] || k < 0 || b.Progress[i] < 1f) continue;
            if (b.Kind[i] == BuildingKinds.Storehouse)
            {
                if (_storeCount[k]++ == 0) { _storeX[k] = b.X[i]; _storeZ[k] = b.Z[i]; }
            }
            else if (b.Kind[i] == BuildingKinds.Well && _wellOf[k] < 0) _wellOf[k] = i;
        }
    }

    // ---- decision hooks (World.Behaviour.cs) ------------------------------------------------

    /// <summary>Adds traits, store food, wells, jobs and orders to the decision context. Allocation-free.</summary>
    private void SocietyContext(int s, LifeStage stage, ref DecisionContext c, ref Targets t)
    {
        var n = Npcs;
        int tb = s * Genetics.Count;
        c.Curiosity = _traits[tb + (int)Gene.Curiosity];
        c.Sociability = _traits[tb + (int)Gene.Sociability];
        c.Aggression = _traits[tb + (int)Gene.Aggression];
        c.Empathy = _traits[tb + (int)Gene.Empathy];
        c.Piety = _traits[tb + (int)Gene.Piety];
        c.Ambition = _traits[tb + (int)Gene.Ambition];
        t.Store = t.Site = t.MatNode = t.Plot = -1;

        bool adult = stage is LifeStage.Adult or LifeStage.Elder;
        float x = n.X[s], z = n.Z[s];
        int home = _home[s];
        float homeDist = home >= 0 ? Dist(x, z, Households.HomeX[home], Households.HomeZ[home]) : 0f;
        if (_order[s] != 0 && _orderUntil[s] <= Tick) ClearOrder(s);
        switch ((Order)_order[s])
        {
            case Order.Trade: c.TradeOrder = adult && Settlements.IsAlive(_orderTarget[s]); break;
            case Order.Raid: c.RaidOrder = adult && Settlements.IsAlive(_orderTarget[s]); break;
            case Order.Migrate: c.HomeFar = homeDist > 12f; if (!c.HomeFar) _order[s] = 0; break;
            case Order.Return: c.HomeFar = homeDist > 40f; if (!c.HomeFar) _order[s] = 0; break;
            default: c.HomeFar = adult && homeDist > 150f; break;
        }

        int set = SettlementOfSlot(s);
        if (set < 0) return;
        float sx = _storeX[set], sz = _storeZ[set];
        float sd = Dist(x, z, sx, sz);
        DepositAtStore(s, set, sd);
        c.CarriedFood = n.Food[s];
        if (n.Food[s] < 2f && Settlements.Food[set] >= 1f && sd < Config.SearchRadius && (!c.FoodKnown || sd < c.FoodDistance))
        {
            c.FoodKnown = true;
            c.FoodDistance = sd;
            t.Store = set;
        }
        int well = _wellOf[set];
        if (well >= 0)
        {
            float wd = Dist(x, z, Buildings.X[well], Buildings.Z[well]);
            float cur = t.WaterCell >= 0 ? Dist(x, z, Terrain.CellX(t.WaterCell), Terrain.CellZ(t.WaterCell)) : float.MaxValue;
            if (wd < 60f && wd < cur) { t.WaterCell = Terrain.CellIndex(Buildings.X[well], Buildings.Z[well]); c.WaterKnown = true; }
        }
        if (!adult) return;

        var job = (Job)_job[s];
        c.IsBuilder = job == Job.Builder;
        c.IsFarmer = job == Job.Farmer;
        int site = FindSite(set, x, z);
        if (site >= 0)
        {
            var b = Buildings;
            bool supplied = (b.NeedWood[site] <= 0.01f || Settlements.Wood[set] >= b.NeedWood[site])
                && (b.NeedStone[site] <= 0.01f || Settlements.Stone[set] >= b.NeedStone[site]);
            if (supplied || _carryMat[s] > 0)
            {
                t.Site = site;
                c.BuildWork = true;
                c.BuildDistance = Dist(x, z, b.X[site], b.Z[site]);
            }
            else if (c.IsBuilder)
            {
                var kind = b.NeedWood[site] > 0.01f ? ResourceKind.Tree : ResourceKind.Stone;
                int node = Resources.FindNearest(kind, b.X[site], b.Z[site], Config.SearchRadius * 1.5f, 1f, n.AvoidNode[s], reachable: true);
                if (node >= 0)
                {
                    t.Site = site;
                    t.MatNode = node;
                    c.BuildWork = true;
                    c.BuildDistance = Dist(x, z, Resources.X[node], Resources.Z[node]);
                }
            }
        }
        int plot = FindFarmWork(set, x, z);
        if (plot >= 0)
        {
            t.Plot = plot;
            c.FarmWork = true;
            c.FarmDistance = Dist(x, z, Buildings.X[plot], Buildings.Z[plot]);
        }
    }

    /// <summary>Starts the M3 actions and the M3 variants of M1 actions. Returns true if it handled <paramref name="action"/>.</summary>
    private bool BeginSociety(int s, ActionKind action, in Targets t, LifeStage stage)
    {
        var n = Npcs;
        _storeTrip[s] = -1;
        _targetBuilding[s] = -1;
        _pathLen[s] = 0;
        int home = _home[s];
        switch (action)
        {
            case ActionKind.Gather when t.Store >= 0:
                _storeTrip[s] = t.Store;
                MoveTo(s, _storeX[t.Store], _storeZ[t.Store]);
                return true;
            case ActionKind.Rest when home >= 0 && stage != LifeStage.Infant:
            {
                float hx = Households.HomeX[home], hz = Households.HomeZ[home];
                float d = Dist(n.X[s], n.Z[s], hx, hz);
                if (d < 6f || d > HomeRestRange) return false;
                MoveTo(s, hx, hz);
                return true;
            }
            case ActionKind.Build when t.Site >= 0:
                _targetBuilding[s] = t.Site;
                if (t.MatNode >= 0 && _carryMat[s] <= 0)
                {
                    n.TargetNode[s] = t.MatNode;
                    MoveTo(s, Resources.X[t.MatNode], Resources.Z[t.MatNode]);
                }
                else MoveTo(s, Buildings.X[t.Site], Buildings.Z[t.Site]);
                return true;
            case ActionKind.Farm when t.Plot >= 0:
                _targetBuilding[s] = t.Plot;
                MoveTo(s, Buildings.X[t.Plot], Buildings.Z[t.Plot]);
                return true;
            case ActionKind.Trade or ActionKind.Raid when Settlements.IsAlive(_orderTarget[s]):
                MoveTo(s, _storeX[_orderTarget[s]], _storeZ[_orderTarget[s]]);
                return true;
            case ActionKind.Settle when home >= 0:
                MoveTo(s, Households.HomeX[home], Households.HomeZ[home]);
                return true;
            case ActionKind.Build or ActionKind.Farm or ActionKind.Trade or ActionKind.Raid or ActionKind.Settle:
                Act(s, 1); // nothing to do after all (e.g. a goal intention without a target)
                return true;
        }
        return false;
    }

    /// <summary>Finishes the M3 actions. Returns true if it handled the NPC's current action.</summary>
    private bool CompleteSociety(int s, LifeStage stage)
    {
        var n = Npcs;
        switch (n.Action[s])
        {
            case ActionKind.Gather when _storeTrip[s] >= 0:
                WithdrawFromStore(s, _storeTrip[s]);
                _storeTrip[s] = -1;
                return true;
            case ActionKind.Build:
                if (_targetBuilding[s] < 0) return true;
                if (n.TargetNode[s] >= 0 && _carryMat[s] <= 0) FetchMaterial(s);
                else WorkOnSite(s, _targetBuilding[s]);
                return true;
            case ActionKind.Farm:
                if (_targetBuilding[s] >= 0) WorkPlot(s, _targetBuilding[s]);
                return true;
            case ActionKind.Trade:
                if (_order[s] == (byte)Order.Trade) CompleteTrade(s);
                return true;
            case ActionKind.Raid:
                if (_order[s] == (byte)Order.Raid) CompleteRaid(s);
                return true;
            case ActionKind.Settle:
                if (_order[s] is (byte)Order.Migrate or (byte)Order.Return) _order[s] = 0;
                n.NeedRef(s, Need.Purpose) = Math.Min(1f, n.Need(s, Need.Purpose) + 0.1f);
                n.NeedRef(s, Need.Safety) = Math.Min(1f, n.Need(s, Need.Safety) + 0.1f);
                return true;
        }
        return false;
    }

    // ---- pathing hooks ----------------------------------------------------------------------

    /// <summary>Called by MoveTo: if the straight line is blocked, plan an A* path and size the walking budget to it.</summary>
    private void PlanPath(int s, float tx, float tz)
    {
        _pathLen[s] = _pathPos[s] = 0;
        _pathTrunc[s] = false;
        float x = Npcs.X[s], z = Npcs.Z[s];
        if (Terrain.LineWalkable(x, z, tx, tz)) return;
        int b = s * MaxWaypoints;
        int k = Pathing.Find(x, z, tx, tz, _pathX.AsSpan(b, MaxWaypoints), _pathZ.AsSpan(b, MaxWaypoints), MaxWaypoints,
            out float length, out bool truncated);
        if (k <= 0) return;
        _pathLen[s] = k;
        _pathTrunc[s] = truncated;
        Npcs.ActTicks[s] = Math.Max(Npcs.ActTicks[s], (int)(length / (Config.WalkSpeed * 0.25f)) + 30);
    }

    /// <summary>Called first in Step: walk toward the next waypoint. Returns false once the path is used up.</summary>
    private bool FollowPath(int s, LifeStage stage)
    {
        if (_pathPos[s] >= _pathLen[s]) return false;
        var n = Npcs;
        int i = s * MaxWaypoints + _pathPos[s];
        float x = n.X[s], z = n.Z[s];
        float dx = _pathX[i] - x, dz = _pathZ[i] - z;
        float d = MathF.Sqrt(dx * dx + dz * dz);
        if (d > 0.01f)
        {
            float step = Math.Min(Speed(s, stage) / Terrain.MoveCost[Terrain.CellIndex(x, z)], d);
            float nx = x + dx / d * step, nz = z + dz / d * step;
            if (!Terrain.IsWalkable(nx, nz)) { _pathLen[s] = 0; return false; }
            n.Heading[s] = MathF.Atan2(dx, dz);
            n.X[s] = nx;
            n.Z[s] = nz;
            d -= step;
        }
        if (d <= 1f && ++_pathPos[s] >= _pathLen[s] && _pathTrunc[s])
        {
            int budget = n.ActTicks[s];
            PlanPath(s, n.TargetX[s], n.TargetZ[s]);
            n.ActTicks[s] = budget;
        }
        return true;
    }

    // ---- test hooks -------------------------------------------------------------------------

    /// <summary>Tests: run the once-a-day society update now.</summary>
    internal void RunSocietyDayForTests() => SocietyDaily();

    /// <summary>Tests: walk one NPC toward a point with the real movement code (A* included), nothing else ticking.</summary>
    internal bool WalkForTests(int s, float x, float z, int maxTicks)
    {
        var stage = StageOf(s);
        Npcs.Action[s] = ActionKind.Explore;
        MoveTo(s, x, z);
        for (int i = 0; i < maxTicks; i++)
        {
            if (Npcs.Phase[s] != ActionPhase.Moving) return false;
            if (Step(s, stage, x, z, ArriveDistance)) return true;
        }
        return false;
    }

    // ---- hashing and persistence ------------------------------------------------------------

    private ulong SocietyHash(ulong h)
    {
        h = Households.Hash(h);
        h = Settlements.Hash(h);
        h = Factions.Hash(h);
        h = Buildings.Hash(h);
        h = Hash.Mix(h, (ulong)TotalTrades);
        h = Hash.Mix(h, (ulong)TotalRaids);
        h = Hash.Mix(h, (ulong)TotalRaidDeaths);
        h = Hash.Mix(h, (ulong)TotalSettlementsFounded);
        h = Hash.Mix(h, (ulong)LeadersChosen);
        h = Hash.Mix(h, (ulong)TotalBuildingsCompleted);
        h = Hash.Mix(h, TotalHarvested);
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            h = Hash.Mix(h, (ulong)_home[s]);
            h = Hash.Mix(h, _job[s]);
            for (int g = 0; g < Genetics.Count; g++) h = Hash.Mix(h, _traits[s * Genetics.Count + g]);
            h = Hash.Mix(h, _traitsSet[s] ? 1UL : 0UL);
            h = Hash.Mix(h, (ulong)_targetBuilding[s]);
            h = Hash.Mix(h, (ulong)_storeTrip[s]);
            h = Hash.Mix(h, _carryMat[s]);
            h = Hash.Mix(h, _carryMatKind[s]);
            h = Hash.Mix(h, _cargo[s]);
            h = Hash.Mix(h, _order[s]);
            h = Hash.Mix(h, (ulong)_orderTarget[s]);
            h = Hash.Mix(h, (ulong)_orderUntil[s]);
            h = Hash.Mix(h, _unlocks[s]);
            h = Hash.Mix(h, (ulong)_pathLen[s]);
            h = Hash.Mix(h, (ulong)_pathPos[s]);
            h = Hash.Mix(h, _pathTrunc[s] ? 1UL : 0UL);
            for (int i = 0; i < _pathLen[s]; i++)
            {
                h = Hash.Mix(h, _pathX[s * MaxWaypoints + i]);
                h = Hash.Mix(h, _pathZ[s * MaxWaypoints + i]);
            }
        }
        return h;
    }

    private void WriteSociety(BinaryWriter w)
    {
        Households.Write(w);
        Settlements.Write(w);
        Factions.Write(w);
        Buildings.Write(w);
        w.Write(TotalTrades); w.Write(TotalRaids); w.Write(TotalRaidDeaths); w.Write(TotalSettlementsFounded);
        w.Write(LeadersChosen); w.Write(TotalBuildingsCompleted); w.Write(TotalHarvested);
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            w.Write(_home[s]); w.Write(_job[s]);
            for (int g = 0; g < Genetics.Count; g++) w.Write(_traits[s * Genetics.Count + g]);
            w.Write(_traitsSet[s]); w.Write(_targetBuilding[s]); w.Write(_storeTrip[s]);
            w.Write(_carryMat[s]); w.Write(_carryMatKind[s]); w.Write(_cargo[s]);
            w.Write(_order[s]); w.Write(_orderTarget[s]); w.Write(_orderUntil[s]); w.Write(_unlocks[s]);
            w.Write(_pathLen[s]); w.Write(_pathPos[s]); w.Write(_pathTrunc[s]);
            for (int i = 0; i < _pathLen[s]; i++) { w.Write(_pathX[s * MaxWaypoints + i]); w.Write(_pathZ[s * MaxWaypoints + i]); }
        }
    }

    /// <summary>Loads the SOCI section, or (for saves from before M3) starts society from scratch.</summary>
    private void ReadSociety(BinaryReader? r)
    {
        Pathing = new Pathfinder(Terrain);
        Resources.Region = Pathing.Region;
        Households = new Households();
        Settlements = new Settlements();
        Factions = new Factions();
        Buildings = new Buildings();
        _home = [];
        EnsureSocietySlots();
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            ResetSocietySlot(s);
            if (Npcs.Alive[s]) Npcs.GenesOf(s).CopyTo(_traits.AsSpan(s * Genetics.Count, Genetics.Count));
        }
        TotalTrades = TotalRaids = TotalRaidDeaths = TotalSettlementsFounded = LeadersChosen = TotalBuildingsCompleted = 0;
        TotalHarvested = 0;
        if (r is null)
        {
            for (int s = 0; s < Npcs.HighWater; s++) if (Npcs.Alive[s]) { _traitsSet[s] = true; AssignInitialHousehold(s); }
            RefreshStores();
            return;
        }
        Households.Read(r);
        Settlements.Read(r);
        Factions.Read(r);
        Buildings.Read(r);
        TotalTrades = r.ReadInt32(); TotalRaids = r.ReadInt32(); TotalRaidDeaths = r.ReadInt32(); TotalSettlementsFounded = r.ReadInt32();
        LeadersChosen = r.ReadInt32(); TotalBuildingsCompleted = r.ReadInt32(); TotalHarvested = r.ReadSingle();
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            _home[s] = r.ReadInt32(); _job[s] = r.ReadByte();
            for (int g = 0; g < Genetics.Count; g++) _traits[s * Genetics.Count + g] = r.ReadSingle();
            _traitsSet[s] = r.ReadBoolean(); _targetBuilding[s] = r.ReadInt32(); _storeTrip[s] = r.ReadInt32();
            _carryMat[s] = r.ReadSingle(); _carryMatKind[s] = r.ReadByte(); _cargo[s] = r.ReadSingle();
            _order[s] = r.ReadByte(); _orderTarget[s] = r.ReadInt32(); _orderUntil[s] = r.ReadInt64(); _unlocks[s] = r.ReadUInt16();
            _pathLen[s] = r.ReadInt32(); _pathPos[s] = r.ReadInt32(); _pathTrunc[s] = r.ReadBoolean();
            for (int i = 0; i < _pathLen[s]; i++) { _pathX[s * MaxWaypoints + i] = r.ReadSingle(); _pathZ[s * MaxWaypoints + i] = r.ReadSingle(); }
        }
        _centerAcc = new (float, float)[Math.Max(16, Settlements.Count * 2)];
        RefreshStores();
    }
}
