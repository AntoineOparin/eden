namespace Eden.Game.Tests;

using Eden.Mind;
using Eden.Mind.Contracts;
using Eden.Sim;
using Eden.Sim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

/// <summary>M5 UI: brain settings (and encrypted key storage), budget meter, dialogue panel. Never calls a real API.</summary>
[TestSuite]
public class MindUiTests
{
    private sealed class EchoProvider : IBrainProvider
    {
        public string Id => "fake";
        public bool SupportsBatching => false;
        public Task<ThinkResult> ThinkAsync(ThinkRequest request, CancellationToken ct)
            => Task.FromResult(new ThinkResult(new ThinkResponse([], null, null, null), new BrainUsage(0, 0, 0, "fake")));
        public Task<ChatResult> ChatAsync(ChatRequest request, CancellationToken ct)
            => Task.FromResult(new ChatResult(new ChatResponse($"You said: {request.Message}", null, "calm"), new BrainUsage(10, 5, 1.5, "fake")));
        public Task<EmbedResult> EmbedAsync(EmbedRequest request, CancellationToken ct)
            => Task.FromException<EmbedResult>(new BrainException(BrainFailure.Unsupported, "none"));
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Brain_settings_panel_round_trips_fields_and_prefills_on_provider_switch()
    {
        var panel = GD.Load<PackedScene>("res://scenes/ui/brain_settings.tscn").Instantiate<BrainSettingsPanel>();
        var runner = ISceneRunner.Load(panel, true);
        await runner.SimulateFrames(2);
        var s = new BrainSettings("openai-compatible", "http://localhost:11434/v1", "llama3.2", "k-1", 7, 1234, 2);
        panel.Load(s);
        AssertThat(panel.Read()).IsEqual(s);

        panel.SelectProvider("anthropic");
        var a = panel.Read();
        AssertThat(a.ProviderId).IsEqual("anthropic");
        AssertThat(a.BaseUrl).IsEqual("https://api.anthropic.com");
        AssertThat(a.Model).IsEqual("claude-haiku-5-5");
        AssertThat(a.ApiKey).IsNull();

        BrainSettings? saved = null;
        panel.SettingsSaved += x => saved = x;
        panel.Save();
        AssertThat(saved).IsNotNull();
        AssertThat(panel.Visible).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Settings_store_encrypts_the_key_and_reads_it_back()
    {
        string dir = $"user://test-brain-{Time.GetTicksUsec()}/";
        var store = new BrainSettingsStore(dir);
        var s = new BrainSettings("anthropic", "https://api.anthropic.com", "claude-haiku-5-5", "sk-ant-secret-123", 10, 5000, 4);
        store.Save(s);

        string raw = Godot.FileAccess.GetFileAsString(store.SettingsPath);
        AssertThat(raw.Contains("sk-ant-secret-123")).IsFalse();
        AssertThat(Godot.FileAccess.GetFileAsBytes(store.KeyPath).Length).IsEqual(32);
        AssertThat(new BrainSettingsStore(dir).Load()).IsEqual(s);

        // A different machine key cannot decrypt it.
        var enc = store.Encrypt("hello");
        AssertThat(store.Decrypt(enc)).IsEqual("hello");
        AssertThat(store.Decrypt("not base64!")).IsNull();

        var ledger = new BudgetLedger();
        ledger.AddNeurons(42.5);
        store.SaveBudget(ledger);
        var restored = new BudgetLedger();
        store.RestoreBudget(restored);
        AssertThat(restored.NeuronsToday).IsEqual(42.5);

        foreach (var f in new[] { store.SettingsPath, store.KeyPath, store.BudgetPath }) DirAccess.RemoveAbsolute(f);
        DirAccess.RemoveAbsolute(dir);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Budget_meter_renders_a_status()
    {
        var meter = new BudgetMeter();
        var runner = ISceneRunner.Load(meter, true);
        await runner.SimulateFrames(1);
        meter.Render(new MindStatus("worker", "", MindState.Ok, 123.44, 9000, 0, 15, 2, 1, 40, 0, null, null));
        AssertThat(meter.Text).IsEqual("Neurons 123.4 / 9000  worker: ok  thinking 15");
        AssertThat(meter.GetNode<ProgressBar>("Bar").Value).IsEqualApprox(123.44 / 9000, 1e-4);

        meter.Render(new MindStatus("anthropic", "claude-haiku-5-5", MindState.DialogueOnly, 0, 9000, 0.01234, 3, 0, 0, 1, 0, null, null));
        AssertThat(meter.Text).IsEqual("Neurons 0.0 / 9000  $0.0123  anthropic: dialogue only  thinking 3");
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Dialogue_panel_sends_through_the_mind_and_keeps_history_per_npc()
    {
        using var host = new SimHost(new SimConfig { Seed = 12 });
        host.Start();
        var mind = new MindPool(host, BrainDefaults.Settings, (_, _) => new EchoProvider());
        var panel = GD.Load<PackedScene>("res://scenes/ui/dialogue.tscn").Instantiate<DialoguePanel>();
        var runner = ISceneRunner.Load(panel, true);
        await runner.SimulateFrames(1);
        panel.Bind(mind);
        int a = host.Latest.Npcs.Span[0].Id, b = host.Latest.Npcs.Span[1].Id;
        panel.ShowNpc(a, "Ava");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(panel.Send("Hello down there")).IsTrue();
        for (int i = 0; i < 200 && panel.Pending is not null; i++) { await runner.AwaitMillis(10); panel.Poll(); }
        AssertThat(panel.LastError).IsNull();
        AssertThat(panel.Transcript).IsEqual("You (the Voice): Hello down there\nAva: You said: Hello down there\n");

        panel.ShowNpc(b, "Adam");
        AssertThat(panel.Transcript).IsEqual("");
        panel.ShowNpc(a, "Ava");
        AssertThat(panel.Transcript.Contains("Hello down there")).IsTrue();
        AssertThat(mind.Budget.NeuronsToday).IsEqual(1.5);
        host.Stop();
    }
}
