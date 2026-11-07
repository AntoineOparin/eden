using Godot;

namespace Eden.Game;

/// <summary>Toolbar powers. M5 shipped the first three; M7 UI appended the rest (append only, values are stable).</summary>
public enum GodPower
{
    Select, Lightning, Whisper,
    SpawnHuman, SpawnAnimal, SpawnPlant, Rain, Drought, RaiseTerrain, LowerTerrain, Bless, Curse, DropFood,
}

/// <summary>
/// God-powers toolbar. M5: Select, Lightning (click the ground to strike) and Whisper (one line to the selected NPC,
/// sent as a WhisperCommand). M7 UI: spawn human / animal / plant, rain, drought, raise / lower terrain (with a brush
/// radius slider), bless, curse and drop food, as icon buttons with tooltips; keys H N T R Y U J B K G pick them
/// (1–5 are the time controls; E, F, P and Escape are left for possession). Main turns
/// the events into commands (see <see cref="PowerTargets"/>); this node holds no game logic.
/// </summary>
public partial class GodPowers : HBoxContainer
{
    private readonly Dictionary<GodPower, Button> _buttons = new();
    private LineEdit? _whisper;
    private HBoxContainer? _brush;
    private HSlider? _brushSlider;
    private Label? _brushLabel;

    public GodPower Current { get; private set; } = GodPower.Select;

    /// <summary>Terraform brush radius in metres (M7 UI).</summary>
    public float BrushRadius { get; private set; } = 12f;

    public event Action<GodPower>? PowerChanged;
    public event Action<string>? WhisperRequested;

    /// <summary>M7 powers: button name, icon, key, tooltip with the effect.</summary>
    public static readonly (GodPower Power, string Icon, Key Key, string Tip)[] M7Powers =
    [
        (GodPower.SpawnHuman, "person", Key.H, "Spawn human (H): click the land to set down a stranger, a young adult with no memories."),
        (GodPower.SpawnAnimal, "paw", Key.N, "Spawn animals (N): click the land to place a herd of deer that hunters can follow."),
        (GodPower.SpawnPlant, "sprout", Key.T, "Spawn plants (T): click the land to grow a few berry bushes."),
        (GodPower.Rain, "rain", Key.R, "Rain (R): click anywhere. Rain falls on the whole island for three days; crops and moods recover."),
        (GodPower.Drought, "drought", Key.Y, "Drought (Y): click anywhere. No rain for three days; crops wither. People who see it fear the sky."),
        (GodPower.RaiseTerrain, "raise", Key.U, "Raise land (U): click to lift the ground inside the brush. Hills, dams, new islands."),
        (GodPower.LowerTerrain, "lower", Key.J, "Lower land (J): click to sink the ground inside the brush. Valleys, lakes, channels."),
        (GodPower.Bless, "star", Key.B, "Bless (B): click a person. Their village grows fond of the god; neighbours may grow envious."),
        (GodPower.Curse, "skull", Key.K, "Curse (K): click a person. They lose heart, their village fears and resents the god."),
        (GodPower.DropFood, "basket", Key.G, "Drop food (G): click the land to let baskets of berries fall from the sky."),
    ];

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);
        var group = new ButtonGroup();
        foreach (var (power, label, tip) in new[]
                 {
                     (GodPower.Select, "Select", "Click an NPC to inspect and talk (Q)"),
                     (GodPower.Lightning, "Lightning", "Click the ground to strike (L)"),
                     (GodPower.Whisper, "Whisper", "Whisper one line to the selected NPC (V)"),
                 })
        {
            var b = new Button { Name = power.ToString(), Text = label, ToggleMode = true, ButtonGroup = group, FocusMode = FocusModeEnum.None, TooltipText = tip };
            b.Pressed += () => Choose(power);
            AddChild(b);
            _buttons[power] = b;
        }
        AddChild(new VSeparator());
        foreach (var (power, icon, _, tip) in M7Powers)
        {
            var b = new Button
            {
                Name = power.ToString(), ToggleMode = true, ButtonGroup = group, FocusMode = FocusModeEnum.None, TooltipText = tip,
                Icon = UiTheme.Icon(icon), IconAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(34, 32),
                ExpandIcon = false,
            };
            b.Pressed += () => Choose(power);
            AddChild(b);
            _buttons[power] = b;
        }
        _brush = new HBoxContainer { Name = "Brush", Visible = false, TooltipText = "Brush radius for raising and lowering land" };
        _brushLabel = new Label { Text = $"{BrushRadius:0} m", CustomMinimumSize = new Vector2(44, 0), VerticalAlignment = VerticalAlignment.Center };
        _brushSlider = new HSlider
        {
            Name = "BrushRadius", MinValue = 4, MaxValue = 40, Step = 1, Value = BrushRadius,
            CustomMinimumSize = new Vector2(110, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None,
        };
        _brushSlider.ValueChanged += v => SetBrushRadius((float)v);
        _brush.AddChild(_brushSlider);
        _brush.AddChild(_brushLabel);
        AddChild(_brush);
        _whisper = new LineEdit { Name = "WhisperText", PlaceholderText = "Whisper to the selected NPC, Enter to send", CustomMinimumSize = new Vector2(280, 0), Visible = false };
        _whisper.TextSubmitted += SubmitWhisper;
        AddChild(_whisper);
        Choose(GodPower.Select);
    }

    public void Choose(GodPower power)
    {
        Current = power;
        if (_buttons.TryGetValue(power, out var b)) b.SetPressedNoSignal(true);
        if (_whisper is not null)
        {
            _whisper.Visible = power == GodPower.Whisper;
            if (_whisper.Visible && _whisper.IsInsideTree()) _whisper.GrabFocus();
        }
        if (_brush is not null) _brush.Visible = power is GodPower.RaiseTerrain or GodPower.LowerTerrain;
        PowerChanged?.Invoke(power);
    }

    public void SetBrushRadius(float metres)
    {
        BrushRadius = Math.Clamp(metres, 4f, 40f);
        if (_brushLabel is not null) _brushLabel.Text = $"{BrushRadius:0} m";
        if (_brushSlider is not null && Math.Abs(_brushSlider.Value - BrushRadius) > 0.01) _brushSlider.SetValueNoSignal(BrushRadius);
    }

    public void SubmitWhisper(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        WhisperRequested?.Invoke(text);
        if (_whisper is not null) _whisper.Text = "";
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.PhysicalKeycode)
        {
            case Key.Q: Choose(GodPower.Select); break;
            case Key.L: Choose(GodPower.Lightning); break;
            case Key.V: Choose(GodPower.Whisper); break;
            default:
                foreach (var p in M7Powers)
                    if (p.Key == key.PhysicalKeycode) { Choose(p.Power); GetViewport()?.SetInputAsHandled(); return; }
                return;
        }
        GetViewport()?.SetInputAsHandled();
    }
}

/// <summary>What a toolbar power needs from a click (M7 UI). Pure; Main/GodUi map it to commands.</summary>
public static class PowerTargets
{
    /// <summary>Powers that act on the ground point under the cursor.</summary>
    public static bool IsGround(GodPower p) => p is GodPower.Lightning or GodPower.SpawnHuman or GodPower.SpawnAnimal
        or GodPower.SpawnPlant or GodPower.Rain or GodPower.Drought or GodPower.RaiseTerrain or GodPower.LowerTerrain or GodPower.DropFood;

    /// <summary>Powers that act on a person (the one under the cursor, or the nearest within a few metres).</summary>
    public static bool IsPerson(GodPower p) => p is GodPower.Bless or GodPower.Curse;
}
