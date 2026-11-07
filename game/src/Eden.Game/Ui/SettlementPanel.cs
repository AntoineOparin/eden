using System.Text;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Settlement panel (M7 UI, scenes/ui/settlement.tscn). Opened by clicking a building or a settlement's land with
/// Select, from the inspector's settlement link, or from the picker at the top. Shows name, faction (the settlements
/// that share it), leader, members, the god's standing (favour and awe bars, stance, the neighbour envied or feared),
/// the faith, finished shrines, food (the live stock from <see cref="SettlementView.Food"/> and what the members eat
/// a day from the <see cref="SettlementDetail"/>), what the settlement knows (every concept a member knows, with
/// holder counts and a marker when a written record of it lies on its land) and its buildings, both from the detail
/// through <see cref="SettlementKnowledge"/> (cached per in-game day), and its recent Chronicle entries (entries that
/// name it). Refreshes every <see cref="RefreshSeconds"/> while visible.
/// </summary>
public partial class SettlementPanel : PanelContainer
{
    [Export] public double RefreshSeconds { get; set; } = 0.5;

    public int SettlementId { get; private set; } = -1;
    public SettlementKnowledge? Knowledge { get; set; }
    public ChronicleLog? Log { get; set; }

    public event Action? Closed;
    public event Action<int>? LeaderRequested;
    public event Action<Vector3>? FocusRequested;

    private OptionButton _picker = null!;
    private Label _title = null!;
    private Label _subtitle = null!;
    private ProgressBar _favour = null!, _awe = null!;
    private Label _favourValue = null!, _aweValue = null!, _stance = null!;
    private Button _leader = null!;
    private RichTextLabel _body = null!;
    private GridContainer _knowledge = null!;
    private readonly List<(Label Name, ProgressBar Bar)> _knowledgeRows = new();
    private Label _knowledgeSummary = null!;
    private Label _food = null!;
    private int _shownVersion = -1, _shownSettlement = -1;
    private readonly StringBuilder _sb = new();
    private double _since = double.MaxValue;
    private int _pickerVersion = -1;
    private readonly List<int> _pickerIds = new();
    private int _leaderId = -1;
    private string _leaderName = "";
    private Vector3 _centre;

    /// <summary>Plain body text as last rendered (tests).</summary>
    public string BodyText => _body?.GetParsedText() ?? "";
    public string StanceText => _stance?.Text ?? "";
    public string KnowledgeText => _knowledgeSummary?.Text ?? "";
    public string FoodText => _food?.Text ?? "";

    /// <summary>Knowledge rows as last rendered: "concept holders/members [written]" (tests).</summary>
    public IEnumerable<string> KnowledgeRows => _knowledgeRows.Where(r => r.Name.Visible).Select(r => (string)r.Bar.GetMeta("row", ""));

    /// <summary>Concepts listed in the panel at most.</summary>
    public const int MaxConceptRows = 12;

