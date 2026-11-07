using Godot;

namespace Eden.Game;

/// <summary>
/// A pool of MultiMeshInstance3Ds keyed by (chunk, mesh). Callers <see cref="Begin"/>, <see cref="Add"/> instances,
/// then <see cref="Commit"/>, which uploads one buffer per batch (transform + custom data, 16 floats) and hides batches
/// that received nothing. Batches persist across commits, so steady-state refreshes allocate nothing new.
/// </summary>
public sealed class PropBatches
{
    private const int Stride = 16;

    private sealed class Batch
    {
        public required MultiMeshInstance3D Node;
        public required MultiMesh Multi;
        public float[] Buffer = new float[Stride * 16];
        public float[] Exact = [];
        public int Count;
    }

    private readonly Node3D _parent;
    private readonly Material _material;
    private readonly GeometryInstance3D.ShadowCastingSetting _shadows;
    private readonly float _visibilityEnd;
    private readonly Dictionary<(int Chunk, Mesh Mesh), Batch> _batches = new();

    public PropBatches(Node3D parent, Material material, bool castShadows, float visibilityEnd = 0f)
    {
        _parent = parent;
        _material = material;
        _shadows = castShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
        _visibilityEnd = visibilityEnd;
    }

    /// <summary>Instances written in the last commit.</summary>
    public int InstanceCount { get; private set; }

    /// <summary>Batches that drew at least one instance in the last commit.</summary>
    public int ActiveBatches { get; private set; }

    public void Begin()
    {
        foreach (var b in _batches.Values) b.Count = 0;
    }

    public void Add(int chunk, Mesh mesh, in Transform3D xf, Color custom)
    {
        if (!_batches.TryGetValue((chunk, mesh), out var b))
        {
            var multi = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = mesh,
            };
            var node = new MultiMeshInstance3D
            {
                Name = $"{mesh.ResourceName.Replace(':', '_')}_{chunk}",
                Multimesh = multi,
                MaterialOverride = _material,
                CastShadow = _shadows,
            };
            if (_visibilityEnd > 0)
            {
                node.VisibilityRangeEnd = _visibilityEnd;
                node.VisibilityRangeEndMargin = _visibilityEnd * 0.15f;
                node.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
            }
            _parent.AddChild(node);
            b = new Batch { Node = node, Multi = multi };
            _batches[(chunk, mesh)] = b;
        }
        if ((b.Count + 1) * Stride > b.Buffer.Length) Array.Resize(ref b.Buffer, b.Buffer.Length * 2);
        int o = b.Count * Stride;
        var buf = b.Buffer;
        var basis = xf.Basis;
        buf[o + 0] = basis.X.X; buf[o + 1] = basis.Y.X; buf[o + 2] = basis.Z.X; buf[o + 3] = xf.Origin.X;
        buf[o + 4] = basis.X.Y; buf[o + 5] = basis.Y.Y; buf[o + 6] = basis.Z.Y; buf[o + 7] = xf.Origin.Y;
        buf[o + 8] = basis.X.Z; buf[o + 9] = basis.Y.Z; buf[o + 10] = basis.Z.Z; buf[o + 11] = xf.Origin.Z;
        buf[o + 12] = custom.R; buf[o + 13] = custom.G; buf[o + 14] = custom.B; buf[o + 15] = custom.A;
        b.Count++;
    }

    public void Commit()
    {
        int instances = 0, active = 0;
        foreach (var b in _batches.Values)
        {
            if (b.Count == 0)
            {
                b.Node.Visible = false;
                continue;
            }
            b.Node.Visible = true;
            if (b.Multi.InstanceCount != b.Count)
            {
                b.Multi.InstanceCount = 0; // resizing clears; set exact count, the buffer is rewritten below
                b.Multi.InstanceCount = b.Count;
            }
            int floats = b.Count * Stride;
            if (b.Exact.Length != floats) b.Exact = new float[floats]; // only when the count changed
            Array.Copy(b.Buffer, b.Exact, floats);
            RenderingServer.MultimeshSetBuffer(b.Multi.GetRid(), b.Exact);
            instances += b.Count;
            active++;
        }
        InstanceCount = instances;
        ActiveBatches = active;
    }

    public void Clear()
    {
        foreach (var b in _batches.Values) b.Node.QueueFree();
        _batches.Clear();
        InstanceCount = 0;
        ActiveBatches = 0;
    }
}
