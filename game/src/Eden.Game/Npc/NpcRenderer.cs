using Eden.Game.Art;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Draws every NPC in the snapshot in three LODs by camera distance:
/// <list type="bullet">
/// <item>hi: the <see cref="HiLodSlots"/> nearest (with hysteresis) get a pooled <see cref="NpcHi"/> (skinned character,
/// AnimationTree);</item>
/// <item>mid: the next <see cref="MidLodSlots"/> are static baked poses of one model per sex
/// (<see cref="CharacterLibrary.Pose"/>) in one MultiMesh per (sex, pose); walkers alternate two walk frames;</item>
/// <item>far: everyone else is a camera-facing quad from the <see cref="ImpostorAtlas"/> (8 headings × stand/lying).
/// Until the atlas has rendered (and in tests without a scene tree) far NPCs use the mid idle/lying meshes.</item>
/// </list>
/// Positions are interpolated between the previous and the latest snapshot so 20 Hz sim output looks smooth.
/// Allocation-free per frame once the arrays have grown to the population.
/// </summary>
public partial class NpcRenderer : Node3D
{
    private const int FloatsPerInstance = 16; // Transform3D (12) + custom data (4)
    private static readonly BakedPose[] MidPoses = Enum.GetValues<BakedPose>();

    [Export] public PackedScene? HiScene { get; set; }
    [Export] public int HiLodSlots { get; set; } = 50;
    /// <summary>NPCs after the hi slots drawn as baked-pose meshes; the rest are impostor quads.</summary>
    [Export] public int MidLodSlots { get; set; } = 700;
    /// <summary>Current hi-LOD NPCs compete with their distance multiplied by this, so they keep the slot until clearly farther.</summary>
    [Export] public float Hysteresis { get; set; } = 0.85f;
    /// <summary>Scale of every NPC; 1 = true size (adult 1.75 m). Spec 45: never scaled up at close range.</summary>
    [Export] public float NpcScale { get; set; } = 1f;
    /// <summary>Mid and far LODs grow to this factor at <see cref="FarZoomHeight"/> camera height for god-view readability.</summary>
    [Export] public float FarZoomScaleMax { get; set; } = 1.6f;
    [Export] public float NearZoomHeight { get; set; } = 60f;
    [Export] public float FarZoomHeight { get; set; } = 400f;

    /// <summary>Scale applied to mid/far NPCs this frame (1 near the ground, up to <see cref="FarZoomScaleMax"/>).</summary>
    public float ZoomScale { get; private set; } = 1f;
    [Export] public bool Interpolate { get; set; } = true;
    [Export] public bool MidCastShadows { get; set; } = true;

    /// <summary>Height of the drawn ground; NPCs never stand below it (flattened yards, terraces). Null: sim heights only.</summary>
    public Func<float, float, float>? Ground { get; set; }

    private sealed class Layer
    {
        public required MultiMeshInstance3D Node;
        public required MultiMesh Multi;
        public float[] Buffer = [];
        public int Count;
    }

    private Layer[,] _mid = new Layer[2, MidPoses.Length];
    private Layer? _far;
    private ImpostorAtlas? _atlas;
    private ShaderMaterial? _farMaterial;
    private bool _farUsesAtlas;
    private MeshInstance3D _selectionRing = null!;
    private NpcHi[] _pool = [];
    private int[] _slotNpc = [];
    private readonly Dictionary<int, int> _slotByNpc = new();
    private readonly HashSet<int> _wantHi = new();
    private readonly Stack<int> _freeSlots = new();
    private bool _initialised;

    private IWorldSnapshot? _current, _previous;
    private double _currentAt, _previousAt;
    private Vector3[] _pos = [];
    private float[] _heading = [];
    private float[] _keys = [];
    private int[] _order = [];
    private byte[] _lod = [];

    public int HiLodCount => _slotByNpc.Count;
    /// <summary>NPCs not in a hi slot (mid + far), as before A1.</summary>
    public int LowLodCount => MidLodCount + FarLodCount;
    public int MidLodCount { get; private set; }
    public int FarLodCount { get; private set; }
    public int SelectedId { get; set; } = -1;
    /// <summary>M6: the possessed NPC is drawn by its <see cref="PossessedController"/>, never here (any LOD). -1: none.</summary>
    public int PossessedId { get; set; } = -1;
    public IReadOnlyCollection<int> HiLodNpcIds => _slotByNpc.Keys;
    public int RenderedCount { get; private set; }
    public bool AtlasReady => _atlas?.IsReady == true;

    public override void _Ready() => Initialise();

