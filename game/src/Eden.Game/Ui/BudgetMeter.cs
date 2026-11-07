using System.Globalization;
using Eden.Mind;
using Godot;

namespace Eden.Game;

/// <summary>
/// HUD budget meter: Neurons used today against the daily budget (bar + text), USD when a paid provider is
/// active, provider state, and how many NPCs are thinking. Main feeds it <see cref="MindPool.Status"/>.
/// </summary>
public partial class BudgetMeter : HBoxContainer
{
    private ProgressBar? _bar;
    private Label? _label;

    /// <summary>The last rendered line, also used by tests and the M5 check log.</summary>
    public string Text { get; private set; } = "";

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        _bar = new ProgressBar
        {
            Name = "Bar", MinValue = 0, MaxValue = 1, Step = 0, ShowPercentage = false,
            CustomMinimumSize = new Vector2(80, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        AddChild(_bar);
        _label = new Label { Name = "Label", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        AddChild(_label);
        TooltipText = "Brain budget: Neurons spent today (Workers AI free tier is 10,000/day). Background thoughts stop at 90%.";
    }

    public static string Format(MindStatus s)
    {
        var inv = CultureInfo.InvariantCulture;
        string state = s.State switch
        {
            MindState.Off => "off (rules only)",
            MindState.Ok => "ok",
            MindState.Backoff => $"retrying{(s.BackoffUntil is { } u ? $" in {Math.Max(0, (u - DateTimeOffset.UtcNow).TotalSeconds):0}s" : "")}",
            MindState.DialogueOnly => "dialogue only",
            _ => "budget spent",
        };
        string usd = s.UsdToday > 0 || s.ProviderId is "anthropic" or "openai-compatible"
            ? string.Format(inv, "  ${0:0.0000}", s.UsdToday) : "";
        return string.Format(inv, "Neurons {0:0.0} / {1:0}{2}  {3}: {4}  thinking {5}",
            s.NeuronsToday, s.NeuronBudget, usd, s.ProviderId, state, s.Thinking);
    }

    public void Render(MindStatus s)
    {
        Text = Format(s);
        if (_label is not null)
        {
            _label.Text = Text;
            _label.TooltipText = s.LastError ?? "";
        }
        if (_bar is not null)
        {
            _bar.Value = s.NeuronBudget > 0 ? Math.Clamp(s.NeuronsToday / s.NeuronBudget, 0, 1) : 0;
            _bar.Modulate = s.State switch
            {
                MindState.Exhausted => new Color(1, 0.35f, 0.3f),
                MindState.DialogueOnly or MindState.Backoff => new Color(1, 0.8f, 0.3f),
                _ => Colors.White,
            };
        }
    }
}