    /// <summary>Name colour of a concept with a written record on the settlement's land.</summary>
    public static readonly Color RecordColour = new(0.98f, 0.82f, 0.45f);
    public SettlementView? View { get; private set; }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(360, 560); // C1: tall enough for food, the concept list and the god's standing
        MouseFilter = MouseFilterEnum.Stop;
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 6);
        AddChild(outer);

        var header = new HBoxContainer();
        outer.AddChild(header);
        _title = new Label { Name = "Title", ThemeTypeVariation = "HeaderLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title.AddThemeFontSizeOverride("font_size", 20);
        header.AddChild(_title);
        var focus = new Button { Name = "Focus", Text = "Go", FocusMode = FocusModeEnum.None, TooltipText = "Move the camera to this settlement" };
        focus.Pressed += () => FocusRequested?.Invoke(_centre);
        header.AddChild(focus);
        var close = new Button { Name = "Close", Text = "x", Flat = true, FocusMode = FocusModeEnum.None };
        close.Pressed += () => { ShowSettlement(-1); Closed?.Invoke(); };
        header.AddChild(close);

        _picker = new OptionButton { Name = "Picker", FocusMode = FocusModeEnum.None, TooltipText = "Pick a settlement" };
        _picker.ItemSelected += i => { if (i >= 0 && i < _pickerIds.Count) ShowSettlement(_pickerIds[(int)i]); };
        outer.AddChild(_picker);

        var scroll = new ScrollContainer { Name = "Scroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(340, 120) };
        outer.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(box);

        _subtitle = new Label { Name = "Subtitle", Modulate = new Color(1, 1, 1, 0.75f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_subtitle);
        var leaderRow = new HBoxContainer();
        box.AddChild(leaderRow);
        leaderRow.AddChild(new Label { Text = "Leader", CustomMinimumSize = new Vector2(70, 0) });
        _leader = new Button { Name = "Leader", Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Inspect the leader", Alignment = HorizontalAlignment.Left };
        _leader.Pressed += () => { if (_leaderId >= 0) LeaderRequested?.Invoke(_leaderId); };
        leaderRow.AddChild(_leader);

        var godHeader = new Label { Text = "The god", ThemeTypeVariation = "HeaderLabel" };
        box.AddChild(godHeader);
        var god = new GridContainer { Name = "God", Columns = 3 };
        box.AddChild(god);
        (_favour, _favourValue) = Bar(god, "Favour", "How much the people here love the god (−1 hate … +1 love)");
        (_awe, _aweValue) = Bar(god, "Awe", "How much the god's power impresses and frightens them (0 … 1)");
        _favour.AddThemeStyleboxOverride("fill", Fill(OverlayColors.Favourable));
        _awe.AddThemeStyleboxOverride("fill", Fill(OverlayColors.Fearful));
        _stance = new Label { Name = "Stance", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_stance);

        box.AddChild(new Label { Text = "Food", ThemeTypeVariation = "HeaderLabel" });
        _food = new Label { Name = "Food", AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Pass, TooltipText = "Food units: one unit restores about a third of a person's hunger. Stock = the shared pile and storehouses plus what the members carry." };
        box.AddChild(_food);

        box.AddChild(new Label { Text = "Knowledge", ThemeTypeVariation = "HeaderLabel" });
        _knowledgeSummary = new Label { Name = "KnowledgeSummary", Modulate = new Color(1, 1, 1, 0.8f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_knowledgeSummary);
        _knowledge = new GridContainer { Name = "Knowledge", Columns = 2 };
        box.AddChild(_knowledge);

        _body = new RichTextLabel
        {
            Name = "Body", BbcodeEnabled = true, FitContent = true, ScrollActive = false, SelectionEnabled = true,
            CustomMinimumSize = new Vector2(320, 0),
        };
        box.AddChild(_body);
        // C1: food and knowledge come first (the panel is short); the god's standing follows, then buildings and events.
        foreach (var node in new Control[] { godHeader, god, _stance, _body }) box.MoveChild(node, box.GetChildCount() - 1);
        Visible = false;
    }

    private static (ProgressBar, Label) Bar(GridContainer grid, string name, string tip)
    {
        grid.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(64, 0), TooltipText = tip, MouseFilter = MouseFilterEnum.Pass });
        var bar = new ProgressBar
        {
            Name = name, MinValue = 0, MaxValue = 1, Step = 0.01, ShowPercentage = false,
            CustomMinimumSize = new Vector2(200, 12), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = tip,
        };
        grid.AddChild(bar);
        var value = new Label { CustomMinimumSize = new Vector2(44, 0), HorizontalAlignment = HorizontalAlignment.Right };
        grid.AddChild(value);
        return (bar, value);
    }

    public void ShowSettlement(int id)
    {
        SettlementId = id;
        Visible = id >= 0;
        _since = double.MaxValue;
        if (id >= 0) Knowledge?.Want(id, first: true);
    }

    /// <summary>GodUi calls this every frame; the panel throttles itself.</summary>
    public void Tick(IWorldSnapshot s, double delta)
    {
        if (!Visible) return;
        _since += delta;
        if (_since < RefreshSeconds) return;
        _since = 0;
        Refresh(s);
    }

    public void Refresh(IWorldSnapshot s)
    {
        var all = s.Settlements.Span;
        SettlementView? found = null;
        foreach (var candidate in all)
            if (candidate.Id == SettlementId) { found = candidate; break; }
        RefreshPicker(all);
        if (found is not { } v0)
        {
            View = null;
            if (_title is null) return;
            _title.Text = SettlementId >= 0 ? "Abandoned" : "Settlements";
            _subtitle.Text = SettlementId >= 0 ? "This settlement no longer exists." : "Pick one above.";
            return;
        }
        var v = v0;
        View = v;
        _centre = new Vector3(v.X, 0, v.Z);
        if (Knowledge is { } kn && kn.IsStale(v.Id, s.Tick)) kn.Want(v.Id, first: true);
        if (_title is null) return; // not in a tree (tests)

        _title.Text = v.Name;
        _sb.Clear();
        _sb.Append(v.Members).Append(v.Members == 1 ? " member" : " members");
        if (v.FactionId >= 0)
        {
            _sb.Append(",  faction of ");
            bool first = true;
            foreach (var o in all)
            {
                if (o.FactionId != v.FactionId) continue;
                if (!first) _sb.Append(", ");
                first = false;
                _sb.Append(o.Name);
            }
        }
        else _sb.Append(",  no faction");
        _subtitle.Text = _sb.ToString();

        if (v.LeaderId != _leaderId) { _leaderId = v.LeaderId; _leaderName = ""; }
        if (_leaderId >= 0 && _leaderName.Length == 0 && s.TryGetNpcDetail(_leaderId, out var ld)) _leaderName = ld.Name;
        _leader.Text = _leaderId < 0 ? "none" : _leaderName.Length > 0 ? _leaderName : $"#{_leaderId}";
        _leader.Disabled = _leaderId < 0;

        _favour.Value = (v.Favour + 1f) * 0.5f;
        _favourValue.Text = v.Favour.ToString("+0.00;-0.00");
        _awe.Value = v.Awe;
        _aweValue.Text = v.Awe.ToString("0.00");
        _sb.Clear();
        _sb.Append("Stance: ").Append(OverlayColors.StanceWord(v.Stance));
        _sb.Append(".  Faith: ").Append(v.ReligionName ?? "none");
        _sb.Append(".  Shrines: ").Append(Shrines(s, v.Id)).Append('.');
        if (v.EnviedId >= 0) _sb.Append("\nEnvies ").Append(NameOf(all, v.EnviedId)).Append($" (envy {v.Envy:0.00}).");
        if (v.FearedId >= 0) _sb.Append("\nFears ").Append(NameOf(all, v.FearedId)).Append($" (fear {v.Fear:0.00}).");
        if (v.EnviedId < 0 && v.FearedId < 0) _sb.Append("\nNo strong feelings toward a neighbour.");
        _stance.Text = _sb.ToString();
        _stance.Modulate = OverlayColors.Stance(v.Stance).Lerp(Colors.White, 0.45f);

        var entry = Knowledge?.Get(v.Id);
        RenderFood(v, entry);
        RenderKnowledge(entry);
        RenderChronicle(v.Name, entry);
    }

    private void RefreshPicker(ReadOnlySpan<SettlementView> all)
    {
        if (_picker is null) return;
        int version = all.Length * 7919;
        foreach (var v in all) version = version * 31 + v.Id;
        if (version != _pickerVersion)
        {
            _pickerVersion = version;
            _picker.Clear();
            _pickerIds.Clear();
            foreach (var v in all)
            {
                _picker.AddItem($"{v.Name}  ({v.Members})");
                _pickerIds.Add(v.Id);
            }
        }
        int at = _pickerIds.IndexOf(SettlementId);
        if (_picker.Selected != at) _picker.Select(at);
    }

    private void RenderFood(in SettlementView v, SettlementKnowledge.Entry? e)
    {
        _sb.Clear();
        _sb.Append($"{v.Food:0} in store");
        if (e is not null && e.Detail.FoodPerDay > 0f)
        {
            float perDay = e.Detail.FoodPerDay;
            _sb.Append($",  eats about {perDay:0.#} a day");
            _sb.Append(v.Food / perDay < 1f ? "  (under a day of stock)" : $"  (about {v.Food / perDay:0} days of stock)");
        }
        else if (e is null) _sb.Append(",  daily use not loaded yet");
        _food.Text = _sb.ToString();
    }

    private void RenderKnowledge(SettlementKnowledge.Entry? e)
    {
        if (e is null)
        {
            _knowledgeSummary.Text = Knowledge?.IsRequesting(SettlementId) == true ? "Asking the settlement…" : "Not loaded yet.";
            foreach (var r in _knowledgeRows) { r.Name.Visible = r.Bar.Visible = false; }
            _shownVersion = -1;
            return;
        }
        if (_shownVersion == Knowledge!.Version && _shownSettlement == SettlementId) return;
        _shownVersion = Knowledge.Version;
        _shownSettlement = SettlementId;
        var d = e.Detail;
        var top = e.Top(MaxConceptRows);
        int recorded = e.Recorded;
        _knowledgeSummary.Text = d.Concepts.Count == 0
            ? "Knows no concepts yet."
            : $"Knows {d.Concepts.Count} concepts, {recorded} written down here (gold). Bar: share of its {d.Members} people"
              + (d.Concepts.Count > top.Count ? $", top {top.Count}." : ".");
        while (_knowledgeRows.Count < top.Count)
        {
            var name = new Label { CustomMinimumSize = new Vector2(130, 0), ClipText = true, MouseFilter = MouseFilterEnum.Pass };
            var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.01, ShowPercentage = false, CustomMinimumSize = new Vector2(180, 10), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            _knowledge.AddChild(name);
            _knowledge.AddChild(bar);
            _knowledgeRows.Add((name, bar));
        }
        for (int i = 0; i < _knowledgeRows.Count; i++)
        {
            var (name, bar) = _knowledgeRows[i];
            bool on = i < top.Count;
            name.Visible = bar.Visible = on;
            if (!on) continue;
            var c = top[i];
            name.Text = c.ConceptId;
            name.Modulate = c.HasRecord ? RecordColour : Colors.White;
            bar.Value = d.Members > 0 ? c.Holders / (float)d.Members : 0;
            string tip = $"{c.Holders} of {d.Members} know {c.ConceptId}; best proficiency {c.BestProficiency:0.00}; "
                + (c.HasRecord ? "a written record of it is kept here" : "no written record here");
            bar.TooltipText = name.TooltipText = tip;
            bar.SetMeta("row", $"{c.ConceptId} {c.Holders}/{d.Members}{(c.HasRecord ? " written" : "")}");
        }
    }

    private void RenderChronicle(string name, SettlementKnowledge.Entry? e)
    {
        _sb.Clear();
        if (e is not null)
        {
            _sb.Append("[b]Buildings[/b]  ");
            if (e.Detail.Buildings.Count == 0) _sb.Append("none finished");
            for (int i = 0; i < e.Detail.Buildings.Count; i++)
            {
                var b = e.Detail.Buildings[i];
                if (i > 0) _sb.Append(",  ");
                _sb.Append(b.Count).Append(' ').Append(b.Kind.Replace('-', ' ')).Append(b.Count == 1 ? "" : "s");
            }
            _sb.Append("\n\n");
        }
        _sb.Append("[b]Recent events[/b]\n");
        int shown = 0;
        if (Log is { } log)
            for (int i = log.Count - 1; i >= 0 && shown < 6; i--)
            {
                var entry = log[i];
                if (!entry.Text.Contains(name, StringComparison.Ordinal)) continue;
                shown++;
                _sb.Append("  • ").Append(entry.Text.Replace("[", "[lb]")).Append('\n');
            }
        if (shown == 0) _sb.Append("  none in the Chronicle\n");
        _body.Text = _sb.ToString();
    }

    private static int Shrines(IWorldSnapshot s, int id)
    {
        int n = 0;
        foreach (var b in s.Buildings.Span)
            if (b.SettlementId == id && b.Kind == "shrine" && b.Progress >= 1f) n++;
        return n;
    }

    private static string NameOf(ReadOnlySpan<SettlementView> all, int id)
    {
        foreach (var v in all) if (v.Id == id) return v.Name;
        return $"#{id}";
    }

    private static readonly Dictionary<Color, StyleBoxFlat> Fills = new();

    private static StyleBoxFlat Fill(Color c)
    {
        if (Fills.TryGetValue(c, out var box)) return box;
        box = new StyleBoxFlat { BgColor = c };
        box.SetCornerRadiusAll(5);
        Fills[c] = box;
        return box;
    }
}
