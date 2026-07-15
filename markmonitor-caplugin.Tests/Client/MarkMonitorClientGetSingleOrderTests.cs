using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;
using Keyfactor.PKI.Enums.EJBCA;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientGetSingleOrderTests
{
    [Fact]
    public async Task GetSingleOrderAsync_ForIssuedOrder_ReturnsNonNullResultWithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order/55555555-5555-5555-5555-555555555555"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert("55555555-5555-5555-5555-555555555555", "DIGI_ISSUED")));
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        var result = await client.GetSingleOrderAsync("55555555-5555-5555-5555-555555555555");

        Assert.NotNull(result);
        Assert.Equal("55555555-5555-5555-5555-555555555555", result.CARequestID);
        Assert.Equal((int)EndEntityStatus.GENERATED, result.Status);
        Assert.Null(result.RevocationDate);
    }

    [Fact]
    public async Task GetSingleOrderAsync_ForRevokedOrder_SetsRevocationDateFromDateValidUntil()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order/66666666-6666-6666-6666-666666666666"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert("66666666-6666-6666-6666-666666666666", "DIGI_REVOKED", "REVOKED", "2026-03-01T00:00:00Z")));
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        var result = await client.GetSingleOrderAsync("66666666-6666-6666-6666-666666666666");

        Assert.NotNull(result);
        Assert.Equal((int)EndEntityStatus.REVOKED, result.Status);
        Assert.Equal(new DateTime(2026, 3, 1), result.RevocationDate);
    }
}
