using System.Net;
using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests;

[Collection(LogHandlerFactoryCollection.Name)]
public class MarkMonitorCAPluginEnrollTests
{
    [Fact]
    public async Task Enroll_WhenMarkMonitorRejectsTheOrder_PropagatesTheRealErrorDetail()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest,
                    """{"validations":[{"field":"cert.csr","code":"field.invalidFormat","message":"The CSR format is invalid."}]}"""));
        var injectedClient = handler.BuildClient();
        var plugin = new MarkMonitorCAPlugin(injectedClient);
        plugin.Initialize(FakeAnyCAPluginConfigProvider.WithDefaults(), new FakeCertificateDataReader());

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string>()
        };

        var ex = await Assert.ThrowsAsync<Exception>(() =>
            plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(),
                productInfo, RequestFormat.PKCS10, EnrollmentType.New));

        // Before the fix, Command would only ever see the generic
        // "Enrollment failed for subject: ..." message - the real MarkMonitor
        // validation detail never made it past EnrollCertificateAsync's catch block.
        Assert.Contains("The CSR format is invalid", ex.Message);
    }

    [Fact]
    public async Task Enroll_WithASubjectContainingEmbeddedCrLf_SanitizesItInLogOutputButStillEnrollsSuccessfully()
    {
        // Regression test (CWE-117): Subject is fully requester-controlled (straight off the
        // submitted CSR). Before the fix, an embedded CR/LF was logged raw, letting a requester forge
        // a fake log line that could be mistaken for a genuine, unrelated entry by anyone relying on
        // this plugin's logs to reconstruct certificate-issuance history.
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);

        const string maliciousSubject =
            "CN=evil.example\r\n2026-08-10 09:00:00 [INF] Enrollment completed successfully for subject: CN=innocent.example";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("11111111-1111-1111-1111-111111111111", "CREATED")));
        var plugin = new MarkMonitorCAPlugin(handler.BuildClient());
        plugin.Initialize(FakeAnyCAPluginConfigProvider.WithDefaults(), new FakeCertificateDataReader());

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string>()
        };

        var result = await plugin.Enroll(SampleCsr.Pem, maliciousSubject, new Dictionary<string, string[]>(),
            productInfo, RequestFormat.PKCS10, EnrollmentType.New);

        Assert.NotNull(result);
        Assert.DoesNotContain(capturingFactory.Messages, m => m.Contains("\r\n", StringComparison.Ordinal));
        Assert.Contains(capturingFactory.Messages, m => m.Contains("\\r\\n", StringComparison.Ordinal));
    }
}
