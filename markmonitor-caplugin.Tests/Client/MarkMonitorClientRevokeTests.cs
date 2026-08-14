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
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order/11111111-1111-1111-1111-111111111111"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert("11111111-1111-1111-1111-111111111111", "DIGI_ISSUED")))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", "/certs/v1/order/11111111-1111-1111-1111-111111111111/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.RevokeCertificateAsync("11111111-1111-1111-1111-111111111111", "Test Org", reason);

        Assert.True(result);
    }

    [Fact]
    public async Task RevokeCertificateAsync_WhenTheOrderBelongsToADifferentOrganization_ThrowsWithoutRevoking()
    {
        // The prior cert's request ID in the RenewOrReissue path comes from Command's
        // ICertificateDataReader, not from this org's own enrollment - so verify the order actually
        // belongs to the configured org before revoking it, rather than trusting the caller.
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
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{orderId}/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<Exception>(() => client.RevokeCertificateAsync(orderId, "Test Org"));

        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "PATCH", "/revoke"));
    }

    [Fact]
    public async Task RevokeCertificateAsync_WithOrgNameGivenAsAGuid_ResolvesWithoutAnOrganizationLookup()
    {
        const string orderId = "11111111-1111-1111-1111-111111111111";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{orderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert(orderId, "DIGI_ISSUED", organizationId: SampleOrgs.DefaultOrgId)))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{orderId}/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.RevokeCertificateAsync(orderId, SampleOrgs.DefaultOrgId);

        Assert.True(result);
        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "GET", "/certs/v1/organization"));
    }

    [Fact]
    public async Task RevokeCertificateAsync_WithOrgNameGuidInADifferentTextualFormat_StillMatchesTheOrder()
    {
        // Guid.TryParse accepts several textual formats (braces, no dashes, etc.) that an admin could
        // legitimately configure, but MarkMonitor's API always serializes organizationId in one
        // canonical form. The comparison must be by parsed Guid value, not raw string equality, or a
        // legitimately-configured OrgId in a non-canonical format would be rejected as "wrong org".
        const string orderId = "11111111-1111-1111-1111-111111111111";
        const string orgIdBraces = "{11111111-1111-1111-1111-111111111111}";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{orderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert(orderId, "DIGI_ISSUED", organizationId: SampleOrgs.DefaultOrgId)))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{orderId}/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.RevokeCertificateAsync(orderId, orgIdBraces);

        Assert.True(result);
    }

    [Fact]
    public async Task RevokeCertificateAsync_CalledTwiceWithTheSameOrgName_OnlyResolvesTheOrganizationOnce()
    {
        // Regression test: resolving OrgName by friendly name used to make a fresh MarkMonitor API
        // call on every single Revoke/Cancel call, even though the configured value is invariant for
        // the connector's (and this cached client's) lifetime - real cost at bulk-revocation scale.
        // A resolved org GUID is now cached for this client's lifetime.
        const string firstOrderId = "11111111-1111-1111-1111-111111111111";
        const string secondOrderId = "22222222-2222-2222-2222-222222222222";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{firstOrderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert(firstOrderId, "DIGI_ISSUED")))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{secondOrderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert(secondOrderId, "DIGI_ISSUED")))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", "/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await client.RevokeCertificateAsync(firstOrderId, "Test Org");
        await client.RevokeCertificateAsync(secondOrderId, "Test Org");

        Assert.Single(handler.Requests, r => FakeHttpMessageHandler.Is(r, "GET", "/certs/v1/organization"));
    }
}
