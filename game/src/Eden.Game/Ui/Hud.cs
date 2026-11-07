using Eden.Mind;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Root of scenes/ui/hud.tscn: time controls, the date/population/fps readout, the brain budget meter, the god
/// powers toolbar, the NPC inspector, the dialogue panel and the brain settings panel.
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label? _status;
    private double _sinceStatus = double.MaxValue;

    public TimeControls? TimeControls { get; private set; }
    public NpcInspector? Inspector { get; private set; }
    public BudgetMeter? BudgetMeter { get; private set; }
    public GodPowers? Powers { get; private set; }
    public DialoguePanel? Dialogue { get; private set; }
    public BrainSettingsPanel? BrainSettings { get; private set; }

    public override void _Ready()
    {
        TimeControls = GetNodeOrNull<TimeControls>("TopBar/Row/TimeControls");
        _status = GetNodeOrNull<Label>("TopBar/Row/Status");
        BudgetMeter = GetNodeOrNull<BudgetMeter>("MindBar/Row/BudgetMeter");
        Powers = GetNodeOrNull<GodPowers>("PowersBar/GodPowers");
        Inspector = GetNodeOrNull<NpcInspector>("Inspector");
        Dialogue = GetNodeOrNull<DialoguePanel>("Dialogue");
        BrainSettings = GetNodeOrNull<BrainSettingsPanel>("BrainSettings");
        if (GetNodeOrNull<Button>("MindBar/Row/BrainButton") is { } brain && BrainSettings is { } panel)
            brain.Pressed += () => panel.Visible = !panel.Visible;
    }

    public void UpdateFrom(IWorldSnapshot snapshot, double delta)
    {
        Inspector?.UpdateFrom(snapshot, delta);
        _sinceStatus += delta;
        if (_status is null || _sinceStatus < 0.2) return;
        _sinceStatus = 0;
        int hour = (int)snapshot.HourOfDay;
        int minute = (int)((snapshot.HourOfDay - hour) * 60);
        _status.Text = $"Year {snapshot.Year + 1}, day {snapshot.DayOfYear + 1}, {hour:00}:{minute:00}    " +
                       $"pop {snapshot.Population}    {Engine.GetFramesPerSecond():0} fps";
    }

    public void UpdateMind(MindStatus status) => BudgetMeter?.Render(status);
}
