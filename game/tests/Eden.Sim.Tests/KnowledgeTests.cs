using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class KnowledgeTests
{
    private sealed class RecordingUnlocks : IBuildingUnlocks
    {
        public readonly List<(int who, string building)> Calls = new();
        public void Unlock(int settlementOrNpcId, string buildingId) => Calls.Add((settlementOrNpcId, buildingId));
    }

    [Fact]
    public void First_npcs_know_foraging_and_fire_at_half()
    {
        var w = new World(new SimConfig { Seed = 3 });
        for (int s = 0; s < 2; s++)
        {
            Assert.Equal(0.5f, w.Proficiency(s, "foraging"));
            Assert.Equal(0.5f, w.Proficiency(s, "fire"));
            Assert.Equal(0f, w.Proficiency(s, "pottery"));
        }
        Assert.True(w.Graph.KnownAnywhere("fire"));
        Assert.False(w.Graph.KnownAnywhere("writing"));
        Assert.Equal(2, w.Graph.KnownAnywhereCount());
        Assert.Equal(w.Graph.Count, w.Concepts.Count);
    }

    [Fact]
    public void Inspector_detail_exposes_knowledge()
    {
        var w = new World(new SimConfig { Seed = 3 });
        w.SetKnowledge(0, "pottery", 0.4f);
        Assert.True(w.TryBuildDetail(w.Npcs.IdOf(0), out var d));
        Assert.Equal(0.5f, d.Knowledge["foraging"]);
        Assert.Equal(0.4f, d.Knowledge["pottery"]);
    }

    [Fact]
    public void Honing_has_diminishing_returns()
    {
        float low = KnowledgeRules.Hone(0.2f, 0.01f) - 0.2f, high = KnowledgeRules.Hone(0.9f, 0.01f) - 0.9f;
        Assert.True(low > 0 && high > 0 && low > 20 * high, $"{low} vs {high}");
        Assert.Equal(1f, KnowledgeRules.Hone(1f, 0.5f));

        // Living hones what you practise: gathering and resting raise foraging and fire.
        var w = new World(new SimConfig { Seed = 3, DiscoveryRate = 0 });
        for (int t = 0; t < 3 * w.Config.TicksPerDay; t++) w.Step();
        int alive = Enumerable.Range(0, w.Npcs.HighWater).First(s => w.Npcs.Alive[s]);
        Assert.True(w.Proficiency(alive, "foraging") > 0.5f);
        Assert.True(w.Proficiency(alive, "fire") > 0.5f);
        Assert.True(w.Proficiency(alive, "foraging") < 0.7f);
    }

    [Fact]
    public void Yield_modifiers_scale_with_proficiency_and_tools()
    {
        var w = new World(new SimConfig { Seed = 3 });
        w.SetKnowledge(0, "foraging", 0f);
        Assert.Equal(1f, w.KnowledgeYieldFactor(0, ResourceKind.Berry), 4);
        w.SetKnowledge(0, "foraging", 0.5f);
        Assert.Equal(1.15f, w.KnowledgeYieldFactor(0, ResourceKind.Berry), 4);
        w.SetKnowledge(0, "foraging", 1f);
        w.SetKnowledge(0, "berry-tending", 1f);
        Assert.Equal(1.3f * 1.3f, w.KnowledgeYieldFactor(0, ResourceKind.Berry), 4);
        Assert.True(w.GiveTool(0, "basket"));
        Assert.Equal(1.3f * 1.3f * 1.2f, w.KnowledgeYieldFactor(0, ResourceKind.Berry), 4);
        Assert.Equal(1f, w.KnowledgeYieldFactor(0, ResourceKind.Stone), 4);
        Assert.Equal(1f + 0.3f * 0.5f, EffectApplier.YieldFactor(w, 1, "berry"), 4);
    }

    [Fact]
    public void Unlock_action_adds_it_to_the_candidates_at_the_unlock_level()
    {
        var w = new World(new SimConfig { Seed = 3 });
        Assert.False(w.IsActionUnlocked(0, ActionKind.Teach));
        w.SetKnowledge(0, "teaching", 0.2f);
        Assert.False(w.IsActionUnlocked(0, ActionKind.Teach));
        w.SetKnowledge(0, "teaching", 0.35f);
        Assert.True(w.IsActionUnlocked(0, ActionKind.Teach));
        Assert.True(w.IsActionUnlocked(0, ActionKind.Learn));
        Assert.False(w.IsActionUnlocked(1, ActionKind.Teach));
        int id = w.Npcs.IdOf(0);
        Assert.Null(w.Validator.Reject(new GoalIntention(id, 0, IntentionSource.Llm, ActionKind.Teach, null, null, 5)));
    }

    [Fact]
    public void Locked_knowledge_actions_never_win_even_with_a_goal()
    {
        var w = new World(new SimConfig { Seed = 3 });
        // Validator refuses the goal, and even a bonus planted directly cannot make a locked action run.
        w.Npcs.GoalAction[0] = ActionKind.Craft;
        w.Npcs.GoalBonus[0] = 5f;
        w.Npcs.GoalUntil[0] = long.MaxValue;
        for (int t = 0; t < 200; t++)
        {
            w.Step();
            if (w.Npcs.Alive[0]) Assert.NotEqual(ActionKind.Craft, w.Npcs.Action[0]);
        }
    }

    [Fact]
    public void Unlocked_tools_get_crafted()
    {
        var w = new World(new SimConfig { Seed = 3, DiscoveryRate = 0 });
        w.SetKnowledge(0, "stone-knapping", 0.8f);
        w.Npcs.NeedRef(0, Need.Purpose) = 0f;
        for (int t = 0; t < 4 * w.Config.TicksPerDay && !w.ToolsOf(0).Contains("stone-knife"); t++) w.Step();
        Assert.Contains("stone-knife", w.ToolsOf(0));
    }

    [Fact]
    public void Belief_effect_writes_an_importance_six_belief_memory()
    {
        var w = new World(new SimConfig { Seed = 3 });
        w.SetKnowledge(0, "sky-worship", 0.4f);
        var m = Assert.Single(w.Memories.Of(0), m => m.Kind == MemoryKind.Belief);
        Assert.Equal(6, m.Importance);
        Assert.Equal("The sky watches and judges.", m.Text);
        w.SetKnowledge(0, "sky-worship", 0.9f); // already held: no second memory
        Assert.Single(w.Memories.Of(0), x => x.Kind == MemoryKind.Belief);
    }

    [Fact]
    public void Unlock_building_reaches_the_building_registry()
    {
        var w = new World(new SimConfig { Seed = 3 });
        var unlocks = new RecordingUnlocks();
        w.BuildingUnlocks = unlocks;
        w.SetKnowledge(0, "pottery", 0.5f);
        Assert.Contains((w.Npcs.IdOf(0), "kiln"), unlocks.Calls);
        Assert.True(w.CanBuild(0, "kiln"));
        Assert.False(w.CanBuild(1, "kiln"));
        Assert.False(w.CanBuild(0, "archive"));
    }

    [Fact]
    public void Discovery_needs_every_prerequisite_mastered()
    {
        // A huge rate makes every eligible roll succeed; only eligibility decides.
        var w = new World(new SimConfig { Seed = 3, DiscoveryRate = 0.05f });
        for (int t = 0; t < 120; t++) w.Step();
        for (int s = 0; s < 2; s++)
            Assert.Equal(["fire", "foraging"], w.Graph.Nodes.Where(n => w.Proficiency(s, n.Id) > 0).Select(n => n.Id).Order());

        w.SetKnowledge(0, "foraging", 0.7f);
        for (int t = 0; t < 120; t++) w.Step();
        var learned = w.Graph.Nodes.Where(n => w.Proficiency(0, n.Id) > 0 && !ConceptGraph.StarterSet.Contains(n.Id)).ToList();
        Assert.NotEmpty(learned);
        Assert.All(learned, n => Assert.All(n.Concept.Prerequisites, p => Assert.Equal("foraging", p)));
        var entries = w.ChronicleTail(50);
        Assert.Contains(entries, e => e.Kind == "discovery" && e.Text.Contains(w.Npcs.Name[0]));
    }

    [Fact]
    public void A_two_person_start_discovers_something_within_a_few_years()
    {
        var w = new World(new SimConfig { Seed = 1 });
        for (int t = 0; t < 5 * w.Config.TicksPerYear && w.Population > 0; t++) w.Step();
        Assert.True(w.Graph.KnownAnywhereCount() > 2, "nothing new after five years");
    }

    [Fact]
    public void Save_and_load_round_trip_knowledge_records_tools_and_inventions()
    {
        var a = new World(new SimConfig { Seed = 6 });
        for (int t = 0; t < 3000; t++) a.Step();
        int s = Enumerable.Range(0, a.Npcs.HighWater).First(i => a.Npcs.Alive[i]);
        a.SetKnowledge(s, "pottery", 0.9f);
        a.SetKnowledge(s, "writing", 0.9f);
        a.GiveTool(s, "spear");
        var rec = a.AddRecordSite(a.Npcs.X[s], a.Npcs.Z[s]);
        a.WriteRecord(rec, "pottery", 0.8f);
        Assert.Null(a.Apply(new InventionIntention(a.Npcs.IdOf(s), a.Tick, IntentionSource.Llm,
            new ConceptProposal("glazed-jars", "Glazed jars", "Jars sealed with ash glaze.", ["pottery"], 0.4f,
                [new ConceptEffect(ConceptEffectKind.YieldMod, Resource: "clay", Factor: 1.3f)]))));
        // Everyone can teach, craft and invent, so saves land mid-lesson and mid-craft.
        for (int i = 0; i < a.Npcs.HighWater; i++)
        {
            if (!a.Npcs.Alive[i]) continue;
            a.SetKnowledge(i, "teaching", 0.9f);
            a.SetKnowledge(i, "stone-knapping", 0.8f);
            a.SetKnowledge(i, "tinkering", 0.9f);
        }
        for (int round = 0; round < 3; round++)
        {
            for (int t = 0; t < 37; t++) a.Step();
            using var probe = new MemoryStream();
            SaveWriter.Write(a, probe);
            probe.Position = 0;
            var c = SaveReader.Load(probe);
            Assert.Equal(a.StateHash(), c.StateHash());
            for (int t = 0; t < 400; t++) { a.Step(); c.Step(); }
            Assert.Equal(a.StateHash(), c.StateHash());
        }

        using var ms = new MemoryStream();
        SaveWriter.Write(a, ms);
        ms.Position = 0;
        var b = SaveReader.Load(ms);

        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.True(b.Graph.TryIndex("glazed-jars", out int inv) && b.Graph[inv].IsInvention);
        Assert.Equal(a.Graph.Nodes.Select(n => n.Id), b.Graph.Nodes.Select(n => n.Id));
        Assert.Equal(a.Records.Count, b.Records.Count);
        Assert.Equal(a.Graph.KnownAnywhereCount(), b.Graph.KnownAnywhereCount());
        if (a.Npcs.Alive[s]) Assert.Equal(a.ToolsOf(s), b.ToolsOf(s));

        for (int t = 0; t < 3000; t++) { a.Step(); b.Step(); }
        Assert.Equal(a.StateHash(), b.StateHash());
    }
}
