// Copyright 2026 Keyfactor
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Net;
using System.Net.Http;
using System.Text;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;

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
        public required Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> Responses;
    }

    private readonly object _lock = new();
    private readonly List<Route> _routes = new();
    public List<HttpRequestMessage> Requests { get; } = new();

    public FakeHttpMessageHandler When(Func<HttpRequestMessage, bool> matches, params HttpResponseMessage[] responses)
    {
        return WhenAsync(matches,
            responses.Select(r => (Func<HttpRequestMessage, Task<HttpResponseMessage>>)(_ => Task.FromResult(r)))
                .ToArray());
    }

    /// <summary>Like When(), but the response is produced asynchronously - e.g. via WhenGated() to
    /// hold a response open until a test explicitly releases it, for exercising genuine
    /// concurrency/in-flight-request behavior.</summary>
    public FakeHttpMessageHandler WhenAsync(Func<HttpRequestMessage, bool> matches,
        params Func<HttpRequestMessage, Task<HttpResponseMessage>>[] responseFactories)
    {
        var route = new Route
        {
            Matches = matches,
            Responses = new Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>>(responseFactories)
        };
        lock (_lock)
        {
            _routes.Add(route);
        }
        return this;
    }

    /// <summary>Registers a response that isn't returned until `gate` completes, so a test can hold
    /// a request "in flight" for as long as it needs before releasing the response.</summary>
    public FakeHttpMessageHandler WhenGated(Func<HttpRequestMessage, bool> matches, Task gate,
        HttpResponseMessage response) =>
        WhenAsync(matches, async _ =>
        {
            await gate;
            return response;
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Func<HttpRequestMessage, Task<HttpResponseMessage>> factory;
        lock (_lock)
        {
            Requests.Add(request);
            var route = _routes.LastOrDefault(r => r.Matches(request) && r.Responses.Count > 0)
                        ?? _routes.LastOrDefault(r => r.Matches(request));

            if (route == null)
                throw new InvalidOperationException($"No fake response registered for {request.Method} {request.RequestUri}");

            factory = route.Responses.Count > 1 ? route.Responses.Dequeue() : route.Responses.Peek();
        }

        // A real HttpMessageHandler observes the cancellation token itself - honor it here too, so a
        // test can exercise a caller's cancellation-propagation behavior against a route (e.g. one
        // registered via WhenGated) that would otherwise hang forever.
        var responseTask = factory(request);
        var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);
        var completed = await Task.WhenAny(responseTask, cancellationTask);
        if (completed == cancellationTask)
            throw new TaskCanceledException("The fake request was cancelled.", null, cancellationToken);
        return await responseTask;
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
            Json(HttpStatusCode.OK, $"{{\"token\":\"{token}\",\"expiresIn\":{expiresIn}}}"));
    }

    /// <summary>Builds a MarkMonitorClient wired to this fake handler, using the same
    /// base URL/credentials every test uses since they're never actually sent anywhere real. Retry
    /// backoff delays are instant by default (no test wants a multi-second real sleep just because a
    /// route happens to return a 5xx/429/network failure) - pass `delay` to observe or slow down the
    /// schedule a test actually cares about verifying.</summary>
    public MarkMonitorClient BuildClient(TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new("https://api.markmonitor.test", "key", "user", "pass", true, this, timeProvider,
            delay: delay ?? ((_, _) => Task.CompletedTask));
}
