using Eden.Game.Art;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// A2 Nova Roma sky: <see cref="Count"/> cartoon cumulus clouds (five procedural shapes, <see cref="ProceduralMeshes.Cloud"/>)
/// drifting slowly with the wind at <see cref="MinHeight"/>–<see cref="MaxHeight"/> metres, most of them around the
/// island's shores and over the sea, a few over land. One MultiMesh per shape, positions updated on the CPU each frame
/// (a few dozen transforms). The weather (<see cref="Weather"/>) sets how many show and their colour: rain thickens and
/// darkens the sky, drought thins it; changes ease over a few seconds. No shadows by default (<see cref="CastShadows"/>).
/// Night tints them blue-grey through <see cref="Daylight"/>.
/// </summary>
public partial class CloudLayer : Node3D
{
    [Export] public int Count { get; set; } = 90;
    [Export] public float MinHeight { get; set; } = 60f;
    [Export] public float MaxHeight { get; set; } = 120f;
    [Export] public float MinSize { get; set; } = 22f;
    [Export] public float MaxSize { get; set; } = 46f;
    /// <summary>Wind, metres per real second (clouds wrap around the area).</summary>
    [Export] public Vector2 Wind { get; set; } = new(1.6f, 0.7f);
    /// <summary>Off by default: cloud shadows cost a shadow pass for a few dozen transparent meshes.</summary>
    [Export] public bool CastShadows { get; set; }
    /// <summary>Seconds for the coverage and tint to ease to a new weather.</summary>
    [Export] public float EaseSeconds { get; set; } = 3f;

    public WeatherState Weather { get; } = new();

    /// <summary>0 at night, 1 by day (Main's hook passes <see cref="SkyCycle.Daylight"/>).</summary>
    public Func<float>? Daylight { get; set; }

    /// <summary>The snapshot read this frame (Main's hook), for weather from the Chronicle and the tick.</summary>
    public Func<IWorldSnapshot?>? Frame { get; set; }

    private readonly record struct Cloud(int Variant, Vector3 Start, Vector3 Scale, float Yaw, float Threshold);

    private Cloud[] _clouds = [];
    private MultiMesh[] _multi = [];
    private int[] _counts = [];
    private ShaderMaterial? _material;
    private Rect2 _area;
    private double _time;
    private int[] _index = [];
    private WorldInfo? _info;

    /// <summary>Coverage and tint actually shown (eased), for tests and the fx check.</summary>
    public float Coverage { get; private set; } = WeatherState.CoverageOf(SkyWeather.Clear);
    public Color Tint { get; private set; } = Colors.White;

    /// <summary>Clouds whose threshold the current coverage passes (roughly the number drawn), for tests.</summary>
    public int VisibleClouds
    {
        get
        {
            int n = 0;
            foreach (var c in _clouds) if (c.Threshold < Coverage) n++;
            return n;
        }
    }

    public int CloudCount => _clouds.Length;

    /// <summary>World extent the clouds drift over (the map plus a margin of sea). Rebuilds the set.</summary>
    public void Configure(WorldInfo info, ReadOnlySpan<float> heights)
    {
        _info = info;
        float sx = info.Width * info.CellSize, sz = info.Height * info.CellSize;
        float margin = MathF.Max(sx, sz) * 0.35f;
        _area = new Rect2(-margin, -margin, sx + 2 * margin, sz + 2 * margin);
        Build(info, heights);
    }

