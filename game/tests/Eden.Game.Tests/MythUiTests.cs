namespace Eden.Game.Tests;

using Eden.Game.Dev;
using Eden.Sim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

/// <summary>M7 UI pass: Chronicle log and timeline, settlement panel and knowledge sampling, overlay, powers, inspector.</summary>
[TestSuite]
public class MythUiTests
{
    private static ChronicleEntry E(long tick, string kind, string text, int importance = 5, params int[] subjects) => new(tick, kind, text, importance, subjects);

    [TestCase]
    public void Chronicle_log_is_bounded_and_keeps_the_newest()
    {
        var log = new ChronicleLog(capacity: 5);
        for (int i = 0; i < 8; i++) log.Offer(E(i, "birth", $"born {i}"));
        AssertThat(log.Count).IsEqual(5);
        AssertThat(log.Accepted).IsEqual(8L);
        AssertThat(log[0].Text).IsEqual("born 3");
        AssertThat(log[4].Text).IsEqual("born 7");
        AssertThat(new ChronicleLog().Capacity).IsEqual(5000);
    }

    [TestCase]
    public void Chronicle_log_drops_duplicates_and_older_entries()
    {
        var log = new ChronicleLog();
        AssertThat(log.Offer(E(10, "raid", "a"))).IsTrue();
        AssertThat(log.Offer(E(10, "raid", "a"))).IsFalse(); // same tick and text: the RecentChronicle replay
        AssertThat(log.Offer(E(10, "trade", "b"))).IsTrue();
        AssertThat(log.Offer(E(9, "trade", "c"))).IsFalse();
        AssertThat(log.Offer(E(11, "raid", "a"))).IsTrue();
        AssertThat(log.Count).IsEqual(3);
    }

    [TestCase]
    public void Chronicle_kinds_map_to_the_filter_groups()
    {
        AssertThat(ChronicleLog.CategoryOf("birth")).IsEqual(ChronicleCategory.Life);
        AssertThat(ChronicleLog.CategoryOf("death")).IsEqual(ChronicleCategory.Life);
        AssertThat(ChronicleLog.CategoryOf("trade")).IsEqual(ChronicleCategory.Trade);
        AssertThat(ChronicleLog.CategoryOf("raid")).IsEqual(ChronicleCategory.Raid);
        AssertThat(ChronicleLog.CategoryOf("discovery")).IsEqual(ChronicleCategory.Discovery);
        AssertThat(ChronicleLog.CategoryOf("invention")).IsEqual(ChronicleCategory.Invention);
        AssertThat(ChronicleLog.CategoryOf("religion")).IsEqual(ChronicleCategory.Religion);
        AssertThat(ChronicleLog.CategoryOf("schism")).IsEqual(ChronicleCategory.Schism);
        AssertThat(ChronicleLog.CategoryOf("shrine")).IsEqual(ChronicleCategory.Shrine);
        AssertThat(ChronicleLog.CategoryOf("prayer")).IsEqual(ChronicleCategory.Prayer);
        foreach (var k in new[] { "god", "blessing", "curse", "weather" }) AssertThat(ChronicleLog.CategoryOf(k)).IsEqual(ChronicleCategory.God);
        AssertThat(ChronicleLog.CategoryOf("knowledge-lost")).IsEqual(ChronicleCategory.KnowledgeLost);
        AssertThat(ChronicleLog.CategoryOf("settlement")).IsEqual(ChronicleCategory.Society);

        var log = new ChronicleLog();
        log.Offer(E(1, "raid", "r", 7));
        log.Offer(E(2, "trade", "t", 3));
        log.Offer(E(3, "shrine", "s", 8));
        var into = new List<int>();
        log.Filter(ChronicleLog.AllCategories, 0, into);
        AssertThat(into.Count).IsEqual(3);
        log.Filter(ChronicleLog.AllCategories, 7, into);
        AssertThat(into.Count).IsEqual(2);
        log.Filter(1 << (int)ChronicleCategory.Trade, 0, into);
        AssertThat(into.Count).IsEqual(1);
        AssertThat(log[into[0]].Text).IsEqual("t");
    }

    [TestCase]
    public void Chronicle_log_merges_the_recent_tail_of_the_real_sim()
    {
        // Two snapshots published between frames: the frame sees only the second one's NewChronicle,
        // the RecentChronicle tail fills the gap without duplicating.
        using var host = new Eden.Sim.SimHost(new Eden.Sim.SimConfig { Seed = 3 });
        var log = new ChronicleLog();
        log.Ingest(host.Latest);
        int first = log.Count;
        AssertThat(first).IsGreater(0); // genesis
        host.StepSync(10);
        log.Ingest(host.Latest);
        log.Ingest(host.Latest); // same snapshot: no-op
        AssertThat(log.Count).IsGreaterEqual(first);
        var texts = new HashSet<string>();
        for (int i = 0; i < log.Count; i++) AssertThat(texts.Add($"{log[i].Tick}:{log[i].Text}")).IsTrue();
    }

