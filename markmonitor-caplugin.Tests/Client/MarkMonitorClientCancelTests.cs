using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientCancelTests
{
    [Fact]
    public async Task CancelCertificateAsync_SendsAValidJsonBody()
    {
        // Confirmed against the live MarkMonitor sandbox: PATCH .../cancel with an empty string body
        // (not valid JSON) fails with a vague "Error retrieving order information (order.getError)".
        // The exact same order succeeded immediately when PATCHed with "{}" instead. An empty string
        // is not the same as an empty JSON object as far as MarkMonitor's API is concerned.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", "/certs/v1/order/11111111-1111-1111-1111-111111111111/cancel"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        var result = await client.CancelCertificateAsync("11111111-1111-1111-1111-111111111111");

        Assert.True(result);
        var cancelRequest =
            Assert.Single(handler.Requests, req => FakeHttpMessageHandler.Is(req, "PATCH", "/cancel"));
        var body = await cancelRequest.Content!.ReadAsStringAsync();
        Assert.Equal("{}", body);
    }

    [Fact]
    public async Task RevokeCertificateAsync_SendsAValidJsonBody()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", "/certs/v1/order/11111111-1111-1111-1111-111111111111/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        var result = await client.RevokeCertificateAsync("11111111-1111-1111-1111-111111111111");

        Assert.True(result);
        var revokeRequest =
            Assert.Single(handler.Requests, req => FakeHttpMessageHandler.Is(req, "PATCH", "/revoke"));
        var body = await revokeRequest.Content!.ReadAsStringAsync();
        Assert.Equal("{}", body);
    }
}
