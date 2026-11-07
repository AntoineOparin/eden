using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Eden.Mind.Contracts;

namespace Eden.Mind;

/// <summary>
/// Default provider: calls the Cloudflare Worker in brain/ (docs/specs/30-brain-worker.md).
/// Wire format is the JSON schema contracts; auth is the X-Eden-Key header. Successful calls report Neurons in
/// <c>usage.neurons</c> (the pool books them); this class books the extras the pool cannot see: the
/// <c>X-Eden-Usd-Cost</c> header (Worker routed to a keyed provider) and the Neurons of failed (422) calls.
/// </summary>
public sealed class WorkerProxyProvider : IBrainProvider
{
    public const string NeuronsHeader = "X-Eden-Neurons-Cost";
    public const string UsdHeader = "X-Eden-Usd-Cost";
    public const string ModelHeader = "X-Eden-Model";

    private readonly HttpClient _http;
    private readonly BudgetLedger? _ledger;

    public WorkerProxyProvider(HttpClient http, Uri baseUrl, string sharedKey, BudgetLedger? ledger = null)
    {
        _http = http;
        // Relative request paths keep any path prefix of the base URL.
        _http.BaseAddress = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
        _http.DefaultRequestHeaders.Remove("X-Eden-Key");
        _http.DefaultRequestHeaders.Add("X-Eden-Key", sharedKey);
        _ledger = ledger;
    }

    public string Id => "worker";
    public bool SupportsBatching => false;

    /// <summary>USD reported by the last successful call (0 for Workers AI). Informational; the ledger has the sum.</summary>
    public double LastUsdCost { get; private set; }

    public async Task<ThinkResult> ThinkAsync(ThinkRequest request, CancellationToken ct)
    {
        var r = await PostAsync<ThinkRequest, Envelope<ThinkResponse>>("think", request, ct);
        return new ThinkResult(r.Response, r.Usage);
    }

    public async Task<ChatResult> ChatAsync(ChatRequest request, CancellationToken ct)
    {
        var r = await PostAsync<ChatRequest, Envelope<ChatResponse>>("chat", request, ct);
        return new ChatResult(r.Response, r.Usage);
    }

    public async Task<EmbedResult> EmbedAsync(EmbedRequest request, CancellationToken ct)
    {
        var r = await PostAsync<EmbedRequest, Envelope<EmbedResponse>>("embed", request, ct);
        return new EmbedResult(r.Response, r.Usage);
    }

    private sealed record Envelope<T>(T Response, BrainUsage Usage);

    private static double Header(HttpResponseMessage r, string name)
        => r.Headers.TryGetValues(name, out var values)
           && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
           && double.IsFinite(v) && v > 0 ? v : 0;

    private async Task<TOut> PostAsync<TIn, TOut>(string path, TIn body, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(path, body, BrainJson.Options, ct);
        }
        catch (HttpRequestException e)
        {
            throw new BrainException(BrainFailure.Network, $"worker unreachable: {e.Message}", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new BrainException(BrainFailure.Network, $"worker timed out on /{path}", e);
        }

        using (response)
        {
            double usd = Header(response, UsdHeader);
            _ledger?.AddUsd(usd);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new BrainException(BrainFailure.Auth, "worker rejected the shared key");
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                _ledger?.AddNeurons(Header(response, NeuronsHeader)); // failed calls are billed too
                throw new BrainException(BrainFailure.InvalidOutput, "worker could not get valid output from the model");
            }
            if (response.StatusCode == HttpStatusCode.BadRequest)
                throw new BrainException(BrainFailure.InvalidOutput, $"worker rejected the /{path} request body: {await Snippet(response, ct)}");
            if (response.StatusCode == HttpStatusCode.NotImplemented)
                throw new BrainException(BrainFailure.Unsupported, $"worker does not support /{path}");
            if (!response.IsSuccessStatusCode)
                throw new BrainException(BrainFailure.Network, $"worker returned {(int)response.StatusCode} for /{path}");

            LastUsdCost = usd;
            try
            {
                return await response.Content.ReadFromJsonAsync<TOut>(BrainJson.Options, ct)
                       ?? throw new BrainException(BrainFailure.InvalidOutput, "empty worker response");
            }
            catch (JsonException e)
            {
                throw new BrainException(BrainFailure.InvalidOutput, $"worker response did not match contract: {e.Message}", e);
            }
        }
    }

    private static async Task<string> Snippet(HttpResponseMessage r, CancellationToken ct)
    {
        var s = await r.Content.ReadAsStringAsync(ct);
        return s.Length > 300 ? s[..300] : s;
    }
}
