using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class ConceptGraphTests
{
    private static readonly ConceptGraph Shipped = ConceptGraph.Load();

    private const string Resources = """[{"id":"tree"},{"id":"stone"},{"id":"ore"},{"id":"berry"},{"id":"game"}]""";
    private const string Buildings = """[{"id":"hut"}]""";
    private const string Tools = """[{"id":"stone-knife","name":"Knife","materials":["stone"]}]""";

    private static string Concept(string id, string prereqs, string effects = """{"type":"yieldMod","resource":"berry","factor":1.2}""")
        => $$"""{"id":"{{id}}","name":"{{id}}","description":"d","prerequisites":[{{prereqs}}],"difficulty":0.3,"effects":[{{effects}}],"tags":["survival"]}""";

    private static KnowledgeData Data(params string[] concepts)
        => KnowledgeData.Parse("[" + string.Join(",", concepts) + "]", Resources, Buildings, Tools);

    [Fact]
    public void Shipped_data_loads_without_problems()
    {
        Assert.Empty(Shipped.Problems());
        Assert.InRange(Shipped.Count, 75, 110);
    }

    [Fact]
    public void Graph_has_no_cycles()
    {
        Assert.Null(Shipped.FindCycle());

        var cyclic = ConceptGraph.Unchecked(Data(
            Concept("foraging", ""), Concept("fire", ""),
            Concept("a", "\"foraging\", \"c\""), Concept("b", "\"a\""), Concept("c", "\"b\"")));
        var cycle = cyclic.FindCycle();
        Assert.NotNull(cycle);
        Assert.Equal(cycle![0], cycle[^1]);
        Assert.Throws<InvalidDataException>(() => ConceptGraph.Load(Data(
            Concept("foraging", ""), Concept("fire", ""), Concept("a", "\"b\""), Concept("b", "\"a\""))));
    }

    [Fact]
    public void All_referenced_ids_exist()
    {
        var data = Shipped.Data;
        foreach (var n in Shipped.Nodes)
        {
            foreach (var p in n.Concept.Prerequisites) Assert.True(Shipped.Contains(p), $"{n.Id}: prerequisite {p}");
            foreach (var e in n.Concept.Effects)
            {
                if (e.Kind == ConceptEffectKind.YieldMod) Assert.Contains(e.Resource!, data.Resources);
                if (e.Kind == ConceptEffectKind.UnlockBuilding) Assert.Contains(e.Building!, data.Buildings);
                if (e.Kind == ConceptEffectKind.UnlockTool) Assert.True(Shipped.ToolIndex(e.Tool!) >= 0, $"{n.Id}: tool {e.Tool}");
            }
        }

        var bad = ConceptGraph.Unchecked(Data(Concept("foraging", ""), Concept("fire", ""),
            Concept("a", "\"nope\""), Concept("b", "\"fire\"", """{"type":"unlockTool","tool":"laser"}"""),
            Concept("c", "\"fire\"", """{"type":"unlockBuilding","building":"castle"}""")));
        var problems = bad.Problems();
        Assert.Contains(problems, p => p.Contains("unknown prerequisite nope"));
        Assert.Contains(problems, p => p.Contains("unknown tool laser"));
        Assert.Contains(problems, p => p.Contains("unknown building castle"));
    }

    [Fact]
    public void Every_concept_is_reachable_from_the_starter_set()
    {
        Assert.Empty(Shipped.Unreachable());
        // The starter concepts are the only roots: everything else is earned through prerequisites.
        var roots = Shipped.Nodes.Where(n => n.Prereqs.Length == 0).Select(n => n.Id).OrderBy(x => x).ToArray();
        Assert.Equal(ConceptGraph.StarterSet.OrderBy(x => x).ToArray(), roots);

        var island = ConceptGraph.Unchecked(Data(Concept("foraging", ""), Concept("fire", ""), Concept("moon-magic", "")));
        Assert.Contains("moon-magic", island.Unreachable());
        Assert.Contains(island.Problems(), p => p.Contains("moon-magic"));
    }

    [Fact]
    public void Data_files_list_the_ids_concepts_may_reference()
    {
        var d = Shipped.Data;
        string[] buildings = ["hut", "storehouse", "farm-plot", "well", "kiln", "workshop", "shrine", "archive", "wall", "dock"];
        Assert.Equal(buildings.OrderBy(x => x), d.Buildings.OrderBy(x => x));
        foreach (var kind in Enum.GetValues<ResourceKind>()) Assert.Contains(ConceptGraph.ResourceId(kind), d.Resources);
        Assert.InRange(d.Tools.Count, 1, ConceptGraph.MaxTools);
        foreach (var t in d.Tools)
            foreach (var m in t.Materials) Assert.True(ConceptGraph.TryParseResourceKind(m, out _), $"{t.Id}: material {m}");
    }

    [Fact]
    public void Concepts_cover_the_eight_categories_and_follow_the_authoring_rules()
    {
        string[] categories = ["survival", "food", "craft", "building", "social", "belief", "metal", "writing"];
        foreach (var c in categories) Assert.True(Shipped.Nodes.Count(n => n.Category == c) >= 5, $"too few {c} concepts");
        Assert.All(Shipped.Nodes, n => Assert.Contains(n.Category, categories));
        foreach (var n in Shipped.Nodes)
        {
            Assert.InRange(n.Name.Length, 1, 48);
            Assert.InRange(n.Concept.Prerequisites.Count, 0, 6);
            Assert.InRange(n.Difficulty, 0f, 1f);
            foreach (var e in n.Concept.Effects.Where(e => e.Kind == ConceptEffectKind.Belief)) Assert.InRange(e.Text!.Length, 1, 140);
        }
        Assert.Equal(0.1f, Shipped[Index("fire")].Difficulty);
        Assert.Equal(0.9f, Shipped[Index("writing")].Difficulty);
    }

    [Fact]
    public void Every_concept_can_be_honed_by_an_action_the_sim_performs()
    {
        // Build, farm, trade, raid and settle are M3's; every concept must also be practised by something M1/M4 runs.
        ActionKind[] runnable = [ActionKind.Gather, ActionKind.Hunt, ActionKind.Rest, ActionKind.Talk, ActionKind.Pray, ActionKind.Craft,
            ActionKind.Teach, ActionKind.Learn, ActionKind.Eat, ActionKind.Drink, ActionKind.Give, ActionKind.Court, ActionKind.Explore];
        uint mask = 0;
        foreach (var a in runnable) mask |= ConceptGraph.Bit(a);
        Assert.All(Shipped.Nodes, n => Assert.True((n.HoneMask & mask) != 0, $"{n.Id} cannot be practised"));
    }

    [Fact]
    public void Starter_concepts_unlock_no_gated_action()
    {
        // The first NPCs know these at 0.5; they must not hand out build/craft/raid/invent on day one.
        foreach (var id in ConceptGraph.StarterSet) Assert.Equal(0u, Shipped[Index(id)].ActionMask);
    }

    private static int Index(string id) => Shipped.TryIndex(id, out int i) ? i : throw new KeyNotFoundException(id);
}
