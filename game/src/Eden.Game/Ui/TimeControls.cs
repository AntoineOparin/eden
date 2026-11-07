using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Pause / 1x / 10x / 100x / 500x toggle buttons. Raises <see cref="ScaleRequested"/>; Main turns that into a
/// <see cref="SetTimeScaleCommand"/>. Space toggles pause, keys 1-5 pick a speed.
/// </summary>
public partial class TimeControls : HBoxContainer
{
    public static readonly TimeScale[] Scales = [TimeScale.Paused, TimeScale.X1, TimeScale.X10, TimeScale.X100, TimeScale.X500];
    private static readonly string[] Labels = ["", "1×", "10×", "100×", "500×"];
    private static readonly string[] IconNames = ["pause", "forward", "fast-forward", "fast-forward", "fast-forward"];

    private readonly Dictionary<TimeScale, Button> _buttons = new();
    private TimeScale _lastRunning = TimeScale.X1;

    public event Action<TimeScale>? ScaleRequested;
    public TimeScale Current { get; private set; } = TimeScale.X1;

    public override void _Ready()
    {
        var group = new ButtonGroup();
        for (int i = 0; i < Scales.Length; i++)
        {
            var scale = Scales[i];
            var button = new Button
            {
                Name = $"Speed{(int)scale}",
                Text = Labels[i],
                Icon = UiTheme.Icon(IconNames[i]),
                ExpandIcon = false,
                ToggleMode = true,
                ButtonGroup = group,
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(i == 0 ? 36 : 56, 32),
                TooltipText = i == 0 ? "Pause the world (Space)" : $"Run time at {Labels[i]} speed (key {i + 1}); Space pauses",
            };
            button.Pressed += () => Request(scale);
            AddChild(button);
            _buttons[scale] = button;
        }
        Show(Current);
    }

    /// <summary>Selects a speed as if the player clicked it.</summary>
    public void Request(TimeScale scale)
    {
        if (scale != TimeScale.Paused) _lastRunning = scale;
        Current = scale;
        Show(scale);
        ScaleRequested?.Invoke(scale);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.PhysicalKeycode)
        {
            case Key.Space: Request(Current == TimeScale.Paused ? _lastRunning : TimeScale.Paused); break;
            case Key.Key1: Request(TimeScale.Paused); break;
            case Key.Key2: Request(TimeScale.X1); break;
            case Key.Key3: Request(TimeScale.X10); break;
            case Key.Key4: Request(TimeScale.X100); break;
            case Key.Key5: Request(TimeScale.X500); break;
            default: return;
        }
        GetViewport()?.SetInputAsHandled();
    }

    private void Show(TimeScale scale)
    {
        foreach (var (s, b) in _buttons) b.SetPressedNoSignal(s == scale);
    }
}
