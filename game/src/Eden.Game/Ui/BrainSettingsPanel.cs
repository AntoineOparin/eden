using Eden.Mind;
using Eden.Mind.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Root of scenes/ui/brain_settings.tscn: provider (worker / anthropic / openai-compatible / none), base URL,
/// model, API key, max thinking NPCs, daily Neuron budget, concurrency. Save raises <see cref="SettingsSaved"/>;
/// Main stores it (key encrypted, <see cref="BrainSettingsStore"/>) and applies it to the Mind.
/// </summary>
public partial class BrainSettingsPanel : PanelContainer
{
    private OptionButton _provider = null!;
    private LineEdit _baseUrl = null!, _model = null!, _key = null!;
    private SpinBox _maxThinking = null!, _budget = null!, _concurrency = null!;
    private BrainSettings _current = BrainDefaults.Settings;
    private string _lastProvider = "worker";

    public event Action<BrainSettings>? SettingsSaved;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(440, 0);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 12);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);
        var title = new Label { Text = "NPC brain" };
        title.AddThemeFontSizeOverride("font_size", 18);
        box.AddChild(title);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 10);
        box.AddChild(grid);
        _provider = new OptionButton { Name = "Provider", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var id in BrainDefaults.ProviderIds) _provider.AddItem(id);
        _provider.ItemSelected += OnProviderSelected;
        Row(grid, "Provider", _provider);
        _baseUrl = new LineEdit { Name = "BaseUrl", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Row(grid, "Base URL", _baseUrl);
        _model = new LineEdit { Name = "Model", PlaceholderText = "default", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Row(grid, "Model", _model);
        _key = new LineEdit { Name = "Key", Secret = true, PlaceholderText = "shared key or API key", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Row(grid, "Key", _key);
        _maxThinking = new SpinBox { Name = "MaxThinking", MinValue = 0, MaxValue = 100, Step = 1 };
        Row(grid, "Thinking NPCs", _maxThinking);
        _budget = new SpinBox { Name = "Budget", MinValue = 0, MaxValue = 1_000_000, Step = 1, Suffix = "Neurons/day" };
        Row(grid, "Daily budget", _budget);
        _concurrency = new SpinBox { Name = "Concurrency", MinValue = 1, MaxValue = 8, Step = 1 };
        Row(grid, "Concurrency", _concurrency);

        box.AddChild(new Label
        {
            Text = "Keys are stored encrypted in user:// with a key file on this machine. Direct providers bill your own account.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0), Modulate = new Color(1, 1, 1, 0.7f),
        });
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        box.AddChild(buttons);
        var cancel = new Button { Name = "Cancel", Text = "Cancel" };
        cancel.Pressed += () => { Load(_current); Visible = false; };
        buttons.AddChild(cancel);
        var save = new Button { Name = "Save", Text = "Save" };
        save.Pressed += Save;
        buttons.AddChild(save);
        Load(_current);
    }

    private static void Row(GridContainer grid, string label, Control field)
    {
        grid.AddChild(new Label { Text = label });
        grid.AddChild(field);
    }

    /// <summary>Fills the fields from settings.</summary>
    public void Load(BrainSettings s)
    {
        _current = s;
        if (_provider is null) return;
        int index = Array.IndexOf(BrainDefaults.ProviderIds, s.ProviderId);
        _provider.Select(index < 0 ? 0 : index);
        _lastProvider = s.ProviderId;
        _baseUrl.Text = s.BaseUrl;
        _model.Text = s.Model;
        _key.Text = s.ApiKey ?? "";
        _maxThinking.Value = s.MaxThinkingNpcs;
        _budget.Value = s.DailyNeuronBudget;
        _concurrency.Value = s.Concurrency;
    }

    /// <summary>The settings the fields describe.</summary>
    public BrainSettings Read()
    {
        if (_provider is null) return _current;
        string id = BrainDefaults.ProviderIds[Math.Clamp(_provider.Selected, 0, BrainDefaults.ProviderIds.Length - 1)];
        return new BrainSettings(id, _baseUrl.Text.Trim(), _model.Text.Trim(), _key.Text.Length == 0 ? null : _key.Text.Trim(),
            (int)_maxThinking.Value, _budget.Value, (int)_concurrency.Value);
    }

    public void Save()
    {
        _current = Read();
        Visible = false;
        SettingsSaved?.Invoke(_current);
    }

    /// <summary>Switching provider pre-fills its default URL and model, and clears a key that belonged to another provider.</summary>
    private void OnProviderSelected(long index)
    {
        string id = BrainDefaults.ProviderIds[(int)index];
        if (id == _lastProvider) return;
        var (url, model) = BrainDefaults.For(id);
        _baseUrl.Text = url;
        _model.Text = model;
        _key.Text = id == "worker" ? BrainDefaults.LocalWorkerKey : "";
        _lastProvider = id;
    }

    public void SelectProvider(string id)
    {
        int index = Array.IndexOf(BrainDefaults.ProviderIds, id);
        if (index < 0 || _provider is null) return;
        _provider.Select(index);
        OnProviderSelected(index);
    }
}
