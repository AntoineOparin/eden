using Godot;

namespace Eden.Game;

public enum GodPower { Select, Lightning, Whisper }

/// <summary>
/// Minimal god-powers toolbar (M5): Select, Lightning (click the ground to strike) and Whisper (one line to the
/// selected NPC, sent as a WhisperCommand). Main turns the events into commands; this node holds no game logic.
/// </summary>
public partial class GodPowers : HBoxContainer
{
    private readonly Dictionary<GodPower, Button> _buttons = new();
    private LineEdit? _whisper;

    public GodPower Current { get; private set; } = GodPower.Select;

    public event Action<GodPower>? PowerChanged;
    public event Action<string>? WhisperRequested;

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
        PowerChanged?.Invoke(power);
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
            default: return;
        }
        GetViewport()?.SetInputAsHandled();
    }
}
