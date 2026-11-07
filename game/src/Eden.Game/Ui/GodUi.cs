using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// M7 UI container (the <c>GodUi</c> node of scenes/ui/hud.tscn): the myth bar (Chronicle, Settlements, overlay mode
/// with its legend and, in knowledge mode, the concept picker), the <see cref="SettlementPanel"/> and the
/// <see cref="ChroniclePanel"/>. Everything M7 added to the HUD lives under this one node, so hiding it hides all of
/// it (M6: <c>Hud.SetPossessed</c> can set <c>Visible = false</c>). It reads the frame's snapshot through
/// <see cref="Frame"/> (Main's per-frame snapshot), feeds the shared <see cref="ChronicleLog"/> every frame (cheap,
/// even while hidden, so nothing is missed) and lets the panels refresh on their own timers.
/// Keys: C Chronicle, O settlements, M cycles the overlay.
/// </summary>
public partial class GodUi : Control
{
    public ChronicleLog Log { get; } = new();
    public SettlementKnowledge Knowledge { get; } = new();

    public ChroniclePanel? Chronicle { get; private set; }
    public SettlementPanel? Settlement { get; private set; }

    /// <summary>The 3D overlay (owned by Main, which sets it).</summary>
    public TerritoryOverlay? Overlay { get; set; }

    /// <summary>The snapshot Main read this frame.</summary>
    public Func<IWorldSnapshot?>? Frame { get; set; }

    /// <summary>Move the god camera to a world point (Main → GodCameraRig.Focus).</summary>
    public event Action<Vector3>? FocusRequested;

    /// <summary>Select an NPC (Main.Select): the settlement panel's leader link.</summary>
    public event Action<int>? NpcRequested;

