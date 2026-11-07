using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Eden.Mind.Contracts;

namespace Eden.Mind.Providers;

/// <summary>One model completion: raw text plus token counts.</summary>
public sealed record Completion(string Text, int InputTokens, int OutputTokens);

/// <summary>
/// Shared plumbing of the providers that call an LLM API directly with the player's key: the prompts from
/// <see cref="PromptBuilder"/>, the same one-repair-retry validation as the Worker, USD accounting, and
/// HTTP status → <see cref="BrainFailure"/> mapping. Subclasses implement one completion call.
/// </summary>
public abstract class DirectProvider(HttpClient http, BudgetLedger? ledger) : IBrainProvider
{
    protected HttpClient Http { get; } = http;

    public abstract string Id { get; }
    public abstract string Model { get; }
    public bool SupportsBatching => false;

    protected abstract Task<Completion> CompleteAsync(string system, string user, int maxTokens, JsonObject outputSchema, CancellationToken ct);

    public abstract Task<EmbedResult> EmbedAsync(EmbedRequest request, CancellationToken ct);

    protected abstract JsonObject ThinkSchema { get; }
    protected abstract JsonObject ChatSchema { get; }

    public async Task<ThinkResult> ThinkAsync(ThinkRequest request, CancellationToken ct)
    {
        var (system, user) = PromptBuilder.Think(request);
        var (response, usage) = await CompleteValidated<ThinkResponse>(system, user, 600, ThinkSchema,
            json => IntentionMapper.TryParseThink(json, out var r, out var e) ? (r, IntentionMapper.Check(r)) : (null, e), ct);
        var trimmed = response with
        {
            Plan = response.Plan.Take(request.MaxGoals).ToList(),
            Invention = request.AllowInvention ? response.Invention : null,
        };
        return new ThinkResult(trimmed, usage);
    }

    public async Task<ChatResult> ChatAsync(ChatRequest request, CancellationToken ct)
    {
        var (system, user) = PromptBuilder.Chat(request);
        var (response, usage) = await CompleteValidated<ChatResponse>(system, user, 400, ChatSchema,
            json => IntentionMapper.TryParseChat(json, out var r, out var e) ? (r, IntentionMapper.Check(r)) : (null, e), ct);
        return new ChatResult(response, usage);
    }

    private async Task<(T, BrainUsage)> CompleteValidated<T>(string system, string user, int maxTokens, JsonObject schema,
        Func<string, (T?, string?)> parse, CancellationToken ct) where T : class
    {
        int inTok = 0, outTok = 0;
        string prompt = user;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var c = await CompleteAsync(system, prompt, maxTokens, (JsonObject)schema.DeepClone(), ct);
            inTok += c.InputTokens;
            outTok += c.OutputTokens;
            ledger?.AddUsd(UsdPricing.Estimate(Model, c.InputTokens, c.OutputTokens));
            var json = PromptBuilder.ExtractJson(c.Text);
            string problem;
            if (json is null) problem = "output was not JSON";
            else
            {
                var (value, error) = parse(json);
                if (value is not null && error is null) return (value, new BrainUsage(inTok, outTok, 0, Model));
                problem = error ?? "invalid";
            }
            prompt = $"{user}\n\n{PromptBuilder.Repair(c.Text, problem)}";
        }
        throw new BrainException(BrainFailure.InvalidOutput, $"{Id} {Model} output failed schema after repair retry");
    }

    /// <summary>POSTs JSON and returns the parsed body; every failure becomes a <see cref="BrainException"/>.</summary>
    protected async Task<JsonNode> PostJsonAsync(string url, JsonObject body, IEnumerable<KeyValuePair<string, string>> headers, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
        HttpResponseMessage res;
        try
        {
            res = await Http.SendAsync(req, ct);
        }
        catch (HttpRequestException e)
        {
            throw new BrainException(BrainFailure.Network, $"{Id} unreachable: {e.Message}", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new BrainException(BrainFailure.Network, $"{Id} timed out", e);
        }
        using (res)
        {
            string text = await res.Content.ReadAsStringAsync(ct);
            JsonNode? json = null;
            try { json = JsonNode.Parse(text); } catch (JsonException) { }
            if (!res.IsSuccessStatusCode)
            {
                string detail = UpstreamMessage(json) ?? (text.Length > 200 ? text[..200] : text);
                throw new BrainException(StatusFailure(res.StatusCode), $"{Id} returned HTTP {(int)res.StatusCode}: {detail}");
            }
            return json ?? throw new BrainException(BrainFailure.InvalidOutput, $"{Id} returned a non-JSON body");
        }
    }

    public static BrainFailure StatusFailure(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => BrainFailure.Auth,
        HttpStatusCode.PaymentRequired => BrainFailure.Budget,
        HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => BrainFailure.InvalidOutput,
        HttpStatusCode.NotImplemented or HttpStatusCode.NotFound => BrainFailure.Unsupported,
        _ => BrainFailure.Network, // 429, 5xx: transient, the pool backs off
    };

    private static string? UpstreamMessage(JsonNode? json)
    {
        var err = json?["error"];
        if (err is JsonValue v && v.TryGetValue(out string? s)) return s.Length > 300 ? s[..300] : s;
        return err?["message"]?.GetValue<string>() is { } m ? (m.Length > 300 ? m[..300] : m) : null;
    }

    protected static int AsInt(JsonNode? n)
        => n is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) ? (int)Math.Round(d) : 0;
}
