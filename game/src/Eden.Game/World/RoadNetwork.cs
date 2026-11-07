using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>A span of a road that crosses water or a stream notch on a bridge: distances along the road, metres.</summary>
public readonly record struct BridgeSpan(float From, float To);

/// <summary>
/// One road: a polyline in world XZ sampled every <see cref="RoadNetwork.Step"/> metres (so a renderer can drape it),
/// the settlement it belongs to, and the spans that need a bridge.
/// </summary>
public sealed record Road(int SettlementId, Vector2[] Points, BridgeSpan[] Bridges)
{
    public float Length => (Points.Length - 1) * RoadNetwork.Step;

    /// <summary>True when the distance <paramref name="along"/> (metres) lies on a bridge.</summary>
    public bool OnBridge(float along)
    {
        foreach (var b in Bridges)
            if (along >= b.From && along <= b.To) return true;
        return false;
    }
}

/// <summary>
/// A2 road layout (render side only; the sim has no roads). Per settlement, the buildings and the settlement centre are
/// joined by a minimum spanning tree (Prim, edge cost = length plus a penalty for water), so roads branch like paths
/// instead of a star of straight lines. Each tree edge runs from the edge of one footprint to the edge of the other and
/// is sampled every metre over the drawn (flattened) terrain. Where the ground dips under the sea or into a notch more
/// than <see cref="NotchDepth"/> below both banks, the span becomes a <see cref="BridgeSpan"/>; an edge whose wet span
/// is longer than <see cref="MaxBridge"/> is dropped (no road). Deterministic: same buildings, same roads.
/// </summary>
public static class RoadNetwork
{
    public const float Step = 1f;
    public const float MaxBridge = 32f;
    public const float NotchDepth = 1.4f;
    /// <summary>How far each side a notch looks for its banks (metres).</summary>
    public const float NotchReach = 10f;
    public const float Width = 2.2f;

    /// <summary>Roads for every settlement with at least two buildings. <paramref name="ground"/> is the drawn terrain, not clamped to sea level.</summary>
    /// <para>
    /// <paramref name="cache"/> (optional) keeps each settlement's roads keyed by a signature of its buildings and
    /// foundations, so a new building re-plans only its own settlement. Clear it when the terrain itself changes.
    /// </para>
    public static List<Road> Plan(ReadOnlySpan<BuildingView> buildings, IReadOnlyList<Foundation> foundations, Func<float, float, float> ground, float seaLevel,
        Dictionary<int, (ulong Signature, List<Road> Roads)>? cache = null)
    {
        var bySettlement = new SortedDictionary<int, List<int>>();
        for (int i = 0; i < buildings.Length; i++)
        {
            if (buildings[i].SettlementId < 0 || buildings[i].Kind == "wall") continue;
            if (!bySettlement.TryGetValue(buildings[i].SettlementId, out var list)) bySettlement[buildings[i].SettlementId] = list = new();
            list.Add(i);
        }
        var foundationById = new Dictionary<int, Foundation>();
        foreach (var f in foundations) foundationById[f.BuildingId] = f;

        var roads = new List<Road>();
        foreach (var (settlement, members) in bySettlement)
        {
            if (members.Count < 2) continue;
            // Nodes: the centre (index 0) then the buildings, ordered by id for determinism.
            var ids = new int[members.Count];
            for (int k = 0; k < ids.Length; k++) ids[k] = buildings[members[k]].Id;
            var order = members.ToArray();
            Array.Sort(ids, order);
            members.Clear();
            members.AddRange(order);
            ulong sig = 1469598103934665603UL;
            foreach (int m in members)
            {
                var bm = buildings[m];
                sig = (sig ^ (ulong)(uint)bm.Id) * 1099511628211UL;
                sig = (sig ^ BitConverter.SingleToUInt32Bits(bm.X) ^ ((ulong)BitConverter.SingleToUInt32Bits(bm.Z) << 32)) * 1099511628211UL;
                if (foundationById.TryGetValue(bm.Id, out var fm)) sig = (sig ^ BitConverter.SingleToUInt32Bits(fm.Top)) * 1099511628211UL;
            }
            if (cache is not null && cache.TryGetValue(settlement, out var hit) && hit.Signature == sig)
            {
                roads.AddRange(hit.Roads);
                continue;
            }
            int firstRoad = roads.Count;
            int n = members.Count + 1;
            var pos = new Vector2[n];
            var radius = new float[n];
            float cx = 0, cz = 0;
            foreach (int m in members) { cx += buildings[m].X; cz += buildings[m].Z; }
            pos[0] = new Vector2(cx / members.Count, cz / members.Count);
            radius[0] = 0f;
            for (int k = 0; k < members.Count; k++)
            {
                var b = buildings[members[k]];
                pos[k + 1] = new Vector2(b.X, b.Z);
                radius[k + 1] = foundationById.TryGetValue(b.Id, out var f) ? MathF.Min(f.HalfExtents.X, f.HalfExtents.Y) + 0.6f : 3f;
            }
            if (ground(pos[0].X, pos[0].Y) <= seaLevel + 0.15f)
            {
                // A town round a bay: its centre is in the water, so the tree grows from the building nearest to it.
                int nearest = 1;
                for (int k = 2; k < n; k++)
                    if (pos[k].DistanceSquaredTo(pos[0]) < pos[nearest].DistanceSquaredTo(pos[0])) nearest = k;
                pos[0] = pos[nearest];
                radius[0] = radius[nearest];
            }
            // Prim's MST from the centre.
            var inTree = new bool[n];
            var best = new float[n];
            var parent = new int[n];
            Array.Fill(best, float.MaxValue);
            Array.Fill(parent, -1);
            best[0] = 0;
            for (int iter = 0; iter < n; iter++)
            {
                int u = -1;
                for (int v = 0; v < n; v++)
                    if (!inTree[v] && (u < 0 || best[v] < best[u])) u = v;
                if (u < 0 || best[u] == float.MaxValue) break;
                inTree[u] = true;
                if (parent[u] >= 0)
                {
                    var road = Edge(settlement, pos[parent[u]], radius[parent[u]], pos[u], radius[u], ground, seaLevel);
                    if (road is not null) roads.Add(road);
                }
                for (int v = 0; v < n; v++)
                {
                    if (inTree[v]) continue;
                    float cost = Cost(pos[u], pos[v], ground, seaLevel);
                    if (cost < best[v]) { best[v] = cost; parent[v] = u; }
                }
            }
            if (cache is not null) cache[settlement] = (sig, roads.GetRange(firstRoad, roads.Count - firstRoad));
        }
        return roads;
    }

