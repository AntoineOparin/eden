using Eden.Game.Art;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Draws <see cref="IWorldSnapshot.Buildings"/>: MultiMeshes per kind (baked by <see cref="BuildingFactory"/>) and per
/// 128 m chunk, so a village costs about one draw call per kind present. Every building stands on a
/// <see cref="Foundation"/>: at the highest terrain sample of its footprint, on a stone plinth reaching the lowest
/// sample (procedural, <see cref="ProceduralMeshes.Plinth"/>); <see cref="Foundations"/> feeds the terrain flattening.
/// Finished buildings take their settlement colour on the banner accents; construction sites draw the scaffold plus
/// the building rising with progress. Docks get a moored boat. Re-batched only when the snapshot's array changed, at
/// most every <see cref="RefreshSeconds"/>.
/// </summary>
public partial class BuildingRenderer : Node3D
{
    [Export] public double RefreshSeconds { get; set; } = 0.25;
    [Export] public float ChunkSize { get; set; } = 128f;

    private PropBatches? _batches;
    private ReadOnlyMemory<BuildingView> _drawn;
    private double _lastRefresh = double.NegativeInfinity;
    private readonly List<Foundation> _foundations = new();

    public int InstanceCount => _batches?.InstanceCount ?? 0;
    public int Batches => _batches?.ActiveBatches ?? 0;

    /// <summary>Foundations of the last batch, one per building with a mesh.</summary>
    public IReadOnlyList<Foundation> Foundations => _foundations;

    /// <summary>Bumped whenever <see cref="Foundations"/> changed (new, removed or moved buildings).</summary>
    public int FoundationsVersion { get; private set; }

    private void Initialise()
    {
        if (_batches is not null) return;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/props.gdshader") };
        material.SetShaderParameter("accent_replace", true);
        _batches = new PropBatches(this, material, castShadows: true);
    }

    public void Render(IWorldSnapshot s, double nowSeconds)
    {
        Initialise();
        if (s.Buildings.Equals(_drawn) || nowSeconds - _lastRefresh < RefreshSeconds) return;
        _drawn = s.Buildings;
        _lastRefresh = nowSeconds;
        Rebatch(s);
    }

    /// <summary>Rebuilds every building batch and foundation from the snapshot. Public for tests.</summary>
    public void Rebatch(IWorldSnapshot s)
    {
        Initialise();
        _batches!.Begin();
        var site = BuildingFactory.Mesh(BuildingFactory.ConstructionSite);
        var plinth = ProceduralMeshes.Plinth();
        var bank = ProceduralMeshes.Plinth(earth: true);
        var boat = ProceduralMeshes.Boat();
        var info = s.World;
        var heights = s.Heights.Span;
        int chunksX = Math.Max(1, (int)MathF.Ceiling(info.Width * info.CellSize / ChunkSize));
        ulong signature = 1469598103934665603UL;
        _foundations.Clear();
        foreach (ref readonly var b in s.Buildings.Span)
        {
            var mesh = BuildingFactory.Mesh(b.Kind);
            if (mesh is null) continue;
            var f = Foundation.Compute(heights, info, b, mesh.GetAabb());
            _foundations.Add(f);
            signature = (signature ^ (ulong)b.Id) * 1099511628211UL;
            signature = (signature ^ BitConverter.SingleToUInt32Bits(f.Top)) * 1099511628211UL;

            int chunk = (int)(b.Z / ChunkSize) * chunksX + (int)(b.X / ChunkSize);
            var accent = Palette.SettlementColor(b.SettlementId).SrgbToLinear();
            var basis = new Basis(Vector3.Up, b.Rotation);
            var origin = f.Centre + new Vector3(0, 0.02f, 0);
            if (f.Height > Foundation.PlinthThreshold + Foundation.Margin && b.Kind != "dock")
            {
                var size = new Vector3(f.HalfExtents.X * 2.1f, f.Height, f.HalfExtents.Y * 2.1f);
                _batches.Add(chunk, b.Kind == "farm-plot" ? bank : plinth, new Transform3D(basis * Basis.FromScale(size), new Vector3(b.X, f.Top + 0.03f, b.Z)), Colors.White);
            }
            if (b.Progress >= 1f)
            {
                _batches.Add(chunk, mesh, new Transform3D(basis, origin), accent);
                if (b.Kind == "dock")
                {
                    var boatPos = origin + basis * new Vector3(f.HalfExtents.X + 1.2f, 0, 0);
                    boatPos.Y = info.SeaLevel - 0.1f;
                    _batches.Add(chunk, boat, new Transform3D(new Basis(Vector3.Up, b.Rotation + 0.15f), boatPos), Colors.White);
                }
                continue;
            }
            // Construction: the scaffold plus the building squashed to its progress (never flat, so it reads as begun).
            if (site is not null) _batches.Add(chunk, site, new Transform3D(basis, origin), accent);
            float p = Math.Clamp(b.Progress, 0f, 1f);
            if (p > 0.05f) _batches.Add(chunk, mesh, new Transform3D(basis * Basis.FromScale(new Vector3(1f, 0.15f + 0.85f * p, 1f)), origin), accent);
        }
        _batches.Commit();
        if (signature != _signature)
        {
            _signature = signature;
            FoundationsVersion++;
        }
    }

    private ulong _signature;
}
