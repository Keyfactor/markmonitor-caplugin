using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;
using Newtonsoft.Json.Linq;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

/// <summary>
/// Regression coverage for GitHub issue #9: ResolveOrganizationAsync/ResolveOrganizationIdAsync used
/// to take the *first* org MarkMonitor's name search returned without checking it was an exact match.
/// If MarkMonitor's /certs/v1/organization name filter does substring/fuzzy matching, a configured org
/// name that's a substring of another org's name (e.g. "Acme" vs "Acme Corp Europe") could silently
/// resolve to the wrong organization - undermining the cross-org ownership check in
/// RevokeCertificateAsync. Both resolvers must filter to an exact (case-insensitive) name match.
/// </summary>
public class MarkMonitorClientOrgResolutionTests
{
    private const string ExactOrgId = "11111111-1111-1111-1111-111111111111";
    private const string SubstringOrgId = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task EnrollCertificateAsync_WhenSearchReturnsASubstringFalsePositive_ResolvesTheExactMatchOnly()
    {
        // MarkMonitor's search for "Acme" returns both the exact org and an unrelated org whose name
        // merely contains "Acme" - and, to prove the fix filters by exact match rather than list
        // position, the substring false positive is returned *first*.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(
                        SampleOrgs.OrgWithContact(SubstringOrgId, "Acme Corp Europe"),
                        SampleOrgs.OrgWithContact(ExactOrgId, "Acme"))))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("44444444-4444-4444-4444-444444444444", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var config = SampleConfig.Default("Acme");

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), config);

        Assert.NotNull(result);
        var orderRequest = Assert.Single(handler.Requests, req => FakeHttpMessageHandler.Is(req, "POST", "/order"));
        var body = await orderRequest.Content!.ReadAsStringAsync();
        Assert.Equal(ExactOrgId, JObject.Parse(body)["organizationId"]!.Value<string>(),
            ignoreCase: true);
    }

    [Fact]
    public async Task EnrollCertificateAsync_WhenNoExactNameMatchExists_ThrowsWithoutSubmittingTheOrder()
    {
        // Only the substring false positive is returned - there is no org actually named "Acme" - so
        // resolution must fail the same way it always has for "no match", not fall back to the
        // unrelated org.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact(SubstringOrgId, "Acme Corp Europe"))));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var config = SampleConfig.Default("Acme");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
                new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), config));

        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(handler.Requests, req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"));
    }

    [Fact]
    public async Task RevokeCertificateAsync_WhenSearchReturnsASubstringFalsePositive_ResolvesTheExactMatchOnly()
    {
        // The order actually belongs to the exact-match org. If the buggy "take the first result"
        // behavior were still present, list order (substring false positive first) would resolve the
        // configured "Acme" to the wrong org and this legitimate revoke would be wrongly refused.
        const string orderId = "55555555-5555-5555-5555-555555555555";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(
                        SampleOrgs.OrgWithContact(SubstringOrgId, "Acme Corp Europe"),
                        SampleOrgs.OrgWithContact(ExactOrgId, "Acme"))))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{orderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert(orderId, "DIGI_ISSUED", organizationId: ExactOrgId)))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{orderId}/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.RevokeCertificateAsync(orderId, "Acme");

        Assert.True(result);
    }

    [Fact]
    public async Task RevokeCertificateAsync_WhenNoExactNameMatchExists_ThrowsWithoutRevoking()
    {
        // Only the substring false positive is returned - there is no org actually named "Acme" - so
        // resolution must come back empty (same "not found" contract as before) rather than falling
        // back to the unrelated org and letting an unauthorized revoke through.
        const string orderId = "55555555-5555-5555-5555-555555555555";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact(SubstringOrgId, "Acme Corp Europe"))))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{orderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert(orderId, "DIGI_ISSUED", organizationId: SubstringOrgId)))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{orderId}/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<Exception>(() => client.RevokeCertificateAsync(orderId, "Acme"));

        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "PATCH", "/revoke"));
    }

    [Fact]
    public async Task EnrollCertificateAsync_ResolvingOrgByName_RequestsAPageSizeLargeEnoughToAvoidOneRoundTripPerMatch()
    {
        // Regression test: ResolveOrganizationAsync/ResolveOrganizationIdAsync used to hard-code a
        // page size of 1 for the org-name search. Since MarkMonitor's name filter can return several
        // fuzzy matches for one configured name, that forced one sequential HTTP round-trip per
        // matching org just to page through them all before the exact-match filter (above) ever ran.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("44444444-4444-4444-4444-444444444444", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(),
            SampleConfig.Default());

        var orgRequest = Assert.Single(handler.Requests,
            req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"));
        var query = orgRequest.RequestUri!.Query;
        Assert.Contains("size=100", query);
        Assert.DoesNotContain("size=1&", query);
    }
}
