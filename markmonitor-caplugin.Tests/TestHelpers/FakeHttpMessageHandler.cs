using System.Net;
using System.Net.Http;
using System.Text;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

/// <summary>
/// A minimal routable fake HttpMessageHandler for exercising MarkMonitorClient against canned
/// responses instead of the live MarkMonitor API. Register routes with When(), most-specific first;
/// each route's responses are consumed in order and the last one registered repeats indefinitely.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private sealed class Route
    {
        public required Func<HttpRequestMessage, bool> Matches;
        public required Queue<Func<HttpRequestMessage, HttpResponseMessage>> Responses;
    }

    private readonly List<Route> _routes = new();
    public List<HttpRequestMessage> Requests { get; } = new();

    public FakeHttpMessageHandler When(Func<HttpRequestMessage, bool> matches, params HttpResponseMessage[] responses)
    {
        _routes.Add(new Route
        {
            Matches = matches,
            Responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(
                responses.Select(r => (Func<HttpRequestMessage, HttpResponseMessage>)(_ => r)))
        });
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var route = _routes.LastOrDefault(r => r.Matches(request) && r.Responses.Count > 0)
                    ?? _routes.LastOrDefault(r => r.Matches(request));

        if (route == null)
            throw new InvalidOperationException($"No fake response registered for {request.Method} {request.RequestUri}");

        var factory = route.Responses.Count > 1 ? route.Responses.Dequeue() : route.Responses.Peek();
        return Task.FromResult(factory(request));
    }

    public static bool Is(HttpRequestMessage req, string method, string pathFragment) =>
        req.Method.Method.Equals(method, StringComparison.OrdinalIgnoreCase) &&
        (req.RequestUri?.ToString().Contains(pathFragment, StringComparison.OrdinalIgnoreCase) ?? false);

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Wires up a successful /auth/v1/auth/authenticate response so callers can focus tests
    /// on the endpoint(s) they actually care about.</summary>
    public FakeHttpMessageHandler WithSuccessfulAuth(string token = "fake-token", int expiresIn = 3600)
    {
        return When(req => Is(req, "POST", "/auth/v1/auth/authenticate"),
            Json(HttpStatusCode.OK, $"{{\"token\":\"{token}\",\"expires_in\":{expiresIn}}}"));
    }
}
