using System.Text.Json;
using Eden.Mind;
using Eden.Sim.Contracts;
using Json.Schema;

namespace Eden.Sim.Tests.Mind;

public class PromptContextTests
{
    private static NpcDetail Detail(params MemoryView[] memories) => new(
        SpotlightTests.Id(0), "Hamelah", 30,
        new Dictionary<string, float> { ["food"] = 0.2f, ["safety"] = 0.9f },
        new Dictionary<string, float> { ["curiosity"] = 0.9f, ["aggression"] = 0.1f, ["piety"] = 0.5f },
        new Dictionary<string, float> { ["fire"] = 0.9f, ["foraging"] = 0.85f, ["Bad Id"] = 1f, ["weaving"] = 0.1f },
        [], memories, "gather berries", null);

    [Fact]
    public void Recent_dramatic_memory_is_on_their_mind_and_old_ones_are_not()
    {
        var snap = SpotlightTests.Snapshot([SpotlightTests.View(0, 1, 1)], tick: 1000, leaders: [SpotlightTests.Id(0)]);
        var fresh = Detail(new MemoryView(995, "god", "Lightning struck from a clear sky.", 9), new MemoryView(990, "gift", "A gift.", 4));
        var npc = PromptContext.Npc(fresh, snap.Npcs.Span[0], snap);
        Assert.Contains("Still shaken by what happened (just now): Lightning struck from a clear sky.", npc.Persona);
        Assert.Contains("the leader of Ashford", npc.Persona);
        Assert.Contains("curious", npc.Persona);
        Assert.Equal(["fire", "foraging"], npc.Knowledge); // ≥ 0.3 and valid identifiers only
        Assert.Equal("in Ashford, where you are the leader", npc.Location);

        var old = Detail(new MemoryView(1000 - 7 * 10, "god", "Lightning struck from a clear sky.", 9));
        Assert.Null(PromptContext.OnTheirMind(old, snap));
    }

    [Fact]
    public void Memory_lines_say_when_and_stay_in_schema_bounds()
    {
        var lines = PromptContext.Lines([new MemoryView(995, "god", new string('x', 400), 12), new MemoryView(0, "birth", "I was born.", 0)], 1000 + 240 * 3, 10, 24);
        Assert.StartsWith("3 days ago: ", lines[0].Text);
        Assert.Equal(280, lines[0].Text.Length);
        Assert.Equal(10, lines[0].Importance);
        Assert.Equal(1, lines[1].Importance);
        Assert.Equal("Just now", PromptContext.When(5, 10, 24));
        Assert.Equal("Yesterday", PromptContext.When(300, 10, 24));
        Assert.Equal("Long ago", PromptContext.When(240 * 40, 10, 24));
    }

    [Fact]
    public void Built_requests_validate_against_the_think_and_chat_schemas()
    {
        foreach (var file in Directory.GetFiles(Contracts.Dir, "*.schema.json"))
            try { SchemaRegistry.Global.Register(JsonSchema.FromFile(file)); } catch (JsonSchemaException) { } catch (ArgumentException) { }
        var snap = SpotlightTests.Snapshot([SpotlightTests.View(0, 1, 1)], tick: 1000);
        var d = Detail(new MemoryView(995, "god", "Lightning struck from a clear sky.", 9));
        var v = snap.Npcs.Span[0];
        var think = new Eden.Mind.Contracts.ThinkRequest(PromptContext.Npc(d, v, snap), PromptContext.Lines(d.Memories, 1000), PromptContext.WorldRules,
            PromptContext.Situation(d, v, snap), 3, PromptContext.AllowInvention(d));
        var chat = new Eden.Mind.Contracts.ChatRequest(PromptContext.Npc(d, v, snap), PromptContext.Lines(d.Memories, 1000), PromptContext.WorldRules,
            Eden.Mind.Contracts.Speaker.God, PromptContext.GodName, [], "How do your people fare?");
        foreach (var (schema, obj) in new (string, object)[] { ("think", think), ("chat", chat) })
        {
            var json = JsonSerializer.SerializeToElement(obj, obj.GetType(), BrainJson.Options);
            var r = JsonSchema.FromText($$"""{"$ref": "https://eden.local/contracts/{{schema}}.schema.json#/$defs/request"}""").Evaluate(json);
            Assert.True(r.IsValid, schema);
        }
        Assert.True(think.AllowInvention);
    }
}