    /// <summary>Creates the MultiMeshes and the hi-LOD pool. Idempotent; tests may call it without a scene tree.</summary>
    public void Initialise()
    {
        if (_initialised) return;
        _initialised = true;

        var everywhere = new Aabb(new Vector3(-4096, -512, -4096), new Vector3(8192, 1024, 8192));
        for (int s = 0; s < 2; s++)
        {
            var model = CharacterLibrary.MidModel(s == 0 ? Sex.Female : Sex.Male);
            var material = CharacterLibrary.MidMaterial(model);
            for (int p = 0; p < MidPoses.Length; p++)
            {
                var mesh = CharacterLibrary.Pose(model, MidPoses[p]) ?? (Mesh)new CapsuleMesh { Radius = 0.3f, Height = 1.7f };
                _mid[s, p] = NewLayer($"Mid_{(s == 0 ? "F" : "M")}_{MidPoses[p]}", mesh, material, everywhere,
                    MidCastShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off);
            }
        }

        _farMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/npc_far.gdshader") };
        _farMaterial.SetShaderParameter("atlas_grid", new Vector2(ImpostorAtlas.Columns, ImpostorAtlas.Rows));
        var quad = new QuadMesh { Size = new Vector2(2.4f, 2.4f) };
        _far = NewLayer("Far", quad, _farMaterial, everywhere, GeometryInstance3D.ShadowCastingSetting.Off);

        var ring = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1.15f, Rings = 24, RingSegments = 8 };
        ring.Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.95f, 0.4f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = true,
        };
        _selectionRing = new MeshInstance3D { Name = "SelectionRing", Mesh = ring, Visible = false };
        AddChild(_selectionRing);

        int slots = Math.Max(0, HiLodSlots);
        _pool = new NpcHi[slots];
        _slotNpc = new int[slots];
        for (int s = slots - 1; s >= 0; s--)
        {
            var node = HiScene?.Instantiate<NpcHi>() ?? new NpcHi();
            node.Name = $"Hi_{s}";
            AddChild(node);
            node.Release();
            _pool[s] = node;
            _slotNpc[s] = -1;
            _freeSlots.Push(s);
        }

        if (IsInsideTree() && DisplayServer.GetName() != "headless")
        {
            _atlas = new ImpostorAtlas { Name = "ImpostorAtlas" };
            AddChild(_atlas);
        }
    }

    private Layer NewLayer(string name, Mesh mesh, Material material, Aabb aabb, GeometryInstance3D.ShadowCastingSetting shadows)
    {
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = mesh,
            CustomAabb = aabb, // NPCs roam the whole map; skip per-upload AABB recomputation
        };
        var node = new MultiMeshInstance3D { Name = name, Multimesh = multi, MaterialOverride = material, CastShadow = shadows };
        AddChild(node);
        return new Layer { Node = node, Multi = multi };
    }

    /// <summary>Call once per frame with the snapshot Main read this frame and the camera position.</summary>
    public void Render(IWorldSnapshot snapshot, Vector3 viewer, double nowSeconds)
    {
        Initialise();
        if (!_farUsesAtlas && _atlas?.Texture is { } atlasTexture)
        {
            _farMaterial!.SetShaderParameter("atlas", atlasTexture);
            _farUsesAtlas = true;
        }
        if (!ReferenceEquals(snapshot, _current))
        {
            _previous = _current;
            _previousAt = _currentAt;
            _current = snapshot;
            _currentAt = nowSeconds;
        }

        var npcs = snapshot.Npcs.Span;
        int n = npcs.Length;
        EnsureCapacity(n);
        float cameraHeight = viewer.Y - (Ground?.Invoke(viewer.X, viewer.Z) ?? 0f);
        ZoomScale = 1f + (FarZoomScaleMax - 1f) * Mathf.SmoothStep(NearZoomHeight, FarZoomHeight, cameraHeight);

        // Interpolate between the previous and current snapshot when both list the same NPC at the same index.
        ReadOnlySpan<NpcView> prev = default;
        float alpha = 1f;
        if (Interpolate && _previous is not null && _previous.Npcs.Length == n && _currentAt > _previousAt)
        {
            prev = _previous.Npcs.Span;
            alpha = (float)Math.Clamp((nowSeconds - _currentAt) / (_currentAt - _previousAt), 0.0, 1.0);
        }
        for (int i = 0; i < n; i++)
        {
            ref readonly var v = ref npcs[i];
            var p = new Vector3(v.X, v.Y, v.Z);
            float h = v.Heading;
            if (!prev.IsEmpty && prev[i].Id == v.Id)
            {
                ref readonly var o = ref prev[i];
                p = new Vector3(o.X, o.Y, o.Z).Lerp(p, alpha);
                h = Mathf.LerpAngle(o.Heading, h, alpha);
            }
            if (Ground is not null) p.Y = MathF.Max(p.Y, Ground(p.X, p.Z));
            _pos[i] = p;
            _heading[i] = h;
            float d2 = p.DistanceSquaredTo(viewer);
            _keys[i] = v.Id == PossessedId ? float.MaxValue : _slotByNpc.ContainsKey(v.Id) ? d2 * Hysteresis * Hysteresis : d2;
            _order[i] = i;
            _lod[i] = 2; // far unless picked below
        }

        // The hi + mid nearest (by hysteresis-weighted distance), then the hi nearest among those.
        int k = Math.Min(_pool.Length, n);
        int km = Math.Min(n, k + Math.Max(0, MidLodSlots));
        if (km > 0 && km < n) SelectSmallest(_order, _keys, n, km);
        if (k > 0 && k < km) SelectSmallest(_order, _keys, km, k);
        _wantHi.Clear();
        for (int j = 0; j < k; j++)
            if (npcs[_order[j]].Id != PossessedId) _wantHi.Add(npcs[_order[j]].Id);
        for (int j = k; j < km; j++) _lod[_order[j]] = 1;

        for (int s = 0; s < _pool.Length; s++)
        {
            int id = _slotNpc[s];
            if (id < 0 || _wantHi.Contains(id)) continue;
            _slotByNpc.Remove(id);
            _slotNpc[s] = -1;
            _pool[s].Release();
            _freeSlots.Push(s);
        }
        foreach (int id in _wantHi)
        {
            if (_slotByNpc.ContainsKey(id) || _freeSlots.Count == 0) continue;
            int s = _freeSlots.Pop();
            _slotNpc[s] = id;
            _slotByNpc[id] = s;
        }

        foreach (var layer in _mid) layer.Count = 0;
        _far!.Count = 0;
        int mid = 0, far = 0;
        bool selectedFound = false;
        for (int i = 0; i < n; i++)
        {
            ref readonly var v = ref npcs[i];
            if (v.Id == PossessedId) continue; // M6: drawn by the possession controller
            float scale = NpcScale * StageScale(v.Stage);
            float state = v.Pose == NpcPose.Dead ? 1f : v.IsThinking ? -1f : 0f;
            var palette = CharacterLibrary.PaletteParams(v.Id, v.SettlementId, state);
            if (v.Id == SelectedId)
            {
                selectedFound = true;
                _selectionRing.Position = _pos[i] + new Vector3(0, 0.15f, 0);
                _selectionRing.Scale = Vector3.One * NpcScale;
            }
            if (_slotByNpc.TryGetValue(v.Id, out int slot))
            {
                _pool[slot].Apply(v, _pos[i], _heading[i], palette, scale, nowSeconds);
                continue;
            }
            scale *= ZoomScale;
            int sex = v.Sex == Sex.Male ? 1 : 0;
            if (_lod[i] == 1 || !_farUsesAtlas)
            {
                var pose = MidPose(v, nowSeconds);
                Write(_mid[sex, (int)pose], _pos[i], _heading[i], scale, palette);
                if (_lod[i] == 1) mid++;
                else far++;
            }
            else
            {
                bool lying = v.Pose is NpcPose.Sleeping or NpcPose.Dead;
                // Heading 0 faces +Z (toward the camera); the atlas column c shows the model turned by c/8 of a turn.
                int heading8 = ((int)MathF.Round(_heading[i] / (MathF.Tau / 8f)) % 8 + 8) % 8;
                var tint = Palette.SettlementColor(v.SettlementId).SrgbToLinear();
                if (v.Pose == NpcPose.Dead) tint = new Color(0.45f, 0.45f, 0.45f);
                tint.A = ImpostorAtlas.Cell(v.Sex, lying, heading8);
                WriteBillboard(_far, _pos[i] + new Vector3(0, (lying ? 0.35f : 0.95f) * scale, 0), scale, tint);
                far++;
            }
        }
        _selectionRing.Visible = selectedFound;

        foreach (var layer in _mid) Upload(layer);
        Upload(_far);
        MidLodCount = mid;
        FarLodCount = far;
        RenderedCount = n;
    }

    private static BakedPose MidPose(in NpcView v, double now) => v.Pose switch
    {
        NpcPose.Walking => ((long)(now * 5.0 + (v.Id & 0xFF) * 0.37) & 1) == 0 ? BakedPose.WalkA : BakedPose.WalkB,
        NpcPose.Working => BakedPose.Work,
        NpcPose.Fighting => BakedPose.Fight,
        NpcPose.Sleeping or NpcPose.Dead => BakedPose.Lying,
        _ => BakedPose.Idle,
    };

    /// <summary>Screen-space pick over every NPC (all LODs). Returns the NPC id or -1.</summary>
    public int Pick(Camera3D camera, Vector2 screenPosition, float maxPixels = 28f)
    {
        if (_current is null) return -1;
        var npcs = _current.Npcs.Span;
        int n = Math.Min(npcs.Length, _pos.Length);
        int best = -1;
        float bestD2 = maxPixels * maxPixels;
        for (int i = 0; i < n; i++)
        {
            if (npcs[i].Id == PossessedId) continue;
            var world = _pos[i] + new Vector3(0, 0.85f * NpcScale * StageScale(npcs[i].Stage), 0);
            if (camera.IsPositionBehind(world)) continue;
            float d2 = camera.UnprojectPosition(world).DistanceSquaredTo(screenPosition);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = npcs[i].Id;
            }
        }
        return best;
    }

    private void EnsureCapacity(int n)
    {
        if (_pos.Length >= n) return;
        int cap = Math.Max(n, _pos.Length * 2);
        _pos = new Vector3[cap];
        _heading = new float[cap];
        _keys = new float[cap];
        _order = new int[cap];
        _lod = new byte[cap];
    }

    private static void Write(Layer layer, Vector3 pos, float heading, float scale, Color custom)
    {
        var basis = new Basis(Vector3.Up, heading).Scaled(new Vector3(scale, scale, scale));
        Put(layer, basis, pos, custom);
    }

    private static void WriteBillboard(Layer layer, Vector3 centre, float scale, Color custom)
        => Put(layer, Basis.FromScale(new Vector3(scale, scale, scale)), centre, custom);

    private static void Put(Layer layer, in Basis basis, Vector3 origin, Color c)
    {
        if ((layer.Count + 1) * FloatsPerInstance > layer.Buffer.Length)
            Array.Resize(ref layer.Buffer, Math.Max(64 * FloatsPerInstance, layer.Buffer.Length * 2));
        int o = layer.Count * FloatsPerInstance;
        var b = layer.Buffer;
        b[o + 0] = basis.X.X; b[o + 1] = basis.Y.X; b[o + 2] = basis.Z.X; b[o + 3] = origin.X;
        b[o + 4] = basis.X.Y; b[o + 5] = basis.Y.Y; b[o + 6] = basis.Z.Y; b[o + 7] = origin.Y;
        b[o + 8] = basis.X.Z; b[o + 9] = basis.Y.Z; b[o + 10] = basis.Z.Z; b[o + 11] = origin.Z;
        b[o + 12] = c.R; b[o + 13] = c.G; b[o + 14] = c.B; b[o + 15] = c.A;
        layer.Count++;
    }

    /// <summary>One buffer upload per layer; capacity only grows, the visible count trims the rest.</summary>
    private static void Upload(Layer layer)
    {
        int capacity = layer.Buffer.Length / FloatsPerInstance;
        if (layer.Multi.InstanceCount != capacity) layer.Multi.InstanceCount = capacity;
        layer.Multi.VisibleInstanceCount = layer.Count;
        layer.Node.Visible = layer.Count > 0;
        if (capacity > 0 && layer.Count > 0) RenderingServer.MultimeshSetBuffer(layer.Multi.GetRid(), layer.Buffer);
    }

    /// <summary>Spec 45 heights over the 1.75 m adult: infants 1.0 m, children 1.35 m, elders 1.65 m (and stooped in hi-LOD).</summary>
    public static float StageScale(LifeStage stage) => stage switch
    {
        LifeStage.Infant => 1.0f / 1.75f,
        LifeStage.Child => 1.35f / 1.75f,
        LifeStage.Elder => 1.65f / 1.75f,
        _ => 1f,
    };

    /// <summary>Partial quickselect: afterwards order[0..k) hold the k smallest keys (unordered).</summary>
    private static void SelectSmallest(int[] order, float[] keys, int n, int k)
    {
        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            float pivot = keys[order[(lo + hi) >>> 1]];
            int i = lo, j = hi;
            while (i <= j)
            {
                while (keys[order[i]] < pivot) i++;
                while (keys[order[j]] > pivot) j--;
                if (i <= j)
                {
                    (order[i], order[j]) = (order[j], order[i]);
                    i++;
                    j--;
                }
            }
            if (k - 1 <= j) hi = j;
            else if (k - 1 >= i) lo = i;
            else break;
        }
    }
}
