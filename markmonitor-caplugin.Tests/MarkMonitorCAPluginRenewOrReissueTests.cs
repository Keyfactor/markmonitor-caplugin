using System.Net;
using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests;

public class MarkMonitorCAPluginRenewOrReissueTests
{
    private static FakeHttpMessageHandler BaseHandler(string newOrderId = "new-order-id") =>
        new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert(newOrderId, "CREATED")));

    private static (MarkMonitorCAPlugin plugin, FakeHttpMessageHandler handler, FakeCertificateDataReader reader)
        BuildPlugin(FakeHttpMessageHandler handler)
    {
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        var plugin = new MarkMonitorCAPlugin(client);
        var reader = new FakeCertificateDataReader();
        plugin.Initialize(FakeAnyCAPluginConfigProvider.WithDefaults(), reader);
        return (plugin, handler, reader);
    }

    [Fact]
    public async Task Enroll_RenewOrReissueWithResolvablePriorCertSN_RevokesThePriorCertificate()
    {
        var handler = BaseHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", "/certs/v1/order/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var (plugin, _, reader) = BuildPlugin(handler);
        reader.SerialNumberToRequestId["ab:cd:ef"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string> { ["PriorCertSN"] = "ab:cd:ef" }
        };

        var result = await plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), productInfo, RequestFormat.PKCS10, EnrollmentType.RenewOrReissue);

        Assert.NotNull(result);
        Assert.Contains(handler.Requests,
            req => FakeHttpMessageHandler.Is(req, "PATCH", "/certs/v1/order/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/revoke"));
    }

    [Fact]
    public async Task Enroll_RenewOrReissueWithoutPriorCertSN_FallsBackToNewEnrollmentWithoutRevoking()
    {
        var handler = BaseHandler();
        var (plugin, _, _) = BuildPlugin(handler);

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string>()
        };

        var result = await plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), productInfo, RequestFormat.PKCS10, EnrollmentType.RenewOrReissue);

        Assert.NotNull(result);
        Assert.DoesNotContain(handler.Requests, req => FakeHttpMessageHandler.Is(req, "PATCH", "/revoke"));
    }

    [Fact]
    public async Task Enroll_RenewOrReissueWithUnresolvablePriorCertSN_StillReturnsTheNewCertWithoutRevoking()
    {
        var handler = BaseHandler();
        var (plugin, _, _) = BuildPlugin(handler);

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string> { ["PriorCertSN"] = "unknown-serial" }
        };

        var result = await plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), productInfo, RequestFormat.PKCS10, EnrollmentType.RenewOrReissue);

        Assert.NotNull(result);
        Assert.DoesNotContain(handler.Requests, req => FakeHttpMessageHandler.Is(req, "PATCH", "/revoke"));
    }

    [Fact]
    public async Task Enroll_PlainNewEnrollment_NeverAttemptsToRevokeAnything()
    {
        var handler = BaseHandler();
        var (plugin, _, reader) = BuildPlugin(handler);
        reader.SerialNumberToRequestId["ab:cd:ef"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string> { ["PriorCertSN"] = "ab:cd:ef" }
        };

        await plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(),
            productInfo, RequestFormat.PKCS10, EnrollmentType.New);

        Assert.DoesNotContain(handler.Requests, req => FakeHttpMessageHandler.Is(req, "PATCH", "/revoke"));
    }
}
