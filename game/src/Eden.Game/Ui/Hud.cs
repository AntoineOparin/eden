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
    private TextureRect? _dayGlyph;
    private bool _night;
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
        _dayGlyph = GetNodeOrNull<TextureRect>("TopBar/Row/DayGlyph");
        ApplyTheme();
    }

    /// <summary>A1: one theme for every HUD control, toolbar icons and tooltips that say what each power does.</summary>
    private void ApplyTheme()
    {
        var theme = UiTheme.Get();
        foreach (var child in GetChildren())
            if (child is Control c) c.Theme = theme;
        if (_dayGlyph is not null) _dayGlyph.Texture = UiTheme.Icon("sun");
        if (GetNodeOrNull<Button>("MindBar/Row/BrainButton") is { } brain)
        {
            brain.Icon = UiTheme.Icon("gear");
            brain.TooltipText = "NPC brain settings: provider, model, API key, daily budget";
        }
        if (Powers is null) return;
        foreach (var (name, icon, tip) in new[]
        {
            ("Select", "pointer", "Select (Q): click a person to open the inspector and talk to them."),
            ("Lightning", "bolt", "Lightning (L): click the ground to strike. Burns trees and bushes, kills anyone at the centre, " +
                                  "wounds and frightens people nearby. They will remember it."),
            ("Whisper", "speech", "Whisper (V): send one line to the selected person. They hear it as a voice from the sky."),
        })
        {
            if (Powers.GetNodeOrNull<Button>(name) is not { } b) continue;
            b.Icon = UiTheme.Icon(icon);
            b.TooltipText = tip;
            b.CustomMinimumSize = new Vector2(0, 32);
        }
    }

    public void UpdateFrom(IWorldSnapshot snapshot, double delta)
    {
        Inspector?.UpdateFrom(snapshot, delta);
        _sinceStatus += delta;
        if (_status is null || _sinceStatus < 0.2) return;
        _sinceStatus = 0;
        int hour = (int)snapshot.HourOfDay;
        bool night = snapshot.HourOfDay < 6f || snapshot.HourOfDay >= 18f;
        if (_dayGlyph is not null && night != _night)
        {
            _night = night;
            _dayGlyph.Texture = UiTheme.Icon(night ? "moon" : "sun");
            _dayGlyph.TooltipText = night ? "Night" : "Day";
        }
        int minute = (int)((snapshot.HourOfDay - hour) * 60);
        _status.Text = $"Year {snapshot.Year + 1}, day {snapshot.DayOfYear + 1}, {hour:00}:{minute:00}    " +
                       $"pop {snapshot.Population}    {Engine.GetFramesPerSecond():0} fps";
    }

    public void UpdateMind(MindStatus status) => BudgetMeter?.Render(status);

    // ---- M7 UI pass: everything it added lives under the one "GodUi" node (myth bar, settlement panel, Chronicle).
    // Hiding that node hides all of it (for possession). Built and driven by GodUi itself; Main wires it in Main.M7.cs.
    public GodUi? God => GetNodeOrNull<GodUi>("GodUi");

    // ---- M6 possession (PossessionDirector) ------------------------------------------------------

    private readonly List<CanvasItem> _hiddenForPossession = new();

    /// <summary>True while the player possesses an NPC (god panels hidden).</summary>
    public bool IsPossessed { get; private set; }

    /// <summary>Raised by <see cref="SetPossessed"/>; panels built elsewhere can hide or show themselves on it.</summary>
    public event Action<bool>? PossessedChanged;

    /// <summary>
    /// M6: hides every god panel (top bar, powers, mind bar, inspector, brain settings, and any panel added to the HUD
    /// later) while possessed, and restores exactly the ones it hid. The dialogue panel stays: the possessed NPC talks
    /// through it.
    /// </summary>
    public void SetPossessed(bool possessed)
    {
        if (possessed == IsPossessed) return;
        IsPossessed = possessed;
        if (possessed)
        {
            _hiddenForPossession.Clear();
            // M7's GodUi (myth bar, settlement panel, Chronicle) hides as one node.
            if (God is { Visible: true } god)
            {
                god.Visible = false;
                _hiddenForPossession.Add(god);
            }
            foreach (var child in GetChildren())
            {
                // Plain Node containers: hide their visible CanvasItem children instead.
                Node[] items = child is CanvasItem ? [child] : [.. child.GetChildren()];
                foreach (var node in items)
                {
                    if (node is not CanvasItem { Visible: true } item || ReferenceEquals(item, Dialogue)) continue;
                    item.Visible = false;
                    _hiddenForPossession.Add(item);
                }
            }
        }
        else
        {
            foreach (var item in _hiddenForPossession)
                if (IsInstanceValid(item)) item.Visible = true;
            _hiddenForPossession.Clear();
        }
        PossessedChanged?.Invoke(possessed);
    }
}
