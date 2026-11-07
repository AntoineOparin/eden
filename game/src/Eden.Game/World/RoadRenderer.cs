using Eden.Game.Art;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// A2: draws the settlement roads planned by <see cref="TerrainBuilder.Roads"/> as strips draped on the drawn terrain
/// (every metre, four vertices across, sampled from the render heights and lifted a few centimetres), plank bridges
/// where a road crosses water or a notch (<see cref="ProceduralMeshes.BridgeSegment"/> instances following a gentle
/// arch from bank to bank, piles down to the ground), and record stelae in front of every finished archive. One road
/// mesh per settlement; a settlement is rebuilt only when its roads, the ground under them or its archives changed,
/// and never in the same frame as the terrain rebuild that caused it.
/// </summary>
public partial class RoadRenderer : Node3D
{
    [Export] public float Lift { get; set; } = 0.08f;
    [Export] public float EdgeLift { get; set; } = 0.04f;
    /// <summary>How high the deck rises at mid-span above the straight line between the banks (metres, plus 2 % of the span).</summary>
    [Export] public float Arch { get; set; } = 0.25f;
    /// <summary>Minimum clearance of the deck over the sea (metres).</summary>
    [Export] public float Clearance { get; set; } = 0.6f;
    [Export] public float PileSpacing { get; set; } = 3f;

    public TerrainBuilder? Terrain { get; set; }
    public Func<IWorldSnapshot?>? Frame { get; set; }

    private readonly Dictionary<int, MeshInstance3D> _roadMeshes = new();
    private readonly Dictionary<int, ulong> _signatures = new();
    private PropBatches? _props;
    private ShaderMaterial? _roadMaterial, _propMaterial;
    private int _seenVersion = -1;
    private int _wait = -1;

    /// <summary>Settlements rebuilt by the last pass and bridge segments / stelae drawn (diagnostics, tests).</summary>
    public int LastRebuilt { get; private set; }
    public int BridgeSegments { get; private set; }
    public int Stelae { get; private set; }
    public int RoadVertices { get; private set; }

    /// <summary>Every bridge laid by the last full pass: deck ends (top surface) and pile feet, for the placement test.</summary>
    public IEnumerable<(Vector3 Start, Vector3 End, List<(Vector3 Top, Vector3 Foot)> Piles)> Bridges => _bridges.Values.SelectMany(b => b);

    /// <summary>Stela positions (base centre) of the last pass, for the placement test.</summary>
    public IEnumerable<Transform3D> StelaTransforms => _stelae.Values.SelectMany(s => s);

    private readonly Dictionary<int, List<(Vector3 Start, Vector3 End, List<(Vector3 Top, Vector3 Foot)> Piles)>> _bridges = new();
    private readonly Dictionary<int, List<Transform3D>> _stelae = new();

    private void Initialise()
    {
        if (_props is not null) return;
        var shader = GD.Load<Shader>("res://shaders/props.gdshader");
        _roadMaterial = new ShaderMaterial { Shader = shader };
        _roadMaterial.SetShaderParameter("ambient_lift", 0.04f);
        _propMaterial = new ShaderMaterial { Shader = shader };
        _props = new PropBatches(this, _propMaterial, castShadows: true);
    }

