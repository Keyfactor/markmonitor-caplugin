using System.Net;
using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests;

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
}
