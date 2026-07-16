using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientEnrollTests
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

    [Fact]
    public async Task EnrollCertificateAsync_WhenCreateOrderReturnsValidationError_ThrowsWithRealDetail()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest,
                    """{"validations":[{"field":"cert.csr","code":"field.invalidFormat","message":"The CSR format is invalid."}]}"""));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<Exception>(() =>
            client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
                new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config()));

        Assert.Contains("The CSR format is invalid", ex.Message);
        Assert.Contains("cert.csr", ex.Message);
    }

    [Fact]
    public async Task EnrollCertificateAsync_WhenOrgNameIsAGuid_FetchesTheOrganizationDirectlyById()
    {
        // The OrgId CA connection setting is documented as accepting either a friendly name or a
        // GUID. Before this fix, a GUID was always passed as a *name* search filter, which would
        // never match a real org (org names aren't GUIDs) and enrollment would fail with
        // "Organization ID not found".
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/organization/{SampleOrgs.DefaultOrgId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrgs.OrgWithContact()))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("33333333-3333-3333-3333-333333333333", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var config = Config();
        config.OrgName = SampleOrgs.DefaultOrgId;

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), config);

        Assert.NotNull(result);
        Assert.DoesNotContain(handler.Requests,
            req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization?"));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WhenOrgNameIsAFriendlyName_SearchesOrganizationsByName()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("44444444-4444-4444-4444-444444444444", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        Assert.NotNull(result);
        Assert.Contains(handler.Requests, req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization?"));
    }
}
