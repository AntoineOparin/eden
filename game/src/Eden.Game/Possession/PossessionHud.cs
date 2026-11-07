using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Root of scenes/possession/possession_hud.tscn. In god view: a "Possess (P)" button while an NPC is selected.
/// While possessed: the NPC's name, needs bars and current action (top left), the interaction prompt for what is
/// in reach (bottom centre), the controls (top right), a crosshair in first person and short notices. Built in code
/// with the one <see cref="UiTheme"/>.
/// </summary>
public partial class PossessionHud : CanvasLayer
{
    private PanelContainer _card = null!, _controls = null!, _promptPanel = null!;
    private Label _name = null!, _doing = null!, _prompt = null!, _notice = null!, _crosshair = null!;
    private GridContainer _needs = null!;
    private Button _possess = null!;
    private readonly Dictionary<string, ProgressBar> _bars = new();
    private double _noticeLeft;

    public event Action? PossessPressed;

    /// <summary>Texts as last rendered (tests, the M6 check log).</summary>
    public string NameText => _name?.Text ?? "";
    public string PromptText => _prompt?.Text ?? "";
    public string DoingText => _doing?.Text ?? "";
    public string NoticeText => _notice?.Text ?? "";
    public bool PossessButtonVisible => _possess?.Visible == true;
    public bool CardVisible => _card?.Visible == true;

    public override void _Ready() => Initialise();

    public void Initialise()
    {
        if (_card is not null) return;
        Layer = 2;
        var theme = UiTheme.Get();

        _card = new PanelContainer { Name = "Card", Theme = theme, Visible = false };
        _card.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft); // the dialogue panel owns the bottom left
        _card.OffsetLeft = 16;
        _card.OffsetTop = 16;
        AddChild(_card);
        var cardBox = Box(_card, 4);
        _name = new Label { Name = "Name" };
        _name.AddThemeFontSizeOverride("font_size", 20);
        cardBox.AddChild(_name);
        _doing = new Label { Name = "Doing", Modulate = new Color(1, 1, 1, 0.75f) };
        cardBox.AddChild(_doing);
        _needs = new GridContainer { Name = "Needs", Columns = 2 };
        _needs.AddThemeConstantOverride("h_separation", 8);
        cardBox.AddChild(_needs);

        _promptPanel = new PanelContainer { Name = "Prompt", Theme = theme, Visible = false };
        _promptPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _promptPanel.GrowHorizontal = Control.GrowDirection.Both;
        _promptPanel.GrowVertical = Control.GrowDirection.Begin;
        _promptPanel.OffsetBottom = -24;
        AddChild(_promptPanel);
        _prompt = new Label { Name = "Text", HorizontalAlignment = HorizontalAlignment.Center };
        Box(_promptPanel, 0).AddChild(_prompt);

        _controls = new PanelContainer { Name = "Controls", Theme = theme, Visible = false };
        _controls.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _controls.GrowHorizontal = Control.GrowDirection.Begin;
        _controls.OffsetRight = -16;
        _controls.OffsetTop = 16;
        AddChild(_controls);
        var help = new Label
        {
            Name = "Help",
            Text = "WASD walk   Shift sprint   Mouse look\nF first person   Esc release",
            Modulate = new Color(1, 1, 1, 0.8f),
        };
        Box(_controls, 0).AddChild(help);

        _notice = new Label
        {
            Name = "Notice", Theme = theme, Visible = false, HorizontalAlignment = HorizontalAlignment.Center,
        };
        _notice.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _notice.GrowHorizontal = Control.GrowDirection.Both;
        _notice.OffsetTop = 24;
        _notice.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_notice);

        _crosshair = new Label { Name = "Crosshair", Text = "·", Visible = false, Theme = theme };
        _crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _crosshair.AddThemeFontSizeOverride("font_size", 28);
        AddChild(_crosshair);

        _possess = new Button
        {
            Name = "Possess", Theme = theme, Text = "Possess (P)", Visible = false, FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Step into this person: walk with WASD, talk with E. Esc gives the body back.",
            CustomMinimumSize = new Vector2(0, 36),
        };
        _possess.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _possess.GrowHorizontal = Control.GrowDirection.Both;
        _possess.GrowVertical = Control.GrowDirection.Begin;
        _possess.OffsetBottom = -20;
        _possess.Pressed += () => PossessPressed?.Invoke();
        AddChild(_possess);
    }

    private static VBoxContainer Box(Control parent, int separation)
    {
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 10);
        parent.AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        margin.AddChild(box);
        return box;
    }

    /// <summary>God view: offer the button for the selected NPC (-1 hides it).</summary>
    public void ShowGodView(int selectedId, string selectedName)
    {
        Initialise();
        _card.Visible = _controls.Visible = _promptPanel.Visible = _crosshair.Visible = false;
        _possess.Visible = selectedId >= 0;
        if (selectedId >= 0) _possess.Text = $"Possess {selectedName} (P)";
    }

    /// <summary>Possessed view. <paramref name="detail"/> may be null until the sim has built it.</summary>
    public void ShowPossessed(string name, NpcDetail? detail, string doing, string prompt, bool firstPerson)
    {
        Initialise();
        _possess.Visible = false;
        _card.Visible = _controls.Visible = true;
        _crosshair.Visible = firstPerson;
        _name.Text = name;
        _doing.Text = doing;
        _prompt.Text = prompt;
        _promptPanel.Visible = prompt.Length > 0;
        if (detail is null) return;
        foreach (var (need, value) in detail.Needs)
        {
            if (!_bars.TryGetValue(need, out var bar))
            {
                _needs.AddChild(new Label { Text = need, CustomMinimumSize = new Vector2(70, 0) });
                bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.01, ShowPercentage = false, CustomMinimumSize = new Vector2(150, 12) };
                _needs.AddChild(bar);
                _bars[need] = bar;
            }
            bar.Value = value;
            bar.Modulate = value < 0.25f ? new Color(1f, 0.55f, 0.45f) : Colors.White;
        }
    }

    public void HideAll()
    {
        Initialise();
        _card.Visible = _controls.Visible = _promptPanel.Visible = _crosshair.Visible = _possess.Visible = false;
    }

    /// <summary>A short message at the top of the screen for a few seconds.</summary>
    public void Notify(string text, double seconds = 3)
    {
        Initialise();
        _notice.Text = text;
        _notice.Visible = true;
        _noticeLeft = seconds;
    }

    public override void _Process(double delta)
    {
        if (_noticeLeft <= 0) return;
        _noticeLeft -= delta;
        if (_noticeLeft <= 0) _notice.Visible = false;
    }
}
