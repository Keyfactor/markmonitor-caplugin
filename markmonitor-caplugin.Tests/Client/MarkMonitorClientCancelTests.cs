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
        var client = handler.BuildClient();
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
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.RevokeCertificateAsync("11111111-1111-1111-1111-111111111111");

        Assert.True(result);
        var revokeRequest =
            Assert.Single(handler.Requests, req => FakeHttpMessageHandler.Is(req, "PATCH", "/revoke"));
        var body = await revokeRequest.Content!.ReadAsStringAsync();
        Assert.Equal("{}", body);
    }

    [Fact]
    public async Task CancelCertificateAsync_WithMatchingOrganization_Succeeds()
    {
        // Mirrors RevokeCertificateAsync's cross-org ownership check (see
        // MarkMonitorClientRevokeTests) - CancelCertificateAsync must apply the identical check.
        const string orderId = "11111111-1111-1111-1111-111111111111";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{orderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert(orderId, "DIGI_ISSUED")))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{orderId}/cancel"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.CancelCertificateAsync(orderId, "Test Org");

        Assert.True(result);
    }

    [Fact]
    public async Task CancelCertificateAsync_WhenTheOrderBelongsToADifferentOrganization_ThrowsWithoutCancelling()
    {
        const string orderId = "11111111-1111-1111-1111-111111111111";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{orderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert(orderId, "DIGI_ISSUED",
                        organizationId: "99999999-9999-9999-9999-999999999999")))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{orderId}/cancel"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<Exception>(() => client.CancelCertificateAsync(orderId, "Test Org"));

        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "PATCH", "/cancel"));
    }

    [Fact]
    public async Task CancelCertificateAsync_WithNoOrganizationGiven_ProceedsWithoutTheOwnershipCheck()
    {
        // A blank orgName intentionally skips the ownership check (ad-hoc/manual callers that don't
        // scope by organization) - matching RevokeCertificateAsync's existing behavior for this case.
        const string orderId = "11111111-1111-1111-1111-111111111111";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{orderId}/cancel"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.CancelCertificateAsync(orderId);

        Assert.True(result);
        // No organization lookup or order fetch should happen when orgName is blank.
        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "GET", "/certs/v1/organization"));
        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "GET", $"/certs/v1/order/{orderId}"));
    }
}
