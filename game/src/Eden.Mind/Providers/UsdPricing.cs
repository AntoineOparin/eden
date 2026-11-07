namespace Eden.Mind.Providers;

/// <summary>
/// USD per million tokens for providers billed to the player's own key. Checked against the live docs on
/// 2026-11-06, not written from memory:
/// https://platform.claude.com/docs/en/about-claude/pricing, "Model pricing":
///   "Claude Haiku 5.5 (for prompts up to 100,000 tokens) | $0.10 / MTok | ... | $0.50 / MTok"
///   "Claude Haiku 5.5 (for prompts over 100,000 tokens)  | $0.50 / MTok | ... | $2.50 / MTok"
///   "Claude Sonnet 5.5 | $2 / MTok | ... | $10 / MTok"
/// OpenAI rows mirror brain/src/providers/pricing.ts (developers.openai.com/api/docs/pricing, 2026-11-06).
/// Unknown models (local Ollama / LM Studio, other vendors) cost 0 here.
/// </summary>
public static class UsdPricing
{
    public sealed record Rate(double Input, double Output, int LongContextThreshold = 0, double LongInput = 0, double LongOutput = 0);

    public static readonly IReadOnlyDictionary<string, Rate> PerMillion = new Dictionary<string, Rate>(StringComparer.Ordinal)
    {
        ["claude-haiku-5-5"] = new(0.10, 0.50, 100_000, 0.50, 2.50),
        ["claude-sonnet-5-5"] = new(2, 10),
        ["gpt-5-mini"] = new(0.25, 2.00),
        ["text-embedding-3-small"] = new(0.02, 0),
    };

    /// <summary>USD for one call; vendor prefixes such as "openai/" are ignored. 0 for unpriced models.</summary>
    public static double Estimate(string model, int inputTokens, int outputTokens)
    {
        string key = model.Contains('/') ? model[(model.LastIndexOf('/') + 1)..] : model;
        if (!PerMillion.TryGetValue(key, out var r)) return 0;
        bool longCtx = r.LongContextThreshold > 0 && inputTokens > r.LongContextThreshold;
        double input = longCtx ? r.LongInput : r.Input, output = longCtx ? r.LongOutput : r.Output;
        return (inputTokens * input + outputTokens * output) / 1_000_000.0;
    }
}
