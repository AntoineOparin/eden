using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class InventionTests
{
    private static World Inventor(out int id)
    {
        var w = new World(new SimConfig { Seed = 8 });
        w.SetKnowledge(0, "pottery", 0.85f);
        id = w.Npcs.IdOf(0);
        return w;
    }

    private static InventionIntention Invent(int id, string conceptId = "glazed-jars", IReadOnlyList<string>? prereqs = null, params ConceptEffect[] effects)
        => new(id, 0, IntentionSource.Llm, new ConceptProposal(conceptId, "Glazed jars", "Jars sealed with an ash glaze keep grain dry.",
            prereqs ?? ["pottery"], 0.4f, effects.Length > 0 ? effects : [new ConceptEffect(ConceptEffectKind.YieldMod, Resource: "grain", Factor: 1.3f)]));

    [Fact]
    public void Accepted_invention_enters_the_live_graph()
    {
        var w = Inventor(out int id);
        int before = w.Graph.Count;
        Assert.Null(w.Apply(Invent(id, effects: [
            new ConceptEffect(ConceptEffectKind.YieldMod, Resource: "grain", Factor: 1.3f),
            new ConceptEffect(ConceptEffectKind.UnlockAction, Action: ActionKind.Craft)])));
        Assert.Equal(before + 1, w.Graph.Count);
        Assert.True(w.Graph.TryIndex("glazed-jars", out int i));
        Assert.True(w.Graph[i].IsInvention);
        Assert.Equal(["pottery"], w.Graph[i].Prereqs.Select(p => w.Graph[p].Id));
        Assert.True(w.Graph.KnownAnywhere("glazed-jars"));
        Assert.Equal(0.3f, w.Proficiency(0, "glazed-jars"));
        Assert.True(w.IsActionUnlocked(0, ActionKind.Craft));
        Assert.True(w.Concepts.ContainsKey("glazed-jars"));
        Assert.Contains(w.ChronicleTail(5), e => e.Kind == "invention" && e.Text.Contains("Glazed jars"));
        // Effects work like any shipped concept: grain yield scales with the inventor's proficiency.
        Assert.Equal(1f + 0.3f * 0.3f, EffectApplier.YieldFactor(w, 0, "grain"), 4);
        Assert.Equal("concept already exists", w.Validator.Reject(Invent(w.Npcs.IdOf(1))));
    }

    [Theory]
    [InlineData(0.79f, "prerequisite pottery not mastered")]
    [InlineData(0.8f, null)]
    public void Prerequisites_must_be_known_at_point_eight(float proficiency, string? expected)
    {
        var w = Inventor(out int id);
        w.SetKnowledge(0, "pottery", proficiency);
        Assert.Equal(expected, w.Validator.Reject(Invent(id)));
    }

    [Fact]
    public void Inventions_obey_the_stricter_rules()
    {
        var w = Inventor(out int id);
        var y = new ConceptEffect(ConceptEffectKind.YieldMod, Resource: "grain", Factor: 1.2f);
        Assert.Equal("inventions may have at most 3 effects", w.Validator.Reject(Invent(id, effects: [y, y, y, y])));
        Assert.Equal("invention yield factor above 1.5", w.Validator.Reject(Invent(id,
            effects: new ConceptEffect(ConceptEffectKind.YieldMod, Resource: "grain", Factor: 2f))));
        Assert.Null(w.Validator.Reject(Invent(id, effects: new ConceptEffect(ConceptEffectKind.YieldMod, Resource: "grain", Factor: 1.5f))));
        Assert.Equal("invention belief longer than 140 characters", w.Validator.Reject(Invent(id,
            effects: new ConceptEffect(ConceptEffectKind.Belief, Text: new string('a', 141)))));
        Assert.Null(w.Validator.Reject(Invent(id, effects: new ConceptEffect(ConceptEffectKind.Belief, Text: new string('a', 140)))));
        Assert.Equal("unknown tool laser", w.Validator.Reject(Invent(id, effects: new ConceptEffect(ConceptEffectKind.UnlockTool, Tool: "laser"))));
        Assert.Equal("unknown building castle", w.Validator.Reject(Invent(id, effects: new ConceptEffect(ConceptEffectKind.UnlockBuilding, Building: "castle"))));
        Assert.Equal("unknown resource unobtainium", w.Validator.Reject(Invent(id,
            effects: new ConceptEffect(ConceptEffectKind.YieldMod, Resource: "unobtainium", Factor: 1.2f))));
        Assert.Equal("concept already exists", w.Validator.Reject(Invent(id, "pottery")));
        Assert.Equal("unknown prerequisite alchemy", w.Validator.Reject(Invent(id, prereqs: ["alchemy"])));
    }

    [Fact]
    public void Inventions_are_saved_with_the_world_and_never_written_to_concepts_json()
    {
        var w = Inventor(out int id);
        Assert.Null(w.Apply(Invent(id)));
        for (int t = 0; t < 200; t++) w.Step();

        using var ms = new MemoryStream();
        SaveWriter.Write(w, ms);
        ms.Position = 0;
        var loaded = SaveReader.Load(ms);
        Assert.Equal(w.StateHash(), loaded.StateHash());
        Assert.True(loaded.Graph.TryIndex("glazed-jars", out int i) && loaded.Graph[i].IsInvention);

        Assert.DoesNotContain(KnowledgeData.Default.Concepts, c => c.Concept.Id == "glazed-jars");
        Assert.False(new World(new SimConfig { Seed = 8 }).Graph.Contains("glazed-jars"));
        var file = FindDataFile("concepts.json");
        Assert.DoesNotContain("glazed-jars", File.ReadAllText(file));
    }

    [Fact]
    public void An_invention_dies_with_its_inventor_like_any_knowledge()
    {
        var w = Inventor(out int id);
        Assert.Null(w.Apply(Invent(id)));
        w.Kill(0, DeathCause.Accident);
        Assert.False(w.Graph.KnownAnywhere("glazed-jars"));
        Assert.True(w.Graph.Contains("glazed-jars")); // the graph remembers it can exist; the world forgot it
    }

    private static string FindDataFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string p = Path.Combine(dir.FullName, "game", "data", name);
            if (File.Exists(p)) return p;
            p = Path.Combine(dir.FullName, "data", name);
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException(name);
    }
}
