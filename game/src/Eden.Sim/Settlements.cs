using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// Settlements: clusters of households. Ids are never reused (a settlement's id stays meaningful in the
/// Chronicle and in grievances after it is abandoned). Shared stock (food, wood, stone) lives here.
/// Grievance is a dense from × to matrix so daily decay never iterates a dictionary.
/// </summary>
public sealed class Settlements
{
    public int Count { get; private set; }

    public bool[] Alive = new bool[16];
    public string[] Name = new string[16];
    public float[] CenterX = new float[16], CenterZ = new float[16];
    public int[] Faction = new int[16];
    public int[] Leader = new int[16];
    public long[] Founded = new long[16];
    public int[] Members = new int[16], Adults = new int[16], Households = new int[16];
    public float[] Food = new float[16], Wood = new float[16], Stone = new float[16];
    public ushort[] Unlocked = new ushort[16];
    public long[] LastMigration = new long[16], LastTrade = new long[16], LastRaid = new long[16];

    /// <summary>The raid this settlement launched last: target settlement, or -1; and whether it has landed.</summary>
    public int[] RaidTarget = new int[16];
    public bool[] RaidLanded = new bool[16];

    /// <summary>Daily statistics (not saved separately: recomputed before use, but hashed for safety).</summary>
    public float[] Hungry = new float[16], Aggression = new float[16];

    private float[] _grievance = new float[16 * 16];
    private int _cap = 16;

    public bool IsAlive(int id) => id >= 0 && id < Count && Alive[id];

    public float Grievance(int from, int to) => _grievance[from * _cap + to];
    public ref float GrievanceRef(int from, int to) => ref _grievance[from * _cap + to];

    public int Add(string name, float x, float z, long tick)
    {
        if (Count == _cap) Grow();
        int id = Count++;
        Alive[id] = true;
        Name[id] = name;
        CenterX[id] = x; CenterZ[id] = z;
        Faction[id] = -1;
        Leader[id] = Npcs.None;
        Founded[id] = tick;
        Members[id] = Adults[id] = Households[id] = 0;
        Food[id] = Wood[id] = Stone[id] = 0;
        Unlocked[id] = BuildingKinds.DefaultMask;
        LastMigration[id] = tick;
        LastTrade[id] = LastRaid[id] = long.MinValue / 2;
        RaidTarget[id] = -1;
        RaidLanded[id] = false;
        Hungry[id] = Aggression[id] = 0;
        return id;
    }

    /// <summary>Daily decay of every grievance.</summary>
    public void DecayGrievances(float factor)
    {
        for (int a = 0; a < Count; a++)
        for (int b = 0; b < Count; b++)
            _grievance[a * _cap + b] *= factor;
    }

    private void Grow()
    {
        int n = _cap * 2;
        Array.Resize(ref Alive, n); Array.Resize(ref Name, n);
        Array.Resize(ref CenterX, n); Array.Resize(ref CenterZ, n);
        Array.Resize(ref Faction, n); Array.Resize(ref Leader, n); Array.Resize(ref Founded, n);
        Array.Resize(ref Members, n); Array.Resize(ref Adults, n); Array.Resize(ref Households, n);
        Array.Resize(ref Food, n); Array.Resize(ref Wood, n); Array.Resize(ref Stone, n);
        Array.Resize(ref Unlocked, n);
        Array.Resize(ref LastMigration, n); Array.Resize(ref LastTrade, n); Array.Resize(ref LastRaid, n);
        Array.Resize(ref RaidTarget, n); Array.Resize(ref RaidLanded, n);
        Array.Resize(ref Hungry, n); Array.Resize(ref Aggression, n);
        var g = new float[n * n];
        for (int a = 0; a < _cap; a++)
        for (int b = 0; b < _cap; b++)
            g[a * n + b] = _grievance[a * _cap + b];
        _grievance = g;
        _cap = n;
    }

