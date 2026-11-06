using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>Root of scenes/world/water.tscn: a flat sea plane at <see cref="WorldInfo.SeaLevel"/>, centred on the map.</summary>
public partial class WaterPlane : MeshInstance3D
{
    /// <summary>Plane edge as a multiple of the map size, so the horizon is sea at max zoom.</summary>
    [Export] public float Margin { get; set; } = 6f;

    private WorldInfo? _info;

    public void Configure(WorldInfo info)
    {
        if (_info == info) return;
        _info = info;
        float sizeX = info.Width * info.CellSize, sizeZ = info.Height * info.CellSize;
        float size = MathF.Max(sizeX, sizeZ) * Margin;
        if (Mesh is PlaneMesh plane) plane.Size = new Vector2(size, size);
        else Mesh = new PlaneMesh { Size = new Vector2(size, size) };
        Position = new Vector3(sizeX * 0.5f, info.SeaLevel, sizeZ * 0.5f);
    }
}
