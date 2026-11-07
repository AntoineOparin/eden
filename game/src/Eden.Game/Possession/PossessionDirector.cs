using Eden.Sim;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// M6 possession, end to end on the Godot side. Created by <see cref="Main"/> (one hook block) as its last child, so it
/// sees key events before the HUD. Owns the <see cref="PossessRig"/>, the <see cref="PossessedController"/>, the
/// <see cref="TerrainCollision"/> around the body and the <see cref="PossessionHud"/>.
/// <list type="bullet">
/// <item>God view: P (or the HUD button) possesses the selected NPC: <see cref="PossessCommand"/>, time to 1×, god HUD
/// hidden (<see cref="Hud.SetPossessed"/>), the renderer leaves the NPC to the controller, and the camera glides from
/// the god rig into the possession rig. The god rig follows the body, so Main's LOD viewer and the Mind's focus need
/// no change.</item>
/// <item>Possessed: WASD/Shift/mouse via the controller and rig, F first person, E talk (opens the dialogue panel with
/// the possessed NPC as speaker) or work at a bush / building site, G give food, R work alongside, Space pause,
/// Esc closes the dialogue or releases. If the sim never confirms the possession (fake sim) or the NPC dies, it
/// falls back to god view.</item>
/// </list>
/// Dev flags: <c>--save-world=PATH</c> (save right after boot, e.g. after <c>--year=120</c>), <c>--load-world=PATH</c>,
/// <c>--possess=ID</c>, <c>--m6-check</c> (scripted acceptance run, see <see cref="PossessionCheck"/>).
/// </summary>
public partial class PossessionDirector : Node
{
    public enum InteractionKind { None, Npc, Bush, Store, Site }

    /// <summary>What E/G/R act on this frame.</summary>
    public readonly record struct Interaction(InteractionKind Kind, int Id, string Name, Vector3 Position);

    [Export] public float TalkReach { get; set; } = 3.5f;
    [Export] public float BushReach { get; set; } = 3.5f;
    [Export] public float SiteReach { get; set; } = 8f;
    /// <summary>The sim must confirm the possession within this many seconds or it is abandoned.</summary>
    [Export] public double ConfirmSeconds { get; set; } = 3.0;

    private Main? _main;
    private ISimHost? _host;
    private SimConfig _config = new();
    private TimeScale _scaleBefore = TimeScale.X1;
    private double _sinceStart, _sinceSeen, _sinceHud = double.MaxValue;
    private bool _confirmed;
    private string _name = "";
    private int _dialogueWith = -1;
    private int _pendingPossess = -1;
    private string? _saveWorld;

    public PossessRig Rig { get; private set; } = null!;
    public PossessedController Controller { get; private set; } = null!;
    public TerrainCollision Collision { get; private set; } = null!;
    public PossessionHud PossessionHud { get; private set; } = null!;

    public bool IsPossessed => PossessedId >= 0;
    public int PossessedId { get; private set; } = -1;
    public string PossessedName => _name;
    public bool Confirmed => _confirmed;
    public Interaction Current { get; private set; }
    /// <summary>Last interaction verb sent (tests, the check log).</summary>
    public string LastVerb { get; private set; } = "";
    public bool MouseCaptureEnabled { get; set; } = true;

    public event Action<int>? Possessed;
    public event Action<int>? Released;

    /// <summary>Main to drive; defaults to the parent.</summary>
    public Main? Main
    {
        get => _main;
        set => _main = value;
    }

