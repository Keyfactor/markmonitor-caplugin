using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientEnrollLoggingTests
{
    [Fact]
    public async Task EnrollCertificateAsync_WithNoResolvableContactOrGroup_StillSucceeds()
    {
        // Logging the resolved ContactId/GroupId against the new order's CARequestID must not throw
        // when either is null (no contact on the org, no MarkmonitorGroup param supplied).
        const string orgWithNoContacts = """
                                         {"id": "11111111-1111-1111-1111-111111111111", "name": "Test Org",
                                          "provider": "DIGICERT", "providerId": 1, "contacts": []}
                                         """;
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrgs.OrgsListResponse(orgWithNoContacts)))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("22222222-2222-2222-2222-222222222222", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var config = SampleConfig.Default();

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), config);

        Assert.NotNull(result);
        Assert.Equal("22222222-2222-2222-2222-222222222222", result.CARequestID);
    }
}
