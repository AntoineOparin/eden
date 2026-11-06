using Eden.Game.Dev;
using Eden.Sim;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Root node of scenes/main.tscn. Owns the sim host; every frame it reads <see cref="ISimHost.Latest"/> once and
/// hands that same snapshot to the terrain, NPC renderer and UI. The real <see cref="SimHost"/> is the default;
/// <see cref="UseFakeSim"/> (or the user arg <c>--fake-sim</c>) swaps in <see cref="FakeSimHost"/> for M2 work.
/// User args (after <c>--</c>): <c>--fake-sim</c>, <c>--npcs=N</c>, <c>--log-fps</c>, <c>--screenshot=PATH</c>,
/// <c>--quit-after=SECONDS</c>.
/// </summary>
public partial class Main : Node
{
    [Export] public int Seed { get; set; } = 1;
    [Export] public bool UseFakeSim { get; set; }
    [Export] public int FakeNpcCount { get; set; } = 1000;
    /// <summary>Logs fps and frame stats every two seconds.</summary>
    [Export] public bool Debug { get; set; }

    public ISimHost? Host { get; private set; }
    public TerrainBuilder? Terrain { get; private set; }
    public WaterPlane? Water { get; private set; }
    public NpcRenderer? Npcs { get; private set; }
    public GodCameraRig? CameraRig { get; private set; }
    public Hud? Hud { get; private set; }

    private IWorldSnapshot? _frame;
    private string? _screenshotPath;
    private double _quitAfter = -1, _elapsed, _fpsLogTimer;
    private bool _screenshotTaken;
    private float _startZoom = -1;
    private readonly List<double> _fpsSamples = new();

    public override void _Ready()
    {
        ParseUserArgs();
        Terrain = GetNodeOrNull<TerrainBuilder>("Terrain");
        Water = GetNodeOrNull<WaterPlane>("Water");
        Npcs = GetNodeOrNull<NpcRenderer>("Npcs");
        CameraRig = GetNodeOrNull<GodCameraRig>("GodRig");
        Hud = GetNodeOrNull<Hud>("Hud");

        ISimHost host = UseFakeSim ? new FakeSimHost(FakeNpcCount, Seed) : new SimHost(new SimConfig { Seed = Seed });
        host.Start();
        Host = host;
        var s = host.Latest;
        _frame = s;

        Terrain?.EnsureBuilt(s);
        Water?.Configure(s.World);
        if (CameraRig is not null)
        {
            float sizeX = s.World.Width * s.World.CellSize, sizeZ = s.World.Height * s.World.CellSize;
            CameraRig.SetBounds(new Rect2(0, 0, sizeX, sizeZ));
            CameraRig.Focus(new Vector2(sizeX * 0.5f, sizeZ * 0.5f));
            CameraRig.GroundHeight = (x, z) => _frame is null ? 0f : MathF.Max(_frame.World.SeaLevel, HeightField.Sample(_frame.Heights.Span, _frame.World, x, z));
            CameraRig.PickRequested += OnPickRequested;
            if (_startZoom > 0) CameraRig.SetZoom(_startZoom, immediate: true);
        }
        if (Hud?.TimeControls is { } time) time.ScaleRequested += scale => Host?.Enqueue(new SetTimeScaleCommand(scale));
        if (Hud?.Inspector is { } inspector) inspector.Closed += () => { if (Npcs is not null) Npcs.SelectedId = -1; };

        GD.Print($"Eden boot ok: {(UseFakeSim ? "fake" : "real")} sim, seed {s.World.Seed}, world {s.World.Width}x{s.World.Height}, tick {s.Tick}, population {s.Population}");
    }

    public override void _Process(double delta)
    {
        if (Host is null) return;
        var s = Host.Latest; // read once; the same instance goes to every consumer this frame
        _frame = s;
        Terrain?.EnsureBuilt(s);
        Water?.Configure(s.World);
        if (Npcs is not null)
        {
            var viewer = CameraRig?.Camera is { } cam && cam.IsInsideTree() ? cam.GlobalPosition : Vector3.Zero;
            Npcs.Render(s, viewer, Time.GetTicksUsec() / 1_000_000.0);
        }
        Hud?.UpdateFrom(s, delta);
        Diagnostics(delta);
    }

    /// <summary>Selects the NPC under a screen position (or clears the selection). Returns the id or -1.</summary>
    public int SelectAt(Vector2 screenPosition)
    {
        int id = Npcs is not null && CameraRig?.Camera is { } cam ? Npcs.Pick(cam, screenPosition) : -1;
        Select(id);
        return id;
    }

    public void Select(int npcId)
    {
        if (Npcs is not null) Npcs.SelectedId = npcId;
        if (Hud?.Inspector is { } inspector)
        {
            inspector.ShowNpc(npcId);
            if (npcId >= 0 && _frame is not null) inspector.Refresh(_frame);
        }
    }

    public override void _ExitTree()
    {
        Host?.Dispose();
        Host = null;
    }

    private void OnPickRequested(Vector2 screenPosition) => SelectAt(screenPosition);

    private void ParseUserArgs()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--fake-sim") UseFakeSim = true;
            else if (arg == "--log-fps") Debug = true;
            else if (arg.StartsWith("--npcs=") && int.TryParse(arg["--npcs=".Length..], out int n)) FakeNpcCount = n;
            else if (arg.StartsWith("--seed=") && int.TryParse(arg["--seed=".Length..], out int seed)) Seed = seed;
            else if (arg.StartsWith("--screenshot=")) _screenshotPath = arg["--screenshot=".Length..];
            else if (arg.StartsWith("--zoom=") && float.TryParse(arg["--zoom=".Length..], System.Globalization.CultureInfo.InvariantCulture, out float zoom)) _startZoom = zoom;
            else if (arg.StartsWith("--quit-after=") && double.TryParse(arg["--quit-after=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double q)) _quitAfter = q;
        }
    }

    private void Diagnostics(double delta)
    {
        _elapsed += delta;
        if (Debug)
        {
            _fpsLogTimer += delta;
            if (_fpsLogTimer >= 2.0)
            {
                _fpsLogTimer = 0;
                double fps = Engine.GetFramesPerSecond();
                if (_elapsed > 4.0) _fpsSamples.Add(fps); // skip warm-up
                GD.Print($"[fps] {fps:0.0} fps, process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.00} ms, " +
                         $"draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}, " +
                         $"npcs {Npcs?.RenderedCount} (hi {Npcs?.HiLodCount}, multimesh {Npcs?.LowLodCount}), tick {_frame?.Tick}");
            }
        }
        if (_screenshotPath is not null && !_screenshotTaken && _elapsed > Math.Max(3.0, _quitAfter > 0 ? _quitAfter - 1.0 : 3.0))
        {
            _screenshotTaken = true;
            var image = GetViewport().GetTexture().GetImage();
            var err = image.SavePng(_screenshotPath);
            GD.Print($"[screenshot] {_screenshotPath} ({err})");
        }
        if (_quitAfter > 0 && _elapsed >= _quitAfter)
        {
            _quitAfter = -1;
            if (_fpsSamples.Count > 0)
                GD.Print($"[fps] summary: min {_fpsSamples.Min():0.0}, avg {_fpsSamples.Average():0.0}, max {_fpsSamples.Max():0.0} over {_fpsSamples.Count} samples");
            GetTree().Quit();
        }
    }
}
