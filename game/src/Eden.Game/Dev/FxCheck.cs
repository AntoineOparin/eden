using Godot;

namespace Eden.Game.Dev;

/// <summary>
/// <c>--fx-check=DIR</c> (A2): plays every god power's local effect at one fixed spot (the town with the most buildings,
/// or the island centre) and saves a screenshot a set time after each starts (<c>DIR/fx-&lt;power&gt;.jpg</c>, 1600 × 900
/// if the window is), so the effects can be reviewed as pictures. Effects only: nothing is sent to the sim except
/// lightning, whose flash lives in <see cref="Main.StrikeAt"/>. Rain and drought also get a wide shot showing the
/// clouds at 320 m (<c>fx-rain-sky.jpg</c>, <c>fx-drought-sky.jpg</c>). Quits when done.
/// </summary>
public partial class FxCheck : Node
{
    public string OutputDir { get; set; } = "user://fx";
    [Export] public float Zoom { get; set; } = 55f;
    [Export] public double WarmUpSeconds { get; set; } = 2.5;

    private readonly Queue<(double Wait, Action Act)> _steps = new();
    private double _timer;
    private Main? _main;
    private Vector3 _spot;

    public override void _Ready()
    {
        _main = GetParent<Main>();
        string dir = OutputDir.StartsWith("res://") || OutputDir.StartsWith("user://") ? ProjectSettings.GlobalizePath(OutputDir) : OutputDir;
        DirAccess.MakeDirRecursiveAbsolute(dir);
        OutputDir = dir;
        _steps.Enqueue((WarmUpSeconds, Aim));
        // (power, seconds after the start when the picture is taken, seconds to let it finish)
        var plan = new (GodPower Power, double Shot, double Settle)[]
        {
            (GodPower.SpawnHuman, 0.35, 1.6), (GodPower.SpawnAnimal, 0.35, 1.6), (GodPower.SpawnPlant, 0.35, 1.6),
            (GodPower.Rain, 1.0, 0.1), (GodPower.Drought, 1.2, 0.1),
            (GodPower.RaiseTerrain, 0.3, 1.5), (GodPower.LowerTerrain, 0.3, 1.5),
            (GodPower.Bless, 0.45, 2.0), (GodPower.Curse, 0.9, 3.0), (GodPower.DropFood, 0.8, 1.6),
            (GodPower.Lightning, 0.06, 1.0),
        };
        foreach (var (power, shot, settle) in plan)
        {
            _steps.Enqueue((0.1, () => Play(power)));
            _steps.Enqueue((shot, () => Shot($"fx-{Name(power)}.jpg")));
            if (power is GodPower.Rain or GodPower.Drought)
            {
                // Let the clouds ease to the new weather, then a wide shot of the sky, then back to clear.
                _steps.Enqueue((0.1, () => _main?.CameraRig?.SetZoom(320f, immediate: true)));
                _steps.Enqueue((3.5, () => Shot($"fx-{Name(power)}-sky.jpg")));
                _steps.Enqueue((0.1, () =>
                {
                    _main?.CameraRig?.SetZoom(Zoom, immediate: true);
                    _main?.Clouds?.Weather.Begin(SkyWeather.Clear, -1);
                    _main?.Clouds?.Settle();
                }));
                _steps.Enqueue((5.0, () => { }));
            }
            else _steps.Enqueue((settle, () => { }));
        }
        _steps.Enqueue((0.2, () =>
        {
            GD.Print($"[fx-check] done, shots in {OutputDir}");
            GetTree().Quit();
        }));
    }

    public override void _Process(double delta)
    {
        if (_steps.Count == 0) return;
        _timer += delta;
        var (wait, act) = _steps.Peek();
        if (_timer < wait) return;
        _timer = 0;
        _steps.Dequeue();
        act();
    }

    private void Aim()
    {
        if (_main?.Host?.Latest is not { } s || _main.CameraRig is not { } rig) return;
        var info = s.World;
        var centre = new Vector2(info.Width * info.CellSize * 0.5f, info.Height * info.CellSize * 0.5f);
        var at = centre;
        var buildings = s.Buildings.Span;
        if (buildings.Length > 0)
        {
            float sx = 0, sz = 0;
            foreach (var b in buildings) { sx += b.X; sz += b.Z; }
            at = new Vector2(sx / buildings.Length, sz / buildings.Length);
        }
        else
        {
            // Fake sim: walk from the centre to the nearest dry land.
            for (float r = 0; r < 200 && _main.Terrain?.SampleRender(at.X, at.Y) <= info.SeaLevel + 1f; r += 4) at = centre + new Vector2(r, r * 0.5f);
        }
        _main.Host?.Enqueue(new Eden.Sim.Contracts.SetTimeScaleCommand(Eden.Sim.Contracts.TimeScale.Paused)); // same light in every shot
        rig.Focus(at);
        rig.SetZoom(Zoom, immediate: true);
        _spot = new Vector3(at.X, _main.Terrain?.SampleRender(at.X, at.Y) ?? 0f, at.Y);
        GD.Print($"[fx-check] spot {_spot}");
    }

    private void Play(GodPower power)
    {
        if (_main is null) return;
        if (power == GodPower.Lightning) _main.StrikeAt(_spot.X, _spot.Z);
        else _main.Fx?.Play(power, _spot, _main.Hud?.Powers?.BrushRadius ?? 12f);
    }

    private void Shot(string file)
    {
        var image = GetViewport().GetTexture().GetImage();
        string path = OutputDir.TrimEnd('/') + "/" + file;
        var err = image.SaveJpg(path, 0.85f);
        GD.Print($"[fx-check] {path} ({err})");
    }

    private static string Name(GodPower p) => p switch
    {
        GodPower.SpawnHuman => "spawn-human",
        GodPower.SpawnAnimal => "spawn-animals",
        GodPower.SpawnPlant => "spawn-plants",
        GodPower.RaiseTerrain => "raise-land",
        GodPower.LowerTerrain => "lower-land",
        GodPower.DropFood => "drop-food",
        _ => p.ToString().ToLowerInvariant(),
    };
}
