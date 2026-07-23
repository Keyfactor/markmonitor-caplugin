using System.Net;
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
    public async Task GetOrganizationAsync_WithNonGuidOrgId_ThrowsWithoutMakingARequest()
    {
        // GetOrganizationAsync's URL interpolates orgId directly (/certs/v1/organization/{orgId}),
        // same class of injection risk ValidateGuidFormat already guards against for order IDs.
        //
        // Asserting the specific exception type matters here: dot-segments in NotAGuid get
        // normalized out of the request URI by Uri/HttpClient regardless of whether validation ran,
        // so a looser assertion (e.g. "no organization/ in the URI") would pass even if
        // ValidateGuidFormat were deleted - only asserting ArgumentException (vs. whatever the fake
        // handler's "no route registered" exception would surface as) actually proves validation
        // ran before any request was attempted.
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetOrganizationAsync(NotAGuid));

        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "GET", "/organization/"));
    }

    [Fact]
    public async Task GetOrganizationAsync_WhenTheApiErrors_ThrowsInsteadOfReturningNull()
    {
        // Before this fix, any failure here (auth expiry, network blip, malformed body) was logged
        // and then swallowed to null - a caller resolving a GUID-configured OrgId (EnrollCertificateAsync's
        // ResolveOrganizationAsync) would report a misleading "Organization ID not found" instead of
        // the real error - same class of gap already fixed in GetSingleOrderAsync.
        const string orgId = "11111111-1111-1111-1111-111111111111";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/organization/{orgId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError,
                    """{"errors":[{"code":"request.genericError","message":"An unexpected error occurred."}]}"""));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<Exception>(() => client.GetOrganizationAsync(orgId));

        Assert.Contains("An unexpected error occurred", ex.Message);
    }
}
