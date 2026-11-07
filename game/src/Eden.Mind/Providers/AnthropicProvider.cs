using System.Text.Json.Nodes;
using Eden.Mind.Contracts;

namespace Eden.Mind.Providers;

/// <summary>
/// Direct Anthropic Messages API with the player's key. Mirrors brain/src/providers/anthropic.ts; checked against
/// the live docs on 2026-11-06, not written from memory:
/// - model ids <c>claude-haiku-5-5</c> (default) and <c>claude-sonnet-5-5</c>: platform.claude.com/docs/en/about-claude/models/overview
/// - header <c>anthropic-version: 2026-10-21</c>; that version answers 400 to <c>temperature</c>/<c>top_p</c>/<c>top_k</c>
///   at any value, so none is sent: platform.claude.com/docs/en/api/migration-2026-10-21
/// - thinking is on by default; Haiku 5.5 turns it off with <c>{type:"disabled"}</c>, Sonnet 5.5 rejects that and its
///   lowest setting is <c>{type:"between_tools"}</c>: platform.claude.com/docs/en/build-with-claude/thinking
/// - JSON via <c>output_config.format = {type:"json_schema", schema}</c> without length/number bounds.
/// Anthropic has no embeddings API: <see cref="EmbedAsync"/> fails with Unsupported and retrieval uses word overlap.
/// </summary>
public sealed class AnthropicProvider : DirectProvider
{
    public const string ApiVersion = "2026-10-21";
    public const string DefaultModel = "claude-haiku-5-5";
    public const string DefaultBaseUrl = "https://api.anthropic.com";

    private readonly string _apiKey;
    private readonly string _messagesUrl;

    public AnthropicProvider(HttpClient http, string apiKey, string? model = null, string? baseUrl = null, BudgetLedger? ledger = null)
        : base(http, ledger)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new BrainException(BrainFailure.Auth, "no Anthropic API key set");
        _apiKey = apiKey.Trim();
        Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        _messagesUrl = (string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim()).TrimEnd('/') + "/v1/messages";
    }

    public override string Id => "anthropic";
    public override string Model { get; }
    protected override JsonObject ThinkSchema { get; } = PromptBuilder.ThinkOutputSchema(forAnthropic: true);
    protected override JsonObject ChatSchema { get; } = PromptBuilder.ChatOutputSchema(forAnthropic: true);

    /// <summary>The lowest thinking setting each model accepts, or null to leave the default.</summary>
    public static string? LowestThinking(string model)
        => model.Contains("haiku-5-5", StringComparison.Ordinal) ? "disabled"
            : model.Contains("sonnet-5-5", StringComparison.Ordinal) ? "between_tools" : null;

    /// <summary>The request body, exposed for the stub-handler tests.</summary>
    public JsonObject BuildBody(string system, string user, int maxTokens, JsonObject schema)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = maxTokens,
            ["system"] = system,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = user }),
            ["output_config"] = new JsonObject { ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = schema } },
        };
        if (LowestThinking(Model) is { } t) body["thinking"] = new JsonObject { ["type"] = t };
        return body;
    }

    protected override async Task<Completion> CompleteAsync(string system, string user, int maxTokens, JsonObject outputSchema, CancellationToken ct)
    {
        var raw = await PostJsonAsync(_messagesUrl, BuildBody(system, user, maxTokens, outputSchema),
            [new("x-api-key", _apiKey), new("anthropic-version", ApiVersion)], ct);
        if (raw["stop_reason"]?.GetValue<string>() == "refusal")
            throw new BrainException(BrainFailure.InvalidOutput, $"Anthropic {Model} refused the request");
        var text = string.Concat((raw["content"] as JsonArray ?? [])
            .Where(b => b?["type"]?.GetValue<string>() == "text")
            .Select(b => b?["text"]?.GetValue<string>() ?? ""));
        if (string.IsNullOrWhiteSpace(text))
            throw new BrainException(BrainFailure.InvalidOutput, $"Anthropic {Model} returned no text");
        var u = raw["usage"];
        int input = AsInt(u?["input_tokens"]) + AsInt(u?["cache_creation_input_tokens"]) + AsInt(u?["cache_read_input_tokens"]);
        return new Completion(text, input, AsInt(u?["output_tokens"]));
    }

    public override Task<EmbedResult> EmbedAsync(EmbedRequest request, CancellationToken ct)
        => Task.FromException<EmbedResult>(new BrainException(BrainFailure.Unsupported, "anthropic has no embeddings endpoint"));
}
