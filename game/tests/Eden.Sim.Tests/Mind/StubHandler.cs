using System.Net;
using System.Text;

namespace Eden.Sim.Tests.Mind;

/// <summary>HttpMessageHandler that records requests and answers from a script. Never touches the network.</summary>
public sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        lock (Calls) Calls.Add((request, body));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode code, string json, params (string Name, string Value)[] headers)
    {
        var r = new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        foreach (var (n, v) in headers) r.Headers.TryAddWithoutValidation(n, v);
        return r;
    }
}
