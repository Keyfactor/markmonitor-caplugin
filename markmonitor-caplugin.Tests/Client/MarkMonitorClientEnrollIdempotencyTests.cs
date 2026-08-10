using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

[Collection(LogHandlerFactoryCollection.Name)]
public class MarkMonitorClientEnrollIdempotencyTests
{
    private static MarkMonitorConfig Config() => SampleConfig.Default();

    private static int OrderCreationCount(FakeHttpMessageHandler handler) =>
        handler.Requests.Count(r => FakeHttpMessageHandler.Is(r, "POST", "/certs/v1/order"));

    private static FakeHttpMessageHandler BuildHandler(string orderId = "11111111-1111-1111-1111-111111111111") =>
        new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted, SampleOrders.OrderWithCert(orderId, "CREATED")));

    [Fact]
    public async Task EnrollCertificateAsync_CalledTwiceWithTheSameCsrAndSubject_OnlyCreatesOneOrder()
    {
        // Simulates Command retrying an Enroll call whose first attempt actually succeeded server-
        // side but whose response was lost (timeout, dropped connection).
        var handler = BuildHandler();
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var first = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());
        var second = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        Assert.Equal(1, OrderCreationCount(handler));
        Assert.Equal(first.CARequestID, second.CARequestID);
    }

    [Fact]
    public async Task EnrollCertificateAsync_CalledTwiceWithDifferentCsrs_CreatesTwoOrders()
    {
        var handler = BuildHandler();
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());
        await client.EnrollCertificateAsync(SampleCsr2.Pem, "CN=other.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        Assert.Equal(2, OrderCreationCount(handler));
    }

    [Fact]
    public async Task EnrollCertificateAsync_CalledTwiceWithTheSameCsrAndSubject_DedupeLogIncludesResolvedCARequestID()
    {
        // Regression test for https://github.com/Keyfactor/markmonitor-caplugin/issues/7 - the
        // dedup-hit warning used to be logged before awaiting the in-flight reservation, so it could
        // never report the CARequestID the caller was actually folded into. It must now be logged
        // after the reservation resolves, and must include that CARequestID.
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);

        var handler = BuildHandler();
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var first = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());
        var second = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        Assert.Equal(1, OrderCreationCount(handler));
        Assert.Equal(first.CARequestID, second.CARequestID);

        var dedupeLog = Assert.Single(capturingFactory.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("already submitted", StringComparison.Ordinal));
        Assert.Contains(first.CARequestID, dedupeLog.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnrollCertificateAsync_CalledAgainAfterTheDedupeWindowExpires_CreatesASecondOrder()
    {
        var clock = new ManualTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var handler = BuildHandler();
        var client = handler.BuildClient(clock);
        await client.AuthenticateAsync();

        await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        clock.UtcNow = clock.UtcNow.AddMinutes(6);

        await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        Assert.Equal(2, OrderCreationCount(handler));
    }

    [Fact]
    public async Task EnrollCertificateAsync_RetriedWhileTheFirstCallIsStillInFlight_OnlyCreatesOneOrder()
    {
        // The scenario this cache actually exists for: Command's retry doesn't wait for the first
        // attempt to finish (or fail) - it can arrive while the original CreateCertificateOrder call
        // is still in flight. Holds the order-creation response open with a gate so both calls are
        // genuinely concurrent, rather than sequential.
        var orderResponseGate = new TaskCompletionSource();
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .WhenGated(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"), orderResponseGate.Task,
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("11111111-1111-1111-1111-111111111111", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var firstCall = client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        // Give the first call a chance to actually reach (and dispatch) the gated POST /order request
        // before starting the "retry".
        await WaitUntil(() => OrderCreationCount(handler) == 1);

        var secondCall = client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        orderResponseGate.SetResult();
        var results = await Task.WhenAll(firstCall, secondCall);

        Assert.Equal(1, OrderCreationCount(handler));
        Assert.Equal(results[0].CARequestID, results[1].CARequestID);
    }

    [Fact]
    public async Task EnrollCertificateAsync_RetriedAfterTheWindowNominallyExpiresButWhileStillGenuinelyInFlight_OnlyCreatesOneOrder()
    {
        // A reservation's nominal window (RecentEnrollmentWindow) is stamped when the call starts, not
        // extended while it runs. If the real work takes longer than that window, a retry arriving
        // after the nominal expiry - but while the original call is still genuinely in flight - must
        // still be deduped, not race the still-running original into creating a second real order.
        var clock = new ManualTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var orderResponseGate = new TaskCompletionSource();
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .WhenGated(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"), orderResponseGate.Task,
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("11111111-1111-1111-1111-111111111111", "CREATED")));
        var client = handler.BuildClient(clock);
        await client.AuthenticateAsync();

        var firstCall = client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        await WaitUntil(() => OrderCreationCount(handler) == 1);

        // Move well past the 5-minute nominal window while the first call is still gated/in-flight.
        clock.UtcNow = clock.UtcNow.AddMinutes(6);

        var secondCall = client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        orderResponseGate.SetResult();
        var results = await Task.WhenAll(firstCall, secondCall);

        Assert.Equal(1, OrderCreationCount(handler));
        Assert.Equal(results[0].CARequestID, results[1].CARequestID);
    }

    [Fact]
    public async Task EnrollCertificateAsync_WhenTheFirstAttemptFails_ARetryGetsAFreshAttemptRatherThanTheCachedFailure()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError,
                    """{"errors":[{"code":"request.genericError","message":"An unexpected error occurred."}]}"""),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("11111111-1111-1111-1111-111111111111", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<Exception>(() => client.EnrollCertificateAsync(SampleCsr.Pem,
            "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(), "SslDvGeotrust",
            new Dictionary<string, string>(), Config()));

        var retryResult = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        Assert.NotNull(retryResult);
        Assert.Equal(2, OrderCreationCount(handler));
    }

    [Fact]
    public async Task EnrollCertificateAsync_ManySuccessfulEnrollmentsOverTime_DoesNotAccumulateReservationsUnbounded()
    {
        // Regression test: _recentEnrollments used to have no eviction path for a *successful*
        // enrollment - every unique CSR/subject left a permanent entry (holding the CSR and issued
        // cert chain) for the remaining lifetime of the process. After the fix, a stale completed
        // reservation is pruned the next time EnrollCertificateAsync runs, so the dictionary tracks
        // only "enrollments within the last window", not "enrollments ever performed".
        var clock = new ManualTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var handler = BuildHandler();
        var client = handler.BuildClient(clock);
        await client.AuthenticateAsync();

        await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());
        Assert.Equal(1, client.RecentEnrollmentsCount);

        clock.UtcNow = clock.UtcNow.AddMinutes(6);

        await client.EnrollCertificateAsync(SampleCsr2.Pem, "CN=other.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        // Only the newest reservation should remain - the first, long-expired-and-completed one must
        // have been pruned rather than kept forever.
        Assert.Equal(1, client.RecentEnrollmentsCount);
    }

    [Fact]
    public async Task
        EnrollCertificateAsync_WhenTheFirstAttemptFailsWithAnAmbiguousNetworkError_ARetryWithinTheWindowGetsTheSameFailureInsteadOfCreatingASecondOrder()
    {
        // Regression test: an ambiguous transport-level failure (here, HttpRequestException - the
        // exception HttpClient throws for a dropped connection) used to be treated identically to a
        // definite rejection, evicting the reservation immediately. A Command retry for the same
        // subject/CSR within the window would then find no reservation and create a second, real
        // MarkMonitor order - even though the first order might have actually gone through server-side
        // before the connection dropped. After the fix, the reservation stays active for an ambiguous
        // failure, so the retry is folded into the same failed reservation instead of racing ahead.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .WhenAsync(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated connection reset")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<HttpRequestException>(() => client.EnrollCertificateAsync(SampleCsr.Pem,
            "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(), "SslDvGeotrust",
            new Dictionary<string, string>(), Config()));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.EnrollCertificateAsync(SampleCsr.Pem,
            "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(), "SslDvGeotrust",
            new Dictionary<string, string>(), Config()));

        Assert.Equal(1, OrderCreationCount(handler));
    }

    [Fact]
    public async Task
        EnrollCertificateAsync_WhenAnEarlierStepFailsWithATransientNetworkError_ARetryGetsAFreshAttemptRatherThanBeingLockedOut()
    {
        // Regression test: EnrollCertificateAsync used to classify ANY HttpRequestException/
        // TaskCanceledException between winning the dedup reservation and CreateCertificateOrder
        // completing as "ambiguous" - even one raised by an earlier step (here, organization
        // resolution) that runs strictly before the order-create POST is ever issued and so can never
        // have created an order. That kept the reservation active, so a same-second retry got the same
        // stale exception replayed at it instead of a fresh attempt - a single transient blip during
        // org lookup meant a guaranteed enrollment failure for the rest of the dedupe window, with zero
        // risk of a duplicate order to justify it.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .WhenAsync(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated connection reset")),
                _ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact()))))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("11111111-1111-1111-1111-111111111111", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<HttpRequestException>(() => client.EnrollCertificateAsync(SampleCsr.Pem,
            "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(), "SslDvGeotrust",
            new Dictionary<string, string>(), Config()));

        var retryResult = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        Assert.NotNull(retryResult);
        Assert.Equal(1, OrderCreationCount(handler));
    }

    [Fact]
    public async Task
        EnrollCertificateAsync_WhenMarkMonitorReturnsSuccessWithAnUnparsableBody_ARetryWithinTheWindowDoesNotCreateASecondOrder()
    {
        // Regression test: a 2xx order-create response whose body fails to deserialize used to fall
        // through CreateCertificateOrder's generic catch as an ordinary exception - not an
        // HttpRequestException/TaskCanceledException - so EnrollCertificateAsync's isAmbiguousOutcome
        // check treated it as a *definite* failure and evicted the dedup reservation. But MarkMonitor
        // had already confirmed (2xx) the order was created - stronger evidence than merely ambiguous
        // - so a Command retry finding no reservation would have created a genuine duplicate order.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted, "not valid json"));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<MarkMonitorOrderCreatedButUnparsableException>(() => client.EnrollCertificateAsync(
            SampleCsr.Pem, "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(), "SslDvGeotrust",
            new Dictionary<string, string>(), Config()));

        await Assert.ThrowsAsync<MarkMonitorOrderCreatedButUnparsableException>(() => client.EnrollCertificateAsync(
            SampleCsr.Pem, "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(), "SslDvGeotrust",
            new Dictionary<string, string>(), Config()));

        Assert.Equal(1, OrderCreationCount(handler));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition was never met.");
            await Task.Delay(5);
        }
    }
}
