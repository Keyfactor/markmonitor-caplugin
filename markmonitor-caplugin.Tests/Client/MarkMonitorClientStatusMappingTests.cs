using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;
using Keyfactor.PKI.Enums.EJBCA;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientStatusMappingTests
{
    private static async Task<int> GetMappedStatus(string markMonitorStatus)
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order/11111111-1111-1111-1111-111111111111"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert("11111111-1111-1111-1111-111111111111", markMonitorStatus)));
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        var result = await client.GetSingleOrderAsync("11111111-1111-1111-1111-111111111111");
        Assert.NotNull(result);
        return result.Status;
    }

    [Fact]
    public async Task CreatedStatus_MapsToExternalValidation_NotInitialized()
    {
        // github.com/Keyfactor/markmonitor-caplugin/issues/2, verified against a real
        // AnyGatewayREST + Command deployment: INITIALIZED (20) is not what the gateway framework
        // treats as "accepted, still pending" - only EXTERNALVALIDATION (90) is. Mapping CREATED to
        // INITIALIZED caused the gateway to report a hard enrollment failure for an order that had
        // actually been created successfully at MarkMonitor and was simply awaiting DCV/issuance.
        var status = await GetMappedStatus("CREATED");

        Assert.Equal((int)EndEntityStatus.EXTERNALVALIDATION, status);
    }

    [Theory]
    [InlineData("DIGI_PENDING", EndEntityStatus.INPROCESS)]
    [InlineData("DIGI_PROCESSING", EndEntityStatus.INPROCESS)]
    [InlineData("DIGI_ISSUED", EndEntityStatus.GENERATED)]
    [InlineData("DIGI_REVOKED", EndEntityStatus.REVOKED)]
    [InlineData("DIGI_FAILED", EndEntityStatus.FAILED)]
    [InlineData("DIGI_CANCELED", EndEntityStatus.CANCELLED)]
    [InlineData("DIGI_REJECTED", EndEntityStatus.CANCELLED)]
    public async Task OtherStatuses_MapAsExpected(string markMonitorStatus, EndEntityStatus expected)
    {
        var status = await GetMappedStatus(markMonitorStatus);

        Assert.Equal((int)expected, status);
    }
}
