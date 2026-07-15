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

    private static FakeHttpMessageHandler BuildHandler(string orderId = "order-1") =>
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
}
