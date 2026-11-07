using System.Text.Json;
using System.Text.Json.Nodes;
using Eden.Mind;
using Eden.Mind.Contracts;
using Eden.Sim.Contracts;
using Json.Schema;

namespace Eden.Sim.Tests;

/// <summary>The C# side of the contracts must agree with the JSON schemas and the shared fixtures.</summary>
public class ContractTests
{
    private static readonly EvaluationOptions Options = BuildOptions();

    private static EvaluationOptions BuildOptions()
    {
        foreach (var file in Directory.GetFiles(Contracts.Dir, "*.schema.json"))
            try { SchemaRegistry.Global.Register(JsonSchema.FromFile(file)); }
            catch (JsonSchemaException) { } // Mind/PromptContextTests may have registered them first
        return new EvaluationOptions { OutputFormat = OutputFormat.List };
    }

    private static JsonSchema RefTo(string schema, string? def)
        => JsonSchema.FromText($$"""{"$ref": "https://eden.local/contracts/{{schema}}.schema.json{{(def is null ? "" : "#/$defs/" + def)}}"}""");

    private static string Describe(EvaluationResults r)
        => string.Join('\n', (r.Details ?? []).Where(d => d.Errors is { Count: > 0 }).Select(d => d.InstanceLocation + " " + string.Join("; ", d.Errors!.Values)));

    private static string CamelCase(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    [Fact]
    public void ActionKind_matches_schema_enum()
    {
        var node = JsonNode.Parse(File.ReadAllText(Contracts.Schema("common.schema.json")))!;
        var schemaValues = node["$defs"]!["action"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        var csharpValues = Enum.GetNames<ActionKind>().Select(CamelCase).ToArray();
        Assert.Equal(schemaValues, csharpValues);
    }

    [Fact]
    public void ConceptEffectKind_matches_schema_consts()
    {
        var node = JsonNode.Parse(File.ReadAllText(Contracts.Schema("concepts.schema.json")))!;
        var consts = node["$defs"]!["effect"]!["oneOf"]!.AsArray()
            .Select(v => v!["properties"]!["type"]!["const"]!.GetValue<string>()).ToArray();
        var csharpValues = Enum.GetNames<ConceptEffectKind>().Select(CamelCase).ToArray();
        Assert.Equal(consts, csharpValues);
    }

    public static IEnumerable<object[]> Fixtures()
        => Directory.GetFiles(Path.Combine(Contracts.Dir, "fixtures"), "*.json").Order()
            .Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_validates_as_named(string file)
    {
        var parts = Path.GetFileNameWithoutExtension(file).Split('.');
        int verdict = Array.FindIndex(parts, 1, p => p is "ok" or "bad");
        string? def = verdict == 1 ? null : string.Join('.', parts[1..verdict]);
        bool expectOk = parts[verdict] == "ok";

        using var data = JsonDocument.Parse(File.ReadAllText(Contracts.Fixture(file)));
        var result = RefTo(parts[0], def).Evaluate(data.RootElement, Options);

        Assert.True(result.IsValid == expectOk, $"{file}: expected {(expectOk ? "valid" : "invalid")}\n{Describe(result)}");
    }

    [Fact]
    public void Think_response_fixture_deserialises_into_dto()
    {
        var r = JsonSerializer.Deserialize<ThinkResponse>(File.ReadAllText(Contracts.Fixture("think.response.ok.json")), BrainJson.Options)!;
        Assert.Equal(2, r.Plan.Count);
        Assert.Equal(ActionKind.Gather, r.Plan[0].Action);
        Assert.Equal("berries", r.Plan[0].Target);
        Assert.Equal(7, r.Belief!.Importance);
        Assert.Null(r.Invention);
    }

    [Fact]
    public void Think_request_round_trips_through_schema()
    {
        var req = JsonSerializer.Deserialize<ThinkRequest>(File.ReadAllText(Contracts.Fixture("think.request.ok.json")), BrainJson.Options)!;
        var json = JsonSerializer.SerializeToElement(req, BrainJson.Options);
        var result = RefTo("think", "request").Evaluate(json, Options);
        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void Chat_and_embed_fixtures_deserialise()
    {
        var chat = JsonSerializer.Deserialize<ChatRequest>(File.ReadAllText(Contracts.Fixture("chat.request.ok.json")), BrainJson.Options)!;
        Assert.Equal(Speaker.God, chat.Speaker);
        var embed = JsonSerializer.Deserialize<EmbedResponse>(File.ReadAllText(Contracts.Fixture("embed.response.ok.json")), BrainJson.Options)!;
        Assert.Equal(3, embed.Dimensions);
        Assert.Equal(2, embed.Vectors.Count);
    }

    [Fact]
    public void Concept_fixture_deserialises_into_proposal()
    {
        var concepts = JsonSerializer.Deserialize<List<ConceptProposalJson>>(File.ReadAllText(Contracts.Fixture("concepts.ok.json")), BrainJson.Options)!;
        Assert.Equal(3, concepts.Count);
        Assert.Equal("yieldMod", concepts[0].Effects[1].Type);
        Assert.Equal(1.3f, concepts[0].Effects[1].Factor);
    }

    // Raw shape of concepts.json; Eden.Sim's loader (M4) maps it onto ConceptProposal/ConceptEffect.
    private sealed record ConceptProposalJson(string Id, string Name, string Description, List<string> Prerequisites, float Difficulty, List<EffectJson> Effects, List<string>? Tags);
    private sealed record EffectJson(string Type, string? Resource, float? Factor, string? Building, string? Tool, string? Action, string? Text);
}
