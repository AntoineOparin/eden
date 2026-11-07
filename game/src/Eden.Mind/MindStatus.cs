using Eden.Mind.Contracts;

namespace Eden.Mind;

public enum MindState
{
    /// <summary>Provider "none": rules only.</summary>
    Off,
    Ok,
    /// <summary>The provider failed; background work waits until <see cref="MindStatus.BackoffUntil"/>.</summary>
    Backoff,
    /// <summary>≥ 90 % of the daily budget: only dialogue runs.</summary>
    DialogueOnly,
    /// <summary>The daily budget is spent: nothing runs until local midnight.</summary>
    Exhausted,
}

/// <summary>What the budget meter shows. Immutable; read <see cref="MindPool.Status"/> whenever needed.</summary>
public sealed record MindStatus(
    string ProviderId,
    string Model,
    MindState State,
    double NeuronsToday,
    double NeuronBudget,
    double UsdToday,
    int Thinking,
    int Queued,
    int InFlight,
    long Completed,
    long Failures,
    string? LastError,
    DateTimeOffset? BackoffUntil)
{
    public static MindStatus Idle(BrainSettings s) =>
        new(s.ProviderId, s.Model, MindState.Off, 0, s.DailyNeuronBudget, 0, 0, 0, 0, 0, 0, null, null);
}

/// <summary>Defaults for the settings panel. The Worker URL is the local dev server until a deployed one exists.</summary>
public static class BrainDefaults
{
    public const string LocalWorkerUrl = "http://localhost:8787";
    public const string LocalWorkerKey = "change-me-dev";
    public const string AnthropicUrl = "https://api.anthropic.com";
    public const string AnthropicModel = "claude-haiku-5-5";
    public const string OpenAiUrl = "https://api.openai.com/v1";
    public const string OpenAiModel = "gpt-5-mini";

    public static readonly string[] ProviderIds = ["worker", "anthropic", "openai-compatible", "none"];

    public static BrainSettings Settings => new("worker", LocalWorkerUrl, "", LocalWorkerKey);

    /// <summary>Base URL and model to prefill when the player switches provider.</summary>
    public static (string BaseUrl, string Model) For(string providerId) => providerId switch
    {
        "worker" => (LocalWorkerUrl, ""),
        "anthropic" => (AnthropicUrl, AnthropicModel),
        "openai-compatible" => (OpenAiUrl, OpenAiModel),
        _ => ("", ""),
    };
}
