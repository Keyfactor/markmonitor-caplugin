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
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<Exception>(() =>
            client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
                new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config()));

        Assert.Contains("The CSR format is invalid", ex.Message);
        Assert.Contains("cert.csr", ex.Message);
    }
}
