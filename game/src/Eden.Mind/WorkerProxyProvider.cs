using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Eden.Mind.Contracts;

namespace Eden.Mind;

/// <summary>
/// Default provider: calls the Cloudflare Worker in brain/ (docs/specs/30-brain-worker.md).
/// Wire format is the JSON schema contracts; auth is the X-Eden-Key header.
/// </summary>
public sealed class WorkerProxyProvider : IBrainProvider
{
    private readonly HttpClient _http;

    public WorkerProxyProvider(HttpClient http, Uri baseUrl, string sharedKey)
    {
        _http = http;
        _http.BaseAddress = baseUrl;
        _http.DefaultRequestHeaders.Remove("X-Eden-Key");
        _http.DefaultRequestHeaders.Add("X-Eden-Key", sharedKey);
    }

    public string Id => "worker";
    public bool SupportsBatching => false;

    public async Task<ThinkResult> ThinkAsync(ThinkRequest request, CancellationToken ct)
    {
        var r = await PostAsync<ThinkRequest, Envelope<ThinkResponse>>("/think", request, ct);
        return new ThinkResult(r.Response, r.Usage);
    }

    public async Task<ChatResult> ChatAsync(ChatRequest request, CancellationToken ct)
    {
        var r = await PostAsync<ChatRequest, Envelope<ChatResponse>>("/chat", request, ct);
        return new ChatResult(r.Response, r.Usage);
    }

    public async Task<EmbedResult> EmbedAsync(EmbedRequest request, CancellationToken ct)
    {
        var r = await PostAsync<EmbedRequest, Envelope<EmbedResponse>>("/embed", request, ct);
        return new EmbedResult(r.Response, r.Usage);
    }

    private sealed record Envelope<T>(T Response, BrainUsage Usage);

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

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new BrainException(BrainFailure.Auth, "worker rejected the shared key");
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                throw new BrainException(BrainFailure.InvalidOutput, "worker could not get valid output from the model");
            if (response.StatusCode == HttpStatusCode.NotImplemented)
                throw new BrainException(BrainFailure.Unsupported, $"worker does not support {path}");
            if (!response.IsSuccessStatusCode)
                throw new BrainException(BrainFailure.Network, $"worker returned {(int)response.StatusCode} for {path}");

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
}
