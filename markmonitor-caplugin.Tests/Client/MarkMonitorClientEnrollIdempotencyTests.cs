using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientEnrollIdempotencyTests
{
    private static MarkMonitorConfig Config() => new()
    {
        BaseUrl = "https://api.markmonitor.test",
        ApiKey = "key",
        ApiUsername = "user",
        ApiPassword = "pass",
        OrgName = "Test Org",
        Enabled = true
    };

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
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
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
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());
        await client.EnrollCertificateAsync(SampleCsr2.Pem, "CN=other.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        Assert.Equal(2, OrderCreationCount(handler));
    }

    [Fact]
    public async Task EnrollCertificateAsync_CalledAgainAfterTheDedupeWindowExpires_CreatesASecondOrder()
    {
        var clock = new ManualTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var handler = BuildHandler();
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler,
            clock);
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
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
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
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<Exception>(() => client.EnrollCertificateAsync(SampleCsr.Pem,
            "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(), "SslDvGeotrust",
            new Dictionary<string, string>(), Config()));

        var retryResult = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        Assert.NotNull(retryResult);
        Assert.Equal(2, OrderCreationCount(handler));
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