    public override void _Ready()
    {
        _main ??= GetParentOrNull<Main>();
        Collision = new TerrainCollision { Name = "TerrainCollision" };
        AddChild(Collision);
        Rig = GD.Load<PackedScene>("res://scenes/cameras/possess_rig.tscn").Instantiate<PossessRig>();
        AddChild(Rig);
        Controller = new PossessedController { Name = "PossessedBody", Visible = false };
        AddChild(Controller);
        Controller.Rig = Rig;
        PossessionHud = GD.Load<PackedScene>("res://scenes/possession/possession_hud.tscn").Instantiate<PossessionHud>();
        AddChild(PossessionHud);
        PossessionHud.PossessPressed += PossessSelected;
        HookInspectorButton();

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        bool check = false;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--save-world=")) _saveWorld = arg["--save-world=".Length..];
            else if (arg.StartsWith("--load-world=")) _main?.Host?.Enqueue(new LoadCommand(ProjectSettings.GlobalizePath(arg["--load-world=".Length..])));
            else if (arg.StartsWith("--possess=") && int.TryParse(arg["--possess=".Length..], System.Globalization.NumberStyles.Integer, inv, out int id)) _pendingPossess = id;
            else if (arg == "--m6-check") check = true;
        }
        if (_saveWorld is not null && _main?.Host is { } h)
        {
            h.Enqueue(new SaveCommand(ProjectSettings.GlobalizePath(_saveWorld)));
            GD.Print($"[m6] saving the world to {_saveWorld}");
        }
        if (check) AddChild(new PossessionCheck { Name = "PossessionCheck" });
    }

    /// <summary>True when M7's inspector header button (<c>NpcInspector.PossessRequested</c>) drives possession.</summary>
    public bool UsesInspectorButton { get; private set; }

    /// <summary>
    /// After the M7 merge the inspector exposes <c>PossessRequested</c> (Action&lt;int&gt;) and shows a Possess button once
    /// someone subscribes. Bound by reflection so this branch compiles without it; the HUD button is the fallback.
    /// </summary>
    private void HookInspectorButton()
    {
        if (_main?.Hud?.Inspector is not { } inspector) return;
        Action<int> handler = id => Possess(id);
        var type = inspector.GetType();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        if (type.GetEvent("PossessRequested", flags) is { } ev && ev.EventHandlerType == typeof(Action<int>))
        {
            ev.AddEventHandler(inspector, handler);
            UsesInspectorButton = true;
        }
        else if (type.GetField("PossessRequested", flags) is { } field && field.FieldType.IsAssignableFrom(typeof(Action<int>)))
        {
            field.SetValue(inspector, Delegate.Combine(field.GetValue(inspector) as Delegate, handler));
            UsesInspectorButton = true;
        }
        else if (type.GetProperty("PossessRequested", flags) is { CanWrite: true } prop && prop.PropertyType.IsAssignableFrom(typeof(Action<int>)))
        {
            prop.SetValue(inspector, Delegate.Combine(prop.GetValue(inspector) as Delegate, handler));
            UsesInspectorButton = true;
        }
        if (UsesInspectorButton) GD.Print("[m6] possession bound to the inspector's Possess button");
    }

    // ---- possess / release ------------------------------------------------------------------

    /// <summary>The P key and the HUD button: possess the NPC selected in the inspector.</summary>
    public void PossessSelected()
    {
        int id = _main?.Npcs?.SelectedId ?? -1;
        if (id >= 0) Possess(id);
    }

    /// <summary>Starts possessing <paramref name="npcId"/>. False if it is not in the snapshot or cannot be possessed.</summary>
    public bool Possess(int npcId)
    {
        if (_main?.Host is not { } host) return false;
        var snap = host.Latest;
        if (!TryFind(snap, npcId, out var view)) return false;
        if (view.Stage == LifeStage.Infant || view.Pose == NpcPose.Dead)
        {
            PossessionHud.Notify("Infants and the dead cannot be possessed.");
            return false;
        }
        if (IsPossessed) ReleaseLocal(sendCommand: true, reselect: false);

        _host = host;
        _config = host is SimHost real ? RealConfig(real) : new SimConfig();
        PossessedId = npcId;
        _confirmed = view.IsPossessed;
        _sinceStart = _sinceSeen = 0;
        _name = snap.TryGetNpcDetail(npcId, out var d) ? d.Name : $"#{npcId}";
        host.Enqueue(new PossessCommand(npcId));

        var hud = _main.Hud;
        var time = hud?.TimeControls;
        _scaleBefore = time?.Current ?? TimeScale.X1;
        if (time is not null) time.Request(TimeScale.X1);
        else host.Enqueue(new SetTimeScaleCommand(TimeScale.X1));
        _main.Select(-1);
        hud?.SetPossessed(true);
        if (_main.Npcs is { } npcs) npcs.PossessedId = npcId;

        if (_main.Terrain is { } terrain)
        {
            Controller.RenderGround = terrain.SampleRender;
            Collision.UpdateAround(new Vector3(view.X, view.Y, view.Z), terrain);
        }
        Controller.SimGround = (x, z) => HeightField.Sample(host.Latest.Heights.Span, host.Latest.World, x, z);
        Controller.SeaLevel = snap.World.SeaLevel;
        Controller.TimeScaleSource = () => _main?.Hud?.TimeControls?.Current ?? TimeScale.X1;
        Controller.InputEnabled = true;
        Controller.Begin(host, _config, view);

        var god = _main.CameraRig;
        var from = god?.Camera is { } cam && cam.IsInsideTree() ? cam.GlobalTransform : new Transform3D(Basis.Identity, Controller.GlobalPosition + new Vector3(0, 30, 30));
        Rig.SetFirstPerson(false);
        Rig.BodyScale = NpcRenderer.StageScale(view.Stage);
        Rig.Activate(Controller.GlobalPosition, view.Heading + MathF.PI, from);
        if (god is not null) god.InputEnabled = false;
        CaptureMouse(true);
        _main.Mind?.NotePlayerInteraction(npcId);
        GD.Print($"[m6] possess {_name} (#{npcId}) at ({view.X:0.0}, {view.Z:0.0})");
        Possessed?.Invoke(npcId);
        return true;
    }

    /// <summary>Esc: gives the body back (<see cref="ReleasePossessionCommand"/>) and glides back to the god view.</summary>
    public void Release() => ReleaseLocal(sendCommand: true, reselect: true);

    private void ReleaseLocal(bool sendCommand, bool reselect)
    {
        if (!IsPossessed) return;
        int id = PossessedId;
        if (sendCommand) _host?.Enqueue(new ReleasePossessionCommand());
        CloseDialogue();
        var body = Controller.GlobalPosition;
        var camFrom = Rig.Camera.IsInsideTree() ? Rig.Camera.GlobalTransform : Transform3D.Identity;
        Controller.End();
        Rig.Deactivate();
        Collision.Clear();
        PossessedId = -1;
        _confirmed = false;
        Current = default;
        CaptureMouse(false);
        if (_main is not null)
        {
            if (_main.Npcs is { } npcs) npcs.PossessedId = -1;
            _main.Hud?.SetPossessed(false);
            if (_main.Hud?.TimeControls is { } time) time.Request(_scaleBefore);
            else _host?.Enqueue(new SetTimeScaleCommand(_scaleBefore));
            if (_main.CameraRig is { } god)
            {
                god.StopFollow();
                god.InputEnabled = true;
                god.Focus(new Vector2(body.X, body.Z));
                god.SetZoom(40, immediate: true);
                if (god.Camera is { } cam && cam.IsInsideTree())
                {
                    cam.MakeCurrent();
                    god.GlideFrom(camFrom, Rig.GlideSeconds);
                }
            }
            if (reselect) _main.Select(id); // the inspector shows what the body remembers
        }
        GD.Print($"[m6] release #{id}");
        Released?.Invoke(id);
    }

    private void CaptureMouse(bool on)
    {
        if (!MouseCaptureEnabled || DisplayServer.GetName() == "headless") return;
        Input.MouseMode = on ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }

    // ---- interactions -----------------------------------------------------------------------

    /// <summary>E: talk to the NPC in reach (dialogue panel as the possessed speaker), or work at the bush / site.</summary>
    public bool InteractPrimary()
    {
        if (!IsPossessed || _host is null) return false;
        var target = Current;
        switch (target.Kind)
        {
            case InteractionKind.Npc:
                Send(target.Id, "talk");
                OpenDialogue(target.Id, target.Name);
                return true;
            case InteractionKind.Bush:
            case InteractionKind.Store:
            case InteractionKind.Site:
                Send(-1, "work");
                Controller.FollowSim();
                PossessionHud.Notify(target.Kind switch
                {
                    InteractionKind.Bush => "You pick berries.",
                    InteractionKind.Store => "You take food from the store.",
                    _ => $"You lend a hand on the {target.Name}.",
                });
                return true;
            default:
                return false;
        }
    }

    /// <summary>G: give food to the NPC in reach.</summary>
    public bool GiveFood()
    {
        if (!IsPossessed || Current.Kind != InteractionKind.Npc) return false;
        Send(Current.Id, "give:food");
        PossessionHud.Notify($"You hold out food to {Current.Name}.");
        return true;
    }

    /// <summary>R: join the NPC in reach at whatever they are working on.</summary>
    public bool WorkAlongside()
    {
        if (!IsPossessed || Current.Kind != InteractionKind.Npc) return false;
        Send(Current.Id, "work");
        Controller.FollowSim();
        PossessionHud.Notify($"You work alongside {Current.Name}.");
        return true;
    }

    private void Send(int targetId, string verb)
    {
        LastVerb = verb;
        _host!.Enqueue(new PossessedInteractCommand(targetId, verb));
        if (targetId >= 0) _main?.Mind?.NotePlayerInteraction(targetId);
    }

    private void OpenDialogue(int npcId, string name)
    {
        var panel = _main?.Hud?.Dialogue;
        if (panel is null) return;
        _dialogueWith = npcId;
        panel.ShowPossessed(npcId, name, PossessedId, _name);
        Controller.InputEnabled = false;
        CaptureMouse(false);
        panel.FocusInput();
    }

    public void CloseDialogue()
    {
        if (_dialogueWith < 0) return;
        _dialogueWith = -1;
        _main?.Hud?.Dialogue?.ShowNpc(-1, "");
        Controller.InputEnabled = true;
        if (IsPossessed) CaptureMouse(true);
    }

    public bool DialogueOpen => _dialogueWith >= 0;

    [Export] public float StoreReach { get; set; } = 8f;

    /// <summary>
    /// The NPC in talking reach, else a berry bush, else a finished storehouse of the body's own village
    /// (<paramref name="settlementId"/>), else a building site (this frame's prompt).
    /// </summary>
    public Interaction FindInteraction(IWorldSnapshot snap, Vector3 at, int settlementId = -1)
    {
        int best = -1;
        float bestD = TalkReach * TalkReach;
        Vector3 bestPos = default;
        foreach (ref readonly var v in snap.Npcs.Span)
        {
            if (v.Id == PossessedId || v.Pose == NpcPose.Dead) continue;
            float d = (v.X - at.X) * (v.X - at.X) + (v.Z - at.Z) * (v.Z - at.Z);
            if (d < bestD) { bestD = d; best = v.Id; bestPos = new Vector3(v.X, v.Y, v.Z); }
        }
        if (best >= 0) return new Interaction(InteractionKind.Npc, best, snap.TryGetNpcDetail(best, out var d0) ? d0.Name : "someone", bestPos);
        return FindThing(snap, at, settlementId);
    }

    /// <summary>A berry bush, else the own village's storehouse, else a building site in reach (T, or E with nobody near).</summary>
    public Interaction FindThing(IWorldSnapshot snap, Vector3 at, int settlementId = -1)
    {
        int best = -1;
        Vector3 bestPos = default;
        float bestD = BushReach * BushReach;
        foreach (ref readonly var r in snap.Resources.Span)
        {
            if (r.Kind != "berry" || r.Amount < 0.05f) continue;
            float d = (r.X - at.X) * (r.X - at.X) + (r.Z - at.Z) * (r.Z - at.Z);
            if (d < bestD) { bestD = d; best = r.Id; bestPos = new Vector3(r.X, r.Y, r.Z); }
        }
        if (best >= 0) return new Interaction(InteractionKind.Bush, best, "berry bush", bestPos);

        bestD = StoreReach * StoreReach;
        if (settlementId >= 0)
            foreach (ref readonly var b in snap.Buildings.Span)
            {
                if (b.Kind != "storehouse" || b.Progress < 1f || b.SettlementId != settlementId) continue;
                float d = (b.X - at.X) * (b.X - at.X) + (b.Z - at.Z) * (b.Z - at.Z);
                if (d < bestD) { bestD = d; best = b.Id; bestPos = new Vector3(b.X, b.Y, b.Z); }
            }
        if (best >= 0) return new Interaction(InteractionKind.Store, best, "storehouse", bestPos);

        bestD = SiteReach * SiteReach;
        string kind = "";
        foreach (ref readonly var b in snap.Buildings.Span)
        {
            if (b.Progress >= 1f) continue;
            float d = (b.X - at.X) * (b.X - at.X) + (b.Z - at.Z) * (b.Z - at.Z);
            if (d < bestD) { bestD = d; best = b.Id; bestPos = new Vector3(b.X, b.Y, b.Z); kind = b.Kind; }
        }
        return best >= 0 ? new Interaction(InteractionKind.Site, best, kind.Replace('_', ' '), bestPos) : default;
    }

    public static string PromptFor(in Interaction i) => i.Kind switch
    {
        InteractionKind.Npc => $"E  talk to {i.Name}     G  give food     R  work alongside",
        InteractionKind.Bush => "E  pick berries",
        InteractionKind.Store => "E  take food from the store",
        InteractionKind.Site => $"E  help build the {i.Name}",
        _ => "",
    };

    /// <summary>The prompt line: the person (E/G/R) and, when someone stands next to a bush or store, T for the thing.</summary>
    public static string PromptFor(in Interaction person, in Interaction thing)
    {
        if (person.Kind != InteractionKind.Npc) return PromptFor(thing);
        string line = PromptFor(person);
        return thing.Kind == InteractionKind.None ? line : line + "     T  " + PromptFor(thing)[3..];
    }

    /// <summary>What T (or E with nobody in reach) works at this frame.</summary>
    public Interaction CurrentThing { get; private set; }

    /// <summary>T: work at the bush, store or site in reach even with people around.</summary>
    public bool WorkThing()
    {
        if (!IsPossessed || CurrentThing.Kind == InteractionKind.None) return false;
        var saved = Current;
        Current = CurrentThing;
        bool done = InteractPrimary();
        Current = saved;
        return done;
    }

    // ---- frame ------------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (IsPossessed && _main?.Terrain is { } terrain) Collision.UpdateAround(Controller.GlobalPosition, terrain);
    }

    public override void _Process(double delta)
    {
        if (_main?.Host is not { } host) return;
        var snap = host.Latest;
        if (_pendingPossess >= 0 && snap.Population > 0 && _sinceStart > 1.0)
        {
            int id = _pendingPossess;
            _pendingPossess = -1;
            if (!Possess(id)) GD.Print($"[m6] --possess={id}: no such NPC");
        }
        _sinceStart += delta;
        if (!IsPossessed)
        {
            UpdateGodHud(snap, delta);
            return;
        }

        // The sim confirms (IsPossessed in the snapshot) or we give up; a death or a load ends it.
        if (TryFind(snap, PossessedId, out var view) && view.IsPossessed)
        {
            _confirmed = true;
            _sinceSeen = 0;
        }
        else _sinceSeen += delta;
        if ((!_confirmed && _sinceStart > ConfirmSeconds) || (_confirmed && _sinceSeen > 1.0))
        {
            string why = !_confirmed ? "The world did not let you in." : view.Id == PossessedId && view.Pose == NpcPose.Dead ? $"{_name} died." : "The possession ended.";
            GD.Print($"[m6] possession of #{PossessedId} ended: {why}");
            ReleaseLocal(sendCommand: false, reselect: false);
            PossessionHud.Notify(why, 4);
            return;
        }

        DrainSimLog();
        var body = Controller.GlobalPosition;
        Rig.Target = body;
        _main.CameraRig?.Follow(body, Rig.Camera.IsInsideTree() ? Rig.Camera.GlobalTransform : Transform3D.Identity);

        Current = FindInteraction(snap, body, view.SettlementId);
        CurrentThing = Current.Kind == InteractionKind.Npc ? FindThing(snap, body, view.SettlementId) : Current;
        if (_dialogueWith >= 0)
        {
            var panel = _main.Hud?.Dialogue;
            bool gone = !TryFind(snap, _dialogueWith, out var other) || new Vector2(other.X - body.X, other.Z - body.Z).Length() > 8f;
            if (gone || panel is null || !panel.Visible) CloseDialogue();
        }

        _sinceHud += delta;
        if (_sinceHud < 0.2) return;
        _sinceHud = 0;
        if (snap.TryGetNpcDetail(PossessedId, out var d)) _name = d.Name;
        else d = null;
        string doing = Controller.Following && view.Pose == NpcPose.Working ? "Working" :
            Controller.InputVector != Vector2.Zero ? (Controller.Sprinting ? "Running" : "Walking") : "Standing";
        string prompt = _dialogueWith >= 0 ? "Esc  stop talking" : PromptFor(Current, CurrentThing);
        PossessionHud.ShowPossessed(_name, d, doing, prompt, Rig.FirstPerson);
    }

    private void UpdateGodHud(IWorldSnapshot snap, double delta)
    {
        _sinceHud += delta;
        if (_sinceHud < 0.2) return;
        _sinceHud = 0;
        int selected = _main?.Npcs?.SelectedId ?? -1;
        string name = selected >= 0 && snap.TryGetNpcDetail(selected, out var d) ? d.Name : "";
        bool can = selected >= 0 && TryFind(snap, selected, out var v) && v.Stage != LifeStage.Infant && v.Pose != NpcPose.Dead;
        PossessionHud.ShowGodView(can && !UsesInspectorButton ? selected : -1, name);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (GetViewport()?.GuiGetFocusOwner() is LineEdit or TextEdit && key.PhysicalKeycode != Key.Escape) return;
        if (!IsPossessed)
        {
            if (key.PhysicalKeycode != Key.P) return;
            PossessSelected();
            GetViewport()?.SetInputAsHandled();
            return;
        }
        switch (key.PhysicalKeycode)
        {
            case Key.Escape:
                if (DialogueOpen) CloseDialogue();
                else Release();
                break;
            case Key.E: InteractPrimary(); break;
            case Key.G: GiveFood(); break;
            case Key.R: WorkAlongside(); break;
            case Key.T: WorkThing(); break;
            case Key.F: Rig.ToggleFirstPerson(); break;
            case Key.Space:
                if (_main?.Hud?.TimeControls is { } time) time.Request(time.Current == TimeScale.Paused ? TimeScale.X1 : TimeScale.Paused);
                break;
            case Key.Q or Key.L or Key.V or Key.P or Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4 or Key.Key5:
                break; // god keys do nothing while possessed
            default:
                return;
        }
        GetViewport()?.SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // A click back into the world recaptures the mouse (after alt-tab or the dialogue).
        if (!IsPossessed || DialogueOpen || @event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        if (Input.MouseMode != Input.MouseModeEnum.Captured) CaptureMouse(true);
        GetViewport()?.SetInputAsHandled();
    }

    public override void _ExitTree() => CaptureMouse(false);

    /// <summary>Last refusal the sim logged for a possession command (e.g. "give rejected: ... carries no food").</summary>
    public string? LastRefusal { get; private set; }

    /// <summary>While possessed, reads the sim's diagnostics (nobody else consumes them in game) to explain refusals.</summary>
    private void DrainSimLog()
    {
        if (_host is not SimHost real) return;
        while (real.Diagnostics.TryDequeue(out var line))
        {
            if (!line.Contains("rejected", StringComparison.Ordinal) || line.StartsWith("intention", StringComparison.Ordinal)) continue;
            LastRefusal = line;
            GD.Print($"[m6] sim: {line}");
            if (line.StartsWith("give rejected", StringComparison.Ordinal) && line.Contains("no food", StringComparison.Ordinal))
                PossessionHud.Notify($"{_name} carries no food. Pick some berries first (E at a bush).");
            else if (line.StartsWith("take rejected", StringComparison.Ordinal))
                PossessionHud.Notify("The store is empty.");
            else if (line.StartsWith("interact rejected: nothing to work", StringComparison.Ordinal))
                PossessionHud.Notify("There is nothing to work at here.");
        }
    }

    private static bool TryFind(IWorldSnapshot snap, int id, out NpcView view)
    {
        foreach (ref readonly var v in snap.Npcs.Span)
            if (v.Id == id) { view = v; return true; }
        view = default;
        return false;
    }

    /// <summary>The running host's config is private; possession tunables are defaults (Main builds it with only a seed).</summary>
    private static SimConfig RealConfig(SimHost _) => new();
}
