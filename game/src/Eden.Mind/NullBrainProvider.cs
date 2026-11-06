using Eden.Mind.Contracts;

namespace Eden.Mind;

/// <summary>Rules-only fallback: thoughts produce no plan, chat and embeddings are unsupported.</summary>
public sealed class NullBrainProvider : IBrainProvider
{
    public static readonly NullBrainProvider Instance = new();
    private static readonly BrainUsage NoUsage = new(0, 0, 0, "none");

    public string Id => "none";
    public bool SupportsBatching => false;

    public Task<ThinkResult> ThinkAsync(ThinkRequest request, CancellationToken ct)
        => Task.FromResult(new ThinkResult(new ThinkResponse([], null, null, null), NoUsage));

    public Task<ChatResult> ChatAsync(ChatRequest request, CancellationToken ct)
        => Task.FromException<ChatResult>(new BrainException(BrainFailure.Unsupported, "no brain provider configured"));

    public Task<EmbedResult> EmbedAsync(EmbedRequest request, CancellationToken ct)
        => Task.FromException<EmbedResult>(new BrainException(BrainFailure.Unsupported, "no brain provider configured"));
}
