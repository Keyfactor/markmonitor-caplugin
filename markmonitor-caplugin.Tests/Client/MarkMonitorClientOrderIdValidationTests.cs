using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientOrderIdValidationTests
{
    private const string NotAGuid = "../../etc/passwd";

    [Fact]
    public async Task GetSingleOrderAsync_WithNonGuidOrderId_ThrowsWithoutMakingARequest()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetSingleOrderAsync(NotAGuid));

        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "GET", "/order/"));
    }

    [Fact]
    public async Task CancelCertificateAsync_WithNonGuidOrderId_ThrowsWithoutMakingARequest()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => client.CancelCertificateAsync(NotAGuid));

        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "PATCH", "/cancel"));
    }

    [Fact]
    public async Task RevokeCertificateAsync_WithNonGuidOrderId_ThrowsWithoutMakingARequest()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => client.RevokeCertificateAsync(NotAGuid));

        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "PATCH", "/revoke"));
    }

    [Fact]
    public async Task GetOrganizationAsync_WithNonGuidOrgId_ReturnsNullWithoutMakingARequest()
    {
        // GetOrganizationAsync's URL interpolates orgId directly (/certs/v1/organization/{orgId}),
        // same class of injection risk ValidateGuidFormat already guards against for order IDs.
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.GetOrganizationAsync(NotAGuid);

        Assert.Null(result);
        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "GET", "/organization/"));
    }
}