    private OptionButton? _mode;
    private OptionButton? _concept;
    private HBoxContainer? _legend;
    private Button? _chronicleButton, _settlementButton;
    private double _sinceKnowledge, _sinceConcepts = double.MaxValue;
    private int _conceptsVersion = -1;
    private readonly List<string> _conceptIds = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Chronicle = GetNodeOrNull<ChroniclePanel>("Chronicle");
        Settlement = GetNodeOrNull<SettlementPanel>("Settlement");
        if (Chronicle is not null)
        {
            Chronicle.Log = Log;
            Chronicle.Visible = false;
            Chronicle.EntryActivated += OnEntry;
            Chronicle.Closed += () => _chronicleButton?.SetPressedNoSignal(false);
        }
        if (Settlement is not null)
        {
            Settlement.Log = Log;
            Settlement.Knowledge = Knowledge;
            Settlement.Closed += () => _settlementButton?.SetPressedNoSignal(false);
            Settlement.LeaderRequested += id => NpcRequested?.Invoke(id);
            Settlement.FocusRequested += p => FocusRequested?.Invoke(p);
        }
        if (GetNodeOrNull<HBoxContainer>("Bar/Row") is { } row) BuildBar(row);
    }

    private void BuildBar(HBoxContainer row)
    {
        _chronicleButton = new Button
        {
            Name = "ChronicleButton", Text = "Chronicle", Icon = UiTheme.Icon("scroll"), ToggleMode = true, FocusMode = FocusModeEnum.None,
            TooltipText = "Chronicle (C): the island's history, with filters by kind and importance. Click an entry to look at it.",
        };
        _chronicleButton.Toggled += on => { if (Chronicle is not null) Chronicle.Visible = on; };
        row.AddChild(_chronicleButton);

        _settlementButton = new Button
        {
            Name = "SettlementsButton", Text = "Settlements", Icon = UiTheme.Icon("house"), ToggleMode = true, FocusMode = FocusModeEnum.None,
            TooltipText = "Settlements (O): leader, faith, the god's standing and what they know. With Select, click a building to open its settlement.",
        };
        _settlementButton.Toggled += on =>
        {
            if (Settlement is null) return;
            if (!on) { Settlement.ShowSettlement(-1); return; }
            int first = -1;
            if (Frame?.Invoke() is { } s && s.Settlements.Length > 0) first = s.Settlements.Span[0].Id;
            Settlement.ShowSettlement(Settlement.SettlementId >= 0 ? Settlement.SettlementId : first);
            Settlement.Visible = true;
        };
        row.AddChild(_settlementButton);

        row.AddChild(new TextureRect { Texture = UiTheme.Icon("map"), CustomMinimumSize = new Vector2(20, 20), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = SizeFlags.ShrinkCenter });
        _mode = new OptionButton { Name = "OverlayMode", FocusMode = FocusModeEnum.None, TooltipText = "Overlay (M): tint each settlement's land by the god's standing, by faith, or by who knows a concept" };
        foreach (var label in new[] { "Overlay off", "God's standing", "Faith", "Knowledge" }) _mode.AddItem(label);
        _mode.ItemSelected += i => SetOverlay((OverlayMode)(int)i);
        row.AddChild(_mode);
        _concept = new OptionButton { Name = "Concept", FocusMode = FocusModeEnum.None, Visible = false, TooltipText = "Concept to trace across settlements", CustomMinimumSize = new Vector2(140, 0) };
        _concept.ItemSelected += i => { if (i >= 0 && i < _conceptIds.Count) SetOverlay(OverlayMode.Knowledge, _conceptIds[(int)i]); };
        row.AddChild(_concept);
        _legend = new HBoxContainer { Name = "Legend" };
        _legend.AddThemeConstantOverride("separation", 6);
        row.AddChild(_legend);
    }

    /// <summary>Opens the settlement panel on a settlement (click on a building, inspector link).</summary>
    public void OpenSettlement(int id)
    {
        if (Settlement is null) return;
        Settlement.ShowSettlement(id);
        _settlementButton?.SetPressedNoSignal(id >= 0);
        if (Frame?.Invoke() is { } s) Settlement.Refresh(s);
    }

    public void ToggleChronicle(bool on)
    {
        if (Chronicle is not null) Chronicle.Visible = on;
        _chronicleButton?.SetPressedNoSignal(on);
        if (on) Chronicle?.Rebuild();
    }

    public void SetOverlay(OverlayMode mode, string? concept = null)
    {
        if (mode == OverlayMode.Knowledge && concept is null)
            concept = _concept is not null && _concept.Selected >= 0 && _concept.Selected < _conceptIds.Count ? _conceptIds[_concept.Selected] : null;
        Overlay?.SetMode(mode, concept);
        if (_mode is not null && _mode.Selected != (int)mode) _mode.Select((int)mode);
        if (_concept is not null) _concept.Visible = mode == OverlayMode.Knowledge;
        if (mode == OverlayMode.Knowledge && Frame?.Invoke() is { } s)
            foreach (var v in s.Settlements.Span) Knowledge.Want(v.Id);
        _sinceConcepts = double.MaxValue;
        RenderLegend(mode, concept);
    }

    public override void _Process(double delta)
    {
        if (Frame?.Invoke() is not { } s) return;
        Log.Ingest(s);
        _sinceKnowledge += delta;
        if (_sinceKnowledge >= 0.25)
        {
            Knowledge.Update(s, _sinceKnowledge);
            _sinceKnowledge = 0;
        }
        Overlay?.UpdateFrom(s, delta);
        if (!IsVisibleInTree()) return;
        Chronicle?.Tick(delta);
        Settlement?.Tick(s, delta);
        if (Overlay?.Mode == OverlayMode.Knowledge)
        {
            _sinceConcepts += delta;
            if (_sinceConcepts >= 1.0) { _sinceConcepts = 0; RefreshConcepts(); }
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || !IsVisibleInTree()) return;
        switch (key.PhysicalKeycode)
        {
            case Key.C: ToggleChronicle(Chronicle is { Visible: false }); break;
            case Key.O: if (_settlementButton is not null) _settlementButton.ButtonPressed = !_settlementButton.ButtonPressed; break;
            case Key.M: SetOverlay((OverlayMode)(((int)(Overlay?.Mode ?? OverlayMode.Off) + 1) % 4)); break;
            default: return;
        }
        GetViewport()?.SetInputAsHandled();
    }

    private void OnEntry(ChronicleEntry e)
    {
        if (Frame?.Invoke() is { } s && ResolveFocus(e, s) is { } p) FocusRequested?.Invoke(p);
    }

    /// <summary>
    /// Where a Chronicle entry happened: its first living subject, else the first settlement named in its text,
    /// else null (the entry has no place: a birth whose people died since, the genesis...).
    /// </summary>
    public static Vector3? ResolveFocus(ChronicleEntry e, IWorldSnapshot s)
    {
        var npcs = s.Npcs.Span;
        foreach (int id in e.Subjects)
        {
            if (id < 0) continue;
            foreach (var n in npcs)
                if (n.Id == id) return new Vector3(n.X, n.Y, n.Z);
        }
        foreach (var v in s.Settlements.Span)
            if (v.Name.Length > 0 && e.Text.Contains(v.Name, StringComparison.Ordinal)) return new Vector3(v.X, 0, v.Z);
        return null;
    }

    private void RefreshConcepts()
    {
        if (_concept is null || Knowledge.Version == _conceptsVersion) return;
        _conceptsVersion = Knowledge.Version;
        var all = new SortedSet<string>(StringComparer.Ordinal);
        if (Frame?.Invoke() is { } s)
            foreach (var v in s.Settlements.Span)
                if (Knowledge.Get(v.Id) is { } e)
                    foreach (var c in e.Concepts.Keys) all.Add(c);
        string? current = _concept.Selected >= 0 && _concept.Selected < _conceptIds.Count ? _conceptIds[_concept.Selected] : Overlay?.Concept;
        if (all.Count == _conceptIds.Count && all.SetEquals(_conceptIds)) return;
        _concept.Clear();
        _conceptIds.Clear();
        foreach (var c in all) { _concept.AddItem(c); _conceptIds.Add(c); }
        int at = current is null ? -1 : _conceptIds.IndexOf(current);
        if (at < 0 && _conceptIds.Count > 0)
        {
            at = 0;
            Overlay?.SetMode(OverlayMode.Knowledge, _conceptIds[0]);
            RenderLegend(OverlayMode.Knowledge, _conceptIds[0]);
        }
        _concept.Select(at);
    }

    private void RenderLegend(OverlayMode mode, string? concept)
    {
        if (_legend is null) return;
        foreach (var c in _legend.GetChildren()) { _legend.RemoveChild(c); c.QueueFree(); }
        switch (mode)
        {
            case OverlayMode.Stance:
                AddKey(OverlayColors.Favourable, "favourable");
                AddKey(OverlayColors.Fearful, "fearful");
                AddKey(OverlayColors.Hostile, "hostile");
                AddKey(OverlayColors.Neutral, "neutral");
                break;
            case OverlayMode.Religion:
                AddKey(OverlayColors.NoFaith, "no faith");
                _legend.AddChild(new Label { Text = "one colour per faith, named on the map", Modulate = new Color(1, 1, 1, 0.75f) });
                break;
            case OverlayMode.Knowledge:
                AddKey(OverlayColors.KnowsAll, "most know");
                AddKey(OverlayColors.Knowledge(0.5f), "some");
                AddKey(OverlayColors.KnowsNone, "nobody");
                AddKey(OverlayColors.KnowledgeUnknown, "not loaded yet");
                break;
        }

        void AddKey(Color c, string text)
        {
            var swatch = new ColorRect { Color = c with { A = 1f }, CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
            _legend.AddChild(swatch);
            _legend.AddChild(new Label { Text = text });
        }
    }

    /// <summary>Legend labels currently shown (tests).</summary>
    public IEnumerable<string> LegendLabels => _legend?.GetChildren().OfType<Label>().Select(l => l.Text) ?? [];
}
