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
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

/// <summary>
/// Regression tests for GitHub issue #8: FetchOrderAsync (the private helper behind
/// GetSingleOrderAsync/RevokeCertificateAsync) used to re-set the shared HttpClient's
/// Authorization header itself, directly from _bearerToken and with no locking - bypassing the
/// _authLock discipline that EnsureAuthenticatedAsync/AuthenticateAsync rely on to keep concurrent
/// callers from racing writes to that shared header. The fix removes the redundant, unguarded
/// re-set and relies solely on EnsureAuthenticatedAsync (called immediately above it) to have
/// already established the header under the lock.
/// </summary>
public class MarkMonitorClientFetchOrderAuthTests
{
    private const string OrderId = "99999999-9999-9999-9999-999999999999";

    private static int AuthCallCount(FakeHttpMessageHandler handler) =>
        handler.Requests.Count(r => FakeHttpMessageHandler.Is(r, "POST", "/auth/v1/auth/authenticate"));

    private static IEnumerable<HttpRequestMessage> OrderRequests(FakeHttpMessageHandler handler) =>
        handler.Requests.Where(r => FakeHttpMessageHandler.Is(r, "GET", $"/certs/v1/order/{OrderId}"));

    [Fact]
    public async Task GetSingleOrderAsync_SendsTheCurrentBearerTokenOnTheOrderRequest()
    {
        // Simple regression check: with the manual (unguarded) header re-set removed from
        // FetchOrderAsync, the request must still carry the Authorization header that
        // EnsureAuthenticatedAsync/AuthenticateAsync established under the lock.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth(token: "regression-token")
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{OrderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(OrderId, "DIGI_ISSUED")));
        var client = handler.BuildClient();

        await client.GetSingleOrderAsync(OrderId);

        var orderRequest = Assert.Single(OrderRequests(handler));
        Assert.NotNull(orderRequest.Headers.Authorization);
        Assert.Equal("Bearer", orderRequest.Headers.Authorization!.Scheme);
        Assert.Equal("regression-token", orderRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetSingleOrderAsync_CalledConcurrentlyWithAnExpiredToken_OnlyAuthenticatesOnceAndSendsTheRefreshedTokenOnBothRequests()
    {
        // Before the fix, FetchOrderAsync set the shared HttpClient's Authorization header itself,
        // unguarded by _authLock, immediately after calling EnsureAuthenticatedAsync. Two concurrent
        // FetchOrderAsync calls racing a concurrent re-authentication could interleave writes to
        // that shared header. Gate the auth response so both calls are genuinely in flight together
        // (mirrors EnsureAuthenticatedAsync_CalledConcurrentlyWithAnExpiredToken_OnlyAuthenticatesOnce
        // in MarkMonitorClientAuthLifecycleTests, applied to the FetchOrderAsync code path).
        var clock = new ManualTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var authGate = new TaskCompletionSource();
        var handler = new FakeHttpMessageHandler()
            .WhenGated(req => FakeHttpMessageHandler.Is(req, "POST", "/auth/v1/auth/authenticate"), authGate.Task,
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"token":"refreshed-token","expiresIn":3600}"""))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{OrderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(OrderId, "DIGI_ISSUED")));
        var client = handler.BuildClient(clock);

        // No prior AuthenticateAsync call, so both concurrent calls see an expired/missing token
        // and race into EnsureAuthenticatedAsync at the same time.
        var firstCall = client.GetSingleOrderAsync(OrderId);
        var secondCall = client.GetSingleOrderAsync(OrderId);
        authGate.SetResult();
        await Task.WhenAll(firstCall, secondCall);

        Assert.Equal(1, AuthCallCount(handler));

        var orderRequests = OrderRequests(handler).ToList();
        Assert.Equal(2, orderRequests.Count);
        Assert.All(orderRequests, req =>
        {
            Assert.NotNull(req.Headers.Authorization);
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("refreshed-token", req.Headers.Authorization!.Parameter);
        });
    }
}