    public override void _Process(double delta)
    {
        if (Terrain is null || Frame?.Invoke() is not { } s) return;
        if (Terrain.RenderVersion != _seenVersion)
        {
            _seenVersion = Terrain.RenderVersion;
            _wait = 1; // the frame after the terrain rebuild
            return;
        }
        if (_wait < 0) return;
        if (--_wait >= 0) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Rebuild(s, Terrain.Roads);
        Dev.FrameTimeProbe.Note($"roads ({LastRebuilt} settlements)", sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>Rebuilds the settlements whose roads, ground or archives changed. Public for tests.</summary>
    public void Rebuild(IWorldSnapshot s, IReadOnlyList<Road> roads)
    {
        Initialise();
        var bySettlement = new SortedDictionary<int, List<Road>>();
        foreach (var r in roads)
        {
            if (!bySettlement.TryGetValue(r.SettlementId, out var list)) bySettlement[r.SettlementId] = list = new();
            list.Add(r);
        }
        var archives = new Dictionary<int, List<BuildingView>>();
        foreach (var b in s.Buildings.Span)
        {
            if (b.Kind != "archive" || b.Progress < 1f || b.SettlementId < 0) continue;
            if (!archives.TryGetValue(b.SettlementId, out var list)) archives[b.SettlementId] = list = new();
            list.Add(b);
            if (!bySettlement.ContainsKey(b.SettlementId)) bySettlement[b.SettlementId] = new();
        }

        var changed = new HashSet<int>();
        foreach (var (settlement, list) in bySettlement)
        {
            ulong sig = RoadNetwork.Signature(list);
            foreach (var r in list)
                for (int k = 0; k < r.Points.Length; k += 4)
                    sig = (sig ^ BitConverter.SingleToUInt32Bits(GroundRaw(r.Points[k].X, r.Points[k].Y))) * 1099511628211UL;
            if (archives.TryGetValue(settlement, out var arch))
                foreach (var a in arch) sig = (sig ^ (ulong)(uint)a.Id ^ BitConverter.SingleToUInt32Bits(Ground(a.X, a.Z))) * 1099511628211UL;
            if (_signatures.TryGetValue(settlement, out var old) && old == sig) continue;
            _signatures[settlement] = sig;
            changed.Add(settlement);
        }
        // Settlements that vanished.
        foreach (var gone in _signatures.Keys.Where(k => !bySettlement.ContainsKey(k)).ToList())
        {
            _signatures.Remove(gone);
            changed.Add(gone);
        }
        LastRebuilt = changed.Count;
        if (changed.Count == 0) return;

        _props!.Begin(changed);
        foreach (int settlement in changed)
        {
            _bridges[settlement] = new();
            _stelae[settlement] = new();
            bySettlement.TryGetValue(settlement, out var list);
            BuildRoadMesh(settlement, list ?? new());
            if (list is not null)
                foreach (var r in list) BuildBridges(settlement, r);
            if (archives.TryGetValue(settlement, out var arch))
                foreach (var a in arch) PlaceStelae(settlement, a);
        }
        _props.Commit();
        RecountProps();
    }

    private void RecountProps()
    {
        BridgeSegments = 0;
        Stelae = StelaTransforms.Count();
        foreach (var b in Bridges) BridgeSegments += Math.Max(1, (int)MathF.Ceiling(new Vector2(b.End.X - b.Start.X, b.End.Z - b.Start.Z).Length()));
        RoadVertices = 0;
        foreach (var mi in _roadMeshes.Values)
            if (mi.Mesh is ArrayMesh am && am.GetSurfaceCount() > 0) RoadVertices += am.SurfaceGetArrayLen(0);
    }

    private float Ground(float x, float z) => Terrain?.SampleRender(x, z) ?? 0f;

    private float GroundRaw(float x, float z) => Terrain?.SampleRenderRaw(x, z) ?? 0f;

    private Vector3 NormalAt(float x, float z)
    {
        const float e = 1f;
        float hl = Ground(x - e, z), hr = Ground(x + e, z), hd = Ground(x, z - e), hu = Ground(x, z + e);
        return new Vector3(hl - hr, 2f * e, hd - hu).Normalized();
    }

    // ---- roads ------------------------------------------------------------------------------------------------------

    /// <summary>Road colours (sRGB): packed dirt down the middle, grassy dirt at the edges.</summary>
    private static readonly Color RoadCentre = Palette.Hex(0xCDA86E), RoadEdge = Palette.Hex(0xA99060);

    private void BuildRoadMesh(int settlement, List<Road> roads)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var cols = new List<Color>();
        var idx = new List<int>();
        float half = RoadNetwork.Width * 0.5f;
        float[] across = [-half, -half + 0.45f, half - 0.45f, half];
        int roadIndex = 0;
        foreach (var r in roads)
        {
            float bias = 0.004f * (roadIndex++ % 4); // overlapping strips at junctions never share a depth
            var pts = r.Points;
            int k = 0;
            while (k < pts.Length)
            {
                if (r.OnBridge(k * RoadNetwork.Step)) { k++; continue; }
                int start = k;
                while (k < pts.Length && !r.OnBridge(k * RoadNetwork.Step)) k++;
                int end = k - 1;
                if (end - start < 1) continue;
                for (int i = start; i <= end; i++)
                {
                    var prev = pts[Math.Max(start, i - 1)];
                    var next = pts[Math.Min(end, i + 1)];
                    var dir = (next - prev).Normalized();
                    var perp = new Vector2(-dir.Y, dir.X);
                    // Taper the last 1.5 m at both ends so roads fade into yards instead of stopping square.
                    float fromEnd = MathF.Min(i - 0, pts.Length - 1 - i) * RoadNetwork.Step;
                    float width = Math.Clamp(0.45f + fromEnd / 1.5f * 0.55f, 0.45f, 1f);
                    for (int c = 0; c < 4; c++)
                    {
                        var p = pts[i] + perp * across[c] * width;
                        bool edge = c is 0 or 3;
                        float y = Ground(p.X, p.Y) + (edge ? EdgeLift : Lift) + bias;
                        verts.Add(new Vector3(p.X, y, p.Y));
                        norms.Add(NormalAt(p.X, p.Y));
                        var col = (edge ? RoadEdge : RoadCentre).SrgbToLinear();
                        col.A = 1f;
                        cols.Add(col);
                    }
                    if (i == start) continue;
                    int b0 = verts.Count - 8, b1 = verts.Count - 4;
                    for (int c = 0; c < 3; c++)
                    {
                        // Two triangles per cell of the strip; clockwise seen from above (Godot's front face).
                        idx.Add(b0 + c); idx.Add(b0 + c + 1); idx.Add(b1 + c);
                        idx.Add(b0 + c + 1); idx.Add(b1 + c + 1); idx.Add(b1 + c);
                    }
                }
            }
        }
        if (!_roadMeshes.TryGetValue(settlement, out var mi))
        {
            mi = new MeshInstance3D
            {
                Name = $"Roads_{settlement}",
                MaterialOverride = _roadMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(mi);
            _roadMeshes[settlement] = mi;
        }
        if (verts.Count == 0)
        {
            mi.Mesh = null;
            return;
        }
        // Fix the winding per triangle: whichever way a strip runs, its faces must look up.
        for (int t = 0; t + 2 < idx.Count; t += 3)
        {
            var a = verts[idx[t]]; var b = verts[idx[t + 1]]; var c = verts[idx[t + 2]];
            if ((b - a).Cross(c - a).Y > 0) (idx[t + 1], idx[t + 2]) = (idx[t + 2], idx[t + 1]);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = cols.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.ResourceName = $"g:roads-{settlement}";
        mi.Mesh = mesh;
    }

    /// <summary>Road vertices of a settlement's mesh (tests: every one sits just above the drawn ground).</summary>
    public Vector3[] RoadVerticesOf(int settlement)
        => _roadMeshes.TryGetValue(settlement, out var mi) && mi.Mesh is ArrayMesh am && am.GetSurfaceCount() > 0
            ? am.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array()
            : [];

    // ---- bridges ----------------------------------------------------------------------------------------------------

    private void BuildBridges(int settlement, Road r)
    {
        if (r.Bridges.Length == 0) return;
        var segment = ProceduralMeshes.BridgeSegment();
        var pile = ProceduralMeshes.BridgePile();
        float sea = Terrain?.Info?.SeaLevel ?? 0f;
        foreach (var span in r.Bridges)
        {
            var a2 = PointAt(r, span.From);
            var b2 = PointAt(r, span.To);
            float len = a2.DistanceTo(b2);
            if (len < 0.5f) continue;
            // Deck top 0.1 m above each bank (the planks bed 2 cm into the ground: resting, not floating).
            float topA = GroundRaw(a2.X, a2.Y) + 0.1f, topB = GroundRaw(b2.X, b2.Y) + 0.1f;
            float rise = Arch + 0.02f * len;
            Vector3 Deck(float t)
            {
                var p = a2.Lerp(b2, t);
                float y = Mathf.Lerp(topA, topB, t) + rise * 4f * t * (1f - t);
                float clear = sea + Clearance;
                // Keep the deck clear of the water in the middle, blending in so the ends still meet the banks.
                float inner = MathF.Min(1f, MathF.Min(t, 1f - t) * len / 2f);
                y = MathF.Max(y, Mathf.Lerp(y, clear, inner));
                return new Vector3(p.X, y, p.Y);
            }
            int n = Math.Max(1, (int)MathF.Ceiling(len / 1f));
            for (int i = 0; i < n; i++)
            {
                var p0 = Deck(i / (float)n);
                var p1 = Deck((i + 1) / (float)n);
                var fwd = p1 - p0;
                float segLen = fwd.Length();
                var f = fwd / segLen;
                var right = Vector3.Up.Cross(f).Normalized();
                var up = f.Cross(right);
                _props!.Add(settlement, segment, new Transform3D(new Basis(right, up, f * segLen), p0), Colors.White);
            }
            var piles = new List<(Vector3, Vector3)>();
            int pileCount = Math.Max(2, (int)MathF.Round(len / PileSpacing) + 1);
            for (int i = 0; i < pileCount; i++)
            {
                float t = i / (float)(pileCount - 1);
                var deck = Deck(t);
                var dir3 = Deck(Math.Min(1f, t + 0.01f)) - Deck(Math.Max(0f, t - 0.01f));
                var right = Vector3.Up.Cross(dir3.Normalized()).Normalized();
                foreach (float side in new[] { -0.95f, 0.95f })
                {
                    var top = deck + right * side - new Vector3(0, 0.3f, 0);
                    float foot = GroundRaw(top.X, top.Z) - 0.3f; // bedded into the ground or the sea floor
                    float height = top.Y - foot;
                    if (height < 0.05f) continue;
                    height = MathF.Min(height, 8f);
                    _props!.Add(settlement, pile, new Transform3D(Basis.FromScale(new Vector3(1f, height, 1f)), top), Colors.White);
                    piles.Add((top, top - new Vector3(0, height, 0)));
                }
            }
            _bridges[settlement].Add((Deck(0f), Deck(1f), piles));
        }
    }

    private static Vector2 PointAt(Road r, float along)
    {
        float f = along / RoadNetwork.Step;
        int i = Math.Clamp((int)f, 0, r.Points.Length - 1);
        int j = Math.Min(i + 1, r.Points.Length - 1);
        return r.Points[i].Lerp(r.Points[j], f - i);
    }

    // ---- stelae -----------------------------------------------------------------------------------------------------

    /// <summary>Two record stelae flank the path in front of an archive's door (+Z), on the yard, tilted with the ground.</summary>
    private void PlaceStelae(int settlement, in BuildingView a)
    {
        var stela = ProceduralMeshes.Stela();
        var basis = new Basis(Vector3.Up, a.Rotation);
        float front = BuildingFactory.FootprintOf("archive") + 1.3f;
        int count = 1 + (int)((uint)a.Id % 3u); // one to three, stable per archive
        for (int k = 0; k < count; k++)
        {
            float side = count == 1 ? 1.6f : -2.2f + 4.4f * k / (count - 1);
            var local = new Vector3(side, 0, front + (k % 2) * 0.6f);
            var p = new Vector3(a.X, 0, a.Z) + basis * local;
            p.Y = Ground(p.X, p.Z) - 0.05f;
            var n = NormalAt(p.X, p.Z);
            float angle = MathF.Min(MathF.Acos(Math.Clamp(n.Y, -1f, 1f)), Mathf.DegToRad(ResourceRenderer.MaxTiltDegrees));
            var axis = Vector3.Up.Cross(n);
            var tilt = axis.LengthSquared() < 1e-6f ? Basis.Identity : new Basis(axis.Normalized(), angle);
            var xf = new Transform3D(tilt * new Basis(Vector3.Up, a.Rotation + (k - 1) * 0.12f), p);
            _props!.Add(settlement, stela, xf, Colors.White);
            _stelae[settlement].Add(xf);
        }
    }
}