    [TestCase]
    public void Fake_host_emits_settlements_and_blessings_raise_favour()
    {
        using var host = new FakeSimHost(300);
        var s = host.Latest;
        AssertThat(s.Settlements.Length).IsEqual(FakeSimHost.SettlementCount);
        int members = 0;
        foreach (var v in s.Settlements.Span) members += v.Members;
        AssertThat(members).IsEqual(300);
        var target = s.Npcs.Span[5];
        float before = s.Settlements.Span[target.SettlementId].Favour;
        host.Enqueue(new BlessCommand(target.Id, false, 240));
        host.StepSync(1);
        AssertThat(host.Latest.Settlements.Span[target.SettlementId].Favour).IsGreater(before);
        bool god = false;
        foreach (var e in host.Latest.NewChronicle) god |= e.Kind == "blessing";
        AssertThat(god).IsTrue();
    }

    [TestCase]
    public void Settlement_knowledge_comes_from_the_detail_and_caches_per_day()
    {
        using var host = new FakeSimHost(200);
        var s = host.Latest;
        var sent = new List<WorldCommand>();
        var k = new SettlementKnowledge { Enqueue = sent.Add };
        AssertThat(k.IsStale(3, s.Tick)).IsTrue();
        k.Want(3, first: true);
        k.Update(s, 0.25);
        AssertThat(sent.OfType<WatchSettlementCommand>().Select(c => c.Id).ToList()).Contains(3);
        var e = k.Get(3);
        AssertThat(e).IsNotNull();
        AssertThat(s.TryGetSettlementDetail(3, out var d)).IsTrue();
        AssertThat(e!.Members).IsEqual(d.Members);
        AssertThat(e.Members).IsEqual(s.Settlements.Span[3].Members);
        AssertThat(e.Concepts.ContainsKey("fire")).IsTrue();
        AssertThat(e.Concepts["fire"].Holders).IsEqual(e.Members); // everyone in the fake knows fire
        AssertThat(e.Concepts.Count).IsGreater(3); // fake settlement 3 knows up to 9 concepts
        AssertThat(e.Recorded).IsGreater(0);
        AssertThat(k.IsStale(3, s.Tick)).IsFalse();
        var top = e.Top(3);
        AssertThat(top.Count).IsEqual(3);
        AssertThat(top[0].Holders).IsGreaterEqual(top[2].Holders);
        AssertThat(d.FoodPerDay).IsGreater(0f);
        AssertThat(d.Food).IsEqual(s.Settlements.Span[3].Food);
        AssertThat(k.Get(4)).IsNull(); // only what was asked for
    }

    [TestCase]
    public void Inspector_reads_the_teacher_from_memories()
    {
        var teachers = new Dictionary<string, string>();
        NpcInspector.LearnedFrom(
        [
            new MemoryView(30, "conversation", "Ada taught me stone knapping.", 3),
            new MemoryView(10, "conversation", "Bo showed me herding.", 3),
            new MemoryView(40, "discovery", "I learned writing from the written records.", 3),
            new MemoryView(50, "conversation", "Cy taught me stone knapping.", 3), // later: the first teacher wins
            new MemoryView(5, "speech", "Dee became the leader of Ashford.", 6),
        ], teachers);
        AssertThat(NpcInspector.TeacherOf("stone-knapping", teachers)).IsEqual("Ada");
        AssertThat(NpcInspector.TeacherOf("animal-herding", teachers)).IsEqual("Bo");
        AssertThat(NpcInspector.TeacherOf("writing", teachers)).IsEqual("the records");
        AssertThat(NpcInspector.TeacherOf("pottery", teachers)).IsNull();
    }

