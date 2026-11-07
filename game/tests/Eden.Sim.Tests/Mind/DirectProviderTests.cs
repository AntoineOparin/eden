using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Eden.Mind;
using Eden.Mind.Contracts;
using Eden.Mind.Providers;

namespace Eden.Sim.Tests.Mind;

/// <summary>AnthropicProvider and OpenAiCompatibleProvider against stub handlers: the pasted-key path, never a real API.</summary>
public class DirectProviderTests
{
    private static T Fixture<T>(string name) => JsonSerializer.Deserialize<T>(File.ReadAllText(Contracts.Fixture(name)), BrainJson.Options)!;

    private static string AnthropicMessage(string text, int input = 400, int output = 80) => new JsonObject
    {
        ["type"] = "message",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["stop_reason"] = "end_turn",
        ["usage"] = new JsonObject { ["input_tokens"] = input, ["output_tokens"] = output },
    }.ToJsonString();

    [Fact]
    public async Task Anthropic_chat_sends_the_verified_request_shape_and_books_usd()
    {
        var stub = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK,
            AnthropicMessage("""{"reply":"The sky split over the hill.","memory":{"text":"The Voice asked about the hill.","importance":8},"mood":"afraid"}""")));
        var ledger = new BudgetLedger();
        var p = new AnthropicProvider(new HttpClient(stub), "sk-ant-test", ledger: ledger);
        var r = await p.ChatAsync(Fixture<ChatRequest>("chat.request.ok.json"), CancellationToken.None);

        Assert.Equal("The sky split over the hill.", r.Response.Reply);
        Assert.Equal("claude-haiku-5-5", r.Usage.Model);
        Assert.Equal(0, r.Usage.Neurons);
        Assert.Equal((400 * 0.10 + 80 * 0.50) / 1e6, ledger.UsdToday, 12);

        var (req, body) = Assert.Single(stub.Calls);
        Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri!.ToString());
        Assert.Equal("sk-ant-test", req.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2026-10-21", req.Headers.GetValues("anthropic-version").Single());
        var json = JsonNode.Parse(body)!;
        Assert.Null(json["temperature"]); // 2026-10-21 rejects it at any value
        Assert.Equal("disabled", json["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal("json_schema", json["output_config"]!["format"]!["type"]!.GetValue<string>());
        Assert.DoesNotContain("maxLength", json["output_config"]!.ToJsonString());
        Assert.Equal("user", json["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Contains("Why do you fear the hill?", json["messages"]![0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Anthropic_repairs_once_then_gives_up()
    {
        int calls = 0;
        var stub = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK,
            AnthropicMessage(calls++ == 0 ? "Sure! Here you go: not json"
                : "```json\n" + """{"plan":[{"action":"gather","target":"berries","reason":"hungry"}],"say":null,"belief":null}""" + "\n```")));
        var p = new AnthropicProvider(new HttpClient(stub), "k", "claude-sonnet-5-5");
        var r = await p.ThinkAsync(Fixture<ThinkRequest>("think.request.ok.json"), CancellationToken.None);
        Assert.Single(r.Response.Plan);
        Assert.Equal(2, stub.Calls.Count);
        Assert.Contains("Your previous answer was not valid", JsonNode.Parse(stub.Calls[1].Body)!["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal("between_tools", JsonNode.Parse(stub.Calls[0].Body)!["thinking"]!["type"]!.GetValue<string>());

        var never = new AnthropicProvider(new HttpClient(new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK, AnthropicMessage("{\"plan\":[{\"action\":\"fly\"}]}")))), "k");
        Assert.Equal(BrainFailure.InvalidOutput, (await Assert.ThrowsAsync<BrainException>(() => never.ThinkAsync(Fixture<ThinkRequest>("think.request.ok.json"), CancellationToken.None))).Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, BrainFailure.Auth)]
    [InlineData(HttpStatusCode.TooManyRequests, BrainFailure.Network)]
    [InlineData(HttpStatusCode.InternalServerError, BrainFailure.Network)]
    [InlineData(HttpStatusCode.BadRequest, BrainFailure.InvalidOutput)]
    public async Task Anthropic_errors_map_to_brain_failures(HttpStatusCode code, BrainFailure expected)
    {
        var p = new AnthropicProvider(new HttpClient(new StubHandler((_, _) =>
            StubHandler.Json(code, """{"type":"error","error":{"type":"x","message":"nope"}}"""))), "k");
        var e = await Assert.ThrowsAsync<BrainException>(() => p.ChatAsync(Fixture<ChatRequest>("chat.request.ok.json"), CancellationToken.None));
        Assert.Equal(expected, e.Failure);
        Assert.Contains("nope", e.Message);
    }

    [Fact]
    public async Task Anthropic_has_no_embeddings_and_needs_a_key()
    {
        var p = new AnthropicProvider(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException())), "k");
        Assert.Equal(BrainFailure.Unsupported, (await Assert.ThrowsAsync<BrainException>(() => p.EmbedAsync(new EmbedRequest(["x"]), CancellationToken.None))).Failure);
        Assert.Throws<BrainException>(() => new AnthropicProvider(new HttpClient(), ""));
        Assert.IsType<NullBrainProvider>(ProviderFactory.Create(new BrainSettings("anthropic", "", "", null), null));
        Assert.IsType<AnthropicProvider>(ProviderFactory.Create(new BrainSettings("anthropic", "https://api.anthropic.com", "claude-haiku-5-5", "k"), null));
    }

    [Fact]
    public async Task OpenAi_compatible_reasoning_and_plain_models_get_their_own_fields()
    {
        string completion = """{"choices":[{"message":{"content":null,"reasoning_content":"{\"plan\":[]}"}}],"usage":{"prompt_tokens":100,"completion_tokens":20}}""";
        var stub = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK, completion));
        var ledger = new BudgetLedger();
        var gpt = new OpenAiCompatibleProvider(new HttpClient(stub), "https://api.openai.com/v1/", "sk-test", "gpt-5-mini", ledger: ledger);
        await gpt.ThinkAsync(Fixture<ThinkRequest>("think.request.ok.json"), CancellationToken.None);
        var body = JsonNode.Parse(stub.Calls[0].Body)!;
        Assert.Equal("https://api.openai.com/v1/chat/completions", stub.Calls[0].Request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", stub.Calls[0].Request.Headers.GetValues("Authorization").Single());
        Assert.NotNull(body["max_completion_tokens"]);
        Assert.Equal("low", body["reasoning_effort"]!.GetValue<string>());
        Assert.Null(body["temperature"]);
        Assert.True(ledger.UsdToday > 0);

        var local = new OpenAiCompatibleProvider(new HttpClient(stub), "http://localhost:11434/v1", null, "llama3.2");
        await local.ThinkAsync(Fixture<ThinkRequest>("think.request.ok.json"), CancellationToken.None);
        var lbody = JsonNode.Parse(stub.Calls[1].Body)!;
        Assert.Equal(600, lbody["max_tokens"]!.GetValue<int>());
        Assert.NotNull(lbody["temperature"]);
        Assert.False(stub.Calls[1].Request.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task OpenAi_compatible_embeddings_are_ordered_by_index()
    {
        var stub = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK,
            """{"data":[{"index":1,"embedding":[0,1]},{"index":0,"embedding":[1,0]}],"usage":{"prompt_tokens":4}}"""));
        var p = new OpenAiCompatibleProvider(new HttpClient(stub), "https://api.openai.com/v1", "k");
        var r = await p.EmbedAsync(new EmbedRequest(["a", "b"]), CancellationToken.None);
        Assert.Equal([1f, 0f], r.Response.Vectors[0]);
        Assert.Equal(2, r.Response.Dimensions);
    }

    [Fact]
    public void Usd_pricing_matches_the_docs_rows()
    {
        Assert.Equal(0.1 * (0.10 + 0.50), UsdPricing.Estimate("claude-haiku-5-5", 100_000, 100_000), 9);
        Assert.Equal(0.50 * 0.2 + 2.50 * 0.001, UsdPricing.Estimate("claude-haiku-5-5", 200_000, 1_000), 9);
        Assert.Equal(2 + 10, UsdPricing.Estimate("claude-sonnet-5-5", 1_000_000, 1_000_000), 9);
        Assert.Equal(0.25, UsdPricing.Estimate("openai/gpt-5-mini", 1_000_000, 0), 9);
        Assert.Equal(0, UsdPricing.Estimate("llama3.2", 1_000_000, 1_000_000));
    }

    [Fact]
    public void Extract_json_strips_think_blocks_and_fences()
    {
        Assert.Equal("{\"a\":1}", PromptBuilder.ExtractJson("<think>hmm</think>```json\n{\"a\":1}\n```"));
        Assert.Equal("{\"a\":1}", PromptBuilder.ExtractJson("Answer: {\"a\":1} done"));
        Assert.Null(PromptBuilder.ExtractJson("no json here"));
    }
}
