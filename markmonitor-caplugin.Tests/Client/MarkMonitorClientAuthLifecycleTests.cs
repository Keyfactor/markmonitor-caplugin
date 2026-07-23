using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;
using Keyfactor.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientAuthLifecycleTests
{
    private static int AuthCallCount(FakeHttpMessageHandler handler) =>
        handler.Requests.Count(r => FakeHttpMessageHandler.Is(r, "POST", "/auth/v1/auth/authenticate"));

    [Fact]
    public async Task ListOrganizationsAsync_WithoutAnyPriorAuthenticateCall_LazilyAuthenticatesRatherThanThrowing()
    {
        // Before the fix, EnsureAuthenticated() called AuthenticateAsync().RunSynchronously(), which
        // throws InvalidOperationException on any Task returned from an async method. Every existing
        // call site happened to pre-await AuthenticateAsync() so this never surfaced - calling a method
        // WITHOUT authenticating first is exactly the case that used to blow up.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
        var client = handler.BuildClient();

        var orgs = await client.ListOrganizationsAsync();

        Assert.NotNull(orgs);
        Assert.Single(orgs);
        Assert.Equal(1, AuthCallCount(handler));
    }

    [Fact]
    public async Task EnsureAuthenticatedAsync_WhenTokenHasExpired_ReAuthenticatesBeforeTheNextCall()
    {
        var clock = new ManualTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth(expiresIn: 60)
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
        var client = handler.BuildClient(clock);

        await client.AuthenticateAsync();
        Assert.Equal(1, AuthCallCount(handler));

        // Token is valid for 60s minus a 30s safety buffer = 30s. Move well past that.
        clock.UtcNow = clock.UtcNow.AddSeconds(31);

        await client.ListOrganizationsAsync();

        Assert.Equal(2, AuthCallCount(handler));
    }

    [Fact]
    public async Task EnsureAuthenticatedAsync_WhenTokenIsStillValid_DoesNotReAuthenticate()
    {
        var clock = new ManualTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth(expiresIn: 3600)
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
        var client = handler.BuildClient(clock);

        await client.AuthenticateAsync();
        clock.UtcNow = clock.UtcNow.AddSeconds(5);
        await client.ListOrganizationsAsync();

        Assert.Equal(1, AuthCallCount(handler));
    }

    [Fact]
    public async Task EnsureAuthenticatedAsync_CalledConcurrentlyWithAnExpiredToken_OnlyAuthenticatesOnce()
    {
        // Without a lock, two callers can both see the expired token, both call AuthenticateAsync
        // concurrently, and race writing _bearerToken/_tokenExpiresAtUtc and the shared HttpClient's
        // Authorization header. Gate the auth response so both calls are genuinely in flight together
        // rather than sequential.
        var clock = new ManualTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var authGate = new TaskCompletionSource();
        var handler = new FakeHttpMessageHandler()
            .WhenGated(req => FakeHttpMessageHandler.Is(req, "POST", "/auth/v1/auth/authenticate"), authGate.Task,
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"token":"fake-token","expiresIn":3600}"""))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
        var client = handler.BuildClient(clock);

        var firstCall = client.ListOrganizationsAsync();
        var secondCall = client.ListOrganizationsAsync();
        authGate.SetResult();
        await Task.WhenAll(firstCall, secondCall);

        Assert.Equal(1, AuthCallCount(handler));
    }

    [Fact]
    public async Task EnsureAuthenticatedAsync_WaiterFindsTokenAlreadyRefreshed_LogsThatItReusedIt()
    {
        // The caller that wins _authLock and re-authenticates logs about it, but the caller that was
        // blocked on the lock previously returned silently once it acquired the lock and found
        // TokenNeedsRefresh() false - no signal that it reused a token a concurrent caller just
        // refreshed. Assert the waiter now logs that too.
        var capturingFactory = new CapturingLoggerFactory();
        LogHandler.Factory = capturingFactory;
        try
        {
            var clock = new ManualTimeProvider { UtcNow = DateTimeOffset.UtcNow };
            var authGate = new TaskCompletionSource();
            var handler = new FakeHttpMessageHandler()
                .WhenGated(req => FakeHttpMessageHandler.Is(req, "POST", "/auth/v1/auth/authenticate"), authGate.Task,
                    FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"token":"fake-token","expiresIn":3600}"""))
                .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                    FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                        SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
            var client = handler.BuildClient(clock);

            // The first call wins the lock and blocks on the gated auth response; the second call
            // blocks on _authLock.WaitAsync() until the first releases it, then must find the token
            // already refreshed.
            var firstCall = client.ListOrganizationsAsync();
            var secondCall = client.ListOrganizationsAsync();
            authGate.SetResult();
            await Task.WhenAll(firstCall, secondCall);

            Assert.Equal(1, AuthCallCount(handler));
            Assert.Contains(capturingFactory.Messages,
                m => m.Contains("refreshed by a concurrent caller", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            LogHandler.Factory = new NullLoggerFactory();
        }
    }
}
