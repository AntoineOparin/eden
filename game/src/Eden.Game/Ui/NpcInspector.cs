using System.Text;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Root of scenes/ui/inspector.tscn. Shows <see cref="IWorldSnapshot.TryGetNpcDetail"/> for the selected NPC:
/// needs as bars, traits, current goal and thought, knowledge, relationships and memories by importance.
/// M7 UI: the NPC's settlement as a link (<see cref="SettlementRequested"/>), knowledge as proficiency bars (strongest
/// first), and a myth marker on memories heard through retellings (<see cref="MemoryView.Generation"/> &gt; 0).
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

    /// <summary>M7 UI: the player clicked the settlement link.</summary>
    public event Action<int>? SettlementRequested;

    /// <summary>
    /// Hook for M6 possession: when anything subscribes, a "Possess" button appears in the header and raises this
    /// with the inspected NPC's id. Nothing in the M7 UI subscribes.
    /// </summary>
    public event Action<int>? PossessRequested
    {
        add { _possess += value; if (_possessButton is not null) _possessButton.Visible = _possess is not null; }
        remove { _possess -= value; if (_possessButton is not null) _possessButton.Visible = _possess is not null; }
    }

    private Action<int>? _possess;
    private Button? _possessButton;
    private LinkButton _settlementLink = null!;
    private int _settlementId = -1;
    private GridContainer _knowledge = null!;
    private Label _knowledgeTitle = null!;
    private RichTextLabel _bodyTail = null!;
    private readonly List<(Label Name, ProgressBar Bar)> _knowledgeRows = new();
    private readonly List<KeyValuePair<string, float>> _knowledgeSorted = new();
    private const int MaxKnowledgeRows = 16;
    public int NpcId { get; private set; } = -1;
    public NpcDetail? Detail { get; private set; }

    /// <summary>The Mind's latest thought for an NPC (M5); used when the sim has none.</summary>
    public Func<int, string?>? ThoughtSource { get; set; }

    /// <summary>The body text as last rendered (tests): goal, thought, traits, then relationships and memories.</summary>
    public string BodyText => (_body?.Text ?? "") + (_bodyTail?.Text ?? "");

    /// <summary>Knowledge rows shown as "concept proficiency" (tests).</summary>
    public IEnumerable<string> KnowledgeRows => _knowledgeRows.Where(r => r.Name.Visible).Select(r => $"{r.Name.Text} {r.Bar.Value:0.00}");

    /// <summary>The settlement link's text, or "" when the NPC has no settlement (tests).</summary>
    public string SettlementText => _settlementLink is { Visible: true } l ? l.Text : "";

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
        _possessButton = new Button { Name = "Possess", Text = "Possess", FocusMode = FocusModeEnum.None, Visible = _possess is not null, TooltipText = "Possess this person (P)" };
        _possessButton.Pressed += () => { if (NpcId >= 0) _possess?.Invoke(NpcId); };
        header.AddChild(_possessButton);
        header.AddChild(close);

        _subtitle = new Label { Name = "Subtitle", Modulate = new Color(1, 1, 1, 0.7f) };
        box.AddChild(_subtitle);
        _settlementLink = new LinkButton { Name = "Settlement", Visible = false, TooltipText = "Open this settlement", Underline = LinkButton.UnderlineMode.OnHover };
        _settlementLink.AddThemeColorOverride("font_color", new Color(1f, 0.80f, 0.55f));
        _settlementLink.AddThemeColorOverride("font_hover_color", Colors.White);
        _settlementLink.Pressed += () => { if (_settlementId >= 0) SettlementRequested?.Invoke(_settlementId); };
        box.AddChild(_settlementLink);

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
        var inner = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        inner.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(inner);
        inner.AddChild(_body);
        _knowledgeTitle = new Label { Name = "KnowledgeTitle", Text = "Knowledge", ThemeTypeVariation = "HeaderLabel" };
        inner.AddChild(_knowledgeTitle);
        _knowledge = new GridContainer { Name = "Knowledge", Columns = 2 };
        inner.AddChild(_knowledge);
        _bodyTail = new RichTextLabel
        {
            Name = "BodyTail", BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            CustomMinimumSize = new Vector2(300, 0), SelectionEnabled = true,
        };
        inner.AddChild(_bodyTail);
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
            _bodyTail.Text = "";
            _settlementLink.Visible = false;
            RenderKnowledge(null);
            return;
        }
        Detail = d;
        if (_title is null) return; // not in a tree yet (tests)
        _needs.Visible = true;
        _title.Text = d.Name;
        string stage = "";
        int settlement = -1;
        foreach (var v in snapshot.Npcs.Span)
            if (v.Id == NpcId) { stage = $"{v.Stage} {v.Sex}, {v.Pose}{(v.IsThinking ? ", thinking" : "")}"; settlement = v.SettlementId; break; }
        _subtitle.Text = $"#{d.Id}  age {d.AgeYears}  {stage}";
        _settlementId = -1;
        foreach (var sv in snapshot.Settlements.Span)
            if (sv.Id == settlement) { _settlementId = sv.Id; _settlementLink.Text = $"of {sv.Name}"; break; }
        _settlementLink.Visible = _settlementId >= 0;

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
        if ((d.CurrentThought ?? ThoughtSource?.Invoke(d.Id)) is { } thought) _sb.Append("[b]Thought[/b]  [i]").Append(Escape(thought)).Append("[/i]\n");
        AppendPairs("Traits", d.Traits);
        _body.Text = _sb.ToString();
        RenderKnowledge(d.Knowledge);
        _sb.Clear();
        _sb.Append("[b]Relationships[/b]\n");
        if (d.Relationships.Count == 0) _sb.Append("  none\n");
        foreach (var r in d.Relationships)
            _sb.Append("  ").Append(Escape(r.OtherName)).Append($"  affinity {r.Affinity:+0.00;-0.00}  trust {r.Trust:0.00}\n");
        _sb.Append("\n[b]Memories[/b]\n");
        var memories = d.Memories.OrderByDescending(m => m.Importance).ThenByDescending(m => m.Tick);
        int shown = 0;
        foreach (var m in memories)
        {
            if (shown++ >= 30) break;
            _sb.Append($"  [{m.Importance}] ");
            if (m.Generation > 0) _sb.Append($"[color=#e0b0ff][hint=Heard through {m.Generation} retelling{(m.Generation == 1 ? "" : "s")}; it may have changed in the telling]myth·{m.Generation}[/hint][/color] ");
            _sb.Append(Escape(m.Text)).Append($"  [color=#999](t{m.Tick}, {Escape(m.Kind)})[/color]\n");
        }
        if (shown == 0) _sb.Append("  none\n");
        _bodyTail.Text = _sb.ToString();
    }

    /// <summary>Knowledge as proficiency bars, strongest first, at most <see cref="MaxKnowledgeRows"/> rows (pooled).</summary>
    private void RenderKnowledge(IReadOnlyDictionary<string, float>? knowledge)
    {
        _knowledgeSorted.Clear();
        if (knowledge is not null) _knowledgeSorted.AddRange(knowledge);
        _knowledgeSorted.Sort((a, b) => b.Value.CompareTo(a.Value));
        int rows = Math.Min(MaxKnowledgeRows, _knowledgeSorted.Count);
        while (_knowledgeRows.Count < rows)
        {
            var name = new Label { CustomMinimumSize = new Vector2(120, 0), ClipText = true };
            var bar = new ProgressBar
            {
                MinValue = 0, MaxValue = 1, Step = 0.01, ShowPercentage = false,
                CustomMinimumSize = new Vector2(160, 10), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            _knowledge.AddChild(name);
            _knowledge.AddChild(bar);
            _knowledgeRows.Add((name, bar));
        }
        for (int i = 0; i < _knowledgeRows.Count; i++)
        {
            var (name, bar) = _knowledgeRows[i];
            bool on = i < rows;
            name.Visible = bar.Visible = on;
            if (!on) continue;
            var (concept, p) = _knowledgeSorted[i];
            name.Text = concept;
            bar.Value = p;
            bar.TooltipText = $"{concept}: proficiency {p:0.00}";
        }
        _knowledgeTitle.Text = _knowledgeSorted.Count > rows
            ? $"Knowledge  ({_knowledgeSorted.Count}, strongest {rows} shown)"
            : _knowledgeSorted.Count == 0 ? "Knowledge  (none)" : $"Knowledge  ({_knowledgeSorted.Count})";
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
