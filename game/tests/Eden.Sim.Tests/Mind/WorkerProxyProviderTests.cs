using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Eden.Mind;
using Eden.Mind.Contracts;

namespace Eden.Sim.Tests.Mind;

public class WorkerProxyProviderTests
{
    private const string ThinkResult = """
        {"response":{"plan":[{"action":"eat","target":null,"reason":null}],"say":null,"belief":null,"invention":null},
         "usage":{"inputTokens":812,"outputTokens":96,"neurons":6.68,"model":"@cf/google/gemma-4-26b-a4b-it"}}
        """;

    private static (WorkerProxyProvider, StubHandler, BudgetLedger) Make(Func<HttpRequestMessage, string, HttpResponseMessage> respond)
    {
        var stub = new StubHandler(respond);
        var ledger = new BudgetLedger();
        return (new WorkerProxyProvider(new HttpClient(stub), new Uri("http://worker.test"), "change-me-dev", ledger), stub, ledger);
    }

    private static T Fixture<T>(string name) => JsonSerializer.Deserialize<T>(File.ReadAllText(Contracts.Fixture(name)), BrainJson.Options)!;

    [Fact]
    public async Task Think_request_body_matches_fixture_and_auth_header_is_sent()
    {
        var (p, stub, _) = Make((_, _) => StubHandler.Json(HttpStatusCode.OK, ThinkResult, ("X-Eden-Neurons-Cost", "6.68")));
        var result = await p.ThinkAsync(Fixture<ThinkRequest>("think.request.ok.json"), CancellationToken.None);

        var (req, body) = Assert.Single(stub.Calls);
        Assert.Equal("http://worker.test/think", req.RequestUri!.ToString());
        Assert.Equal("change-me-dev", req.Headers.GetValues("X-Eden-Key").Single());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Contracts.Fixture("think.request.ok.json"))), JsonNode.Parse(body)), body);
        Assert.Equal(6.68, result.Usage.Neurons);
        Assert.Equal(Sim.Contracts.ActionKind.Eat, Assert.Single(result.Response.Plan).Action);
    }

    [Fact]
    public async Task Chat_and_embed_bodies_match_fixtures()
    {
        var (p, stub, _) = Make((req, _) => req.RequestUri!.AbsolutePath == "/chat"
            ? StubHandler.Json(HttpStatusCode.OK, "{\"response\":" + File.ReadAllText(Contracts.Fixture("chat.response.ok.json"))
                + ""","usage":{"inputTokens":1,"outputTokens":1,"neurons":0.1,"model":"m"}}""")
            : StubHandler.Json(HttpStatusCode.OK, """{"response":{"model":"@cf/baai/bge-m3","dimensions":2,"vectors":[[1,0],[0,1]]},"usage":{"inputTokens":9,"outputTokens":0,"neurons":0.01,"model":"@cf/baai/bge-m3"}}"""));
        var chat = await p.ChatAsync(Fixture<ChatRequest>("chat.request.ok.json"), CancellationToken.None);
        var embed = await p.EmbedAsync(Fixture<EmbedRequest>("embed.request.ok.json"), CancellationToken.None);

        Assert.Equal("afraid", chat.Response.Mood);
        Assert.Equal(2, embed.Response.Vectors.Count);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Contracts.Fixture("chat.request.ok.json"))), JsonNode.Parse(stub.Calls[0].Body)), stub.Calls[0].Body);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Contracts.Fixture("embed.request.ok.json"))), JsonNode.Parse(stub.Calls[1].Body)), stub.Calls[1].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, BrainFailure.Auth)]
    [InlineData(HttpStatusCode.UnprocessableEntity, BrainFailure.InvalidOutput)]
    [InlineData(HttpStatusCode.BadRequest, BrainFailure.InvalidOutput)]
    [InlineData(HttpStatusCode.NotImplemented, BrainFailure.Unsupported)]
    [InlineData(HttpStatusCode.InternalServerError, BrainFailure.Network)]
    [InlineData(HttpStatusCode.BadGateway, BrainFailure.Network)]
    [InlineData(HttpStatusCode.ServiceUnavailable, BrainFailure.Network)]
    public async Task Error_status_maps_to_brain_failure(HttpStatusCode code, BrainFailure expected)
    {
        var (p, _, _) = Make((_, _) => StubHandler.Json(code, """{"error":{"code":"provider_error","message":"x"}}"""));
        var e = await Assert.ThrowsAsync<BrainException>(() => p.ThinkAsync(Fixture<ThinkRequest>("think.request.ok.json"), CancellationToken.None));
        Assert.Equal(expected, e.Failure);
    }

    [Fact]
    public async Task Unreachable_worker_and_contract_drift_are_brain_failures()
    {
        var (down, _, _) = Make((_, _) => throw new HttpRequestException("connection refused"));
        Assert.Equal(BrainFailure.Network, (await Assert.ThrowsAsync<BrainException>(() => down.EmbedAsync(new EmbedRequest(["x"]), CancellationToken.None))).Failure);

        var (drift, _, _) = Make((_, _) => StubHandler.Json(HttpStatusCode.OK, """{"response":{"plan":[],"surprise":1},"usage":{"inputTokens":1,"outputTokens":1,"neurons":1,"model":"m"}}"""));
        Assert.Equal(BrainFailure.InvalidOutput, (await Assert.ThrowsAsync<BrainException>(() => drift.ThinkAsync(Fixture<ThinkRequest>("think.request.ok.json"), CancellationToken.None))).Failure);
    }

    [Fact]
    public async Task Usd_header_and_failed_call_neurons_reach_the_ledger()
    {
        var (p, _, ledger) = Make((_, _) => StubHandler.Json(HttpStatusCode.OK, ThinkResult, ("X-Eden-Neurons-Cost", "0"), ("X-Eden-Usd-Cost", "0.00012")));
        await p.ThinkAsync(Fixture<ThinkRequest>("think.request.ok.json"), CancellationToken.None);
        Assert.Equal(0.00012, ledger.UsdToday, 9);
        Assert.Equal(0.00012, p.LastUsdCost, 9);
        Assert.Equal(0, ledger.NeuronsToday); // successful Neurons are booked by the pool from usage.neurons

        var (bad, _, ledger2) = Make((_, _) => StubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"error":{"code":"invalid_output","message":"x"}}""", ("X-Eden-Neurons-Cost", "11.5")));
        await Assert.ThrowsAsync<BrainException>(() => bad.ThinkAsync(Fixture<ThinkRequest>("think.request.ok.json"), CancellationToken.None));
        Assert.Equal(11.5, ledger2.NeuronsToday, 6);
    }

    [Fact]
    public void Base_url_with_a_path_keeps_it()
    {
        var stub = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK, ThinkResult));
        var http = new HttpClient(stub);
        _ = new WorkerProxyProvider(http, new Uri("https://example.test/brain"), "k");
        Assert.Equal("https://example.test/brain/think", new Uri(http.BaseAddress!, "think").ToString());
    }
}