    public ulong Hash(ulong h)
    {
        h = Eden.Sim.Hash.Mix(h, (ulong)Count);
        for (int i = 0; i < Count; i++)
        {
            h = Eden.Sim.Hash.Mix(h, Alive[i] ? 1UL : 0UL);
            h = Eden.Sim.Hash.Mix(h, StableHash.Of(Name[i]));
            if (!Alive[i]) continue;
            h = Eden.Sim.Hash.Mix(h, CenterX[i]); h = Eden.Sim.Hash.Mix(h, CenterZ[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Faction[i]); h = Eden.Sim.Hash.Mix(h, (ulong)Leader[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Founded[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Members[i]); h = Eden.Sim.Hash.Mix(h, (ulong)Adults[i]); h = Eden.Sim.Hash.Mix(h, (ulong)Households[i]);
            h = Eden.Sim.Hash.Mix(h, Food[i]); h = Eden.Sim.Hash.Mix(h, Wood[i]); h = Eden.Sim.Hash.Mix(h, Stone[i]);
            h = Eden.Sim.Hash.Mix(h, Unlocked[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)LastMigration[i]); h = Eden.Sim.Hash.Mix(h, (ulong)LastTrade[i]); h = Eden.Sim.Hash.Mix(h, (ulong)LastRaid[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)RaidTarget[i]); h = Eden.Sim.Hash.Mix(h, RaidLanded[i] ? 1UL : 0UL);
            h = Eden.Sim.Hash.Mix(h, Hungry[i]); h = Eden.Sim.Hash.Mix(h, Aggression[i]);
        }
        for (int a = 0; a < Count; a++)
        for (int b = 0; b < Count; b++)
            h = Eden.Sim.Hash.Mix(h, _grievance[a * _cap + b]);
        return h;
    }

    public void Write(BinaryWriter w)
    {
        w.Write(Count);
        for (int i = 0; i < Count; i++)
        {
            w.Write(Alive[i]); w.Write(Name[i]);
            w.Write(CenterX[i]); w.Write(CenterZ[i]); w.Write(Faction[i]); w.Write(Leader[i]); w.Write(Founded[i]);
            w.Write(Members[i]); w.Write(Adults[i]); w.Write(Households[i]);
            w.Write(Food[i]); w.Write(Wood[i]); w.Write(Stone[i]); w.Write(Unlocked[i]);
            w.Write(LastMigration[i]); w.Write(LastTrade[i]); w.Write(LastRaid[i]);
            w.Write(RaidTarget[i]); w.Write(RaidLanded[i]); w.Write(Hungry[i]); w.Write(Aggression[i]);
        }
        for (int a = 0; a < Count; a++)
        for (int b = 0; b < Count; b++)
            w.Write(_grievance[a * _cap + b]);
    }

    public void Read(BinaryReader r)
    {
        int count = r.ReadInt32();
        while (_cap < count) Grow();
        Array.Clear(_grievance);
        Count = count;
        for (int i = 0; i < count; i++)
        {
            Alive[i] = r.ReadBoolean(); Name[i] = r.ReadString();
            CenterX[i] = r.ReadSingle(); CenterZ[i] = r.ReadSingle(); Faction[i] = r.ReadInt32(); Leader[i] = r.ReadInt32(); Founded[i] = r.ReadInt64();
            Members[i] = r.ReadInt32(); Adults[i] = r.ReadInt32(); Households[i] = r.ReadInt32();
            Food[i] = r.ReadSingle(); Wood[i] = r.ReadSingle(); Stone[i] = r.ReadSingle(); Unlocked[i] = r.ReadUInt16();
            LastMigration[i] = r.ReadInt64(); LastTrade[i] = r.ReadInt64(); LastRaid[i] = r.ReadInt64();
            RaidTarget[i] = r.ReadInt32(); RaidLanded[i] = r.ReadBoolean(); Hungry[i] = r.ReadSingle(); Aggression[i] = r.ReadSingle();
        }
        for (int a = 0; a < count; a++)
        for (int b = 0; b < count; b++)
            _grievance[a * _cap + b] = r.ReadSingle();
    }
}

public sealed partial class World
{
    public Settlements Settlements { get; private set; } = new();

    private static readonly string[] PlacePrefixes =
        ["Ash", "Oak", "Fen", "Brook", "Stone", "Elder", "Wil", "Thorn", "Mar", "Hol", "Rav", "Sun", "Elm", "Bryn", "Cal", "Dun"];
    private static readonly string[] PlaceSuffixes =
        ["ford", "holm", "stead", "by", "wick", "ton", "mere", "dale", "fell", "haven", "ridge", "well"];

    /// <summary>Settlement of a living NPC slot (through its household), or -1.</summary>
    public int SettlementOfSlot(int s)
    {
        int h = s < _home.Length ? _home[s] : -1;
        return h >= 0 && Households.Alive[h] ? Households.Settlement[h] : -1;
    }

    /// <summary>Where a settlement keeps its stock: its first finished storehouse, else its centre.</summary>
    private (float x, float z) StorePoint(int set) => (_storeX[set], _storeZ[set]);

    /// <summary>
    /// Daily clustering. Households keep their settlement while their home stays within 2 × radius of its
    /// centre; unassigned households join the nearest settlement within 1.5 × radius; the rest are clustered
    /// DBSCAN-style (eps = radius, min 2 households) and a cluster with enough people founds a settlement.
    /// </summary>
    private void ClusterSettlements()
    {
        var hh = Households;
        var st = Settlements;
        float eps = Config.SettlementRadius;
        for (int i = 0; i < hh.HighWater; i++)
        {
            if (!hh.Alive[i]) continue;
            int set = hh.Settlement[i];
            if (set >= 0 && (!st.IsAlive(set) || Sq(hh.HomeX[i] - st.CenterX[set]) + Sq(hh.HomeZ[i] - st.CenterZ[set]) > 4f * eps * eps))
                hh.Settlement[i] = set = -1;
            if (set >= 0) continue;
            float best = 2.25f * eps * eps;
            for (int k = 0; k < st.Count; k++)
            {
                if (!st.Alive[k]) continue;
                float d = Sq(hh.HomeX[i] - st.CenterX[k]) + Sq(hh.HomeZ[i] - st.CenterZ[k]);
                if (d < best) { best = d; hh.Settlement[i] = k; }
            }
        }

        // DBSCAN over the still-unassigned households.
        RebuildHouseholdBins();
        _clusterOf.Clear();
        for (int i = 0; i < hh.HighWater; i++) _clusterOf.Add(-1);
        int clusters = 0;
        for (int i = 0; i < hh.HighWater; i++)
        {
            if (!hh.Alive[i] || hh.Settlement[i] >= 0 || _clusterOf[i] >= 0) continue;
            if (CountUnassignedNeighbours(i, eps) < 1) continue;
            int c = clusters++;
            _queue.Clear();
            _queue.Add(i);
            _clusterOf[i] = c;
            for (int q = 0; q < _queue.Count; q++)
            {
                int a = _queue[q];
                bool core = CountUnassignedNeighbours(a, eps) >= 1;
                if (!core) continue;
                HouseholdBinRange(hh.HomeX[a], hh.HomeZ[a], out int bx0, out int bx1, out int bz0, out int bz1);
                for (int bz = bz0; bz <= bz1; bz++)
                for (int bx = bx0; bx <= bx1; bx++)
                for (int j = _hhHead[bz * _hhBinsX + bx]; j >= 0; j = _hhNext[j])
                {
                    if (!hh.Alive[j] || hh.Settlement[j] >= 0 || _clusterOf[j] >= 0) continue;
                    if (Sq(hh.HomeX[j] - hh.HomeX[a]) + Sq(hh.HomeZ[j] - hh.HomeZ[a]) > eps * eps) continue;
                    _clusterOf[j] = c;
                    _queue.Add(j);
                }
            }
        }
        for (int c = 0; c < clusters; c++)
        {
            int households = 0, members = 0, first = -1, from = -1;
            float sx = 0, sz = 0;
            for (int i = 0; i < hh.HighWater; i++)
            {
                if (_clusterOf[i] != c) continue;
                households++;
                members += hh.Members[i];
                sx += hh.HomeX[i]; sz += hh.HomeZ[i];
                if (first < 0) first = i;
                if (hh.MigratedFrom[i] >= 0) from = hh.MigratedFrom[i];
            }
            if (households < 2 || members < Config.SettlementMinMembers) continue;
            FoundSettlement(c, sx / households, sz / households, first, from);
        }

        // Abandon empty settlements; recentre the rest.
        for (int k = 0; k < st.Count; k++) { if (st.Alive[k]) { st.Households[k] = 0; st.Members[k] = 0; } }
        for (int k = 0; k < st.Count; k++) _centerAcc[k] = default;
        for (int i = 0; i < hh.HighWater; i++)
        {
            if (!hh.Alive[i] || hh.Settlement[i] < 0) continue;
            int k = hh.Settlement[i];
            st.Households[k]++;
            st.Members[k] += hh.Members[i];
            _centerAcc[k] = (_centerAcc[k].x + hh.HomeX[i], _centerAcc[k].z + hh.HomeZ[i]);
        }
        for (int k = 0; k < st.Count; k++)
        {
            if (!st.Alive[k]) continue;
            if (st.Households[k] == 0) { Abandon(k); continue; }
            st.CenterX[k] = _centerAcc[k].x / st.Households[k];
            st.CenterZ[k] = _centerAcc[k].z / st.Households[k];
        }
    }

    private readonly List<int> _clusterOf = new();
    private readonly List<int> _queue = new();
    private (float x, float z)[] _centerAcc = new (float, float)[16];

    private int CountUnassignedNeighbours(int i, float eps)
    {
        var hh = Households;
        int n = 0;
        HouseholdBinRange(hh.HomeX[i], hh.HomeZ[i], out int bx0, out int bx1, out int bz0, out int bz1);
        for (int bz = bz0; bz <= bz1; bz++)
        for (int bx = bx0; bx <= bx1; bx++)
        for (int j = _hhHead[bz * _hhBinsX + bx]; j >= 0; j = _hhNext[j])
        {
            if (j == i || !hh.Alive[j] || hh.Settlement[j] >= 0) continue;
            if (Sq(hh.HomeX[j] - hh.HomeX[i]) + Sq(hh.HomeZ[j] - hh.HomeZ[i]) <= eps * eps) n++;
        }
        return n;
    }

    private void FoundSettlement(int cluster, float cx, float cz, int firstHousehold, int migratedFrom)
    {
        var st = Settlements;
        var hh = Households;
        string name = PlacePrefixes[Rng.NextInt(PlacePrefixes.Length)] + PlaceSuffixes[Rng.NextInt(PlaceSuffixes.Length)];
        for (int k = 0; k < st.Count; k++) if (st.Name[k] == name) { name += " II"; break; }
        int id = st.Add(name, cx, cz, Tick);
        while (_centerAcc.Length < st.Count) Array.Resize(ref _centerAcc, _centerAcc.Length * 2);
        for (int i = 0; i < hh.HighWater; i++)
        {
            if (_clusterOf[i] != cluster) continue;
            hh.Settlement[i] = id;
            hh.MigratedFrom[i] = -1;
        }
        // Ruins inside the new settlement are reclaimed.
        var b = Buildings;
        float r2 = Sq(Config.SettlementRadius);
        for (int i = 0; i < b.HighWater; i++)
            if (b.Alive[i] && b.Settlement[i] < 0 && Sq(b.X[i] - cx) + Sq(b.Z[i] - cz) <= r2) b.Settlement[i] = id;
        // Unlocks carried by the founders (M4 knowledge) come with them.
        for (int s = 0; s < Npcs.HighWater; s++)
            if (Npcs.Alive[s] && SettlementOfSlot(s) == id) st.Unlocked[id] |= _unlocks[s];

        int founder = hh.HeadA[firstHousehold];
        string who = Npcs.TryGetSlot(founder, out int fs) ? Npcs.Name[fs] + "'s people" : "A few families";
        string text = st.IsAlive(migratedFrom)
            ? $"{who}, who left {st.Name[migratedFrom]}, founded {name}."
            : $"{who} founded the settlement of {name}.";
        TotalSettlementsFounded++;
        Chronicle(Tick, "settlement", text, 7, founder != Npcs.None ? [founder] : Array.Empty<int>());
        JoinNewFaction(id);
    }

    private void Abandon(int k)
    {
        var st = Settlements;
        st.Alive[k] = false;
        st.Leader[k] = Npcs.None;
        var b = Buildings;
        for (int i = 0; i < b.HighWater; i++)
            if (b.Alive[i] && b.Settlement[i] == k) b.Settlement[i] = -1;
        Chronicle(Tick, "abandoned", $"{st.Name[k]} was abandoned.", 5, Array.Empty<int>());
        LeaveFaction(k);
    }

    /// <summary>Daily per-settlement statistics: adults, hunger, mean aggression (traits).</summary>
    private void SettlementStats()
    {
        var st = Settlements;
        for (int k = 0; k < st.Count; k++) { st.Adults[k] = 0; st.Hungry[k] = 0; st.Aggression[k] = 0; }
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            int k = SettlementOfSlot(s);
            if (k < 0) continue;
            if (Npcs.Need(s, Need.Food) < 0.4f) st.Hungry[k]++;
            if (StageOf(s) is not (LifeStage.Adult or LifeStage.Elder)) continue;
            st.Adults[k]++;
            st.Aggression[k] += Trait(s, Gene.Aggression);
            if (_unlocks[s] != 0) st.Unlocked[k] |= _unlocks[s];
        }
        for (int k = 0; k < st.Count; k++)
        {
            if (!st.Alive[k]) continue;
            st.Hungry[k] = st.Members[k] > 0 ? st.Hungry[k] / st.Members[k] : 0;
            st.Aggression[k] = st.Adults[k] > 0 ? st.Aggression[k] / st.Adults[k] : 0;
        }

        // Member lists (slot order) for the rest of the day's per-settlement passes.
        if (_memberStart.Length < st.Count + 1) _memberStart = new int[Math.Max(16, st.Count * 2)];
        if (_memberFill.Length < st.Count) _memberFill = new int[Math.Max(16, st.Count * 2)];
        if (_memberList.Length < Npcs.HighWater) _memberList = new int[Math.Max(64, Npcs.Capacity)];
        Array.Clear(_memberStart, 0, st.Count + 1);
        for (int s = 0; s < Npcs.HighWater; s++)
            if (Npcs.Alive[s] && SettlementOfSlot(s) is int k and >= 0) _memberStart[k + 1]++;
        for (int k = 0; k < st.Count; k++) _memberStart[k + 1] += _memberStart[k];
        for (int k = 0; k < st.Count; k++) _memberFill[k] = _memberStart[k];
        for (int s = 0; s < Npcs.HighWater; s++)
            if (Npcs.Alive[s] && SettlementOfSlot(s) is int k and >= 0) _memberList[_memberFill[k]++] = s;
    }

    private int[] _memberStart = [], _memberList = [], _memberFill = [];

    /// <summary>Members of a settlement as of today's stats pass (callers re-check membership).</summary>
    private ReadOnlySpan<int> MembersOf(int k)
        => k + 1 < _memberStart.Length ? _memberList.AsSpan(_memberStart[k], _memberStart[k + 1] - _memberStart[k]) : ReadOnlySpan<int>.Empty;

    /// <summary>
    /// Leadership = highest weighted degree in the settlement's relationship graph
    /// (sum over other adult members of positive affinity + half positive trust). A sitting leader is replaced
    /// only when a rival clearly overtakes them.
    /// </summary>
    private const int LeaderReviewDays = 5;
    private const int MaxLeaderCandidates = 48;

    private void ChooseLeader(int k)
    {
        var st = Settlements;
        var n = Npcs;
        _queue.Clear();
        foreach (int s in MembersOf(k))
            if (n.Alive[s] && SettlementOfSlot(s) == k && StageOf(s) is LifeStage.Adult or LifeStage.Elder) _queue.Add(s);
        if (_queue.Count == 0) return;
        bool seated = n.TryGetSlot(st.Leader[k], out int sitting) && SettlementOfSlot(sitting) == k;
        // Reviewed every few days (staggered), at once when the seat is empty.
        if (seated && (DayIndex + k) % LeaderReviewDays != 0) return;
        int best = -1, current = -1;
        float bestScore = -1f, currentScore = -1f;
        // Weighted degree is computed for at most ~MaxLeaderCandidates people (an even spread, plus the sitting leader).
        int stride = Math.Max(1, _queue.Count / MaxLeaderCandidates);
        for (int i = 0; i < _queue.Count; i++)
        {
            int a = _queue[i], ida = n.IdOf(a);
            if (i % stride != 0 && ida != st.Leader[k]) continue;
            float score = 0;
            for (int j = 0; j < _queue.Count; j++)
            {
                if (i == j) continue;
                var r = Relationships.Get(ida, n.IdOf(_queue[j]));
                score += Math.Max(0f, r.Affinity) + 0.5f * Math.Max(0f, r.Trust);
            }
            score += 0.05f * (Trait(a, Gene.Ambition) + Trait(a, Gene.Sociability));
            if (score > bestScore) { bestScore = score; best = a; }
            if (ida == st.Leader[k]) { current = a; currentScore = score; }
        }
        if (current >= 0 && (current == best || bestScore < currentScore * 1.25f + 0.1f)) return;
        int prev = st.Leader[k];
        st.Leader[k] = n.IdOf(best);
        LeadersChosen++;
        string text = prev == Npcs.None || !n.IsAlive(prev)
            ? $"{n.Name[best]} became the leader of {st.Name[k]}."
            : $"{n.Name[best]} replaced {NameOf(prev)} as the leader of {st.Name[k]}.";
        Events.Publish(new SimEvent(Tick, MemoryKind.Speech, text, 6, [st.Leader[k]], n.X[best], n.Z[best],
            Config.PerceptionRadius, "leader", 5));
    }

    /// <summary>
    /// Crowded settlements shed a band of young households, who pick a site with food and water 90-180 m
    /// away and walk there (Settle). The next clustering pass founds their settlement.
    /// </summary>
    private void TryMigration(int k)
    {
        var st = Settlements;
        var hh = Households;
        int members = st.Members[k];
        if (members < Config.MigrationMinMembers || Tick - st.LastMigration[k] < 4L * Config.TicksPerYear) return;
        float crowd = members / (float)Config.MigrationMinMembers;
        float pressure = 0.5f + st.Hungry[k];
        if (!Rng.Chance(Config.MigrationChancePerDay * crowd * pressure)) return;
        if (!TryPickColonySite(k, out float sx, out float sz)) return;

        // The youngest households (not the leader's) go, up to three; a lone household stays put.
        int leaderHome = Npcs.TryGetSlot(st.Leader[k], out int ls) ? _home[ls] : -1;
        _queue.Clear();
        for (int pass = 0; pass < 3; pass++)
        {
            int pick = -1;
            for (int i = 0; i < hh.HighWater; i++)
            {
                if (!hh.Alive[i] || hh.Settlement[i] != k || i == leaderHome || _queue.Contains(i)) continue;
                if (!Npcs.IsAlive(hh.HeadA[i]) && !Npcs.IsAlive(hh.HeadB[i])) continue;
                if (pick < 0 || hh.Founded[i] > hh.Founded[pick]) pick = i;
            }
            if (pick < 0) break;
            _queue.Add(pick);
        }
        if (_queue.Count < 2) return;
        int moved = _queue.Count, firstHead = Npcs.None;
        foreach (int pick in _queue)
        {
            var (hx, hz) = PickHomeSite(sx, sz, 2f, 12f);
            if (hh.Hut[pick] >= 0 && Buildings.Alive[hh.Hut[pick]]) Buildings.Household[hh.Hut[pick]] = -1;
            hh.Hut[pick] = -1;
            hh.HomeX[pick] = hx; hh.HomeZ[pick] = hz;
            hh.Settlement[pick] = -1;
            hh.MigratedFrom[pick] = k;
            RebuildHouseholdBins();
            if (firstHead == Npcs.None) firstHead = Npcs.IsAlive(hh.HeadA[pick]) ? hh.HeadA[pick] : hh.HeadB[pick];
            foreach (int s in MembersOf(k))
            {
                if (!Npcs.Alive[s] || _home[s] != pick) continue;
                _order[s] = (byte)Order.Migrate;
                _orderTarget[s] = -1;
                _orderUntil[s] = Tick + 6L * Config.TicksPerDay;
                Npcs.Phase[s] = ActionPhase.Done;
            }
        }
        st.LastMigration[k] = Tick;
        if (Npcs.TryGetSlot(firstHead, out int fh))
            Events.Publish(new SimEvent(Tick, MemoryKind.Speech, $"{Npcs.Name[fh]} led {moved} families out of {st.Name[k]} to find new land.", 5,
                [firstHead], Npcs.X[fh], Npcs.Z[fh], Config.PerceptionRadius, "migration", 4));
    }

    private bool TryPickColonySite(int k, out float bx, out float bz)
    {
        var st = Settlements;
        float cx = st.CenterX[k], cz = st.CenterZ[k];
        int region = Pathing.Region[Terrain.CellIndex(cx, cz)];
        float bestScore = float.NegativeInfinity;
        bx = bz = 0;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            float a = Rng.NextFloat() * MathF.Tau, r = Config.MigrationMinDistance + Rng.NextFloat() * Config.MigrationMinDistance;
            float x = cx + MathF.Cos(a) * r, z = cz + MathF.Sin(a) * r;
            if (!BuildableAt(x, z)) continue;
            int cell = Terrain.CellIndex(x, z);
            if (Pathing.Region[cell] != region) continue;
            bool clear = true;
            for (int o = 0; o < st.Count && clear; o++)
                if (st.Alive[o] && Sq(st.CenterX[o] - x) + Sq(st.CenterZ[o] - z) < Sq(Config.MigrationMinDistance * 0.8f)) clear = false;
            if (!clear) continue;
            int water = Terrain.NearestWater[cell];
            if (water < 0) continue;
            float wd = Dist(x, z, Terrain.CellX(water), Terrain.CellZ(water));
            if (wd > 80f) continue;
            float score = Terrain.Fertility[cell] * 4f - wd / 20f;
            if (Resources.FindNearest(ResourceKind.Berry, x, z, 40f) >= 0) score += 2f;
            if (Resources.FindNearest(ResourceKind.Game, x, z, 60f) >= 0) score += 1f;
            if (score > bestScore) { bestScore = score; bx = x; bz = z; }
        }
        return bestScore > float.NegativeInfinity;
    }
}
