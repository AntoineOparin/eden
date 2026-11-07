using System.Collections.Concurrent;
using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// C1: read-only settlement views for the client. <see cref="SettlementDetail"/> is built on request for a bounded
/// watch list (<see cref="MaxWatchedSettlements"/>), mirroring the NPC detail watch list in World.cs, and
/// <see cref="SettlementView.Food"/> / <see cref="SettlementView.TerritoryRadius"/> are filled with every snapshot.
/// Nothing here touches hashed state, the RNG or the save; it runs only while a snapshot is built (never per tick).
/// </summary>
public sealed partial class World
{
    /// <summary>How many settlements the sim builds details for at once (least recently requested dropped first).</summary>
    public const int MaxWatchedSettlements = 8;

    private readonly ConcurrentQueue<int> _settlementRequests = new();
    private readonly List<int> _watchedSettlements = new();
    private readonly Dictionary<int, (SettlementDetail Detail, long Built)> _settlementDetailCache = new();
    private float[] _settlementFood = [];

    /// <summary>Radius (m) of a settlement's own land: the circle trespass and grievance use (Conflict.cs).</summary>
    public float TerritoryRadius => Config.SettlementRadius * Config.TerritoryFactor;

    /// <summary>Ask for a settlement's detail from the next snapshot on (<see cref="WatchSettlementCommand"/>). Any thread.</summary>
    public void WatchSettlement(int id) => _settlementRequests.Enqueue(id);

    /// <summary>Settlement ids currently watched, oldest request first (tests).</summary>
    public IReadOnlyList<int> WatchedSettlements => _watchedSettlements;

    /// <summary>
    /// Food each settlement holds (indexed by settlement id): shared stock plus what its living members carry. The
    /// returned array is scratch, valid until the next call.
    /// </summary>
    private float[] SettlementFood()
    {
        var st = Settlements;
        if (_settlementFood.Length < st.Count) _settlementFood = new float[Math.Max(16, st.Count * 2)];
        for (int k = 0; k < st.Count; k++) _settlementFood[k] = st.Alive[k] ? st.Food[k] : 0f;
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            int k = SettlementOfSlot(s);
            if (k >= 0 && k < st.Count) _settlementFood[k] += Npcs.Food[s];
        }
        return _settlementFood;
    }

    /// <summary>Drains the requests into the watch list and returns this snapshot's details.</summary>
    private Dictionary<int, SettlementDetail> SettlementDetails()
    {
        while (_settlementRequests.TryDequeue(out int req))
        {
            if (!Settlements.IsAlive(req)) continue;
            _watchedSettlements.Remove(req);
            _watchedSettlements.Add(req);
            if (_watchedSettlements.Count > MaxWatchedSettlements)
            {
                _settlementDetailCache.Remove(_watchedSettlements[0]);
                _watchedSettlements.RemoveAt(0);
            }
        }
        var details = new Dictionary<int, SettlementDetail>(_watchedSettlements.Count);
        int stale = -1;
        long staleBuilt = long.MaxValue;
        for (int i = _watchedSettlements.Count - 1; i >= 0; i--)
        {
            int id = _watchedSettlements[i];
            if (!Settlements.IsAlive(id))
            {
                _watchedSettlements.RemoveAt(i);
                _settlementDetailCache.Remove(id);
                continue;
            }
            // A new request is built at once; a cached detail is reused while younger than an in-game hour.
            if (!_settlementDetailCache.TryGetValue(id, out var cached) || Tick < cached.Built || cached.Detail.Name != Settlements.Name[id])
            {
                if (!TryBuildSettlementDetail(id, out var d)) continue;
                cached = (d, Tick);
                _settlementDetailCache[id] = cached;
            }
            else if (Tick - cached.Built >= Config.TicksPerHour && cached.Built < staleBuilt) { stale = id; staleBuilt = cached.Built; }
            details[id] = cached.Detail;
        }
        // At most one refresh per snapshot (the oldest), so 100x-500x batches never rebuild all eight every frame.
        if (stale >= 0 && TryBuildSettlementDetail(stale, out var fresh))
        {
            _settlementDetailCache[stale] = (fresh, Tick);
            details[stale] = fresh;
        }
        return details;
    }

    /// <summary>Builds a settlement's detail straight from sim state. Sim thread (or a stopped sim) only.</summary>
    public bool TryBuildSettlementDetail(int id, out SettlementDetail detail)
    {
        detail = null!;
        var st = Settlements;
        if (!st.IsAlive(id)) return false;
        var cfg = Config;
        int members = 0, adults = 0;
        float food = st.Food[id], perDay = 0f;
        float eatPerDay = cfg.FoodDecayPerDay / Math.Max(1e-6f, cfg.FoodPerUnit);
        var concepts = new Dictionary<string, (int Holders, float Best)>(StringComparer.Ordinal);
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s] || SettlementOfSlot(s) != id) continue;
            members++;
            var stage = StageOf(s);
            if (stage is LifeStage.Adult or LifeStage.Elder) adults++;
            food += Npcs.Food[s];
            perDay += eatPerDay * (stage == LifeStage.Infant ? 0.8f : stage == LifeStage.Child ? 0.85f : 1f); // World.Life.cs stage rates
            if (s >= _knowledge.Length || _knowledge[s] is not { Count: > 0 } known) continue;
            foreach (var (concept, p) in known)
            {
                if (p <= 0f) continue;
                concepts.TryGetValue(concept, out var c);
                concepts[concept] = (c.Holders + 1, MathF.Max(c.Best, p));
            }
        }

        // Records on the settlement's land: inside the territory circle, or in one of its buildings.
        float cx = st.CenterX[id], cz = st.CenterZ[id], r2 = TerritoryRadius * TerritoryRadius;
        var recorded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rec in Records.All)
        {
            bool own = (rec.X - cx) * (rec.X - cx) + (rec.Z - cz) * (rec.Z - cz) <= r2
                || (rec.BuildingId >= 0 && rec.BuildingId < Buildings.HighWater && Buildings.Alive[rec.BuildingId] && Buildings.Settlement[rec.BuildingId] == id);
            if (!own) continue;
            foreach (var (concept, p) in rec.Entries) if (p > 0f) recorded.Add(concept);
        }

        var conceptViews = new List<SettlementConceptView>(concepts.Count);
        foreach (var (concept, c) in concepts)
            conceptViews.Add(new SettlementConceptView(concept, c.Holders, c.Best, recorded.Contains(concept)));
        conceptViews.Sort((a, b) => a.Holders != b.Holders ? b.Holders.CompareTo(a.Holders)
            : a.BestProficiency != b.BestProficiency ? b.BestProficiency.CompareTo(a.BestProficiency)
            : string.CompareOrdinal(a.ConceptId, b.ConceptId));

        var kinds = new int[BuildingKinds.All.Length];
        var b = Buildings;
        for (int i = 0; i < b.HighWater; i++)
            if (b.Alive[i] && b.Settlement[i] == id && b.Progress[i] >= 1f && b.Kind[i] < kinds.Length) kinds[b.Kind[i]]++;
        var buildings = new List<BuildingCount>();
        for (int k = 0; k < kinds.Length; k++) if (kinds[k] > 0) buildings.Add(new BuildingCount(BuildingKinds.All[k].Id, kinds[k]));
        buildings.Sort((x, y) => x.Count != y.Count ? y.Count.CompareTo(x.Count) : string.CompareOrdinal(x.Kind, y.Kind));

        detail = new SettlementDetail(id, st.Name[id], members, adults, conceptViews, buildings, food, perDay);
        return true;
    }
}
