using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Draws every NPC in the snapshot. The <see cref="HiLodSlots"/> nearest NPCs to the viewer (with
/// hysteresis so slots do not flicker at the boundary) get a pooled <see cref="NpcHi"/> instance; everyone
/// else is one instance of a single <see cref="MultiMeshInstance3D"/> written with one buffer upload per frame.
/// Positions are interpolated between the previous and the latest snapshot so 20 Hz sim output looks smooth.
/// Allocation-free per frame once the arrays have grown to the population.
/// </summary>
public partial class NpcRenderer : Node3D
{
    private const int FloatsPerInstance = 16; // Transform3D (12) + Color (4)

    [Export] public PackedScene? HiScene { get; set; }
    [Export] public int HiLodSlots { get; set; } = 50;
    /// <summary>Current hi-LOD NPCs compete with their distance multiplied by this, so they keep the slot until clearly farther.</summary>
    [Export] public float Hysteresis { get; set; } = 0.85f;
    /// <summary>Visual exaggeration so people read at god-camera distances.</summary>
    [Export] public float NpcScale { get; set; } = 1.6f;
    [Export] public bool Interpolate { get; set; } = true;

    private static readonly Color[] SettlementColors =
    [
        new(0.90f, 0.35f, 0.30f), new(0.30f, 0.55f, 0.90f), new(0.95f, 0.80f, 0.25f),
        new(0.55f, 0.35f, 0.80f), new(0.95f, 0.55f, 0.20f), new(0.25f, 0.80f, 0.75f),
        new(0.85f, 0.45f, 0.70f), new(0.60f, 0.75f, 0.30f),
    ];

    private MultiMeshInstance3D _multiInstance = null!;
    private MultiMesh _multiMesh = null!;
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
    private float[] _buffer = [];

    public int HiLodCount => _slotByNpc.Count;
    public int LowLodCount { get; private set; }
    public int SelectedId { get; set; } = -1;
    public IReadOnlyCollection<int> HiLodNpcIds => _slotByNpc.Keys;
    public int RenderedCount { get; private set; }

    public override void _Ready() => Initialise();

    /// <summary>Creates the MultiMesh and the hi-LOD pool. Idempotent; tests may call it without a scene tree.</summary>
    public void Initialise()
    {
        if (_initialised) return;
        _initialised = true;

        var bodyMesh = new CapsuleMesh { Radius = 0.3f, Height = 1.7f, RadialSegments = 6, Rings = 1 };
        bodyMesh.Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.85f };
        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = bodyMesh,
        };
        _multiInstance = new MultiMeshInstance3D { Name = "LowLod", Multimesh = _multiMesh };
        AddChild(_multiInstance);

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
    }

    /// <summary>Call once per frame with the snapshot Main read this frame and the camera position.</summary>
    public void Render(IWorldSnapshot snapshot, Vector3 viewer, double nowSeconds)
    {
        Initialise();
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
            _pos[i] = p;
            _heading[i] = h;
            float d2 = p.DistanceSquaredTo(viewer);
            _keys[i] = _slotByNpc.ContainsKey(v.Id) ? d2 * Hysteresis * Hysteresis : d2;
            _order[i] = i;
        }

        // Pick the K nearest (by hysteresis-weighted distance).
        int k = Math.Min(_pool.Length, n);
        if (k > 0 && k < n) SelectSmallest(_order, _keys, n, k);
        _wantHi.Clear();
        for (int j = 0; j < k; j++) _wantHi.Add(npcs[_order[j]].Id);

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

        // Write everyone: hi-LOD nodes or MultiMesh instances.
        int low = 0;
        bool selectedFound = false;
        for (int i = 0; i < n; i++)
        {
            ref readonly var v = ref npcs[i];
            float scale = NpcScale * StageScale(v.Stage);
            var color = ColorFor(v);
            if (v.Id == SelectedId)
            {
                selectedFound = true;
                _selectionRing.Position = _pos[i] + new Vector3(0, 0.15f, 0);
                _selectionRing.Scale = Vector3.One * NpcScale;
            }
            if (_slotByNpc.TryGetValue(v.Id, out int slot))
            {
                _pool[slot].Apply(v, _pos[i], _heading[i], color, scale, nowSeconds);
                continue;
            }
            WriteInstance(low++, v, _pos[i], _heading[i], color, scale);
        }
        _selectionRing.Visible = selectedFound;

        LowLodCount = low;
        RenderedCount = n;
        _multiMesh.VisibleInstanceCount = low;
        if (_multiMesh.InstanceCount > 0)
            RenderingServer.MultimeshSetBuffer(_multiMesh.GetRid(), _buffer);
    }

    /// <summary>Screen-space pick over every NPC (hi and MultiMesh alike). Returns the NPC id or -1.</summary>
    public int Pick(Camera3D camera, Vector2 screenPosition, float maxPixels = 28f)
    {
        if (_current is null) return -1;
        var npcs = _current.Npcs.Span;
        int n = Math.Min(npcs.Length, _pos.Length);
        int best = -1;
        float bestD2 = maxPixels * maxPixels;
        for (int i = 0; i < n; i++)
        {
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
        if (_pos.Length < n)
        {
            int cap = Math.Max(n, _pos.Length * 2);
            _pos = new Vector3[cap];
            _heading = new float[cap];
            _keys = new float[cap];
            _order = new int[cap];
        }
        if (_multiMesh.InstanceCount < n)
        {
            int cap = Math.Max(n, _multiMesh.InstanceCount * 2);
            _multiMesh.InstanceCount = cap; // resizing clears the buffer, which we rewrite below
            _buffer = new float[cap * FloatsPerInstance];
        }
    }

    private void WriteInstance(int index, in NpcView v, Vector3 pos, float heading, Color color, float scale)
    {
        var basis = new Basis(Vector3.Up, heading);
        Vector3 origin;
        if (v.Pose is NpcPose.Sleeping or NpcPose.Dead)
        {
            basis *= new Basis(Vector3.Right, MathF.PI * 0.5f);
            origin = pos + new Vector3(0, 0.3f * scale, 0);
        }
        else
        {
            origin = pos + new Vector3(0, 0.85f * scale, 0);
        }
        basis = basis.Scaled(new Vector3(scale, scale, scale));
        int o = index * FloatsPerInstance;
        var b = _buffer;
        b[o + 0] = basis.X.X; b[o + 1] = basis.Y.X; b[o + 2] = basis.Z.X; b[o + 3] = origin.X;
        b[o + 4] = basis.X.Y; b[o + 5] = basis.Y.Y; b[o + 6] = basis.Z.Y; b[o + 7] = origin.Y;
        b[o + 8] = basis.X.Z; b[o + 9] = basis.Y.Z; b[o + 10] = basis.Z.Z; b[o + 11] = origin.Z;
        b[o + 12] = color.R; b[o + 13] = color.G; b[o + 14] = color.B; b[o + 15] = color.A;
    }

    private static float StageScale(LifeStage stage) => stage switch
    {
        LifeStage.Infant => 0.45f,
        LifeStage.Child => 0.65f,
        LifeStage.Elder => 0.95f,
        _ => 1f,
    };

    private static Color ColorFor(in NpcView v)
    {
        if (v.Pose == NpcPose.Dead) return new Color(0.35f, 0.35f, 0.35f);
        var c = SettlementColors[(int)((uint)v.SettlementId % SettlementColors.Length)];
        if (v.IsThinking) c = c.Lightened(0.35f);
        if (v.Stage == LifeStage.Elder) c = c.Lerp(new Color(0.8f, 0.8f, 0.8f), 0.35f);
        return c;
    }

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