    private static float Cost(Vector2 a, Vector2 b, Func<float, float, float> ground, float sea)
    {
        float len = a.DistanceTo(b);
        int samples = Math.Max(2, (int)(len / 4f));
        int wet = 0;
        for (int k = 1; k < samples; k++)
        {
            var p = a.Lerp(b, k / (float)samples);
            if (ground(p.X, p.Y) <= sea + 0.15f) wet++;
        }
        return len * (1f + 3f * wet / samples);
    }

    /// <summary>One road between two nodes, from footprint edge to footprint edge; null when too short or too wet.</summary>
    public static Road? Edge(int settlement, Vector2 a, float ra, Vector2 b, float rb, Func<float, float, float> ground, float sea)
    {
        float len = a.DistanceTo(b);
        if (len <= ra + rb + 1.5f) return null;
        var dir = (b - a) / len;
        var start = a + dir * ra;
        var end = b - dir * rb;
        float run = start.DistanceTo(end);
        int count = Math.Max(2, (int)MathF.Ceiling(run / Step) + 1);
        var points = new Vector2[count];
        var h = new float[count];
        for (int k = 0; k < count; k++)
        {
            float t = Math.Min(1f, k * Step / run);
            points[k] = start.Lerp(end, t);
            h[k] = ground(points[k].X, points[k].Y);
        }
        var bridges = FindBridges(h, sea);
        foreach (var span in bridges)
            if (span.To - span.From > MaxBridge) return null;
        // A road that starts or ends in the water is not a road.
        if (h[0] <= sea + 0.15f || h[^1] <= sea + 0.15f) return null;
        return new Road(settlement, points, bridges);
    }

    /// <summary>
    /// Bridge spans over a height profile sampled every <see cref="Step"/>: wet samples (at or below the sea plus 0.15 m)
    /// and notch samples (more than <see cref="NotchDepth"/> under the lower of the highest ground within
    /// <see cref="NotchReach"/> on each side), each run widened by one sample onto its banks.
    /// </summary>
    public static BridgeSpan[] FindBridges(float[] h, float sea)
    {
        int n = h.Length;
        int reach = (int)(NotchReach / Step);
        var mark = new bool[n];
        for (int k = 0; k < n; k++)
        {
            if (h[k] <= sea + 0.15f) { mark[k] = true; continue; }
            float left = float.MinValue, right = float.MinValue;
            for (int j = Math.Max(0, k - reach); j < k; j++) left = MathF.Max(left, h[j]);
            for (int j = k + 1; j <= Math.Min(n - 1, k + reach); j++) right = MathF.Max(right, h[j]);
            if (left == float.MinValue || right == float.MinValue) continue;
            if (MathF.Min(left, right) - h[k] > NotchDepth) mark[k] = true;
        }
        var spans = new List<BridgeSpan>();
        for (int k = 0; k < n; k++)
        {
            if (!mark[k]) continue;
            int s = k;
            while (k + 1 < n && mark[k + 1]) k++;
            int from = Math.Max(0, s - 1), to = Math.Min(n - 1, k + 1);
            spans.Add(new BridgeSpan(from * Step, to * Step));
        }
        return spans.ToArray();
    }

    /// <summary>Order-independent signature of a settlement's roads (renderers rebuild only settlements whose signature moved).</summary>
    public static ulong Signature(IEnumerable<Road> roads)
    {
        ulong sig = 1469598103934665603UL;
        foreach (var r in roads)
        {
            ulong e = (ulong)r.Points.Length * 31UL + (ulong)r.Bridges.Length;
            foreach (var p in new[] { r.Points[0], r.Points[^1] })
            {
                e = (e ^ BitConverter.SingleToUInt32Bits(p.X)) * 1099511628211UL;
                e = (e ^ BitConverter.SingleToUInt32Bits(p.Y)) * 1099511628211UL;
            }
            sig += e; // sum: order-independent
        }
        return sig;
    }
}
