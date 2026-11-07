using Eden.Game.Dev;
using Eden.Mind;
using Eden.Mind.Contracts;
using Eden.Sim;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Root node of scenes/main.tscn. Owns the sim host and the Mind; every frame it reads <see cref="ISimHost.Latest"/>
/// once and hands that same snapshot to the terrain, NPC renderer and UI. The real <see cref="SimHost"/> is the
/// default; <see cref="UseFakeSim"/> (or the user arg <c>--fake-sim</c>) swaps in <see cref="FakeSimHost"/> for M2 work.
/// The <see cref="MindPool"/> is built next to the host; with the fake sim, headless, or <c>--no-brain</c> it runs the
/// "none" provider (rules only).
/// User args (after <c>--</c>): <c>--fake-sim</c>, <c>--npcs=N</c>, <c>--log-fps</c>, <c>--screenshot=PATH</c>,
/// <c>--quit-after=SECONDS</c>, <c>--seed=N</c>, <c>--zoom=M</c>, and for M5 <c>--brain</c> (enable the Mind in headless
/// runs), <c>--no-brain</c>, <c>--log-mind</c>, <c>--m5-check</c> (scripted acceptance run, see <see cref="M5Check"/>).
/// </summary>
public partial class Main : Node
{
    [Export] public int Seed { get; set; } = 1;
    [Export] public bool UseFakeSim { get; set; }
    [Export] public int FakeNpcCount { get; set; } = 1000;
    /// <summary>Logs fps and frame stats every two seconds.</summary>
    [Export] public bool Debug { get; set; }
    /// <summary>Run the Mind with the saved provider. Off: provider "none". Headless runs default to off.</summary>
    [Export] public bool UseBrain { get; set; } = true;
    /// <summary>Radius of a lightning strike from the toolbar, metres.</summary>
    [Export] public float LightningRadius { get; set; } = 3f;

    public ISimHost? Host { get; private set; }
    public TerrainBuilder? Terrain { get; private set; }
    public WaterPlane? Water { get; private set; }
    public NpcRenderer? Npcs { get; private set; }
    public GodCameraRig? CameraRig { get; private set; }
    public Hud? Hud { get; private set; }
    public MindPool? Mind { get; private set; }
    public BrainSettingsStore BrainStore { get; set; } = new();
    public bool LogMind { get; set; }

