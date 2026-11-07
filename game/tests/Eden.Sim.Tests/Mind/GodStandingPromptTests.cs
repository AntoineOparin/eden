using System.Text.Json;
using Eden.Mind;
using Eden.Sim.Contracts;
using Json.Schema;

namespace Eden.Sim.Tests.Mind;

/// <summary>M7: the god-standing line in the persona and the reflection prompt's call for a belief about the god.</summary>
public class GodStandingPromptTests
{
    private static WorldSnapshot Snapshot(GodStandingView? standing)
    {
        var views = new[] { SpotlightTests.View(0, 1, 1) };
        var extras = new MindExtras(10, 24, [], ["Ashford"], [-1L], []) { GodStandings = [standing] };
        return new WorldSnapshot(1000, 0, 0, 12f, new WorldInfo(1, 4, 4, 2f, 0f, 32), new float[16], new byte[16],
            views, [], [], null, [1], null, extras);
    }

    private static NpcDetail Detail() => new(SpotlightTests.Id(0), "Hamelah", 30,
        new Dictionary<string, float> { ["food"] = 0.8f }, new Dictionary<string, float> { ["piety"] = 0.9f },
        new Dictionary<string, float>(), [], [new MemoryView(900, "god", "The god blessed Hamelah.", 8)], "pray", null);

    private static GodStandingView Standing(float favour, float awe, string stance, float envy = 0, float fear = 0, string? religion = null)
        => new(favour, awe, envy, fear, envy > 0 ? "Oakford" : null, fear > 0 ? "Oakford" : null, stance, religion);

    private static string Persona(GodStandingView? g)
    {
        var snap = Snapshot(g);
        return PromptContext.Npc(Detail(), snap.Npcs.Span[0], snap).Persona;
    }

    [Fact]
    public void Persona_says_how_the_settlement_stands_with_the_sky()
    {
        Assert.Contains("Your people believe the sky favours them.", Persona(Standing(0.7f, 0.2f, "favourable")));
        Assert.Contains("Your people fear the sky.", Persona(Standing(-0.5f, 0.8f, "fearful")));
        Assert.Contains("Your people believe the sky has turned against them.", Persona(Standing(-0.5f, 0.1f, "hostile")));
        Assert.Contains("Your people do not know what the sky wants of them.", Persona(Standing(0f, 0f, "neutral")));
    }

    [Fact]
    public void Persona_names_the_faith_and_the_envied_or_feared_neighbour()
    {
        string p = Persona(Standing(0f, 0.1f, "neutral", envy: 0.5f, religion: "the Way of Kalora"));
        Assert.Contains("They keep the Way of Kalora.", p);
        Assert.Contains("They envy Oakford, whom the sky favours more.", p);
        Assert.Contains("They fear Oakford", Persona(Standing(0f, 0.1f, "neutral", envy: 0.1f, fear: 0.4f)));
        Assert.DoesNotContain("envy", Persona(Standing(0f, 0.1f, "neutral", envy: 0.1f)));
    }

    [Fact]
    public void No_standing_no_line()
    {
        string p = Persona(null);
        Assert.DoesNotContain("sky", p);
        Assert.Null(PromptContext.GodLine(null, Snapshot(Standing(0.7f, 0.2f, "favourable"))));
    }

    [Fact]
    public void Reflection_asks_for_a_belief_about_the_god_when_awe_is_high()
    {
        var low = Snapshot(Standing(0.7f, 0.1f, "favourable"));
        Assert.Equal(PromptContext.ReflectionSituation, PromptContext.ReflectionPrompt(low.Npcs.Span[0], low));
        var high = Snapshot(Standing(0.7f, PromptContext.ReflectAwe, "favourable"));
        string prompt = PromptContext.ReflectionPrompt(high.Npcs.Span[0], high);
        Assert.StartsWith(PromptContext.ReflectionSituation, prompt);
        Assert.Contains("about the sky", prompt);
    }

    [Fact]
    public void Request_with_the_god_line_still_validates()
    {
        foreach (var file in Directory.GetFiles(Contracts.Dir, "*.schema.json"))
            try { SchemaRegistry.Global.Register(JsonSchema.FromFile(file)); } catch (JsonSchemaException) { } catch (ArgumentException) { }
        var snap = Snapshot(Standing(0.7f, 0.9f, "favourable", envy: 0.5f, religion: "the Way of Kalora"));
        var d = Detail();
        var v = snap.Npcs.Span[0];
        var think = new Eden.Mind.Contracts.ThinkRequest(PromptContext.Npc(d, v, snap), PromptContext.Lines(d.Memories, 1000), PromptContext.WorldRules,
            PromptContext.ReflectionPrompt(v, snap), 3, false);
        var json = JsonSerializer.SerializeToElement(think, BrainJson.Options);
        var r = JsonSchema.FromText("""{"$ref": "https://eden.local/contracts/think.schema.json#/$defs/request"}""").Evaluate(json);
        Assert.True(r.IsValid);
    }

    [Fact]
    public void A_blessed_village_reaches_the_prompt_through_the_real_snapshot()
    {
        var (w, x, z) = Villages.World(5);
        int set = Villages.Found(w, x, z);
        for (int i = 0; i < 5; i++) w.Apply(new BlessCommand(w.Settlements.Leader[set], false, 100));
        var snap = w.Snapshot();
        var view = snap.Npcs.ToArray().First(n => n.SettlementId == set);
        Assert.Equal("Your people believe the sky favours them.", PromptContext.GodLine(view, snap));
    }
}
