using Eden.Mind.Contracts;
using Eden.Mind.Providers;

namespace Eden.Mind;

/// <summary>Turns <see cref="BrainSettings"/> into a provider. A misconfigured provider falls back to rules only.</summary>
public static class ProviderFactory
{
    /// <summary>Per-HTTP-call cap; the pool's own 20 s task timeout is the one that normally fires.</summary>
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

    public static IBrainProvider Create(BrainSettings s, BudgetLedger? ledger, Func<HttpClient>? httpFactory = null)
        => TryCreate(s, ledger, httpFactory, out var p, out _) ? p : NullBrainProvider.Instance;

    public static bool TryCreate(BrainSettings s, BudgetLedger? ledger, Func<HttpClient>? httpFactory,
        out IBrainProvider provider, out string? error)
    {
        HttpClient Http() => httpFactory?.Invoke() ?? new HttpClient { Timeout = HttpTimeout };
        error = null;
        try
        {
            switch (s.ProviderId)
            {
                case "worker":
                    if (!Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                        throw new BrainException(BrainFailure.Unsupported, $"bad Worker URL '{s.BaseUrl}'");
                    provider = new WorkerProxyProvider(Http(), uri, s.ApiKey ?? "", ledger);
                    return true;
                case "anthropic":
                    provider = new AnthropicProvider(Http(), s.ApiKey ?? "", s.Model, s.BaseUrl, ledger);
                    return true;
                case "openai-compatible":
                    provider = new OpenAiCompatibleProvider(Http(), s.BaseUrl, s.ApiKey, s.Model, null, ledger);
                    return true;
                case "none":
                    provider = NullBrainProvider.Instance;
                    return true;
                default:
                    throw new BrainException(BrainFailure.Unsupported, $"unknown provider '{s.ProviderId}'");
            }
        }
        catch (BrainException e)
        {
            provider = NullBrainProvider.Instance;
            error = e.Message;
            return false;
        }
    }
}