    [TestCase]
    public void Overlay_palette_is_stable_and_distinct()
    {
        AssertThat(OverlayColors.Stance(GodStance.Favourable)).IsNotEqual(OverlayColors.Stance(GodStance.Hostile));
        AssertThat(OverlayColors.Stance(GodStance.Fearful)).IsNotEqual(OverlayColors.Stance(GodStance.Neutral));
        AssertThat(OverlayColors.Religion("the Way of Palom")).IsEqual(OverlayColors.Religion("the Way of Palom"));
        AssertThat(OverlayColors.Religion(null)).IsEqual(OverlayColors.NoFaith);
        AssertThat(OverlayColors.Knowledge(1f)).IsEqual(OverlayColors.KnowsAll);
        AssertThat(OverlayColors.Knowledge(0f)).IsEqual(OverlayColors.KnowsNone);
        AssertThat(TerritoryOverlay.DefaultRadius).IsEqual(64f); // fallback: SettlementRadius 40 × TerritoryFactor 1.6
        float fallback = TerritoryOverlay.DefaultRadius;
        AssertThat(TerritoryOverlay.RadiusFor(new SettlementView(0, "a", 0, 0, 1, -1, -1, 0, 0, 0, 0, -1, -1, GodStance.Neutral, null, 10f, 75f), fallback)).IsEqual(75f);
        AssertThat(TerritoryOverlay.RadiusFor(new SettlementView(0, "a", 0, 0, 1, -1, -1, 0, 0, 0, 0, -1, -1, GodStance.Neutral, null), fallback)).IsEqual(64f);
        AssertThat(PowerTargets.IsGround(GodPower.RaiseTerrain)).IsTrue();
        AssertThat(PowerTargets.IsPerson(GodPower.Bless)).IsTrue();
        AssertThat(PowerTargets.IsGround(GodPower.Select)).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Toolbar_glyphs_are_drawn_for_every_m7_icon()
    {
        foreach (var name in UiTheme.M7Glyphs.Keys)
        {
            var tex = UiTheme.Icon(name);
            AssertThat(tex).IsNotNull();
            var img = tex!.GetImage();
            int opaque = 0;
            for (int y = 0; y < img.GetHeight(); y += 2)
            for (int x = 0; x < img.GetWidth(); x += 2)
                if (img.GetPixel(x, y).A > 0.5f) opaque++;
            AssertThat(opaque).OverrideFailureMessage($"glyph {name} is empty").IsGreater(40);
        }
        foreach (var p in GodPowers.M7Powers) AssertThat(UiTheme.M7Glyphs.ContainsKey(p.Icon)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Main_scene_wires_powers_panels_overlay_and_inspector()
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        main.UseFakeSim = true;
        main.FakeNpcCount = 300;
        var runner = ISceneRunner.Load(main, true);
        await runner.SimulateFrames(5);
        var host = (FakeSimHost)main.Host!;
        var ui = main.Hud!.God;
        AssertThat(ui).IsNotNull();
        AssertThat(ui!.Chronicle).IsNotNull();
        AssertThat(ui.Settlement).IsNotNull();
        AssertThat(main.Fx).IsNotNull();
        AssertThat(main.Territory).IsNotNull();

        // Toolbar: the M5 three plus ten M7 powers, each with an icon and a tooltip.
        foreach (var p in GodPowers.M7Powers)
        {
            var b = main.Hud.Powers!.GetNodeOrNull<Button>(p.Power.ToString());
            AssertThat(b).IsNotNull();
            AssertThat(b!.Icon).IsNotNull();
            AssertThat(b.TooltipText.Length).IsGreater(20);
        }

        // Each ground power sends its command and plays a local effect.
        var land = host.Latest.Npcs.Span[0];
        int fx = main.Fx!.Started;
        foreach (var p in new[] { GodPower.SpawnHuman, GodPower.SpawnAnimal, GodPower.SpawnPlant, GodPower.Rain, GodPower.Drought,
                     GodPower.RaiseTerrain, GodPower.LowerTerrain, GodPower.DropFood })
            AssertThat(main.ApplyPower(p, land.X, land.Z)).IsTrue();
        AssertThat(main.Fx.Started).IsEqual(fx + 8);
        main.BlessNpc(land.Id, curse: false);
        AssertThat(main.Fx.Started).IsEqual(fx + 9);
        main.Hud.Powers!.Choose(GodPower.RaiseTerrain);
        main.Hud.Powers.SetBrushRadius(20);
        AssertThat(main.Hud.Powers.BrushRadius).IsEqual(20f);

        // The fake host turns the commands into Chronicle entries; the log and the timeline pick them up.
        // (The fake host publishes only when a tick passes: 20 per second at 1×.)
        for (int i = 0; i < 150 && ui.Log.Count < 9; i++) { await runner.AwaitMillis(20); await runner.SimulateFrames(1); }
        AssertThat(ui.Log.Count).IsGreaterEqual(9);
        ui.ToggleChronicle(true);
        ui.Chronicle!.Rebuild();
        AssertThat(ui.Chronicle.Rows.Count).IsEqual(ui.Log.Count);
        ui.Chronicle.SetMask(1 << (int)ChronicleCategory.God);
        ui.Chronicle.Rebuild();
        AssertThat(ui.Chronicle.Rows.Count).IsGreaterEqual(9);
        foreach (var r in ui.Chronicle.Rows) AssertThat(ChronicleLog.CategoryOf(r.Kind)).IsEqual(ChronicleCategory.God);
        ui.Chronicle.SetMinImportance(10);
        ui.Chronicle.Rebuild();
        AssertThat(ui.Chronicle.Rows.Count).IsEqual(0);

        // A blessing entry focuses on its subject.
        var s = main.Host!.Latest;
        var focus = GodUi.ResolveFocus(new ChronicleEntry(s.Tick, "blessing", "x", 8, [land.Id]), s);
        AssertThat(focus).IsNotNull();
        var named = GodUi.ResolveFocus(new ChronicleEntry(s.Tick, "raid", $"Raiders fell upon {s.Settlements.Span[2].Name}.", 7, []), s);
        AssertThat(named!.Value.X).IsEqual(s.Settlements.Span[2].X);

        // Settlement panel.
        ui.OpenSettlement(land.SettlementId);
        AssertThat(ui.Settlement!.Visible).IsTrue();
        AssertThat(ui.Settlement.View).IsNotNull();
        AssertThat(ui.Settlement.View!.Value.Id).IsEqual(land.SettlementId);
        AssertThat(ui.Settlement.StanceText).Contains("Stance:");
        for (int i = 0; i < 60 && ui.Knowledge.Get(land.SettlementId) is null; i++) await runner.SimulateFrames(1);
        await runner.AwaitMillis(600);
        await runner.SimulateFrames(2);
        AssertThat(ui.Settlement.KnowledgeText).Contains("Knows").Contains("concepts").Contains("written down");
        AssertThat(ui.Settlement.KnowledgeText).NotContains("asked");
        AssertThat(ui.Settlement.FoodText).Contains("in store").Contains("a day");
        AssertThat(ui.Settlement.KnowledgeRows.First()).StartsWith("foraging"); // everyone knows foraging and fire
        AssertThat(ui.Settlement.BodyText).Contains("Buildings").Contains("hut");
        AssertThat(main.SettlementAt(s.Settlements.Span[1].X, s.Settlements.Span[1].Z) ?? -1).IsEqual(s.Settlements.Span[1].Id);

        // Overlay: one disc per settlement, tinted by stance.
        ui.SetOverlay(OverlayMode.Stance);
        main.Territory!.Refresh(main.Host.Latest);
        AssertThat(main.Territory.Visible).IsTrue();
        AssertThat(main.Territory.DiscCount).IsEqual(FakeSimHost.SettlementCount);
        var sv = main.Host.Latest.Settlements.Span[0];
        AssertThat(main.Territory.DrawnRadiusOf(5)).IsEqual(FakeSimHost.FakeTerritoryRadius(5)); // the sim's radius, not SimConfig
        AssertThat(main.Territory.TintOf(sv.Id)).IsEqual(OverlayColors.Stance(sv.Stance));
        AssertThat(ui.LegendLabels).Contains("favourable", "hostile");
        ui.SetOverlay(OverlayMode.Religion);
        main.Territory.Refresh(main.Host.Latest);
        AssertThat(main.Territory.TintOf(sv.Id)).IsEqual(OverlayColors.Religion(sv.ReligionName));
        ui.SetOverlay(OverlayMode.Knowledge, "fire");
        for (int i = 0; i < 20 && FakeSimHost.SettlementCount > Enumerable.Range(0, FakeSimHost.SettlementCount).Count(k => ui.Knowledge.Get(k) is not null); i++)
        {
            await runner.AwaitMillis(100);
            await runner.SimulateFrames(1);
        }
        main.Territory.Refresh(main.Host.Latest);
        AssertThat(main.Territory.Concept).IsEqual("fire");
        AssertThat(main.Territory.CaptionOf(sv.Id)).Contains($"{sv.Members}/{sv.Members} know fire");
        AssertThat(main.Territory.TintOf(sv.Id)).IsEqual(OverlayColors.Knowledge(1f));
        ui.SetOverlay(OverlayMode.Off);
        AssertThat(main.Territory.Visible).IsFalse();

        // Inspector: settlement link, knowledge bars (strongest first), myth marker.
        main.Select(land.Id);
        var inspector = main.Hud.Inspector!;
        AssertThat(inspector.SettlementText).IsEqual($"of {s.Settlements.Span[land.SettlementId].Name}");
        var rows = inspector.KnowledgeRows.ToList();
        AssertThat(rows.Count).IsGreater(1);
        var values = rows.Select(r => double.Parse(r[(r.LastIndexOf(' ') + 1)..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        for (int i = 1; i < values.Count; i++) AssertThat(values[i]).IsLessEqual(values[i - 1]);
        AssertThat(inspector.BodyText).Contains("myth·2");
        AssertThat(inspector.KnowledgeTeachers.ToList()).Contains($"fire: from {FakeWorldSnapshot.NameOf((land.Id + 11) % 300)}");

        // Everything M7 added hides with one switch (for possession).
        ui.Visible = false;
        AssertThat(ui.Chronicle.IsVisibleInTree()).IsFalse();
        AssertThat(ui.Settlement.IsVisibleInTree()).IsFalse();
    }
}
