using System.Text;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Chronicle timeline (M7 UI, scenes/ui/chronicle.tscn). Reads the shared <see cref="ChronicleLog"/>: kind filters
/// (one toggle per <see cref="ChronicleCategory"/>), an importance threshold, a per-year density strip (click a bar
/// to jump to that year) and the newest <see cref="MaxRows"/> matching entries, newest first. Clicking an entry asks
/// for the camera to focus on its subject (<see cref="EntryActivated"/>; GodUi resolves the position). Rebuilds only
/// when the log or the filters changed, at most every <see cref="RefreshSeconds"/>, and only while visible.
/// </summary>
public partial class ChroniclePanel : PanelContainer
{
    public const int MaxRows = 800;
    [Export] public double RefreshSeconds { get; set; } = 0.5;

    public ChronicleLog? Log { get; set; }
    public int TicksPerYear { get; set; } = 2400;
    public int TicksPerDay { get; set; } = 240;

    /// <summary>Filter mask, bit per <see cref="ChronicleCategory"/>.</summary>
    public int Mask { get; private set; } = ChronicleLog.AllCategories;
    public int MinImportance { get; private set; }

    /// <summary>The player clicked an entry.</summary>
    public event Action<ChronicleEntry>? EntryActivated;
    public event Action? Closed;

    private ItemList _list = null!;
    private Label _count = null!;
    private HSlider _importance = null!;
    private Label _importanceLabel = null!;
    private DensityStrip _strip = null!;
    private readonly List<int> _filtered = new();
    private readonly List<ChronicleEntry> _rows = new();
    private readonly StringBuilder _sb = new();
    private int _builtVersion = -1, _builtMask = -1, _builtImportance = -1;
    private double _since = double.MaxValue;
    private static readonly Dictionary<ChronicleCategory, Texture2D> Swatches = new();

    /// <summary>Rows shown, newest first (tests).</summary>
    public IReadOnlyList<ChronicleEntry> Rows => _rows;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(460, 300);
        MouseFilter = MouseFilterEnum.Stop;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);

        var header = new HBoxContainer();
        box.AddChild(header);
        header.AddChild(new Label { Text = "Chronicle", ThemeTypeVariation = "HeaderLabel" });
        _count = new Label { Name = "Count", Modulate = new Color(1, 1, 1, 0.7f), SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        header.AddChild(_count);
        var close = new Button { Name = "Close", Text = "x", Flat = true, FocusMode = FocusModeEnum.None };
        close.Pressed += () => { Visible = false; Closed?.Invoke(); };
        header.AddChild(close);

        var filters = new HFlowContainer { Name = "Filters" };
        filters.AddThemeConstantOverride("h_separation", 4);
        filters.AddThemeConstantOverride("v_separation", 4);
        box.AddChild(filters);
        for (int i = 0; i < ChronicleLog.CategoryCount; i++)
        {
            var cat = (ChronicleCategory)i;
            var b = new Button
            {
                Name = cat.ToString(), Text = ChronicleLog.Label(cat), ToggleMode = true, ButtonPressed = true, FocusMode = FocusModeEnum.None,
                Icon = Swatch(cat), TooltipText = $"Show or hide {ChronicleLog.Label(cat).ToLowerInvariant()} entries",
            };
            b.AddThemeFontSizeOverride("font_size", 12);
            int bit = 1 << i;
            b.Toggled += on => SetMask(on ? Mask | bit : Mask & ~bit);
            filters.AddChild(b);
        }

        var row = new HBoxContainer();
        box.AddChild(row);
        row.AddChild(new Label { Text = "Importance ≥" });
        _importance = new HSlider { Name = "Importance", MinValue = 0, MaxValue = 10, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None };
        _importance.ValueChanged += v => SetMinImportance((int)v);
        row.AddChild(_importance);
        _importanceLabel = new Label { Text = "0", CustomMinimumSize = new Vector2(20, 0) };
        row.AddChild(_importanceLabel);

        _strip = new DensityStrip { Name = "Density", CustomMinimumSize = new Vector2(0, 28), TooltipText = "Entries per year (click a bar to jump there)" };
        _strip.YearClicked += JumpToYear;
        box.AddChild(_strip);

        _list = new ItemList
        {
            Name = "Entries", SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 110),
            SelectMode = ItemList.SelectModeEnum.Single, AllowReselect = true, FocusMode = FocusModeEnum.None,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _list.AddThemeColorOverride("font_color", UiTheme.Ink);
        _list.AddThemeFontSizeOverride("font_size", 13);
        _list.ItemSelected += i => Activate((int)i);
        box.AddChild(_list);
    }

    public void SetMask(int mask)
    {
        Mask = mask & ChronicleLog.AllCategories;
        _since = double.MaxValue;
    }

    public void SetMinImportance(int value)
    {
        MinImportance = Math.Clamp(value, 0, 10);
        if (_importanceLabel is not null) _importanceLabel.Text = MinImportance.ToString();
        if (_importance is not null && (int)_importance.Value != MinImportance) _importance.SetValueNoSignal(MinImportance);
        _since = double.MaxValue;
    }

    /// <summary>GodUi calls this every frame; the panel throttles itself and skips work while hidden.</summary>
    public void Tick(double delta)
    {
        if (!Visible || Log is null) return;
        _since += delta;
        if (_since < RefreshSeconds) return;
        _since = 0;
        Rebuild();
    }

    /// <summary>Re-filters and refills the list if anything changed.</summary>
    public void Rebuild()
    {
        if (Log is null || (Log.Version == _builtVersion && Mask == _builtMask && MinImportance == _builtImportance)) return;
        _builtVersion = Log.Version;
        _builtMask = Mask;
        _builtImportance = MinImportance;
        Log.Filter(Mask, MinImportance, _filtered);
        _rows.Clear();
        for (int k = _filtered.Count - 1; k >= 0 && _rows.Count < MaxRows; k--) _rows.Add(Log[_filtered[k]]);
        if (_list is null) return; // not in a tree (tests)
        _list.Clear();
        foreach (var e in _rows)
        {
            _sb.Clear();
            long year = e.Tick / Math.Max(1, TicksPerYear), day = e.Tick % Math.Max(1, TicksPerYear) / Math.Max(1, TicksPerDay);
            _sb.Append('y').Append(year + 1).Append(" d").Append(day + 1).Append("   ").Append(e.Text);
            int i = _list.AddItem(_sb.ToString(), Swatch(ChronicleLog.CategoryOf(e.Kind)));
            _list.SetItemTooltip(i, $"{e.Kind}, importance {e.Importance}\n{e.Text}");
            if (e.Importance >= 8) _list.SetItemCustomFgColor(i, new Color(1f, 0.86f, 0.62f));
        }
        _count.Text = _filtered.Count == Log.Count ? $"{Log.Count} entries" : $"{_filtered.Count} of {Log.Count}";
        _strip.SetCounts(_filtered, Log, TicksPerYear);
    }

    private void Activate(int row)
    {
        if (row >= 0 && row < _rows.Count) EntryActivated?.Invoke(_rows[row]);
    }

    private void JumpToYear(long year)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Tick / Math.Max(1, TicksPerYear) > year) continue;
            _list.Select(i);
            _list.EnsureCurrentIsVisible();
            return;
        }
    }

    private static Texture2D Swatch(ChronicleCategory c)
    {
        if (Swatches.TryGetValue(c, out var t)) return t;
        var img = Image.CreateEmpty(10, 10, false, Image.Format.Rgba8);
        img.Fill(CategoryColour(c));
        t = ImageTexture.CreateFromImage(img);
        Swatches[c] = t;
        return t;
    }

    public static Color CategoryColour(ChronicleCategory c) => c switch
    {
        ChronicleCategory.Life => new Color(0.85f, 0.85f, 0.80f),
        ChronicleCategory.Trade => new Color(0.45f, 0.80f, 0.45f),
        ChronicleCategory.Raid => new Color(0.90f, 0.30f, 0.25f),
        ChronicleCategory.Discovery => new Color(0.35f, 0.70f, 0.95f),
        ChronicleCategory.Invention => new Color(0.30f, 0.90f, 0.90f),
        ChronicleCategory.Religion => new Color(0.98f, 0.78f, 0.28f),
        ChronicleCategory.Schism => new Color(0.95f, 0.50f, 0.20f),
        ChronicleCategory.Shrine => new Color(0.85f, 0.65f, 0.95f),
        ChronicleCategory.Prayer => new Color(0.70f, 0.60f, 0.95f),
        ChronicleCategory.God => new Color(1f, 0.95f, 0.60f),
        ChronicleCategory.KnowledgeLost => new Color(0.55f, 0.50f, 0.50f),
        _ => new Color(0.70f, 0.60f, 0.50f),
    };
}

