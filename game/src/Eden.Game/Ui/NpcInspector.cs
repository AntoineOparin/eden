using System.Text;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Root of scenes/ui/inspector.tscn. Shows <see cref="IWorldSnapshot.TryGetNpcDetail"/> for the selected NPC:
/// needs as bars, traits, current goal and thought, knowledge, relationships and memories by importance.
/// Refreshes a few times per second (details allocate, so not every frame).
/// </summary>
public partial class NpcInspector : PanelContainer
{
    [Export] public double RefreshSeconds { get; set; } = 0.25;

    private Label _title = null!;
    private Label _subtitle = null!;
    private GridContainer _needs = null!;
    private RichTextLabel _body = null!;
    private readonly Dictionary<string, ProgressBar> _needBars = new();
    private readonly StringBuilder _sb = new();
    private double _sinceRefresh = double.MaxValue;

    public event Action? Closed;
    public int NpcId { get; private set; } = -1;
    public NpcDetail? Detail { get; private set; }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(320, 0);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 10);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        margin.AddChild(box);

        var header = new HBoxContainer();
        box.AddChild(header);
        _title = new Label { Name = "Title", Text = "", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title.AddThemeFontSizeOverride("font_size", 20);
        header.AddChild(_title);
        var close = new Button { Name = "Close", Text = "x", FocusMode = FocusModeEnum.None, Flat = true };
        close.Pressed += () => { Clear(); Closed?.Invoke(); };
        header.AddChild(close);

        _subtitle = new Label { Name = "Subtitle", Modulate = new Color(1, 1, 1, 0.7f) };
        box.AddChild(_subtitle);

        _needs = new GridContainer { Name = "Needs", Columns = 2 };
        _needs.AddThemeConstantOverride("h_separation", 8);
        box.AddChild(_needs);

        _body = new RichTextLabel
        {
            Name = "Body",
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(300, 0),
            SelectionEnabled = true,
        };
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(300, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(_body);
        box.AddChild(scroll);
        Visible = NpcId >= 0;
    }

    public void ShowNpc(int npcId)
    {
        NpcId = npcId;
        Visible = npcId >= 0;
        _sinceRefresh = double.MaxValue;
    }

    public void Clear() => ShowNpc(-1);

    /// <summary>Main calls this every frame with the frame's snapshot; the panel throttles itself.</summary>
    public void UpdateFrom(IWorldSnapshot snapshot, double delta)
    {
        if (NpcId < 0) return;
        _sinceRefresh += delta;
        if (_sinceRefresh < RefreshSeconds) return;
        _sinceRefresh = 0;
        Refresh(snapshot);
    }

    public void Refresh(IWorldSnapshot snapshot)
    {
        if (NpcId < 0) return;
        if (!snapshot.TryGetNpcDetail(NpcId, out var d))
        {
            Detail = null;
            if (_title is null) return;
            _title.Text = $"NPC {NpcId}";
            _subtitle.Text = "No details (dead, or the sim does not provide them yet).";
            _needs.Visible = false;
            _body.Text = "";
            return;
        }
        Detail = d;
        if (_title is null) return; // not in a tree yet (tests)
        _needs.Visible = true;
        _title.Text = d.Name;
        string stage = "";
        foreach (var v in snapshot.Npcs.Span)
            if (v.Id == NpcId) { stage = $"{v.Stage} {v.Sex}, {v.Pose}{(v.IsThinking ? ", thinking" : "")}"; break; }
        _subtitle.Text = $"#{d.Id}  age {d.AgeYears}  {stage}";

        foreach (var (need, value) in d.Needs)
        {
            if (!_needBars.TryGetValue(need, out var bar))
            {
                _needs.AddChild(new Label { Text = need, CustomMinimumSize = new Vector2(70, 0) });
                bar = new ProgressBar
                {
                    MinValue = 0, MaxValue = 1, Step = 0.01, ShowPercentage = false,
                    CustomMinimumSize = new Vector2(200, 12), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                _needs.AddChild(bar);
                _needBars[need] = bar;
            }
            bar.Value = value;
        }

        _sb.Clear();
        _sb.Append("[b]Goal[/b]  ").Append(Escape(d.CurrentGoal ?? "none")).Append('\n');
        if (d.CurrentThought is { } thought) _sb.Append("[b]Thought[/b]  [i]").Append(Escape(thought)).Append("[/i]\n");
        AppendPairs("Traits", d.Traits);
        AppendPairs("Knowledge", d.Knowledge);
        _sb.Append("\n[b]Relationships[/b]\n");
        if (d.Relationships.Count == 0) _sb.Append("  none\n");
        foreach (var r in d.Relationships)
            _sb.Append("  ").Append(Escape(r.OtherName)).Append($"  affinity {r.Affinity:+0.00;-0.00}  trust {r.Trust:0.00}\n");
        _sb.Append("\n[b]Memories[/b]\n");
        var memories = d.Memories.OrderByDescending(m => m.Importance).ThenByDescending(m => m.Tick);
        int shown = 0;
        foreach (var m in memories)
        {
            if (shown++ >= 30) break;
            _sb.Append($"  [{m.Importance}] ").Append(Escape(m.Text)).Append($"  [color=#999](t{m.Tick}, {Escape(m.Kind)})[/color]\n");
        }
        if (shown == 0) _sb.Append("  none\n");
        _body.Text = _sb.ToString();
    }

    private void AppendPairs(string title, IReadOnlyDictionary<string, float> values)
    {
        _sb.Append("\n[b]").Append(title).Append("[/b]\n  ");
        if (values.Count == 0) _sb.Append("none");
        bool first = true;
        foreach (var (k, v) in values)
        {
            if (!first) _sb.Append(", ");
            first = false;
            _sb.Append(Escape(k)).Append($" {v:0.00}");
        }
        _sb.Append('\n');
    }

    private static string Escape(string s) => s.Replace("[", "[lb]");
}
