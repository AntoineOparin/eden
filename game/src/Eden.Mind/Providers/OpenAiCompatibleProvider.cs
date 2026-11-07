using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Eden.Mind.Contracts;

namespace Eden.Mind.Providers;

/// <summary>
/// Any server with <c>/chat/completions</c> and <c>/embeddings</c> (OpenAI, Groq, OpenRouter, Ollama, LM Studio).
/// Mirrors brain/src/providers/openai-compatible.ts. Base URL is the API root including the version segment,
/// e.g. https://api.openai.com/v1 or http://localhost:11434/v1; the key may be empty for local servers.
/// OpenAI reasoning models (o-series, GPT-5) get <c>max_completion_tokens</c> + <c>reasoning_effort</c>, everything
/// else <c>max_tokens</c> + <c>temperature</c> (the heuristic carried over in 90-backlog.md).
/// </summary>
public sealed partial class OpenAiCompatibleProvider : DirectProvider
{
    public const string DefaultModel = "gpt-5-mini";
    public const string DefaultEmbedModel = "text-embedding-3-small";
    public const int ReasoningHeadroomTokens = 2000;

    private readonly string _root;
    private readonly string? _apiKey;
    private readonly BudgetLedger? _ledger;

    public OpenAiCompatibleProvider(HttpClient http, string baseUrl, string? apiKey, string? model = null,
        string? embedModel = null, BudgetLedger? ledger = null) : base(http, ledger)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || !Regex.IsMatch(baseUrl.Trim(), "^https?://", RegexOptions.IgnoreCase))
            throw new BrainException(BrainFailure.Unsupported, "openai-compatible needs a base URL starting with http(s)://");
        _root = baseUrl.Trim().TrimEnd('/');
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        EmbedModel = string.IsNullOrWhiteSpace(embedModel) ? DefaultEmbedModel : embedModel.Trim();
        _ledger = ledger;
    }

    public override string Id => "openai-compatible";
    public override string Model { get; }
    public string EmbedModel { get; }
    protected override JsonObject ThinkSchema { get; } = PromptBuilder.ThinkOutputSchema(forAnthropic: false);
    protected override JsonObject ChatSchema { get; } = PromptBuilder.ChatOutputSchema(forAnthropic: false);

    [GeneratedRegex(@"(^|/)(o\d|gpt-5)", RegexOptions.IgnoreCase)]
    private static partial Regex ReasoningModel();

    public static bool IsReasoningModel(string model) => ReasoningModel().IsMatch(model);

    private IEnumerable<KeyValuePair<string, string>> Headers()
        => _apiKey is null ? [] : [new("Authorization", $"Bearer {_apiKey}")];

    public JsonObject BuildBody(string system, string user, int maxTokens, JsonObject schema, float temperature = 0.7f)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "eden_output", ["schema"] = schema, ["strict"] = false },
            },
        };
        if (IsReasoningModel(Model))
        {
            body["max_completion_tokens"] = maxTokens + ReasoningHeadroomTokens;
            body["reasoning_effort"] = "low";
        }
        else
        {
            body["max_tokens"] = maxTokens;
            body["temperature"] = temperature;
        }
        return body;
    }

    protected override async Task<Completion> CompleteAsync(string system, string user, int maxTokens, JsonObject outputSchema, CancellationToken ct)
    {
        var raw = await PostJsonAsync($"{_root}/chat/completions", BuildBody(system, user, maxTokens, outputSchema), Headers(), ct);
        var msg = raw["choices"]?[0]?["message"];
        string? text = null;
        foreach (var key in new[] { "content", "reasoning_content", "reasoning" })
            if (msg?[key] is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s)) { text = s; break; }
        if (text is null && raw["response"] is JsonValue r && r.TryGetValue(out string? legacy)) text = legacy;
        if (string.IsNullOrWhiteSpace(text))
            throw new BrainException(BrainFailure.InvalidOutput, $"openai-compatible {Model} returned no content");
        return new Completion(text, AsInt(raw["usage"]?["prompt_tokens"]), AsInt(raw["usage"]?["completion_tokens"]));
    }

    public override async Task<EmbedResult> EmbedAsync(EmbedRequest request, CancellationToken ct)
    {
        var body = new JsonObject { ["model"] = EmbedModel, ["input"] = new JsonArray(request.Texts.Select(t => (JsonNode)t).ToArray()) };
        var raw = await PostJsonAsync($"{_root}/embeddings", body, Headers(), ct);
        var data = raw["data"] as JsonArray ?? [];
        var vectors = data.Select((d, i) => (At: d?["index"] is JsonValue iv && iv.TryGetValue(out int ix) ? ix : i,
                V: (d?["embedding"] as JsonArray)?.Select(x => x?.GetValue<float>() ?? 0f).ToArray()))
            .OrderBy(x => x.At).Select(x => x.V).ToList();
        if (vectors.Count != request.Texts.Count || vectors.Any(v => v is null || v.Length == 0))
            throw new BrainException(BrainFailure.InvalidOutput, $"openai-compatible {EmbedModel} returned an unexpected shape");
        int tokens = AsInt(raw["usage"]?["prompt_tokens"]);
        if (tokens == 0) tokens = (int)Math.Ceiling(request.Texts.Sum(t => t.Length) / 4.0);
        _ledger?.AddUsd(UsdPricing.Estimate(EmbedModel, tokens, 0));
        return new EmbedResult(new EmbedResponse(EmbedModel, vectors[0]!.Length, vectors!), new BrainUsage(tokens, 0, 0, EmbedModel));
    }
}