/// <summary>Bar chart of filtered Chronicle entries per year (M7 UI). Drawn in <see cref="_Draw"/>, no nodes per bar.</summary>
public partial class DensityStrip : Control
{
    private int[] _counts = [];
    private long _firstYear;
    private int _max;

    public event Action<long>? YearClicked;

    public int Years => _counts.Length;
    public int Max => _max;

    public void SetCounts(List<int> indices, ChronicleLog log, int ticksPerYear)
    {
        int tpy = Math.Max(1, ticksPerYear);
        if (indices.Count == 0) { _counts = []; _max = 0; QueueRedraw(); return; }
        long first = log[indices[0]].Tick / tpy, last = log[indices[^1]].Tick / tpy;
        int years = (int)Math.Min(400, last - first + 1);
        first = last - years + 1;
        if (_counts.Length != years) _counts = new int[years];
        else Array.Clear(_counts);
        _firstYear = first;
        _max = 0;
        foreach (int i in indices)
        {
            long y = log[i].Tick / tpy - first;
            if (y < 0) continue;
            int c = ++_counts[y];
            if (c > _max) _max = c;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.08f, 0.06f, 0.05f, 0.7f));
        if (_counts.Length == 0 || _max == 0) return;
        float w = MathF.Min(24f, size.X / _counts.Length);
        for (int i = 0; i < _counts.Length; i++)
        {
            if (_counts[i] == 0) continue;
            float h = MathF.Max(2f, (size.Y - 4) * _counts[i] / _max);
            DrawRect(new Rect2(i * w + 0.5f, size.Y - 2 - h, MathF.Max(1f, w - 1f), h), UiTheme.Accent);
        }
    }

    private int BarAt(float x) => Math.Clamp((int)(x / MathF.Max(1f, MathF.Min(24f, Size.X / _counts.Length))), 0, _counts.Length - 1);

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb || _counts.Length == 0) return;
        int i = BarAt(mb.Position.X);
        YearClicked?.Invoke(_firstYear + i);
        AcceptEvent();
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (_counts.Length == 0) return "No entries";
        int i = BarAt(atPosition.X);
        return $"Year {_firstYear + i + 1}: {_counts[i]} entries";
    }
}