    private IWorldSnapshot? _frame;
    private string? _screenshotPath;
    private double _quitAfter = -1, _elapsed, _fpsLogTimer;
    private bool _screenshotTaken;
    private float _startZoom = -1;
    private readonly List<double> _fpsSamples = new();
    private bool _brainForced, _m5Check, _brainEnabled;
    private double _sinceMindUi = double.MaxValue, _sinceBudgetSave;
    private volatile bool _budgetDirty;

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
        StartMind(host);
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
        if (Hud?.Inspector is { } inspector)
        {
            inspector.Closed += () => Select(-1);
            inspector.ThoughtSource = id => Mind?.CurrentThought(id);
        }
        Hud?.Dialogue?.Bind(Mind);
        if (Hud?.BrainSettings is { } settingsPanel && Mind is not null)
        {
            settingsPanel.Load(Mind.Settings);
            settingsPanel.SettingsSaved += ApplyBrainSettings;
        }
        if (Hud?.Powers is { } powers) powers.WhisperRequested += Whisper;
        if (_m5Check) AddChild(new M5Check { Name = "M5Check" });

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
        UpdateMind(delta);
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
        if (npcId >= 0) Mind?.NotePlayerInteraction(npcId);
        string name = $"#{npcId}";
        if (npcId >= 0 && _frame is not null && _frame.TryGetNpcDetail(npcId, out var d)) name = d.Name;
        Hud?.Dialogue?.ShowNpc(npcId, name);
        if (Hud?.Inspector is { } inspector)
        {
            inspector.ShowNpc(npcId);
            if (npcId >= 0 && _frame is not null) inspector.Refresh(_frame);
        }
    }

    public override void _ExitTree()
    {
        if (Mind is not null)
        {
            Mind.Stop();
            if (_brainEnabled) BrainStore.SaveBudget(Mind.Budget);
            Mind = null;
        }
        Host?.Dispose();
        Host = null;
    }

    private void OnPickRequested(Vector2 screenPosition)
    {
        if (Hud?.Powers?.Current == GodPower.Lightning)
        {
            if (GroundAt(screenPosition) is { } ground) StrikeAt(ground.X, ground.Z);
            return;
        }
        SelectAt(screenPosition);
    }

    // ---- M5: mind, god powers -------------------------------------------------------------------

    private void StartMind(ISimHost host)
    {
        bool headless = DisplayServer.GetName() == "headless";
        _brainEnabled = !UseFakeSim && UseBrain && (!headless || _brainForced);
        var settings = _brainEnabled ? BrainStore.Load() : BrainDefaults.Settings;
        var mind = new MindPool(host, _brainEnabled ? settings : settings with { ProviderId = "none" });
        if (_brainEnabled) BrainStore.RestoreBudget(mind.Budget);
        mind.Budget.Changed += () => _budgetDirty = true;
        mind.Start();
        Mind = mind;
        GD.Print($"Eden mind: provider {mind.Provider.Id}{(_brainEnabled ? $" at {settings.BaseUrl}" : "")}, " +
                 $"{mind.Budget.NeuronsToday:0.0} Neurons used today");
    }

    private void ApplyBrainSettings(BrainSettings settings)
    {
        BrainStore.Save(settings);
        if (Mind is null) return;
        Mind.ApplySettings(_brainEnabled ? settings : settings with { ProviderId = "none" });
        GD.Print($"[mind] settings applied: provider {Mind.Provider.Id}");
    }

    private void UpdateMind(double delta)
    {
        if (Mind is null) return;
        if (CameraRig is not null) Mind.SetFocus(CameraRig.Position.X, CameraRig.Position.Z);
        _sinceMindUi += delta;
        if (_sinceMindUi >= 0.25)
        {
            _sinceMindUi = 0;
            Hud?.UpdateMind(Mind.Status);
        }
        while (Mind.TryDequeueLog(out var line))
            if (Debug || LogMind) GD.Print($"[mind] {line}");
        _sinceBudgetSave += delta;
        if (_brainEnabled && _budgetDirty && _sinceBudgetSave > 5)
        {
            _sinceBudgetSave = 0;
            _budgetDirty = false;
            BrainStore.SaveBudget(Mind.Budget);
        }
    }

    /// <summary>Lightning from the toolbar: a <see cref="LightningCommand"/> plus a local flash.</summary>
    public void StrikeAt(float x, float z)
    {
        Host?.Enqueue(new LightningCommand(x, z, LightningRadius));
        if (!IsInsideTree()) return;
        float y = _frame is null ? 0 : MathF.Max(_frame.World.SeaLevel, HeightField.Sample(_frame.Heights.Span, _frame.World, x, z));
        var flash = new OmniLight3D { LightColor = new Color(0.8f, 0.85f, 1f), LightEnergy = 16f, OmniRange = 40f, Position = new Vector3(x, y + 12f, z) };
        AddChild(flash);
        var tween = CreateTween();
        tween.TweenProperty(flash, "light_energy", 0f, 0.6);
        tween.TweenCallback(Callable.From(flash.QueueFree));
    }

    /// <summary>Whisper from the toolbar to the selected NPC.</summary>
    public void Whisper(string text)
    {
        int id = Npcs?.SelectedId ?? -1;
        if (id < 0) return;
        Host?.Enqueue(new WhisperCommand(id, text));
        Mind?.NotePlayerInteraction(id);
    }

    /// <summary>The terrain point under a screen position (ray marched against the snapshot heightmap), or null.</summary>
    public Vector3? GroundAt(Vector2 screenPosition)
    {
        if (CameraRig?.Camera is not { } cam || _frame is null) return null;
        var from = cam.ProjectRayOrigin(screenPosition);
        var dir = cam.ProjectRayNormal(screenPosition);
        var w = _frame.World;
        float step = 1f;
        for (float t = 0; t < 3000f; t += step)
        {
            var p = from + dir * t;
            float ground = MathF.Max(w.SeaLevel, HeightField.Sample(_frame.Heights.Span, w, p.X, p.Z));
            if (p.Y <= ground) return new Vector3(p.X, ground, p.Z);
            step = MathF.Max(0.5f, MathF.Min(8f, (p.Y - ground) * 0.5f));
        }
        return null;
    }

    private void ParseUserArgs()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--fake-sim") UseFakeSim = true;
            else if (arg == "--log-fps") Debug = true;
            else if (arg == "--brain") _brainForced = true;
            else if (arg == "--no-brain") UseBrain = false;
            else if (arg == "--log-mind") LogMind = true;
            else if (arg == "--m5-check") { _m5Check = true; LogMind = true; }
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
