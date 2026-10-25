// Flight Lab · tests/LunaEvo.FlightLab.Core.Tests/FakeHttp.cs
// [Test] An HttpMessageHandler that serves canned responses by URL and records requests. No real network in tests.
// Used by: WingmanTests, LabTests, UI tests
// Guide:   Meat and Potatoes/01-Architecture.md

using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace LunaEvo.FlightLab.Core.Tests;

internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<(HttpMethod Method, string Url, string Body)> Requests { get; } = new();

    public HttpClient Client => new(this) { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Serves text (JSON) at a URL. Matches on the URL without its query string too.</summary>
    public FakeHttp Text(string url, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[url] = _ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        return this;
    }

    public FakeHttp Bytes(string url, byte[] body)
    {
        _routes[url] = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        return this;
    }

    public FakeHttp Status(string url, HttpStatusCode status)
    {
        _routes[url] = _ => new HttpResponseMessage(status);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((request.Method, url, body));
        if (_routes.TryGetValue(url, out var route) || _routes.TryGetValue(url.Split('?')[0], out route))
            return route(request);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