    private void Build(WorldInfo info, ReadOnlySpan<float> heights)
    {
        foreach (var child in GetChildren()) child.QueueFree();
        _material ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/clouds.gdshader") };
        var list = new List<Cloud>(Count);
        uint h = (uint)info.Seed * 2654435761u + 77u;
        float Next() { h = h * 1664525u + 1013904223u; return (h >> 8) / 16777216f; }
        float sx = info.Width * info.CellSize, sz = info.Height * info.CellSize;
        int attempts = 0;
        while (list.Count < Count && attempts++ < Count * 20)
        {
            var p = new Vector2(_area.Position.X + Next() * _area.Size.X, _area.Position.Y + Next() * _area.Size.Y);
            // Prefer shores and sea (Nova Roma): over high land keep only one in two.
            bool land = p.X >= 0 && p.Y >= 0 && p.X < sx && p.Y < sz && HeightField.Sample(heights, info, p.X, p.Y) > info.SeaLevel + 3f;
            float keep = Next();
            if (land && keep > 0.5f) continue;
            float size = MinSize + (MaxSize - MinSize) * Next();
            float y = MinHeight + (MaxHeight - MinHeight) * Next();
            var scale = new Vector3(size, size * (0.75f + 0.25f * Next()), size * (0.55f + 0.25f * Next()));
            list.Add(new Cloud(list.Count % ProceduralMeshes.CloudVariants, new Vector3(p.X, y, p.Y), scale, Next() * 0.6f - 0.3f, Next()));
        }
        _clouds = list.ToArray();
        _multi = new MultiMesh[ProceduralMeshes.CloudVariants];
        _counts = new int[_multi.Length];
        foreach (var c in _clouds) _counts[c.Variant]++;
        for (int v = 0; v < _multi.Length; v++)
        {
            _multi[v] = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = ProceduralMeshes.Cloud(v),
                InstanceCount = _counts[v],
                CustomAabb = new Aabb(new Vector3(_area.Position.X - 100, 0, _area.Position.Y - 100), new Vector3(_area.Size.X + 200, 300, _area.Size.Y + 200)),
            };
            AddChild(new MultiMeshInstance3D
            {
                Name = $"Clouds_{v}",
                Multimesh = _multi[v],
                MaterialOverride = _material,
                CastShadow = CastShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        Coverage = WeatherState.CoverageOf(SkyWeather.Clear);
        Place(0);
    }

    public override void _Process(double delta) => Step(delta);

    /// <summary>Advances drift and weather by <paramref name="delta"/> seconds (public for tests and the fx check).</summary>
    public void Step(double delta)
    {
        if (_clouds.Length == 0 || _material is null) return;
        _time += delta;
        if (Frame?.Invoke() is { } s)
        {
            if (s.World != _info) Configure(s.World, s.Heights.Span); // a loaded world
            Weather.Observe(s);
        }
        float k = 1f - MathF.Exp(-(float)delta * 3f / MathF.Max(0.1f, EaseSeconds));
        Coverage = Mathf.Lerp(Coverage, WeatherState.CoverageOf(Weather.Kind), k);
        Tint = Tint.Lerp(WeatherState.CloudTintOf(Weather.Kind), k);
        _material.SetShaderParameter("coverage", Coverage);
        _material.SetShaderParameter("tint", new Vector3(Tint.R, Tint.G, Tint.B));
        _material.SetShaderParameter("daylight", Daylight?.Invoke() ?? 1f);
        Place(_time);
    }

    /// <summary>Snaps coverage and tint to the current weather (screenshots).</summary>
    public void Settle()
    {
        Coverage = WeatherState.CoverageOf(Weather.Kind);
        Tint = WeatherState.CloudTintOf(Weather.Kind);
        Step(0);
    }

    private void Place(double time)
    {
        if (_index.Length != _multi.Length) _index = new int[_multi.Length];
        Array.Clear(_index);
        var index = _index;
        var drift = Wind * (float)time;
        foreach (var c in _clouds)
        {
            float x = Wrap(c.Start.X + drift.X, _area.Position.X, _area.Size.X);
            float z = Wrap(c.Start.Z + drift.Y, _area.Position.Y, _area.Size.Y);
            var xf = new Transform3D(new Basis(Vector3.Up, c.Yaw).Scaled(c.Scale), new Vector3(x, c.Start.Y, z));
            int i = index[c.Variant]++;
            _multi[c.Variant].SetInstanceTransform(i, xf);
            _multi[c.Variant].SetInstanceCustomData(i, new Color(1, 1, 1, c.Threshold));
        }
    }

    private static float Wrap(float v, float start, float size) => start + ((v - start) % size + size) % size;
}
