using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>Root of scenes/ui/hud.tscn: time controls, the date/population/fps readout and the NPC inspector.</summary>
public partial class Hud : CanvasLayer
{
    private Label? _status;
    private double _sinceStatus = double.MaxValue;

    public TimeControls? TimeControls { get; private set; }
    public NpcInspector? Inspector { get; private set; }

    public override void _Ready()
    {
        TimeControls = GetNodeOrNull<TimeControls>("TopBar/Row/TimeControls");
        _status = GetNodeOrNull<Label>("TopBar/Row/Status");
        Inspector = GetNodeOrNull<NpcInspector>("Inspector");
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
}
