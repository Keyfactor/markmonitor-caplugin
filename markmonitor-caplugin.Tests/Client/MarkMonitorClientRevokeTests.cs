using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientRevokeTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)] // keyCompromise
    [InlineData(4u)] // superseded
    public async Task RevokeCertificateAsync_WithAnyReasonCode_StillRevokesSuccessfully(uint reason)
    {
        // MarkMonitor's revoke API has no field for a reason code at all (confirmed against its
        // published schema - OrderActionPatchObject only has cert/ignoreOrgCheck/additionalEmails),
        // so the reason can't change what's sent. This just confirms passing one doesn't break the
        // call - the actual "can't forward it" behavior is logged, not independently observable.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", "/certs/v1/order/11111111-1111-1111-1111-111111111111/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        var result = await client.RevokeCertificateAsync("11111111-1111-1111-1111-111111111111", "Test Org", reason);

        Assert.True(result);
    }
}
