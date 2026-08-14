// Copyright 2026 Keyfactor
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Net;
using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests;

[Collection(LogHandlerFactoryCollection.Name)]
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
        var client = handler.BuildClient();
        var plugin = new MarkMonitorCAPlugin(client);
        var reader = new FakeCertificateDataReader();
        plugin.Initialize(FakeAnyCAPluginConfigProvider.WithDefaults(), reader);
        return (plugin, handler, reader);
    }

    [Fact]
    public async Task Enroll_RenewOrReissueWithResolvablePriorCertSN_RevokesThePriorCertificate()
    {
        var handler = BaseHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "DIGI_ISSUED")))
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
    public async Task Enroll_RenewOrReissueWithPriorCertWithinTheDefaultRenewalWindow_RevokesIt()
    {
        var priorId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var handler = BaseHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{priorId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(priorId, "DIGI_ISSUED")))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{priorId}/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var (plugin, _, reader) = BuildPlugin(handler);
        reader.SerialNumberToRequestId["ab:cd:ef"] = priorId;
        reader.ExpirationDateByRequestId[priorId] = DateTime.UtcNow.AddDays(10); // well inside the default 90-day window

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string> { ["PriorCertSN"] = "ab:cd:ef" }
        };

        await plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(),
            productInfo, RequestFormat.PKCS10, EnrollmentType.RenewOrReissue);

        Assert.Contains(handler.Requests, req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{priorId}/revoke"));
    }

    [Fact]
    public async Task Enroll_RenewOrReissueWithPriorCertOutsideTheDefaultRenewalWindow_LeavesItUnrevoked()
    {
        var priorId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var handler = BaseHandler();
        var (plugin, _, reader) = BuildPlugin(handler);
        reader.SerialNumberToRequestId["ab:cd:ef"] = priorId;
        reader.ExpirationDateByRequestId[priorId] = DateTime.UtcNow.AddDays(200); // still has substantial life left

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string> { ["PriorCertSN"] = "ab:cd:ef" }
        };

        var result = await plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), productInfo, RequestFormat.PKCS10, EnrollmentType.RenewOrReissue);

        Assert.NotNull(result);
        Assert.DoesNotContain(handler.Requests, req => FakeHttpMessageHandler.Is(req, "PATCH", "/revoke"));
    }

    [Fact]
    public async Task Enroll_RenewOrReissueWithAnAlreadyExpiredPriorCert_StillRevokesIt()
    {
        var priorId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var handler = BaseHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{priorId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(priorId, "DIGI_ISSUED")))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{priorId}/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var (plugin, _, reader) = BuildPlugin(handler);
        reader.SerialNumberToRequestId["ab:cd:ef"] = priorId;
        reader.ExpirationDateByRequestId[priorId] = DateTime.UtcNow.AddDays(-5); // already expired

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string> { ["PriorCertSN"] = "ab:cd:ef" }
        };

        await plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(),
            productInfo, RequestFormat.PKCS10, EnrollmentType.RenewOrReissue);

        Assert.Contains(handler.Requests, req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{priorId}/revoke"));
    }

    [Fact]
    public async Task Enroll_RenewOrReissueWithACustomRenewalWindowDays_RespectsIt()
    {
        var priorId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var handler = BaseHandler();
        var (plugin, _, reader) = BuildPlugin(handler);
        reader.SerialNumberToRequestId["ab:cd:ef"] = priorId;
        // 50 days out - within the default 90-day window, but outside a configured 30-day window.
        reader.ExpirationDateByRequestId[priorId] = DateTime.UtcNow.AddDays(50);

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string>
                { ["PriorCertSN"] = "ab:cd:ef", ["RenewalWindowDays"] = "30" }
        };

        var result = await plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), productInfo, RequestFormat.PKCS10, EnrollmentType.RenewOrReissue);

        Assert.NotNull(result);
        Assert.DoesNotContain(handler.Requests, req => FakeHttpMessageHandler.Is(req, "PATCH", "/revoke"));
    }

    [Fact]
    public async Task Enroll_RenewOrReissueWithAnInvalidRenewalWindowDaysValue_FallsBackToTheDefault()
    {
        var priorId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var handler = BaseHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{priorId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(priorId, "DIGI_ISSUED")))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{priorId}/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var (plugin, _, reader) = BuildPlugin(handler);
        reader.SerialNumberToRequestId["ab:cd:ef"] = priorId;
        // 50 days out - within the default 90-day window a non-numeric value must fall back to.
        reader.ExpirationDateByRequestId[priorId] = DateTime.UtcNow.AddDays(50);

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string>
                { ["PriorCertSN"] = "ab:cd:ef", ["RenewalWindowDays"] = "not-a-number" }
        };

        await plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(),
            productInfo, RequestFormat.PKCS10, EnrollmentType.RenewOrReissue);

        Assert.Contains(handler.Requests, req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{priorId}/revoke"));
    }

    [Fact]
    public async Task Enroll_RenewOrReissueWithAnInvalidRenewalWindowDaysValue_LogsAWarningNamingTheRejectedValue()
    {
        // Regression test: a mistyped/invalid RenewalWindowDays used to be silently coerced to the
        // default with zero audit trail - indistinguishable in the logs from "not configured at
        // all," despite affecting a security-relevant revoke decision.
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);
        var priorId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var handler = BaseHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{priorId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(priorId, "DIGI_ISSUED")))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{priorId}/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var (plugin, _, reader) = BuildPlugin(handler);
        reader.SerialNumberToRequestId["ab:cd:ef"] = priorId;
        reader.ExpirationDateByRequestId[priorId] = DateTime.UtcNow.AddDays(50);

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string>
                { ["PriorCertSN"] = "ab:cd:ef", ["RenewalWindowDays"] = "not-a-number" }
        };

        await plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(),
            productInfo, RequestFormat.PKCS10, EnrollmentType.RenewOrReissue);

        var warning = Assert.Single(capturingFactory.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("Invalid RenewalWindowDays", StringComparison.Ordinal));
        Assert.Contains("not-a-number", warning.Message);
    }

    [Fact]
    public async Task Enroll_RenewOrReissueWithNoRenewalWindowDaysParameter_LogsNoWarning()
    {
        // Absent (never configured) must not be logged the same as present-but-rejected.
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);
        var priorId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var handler = BaseHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{priorId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(priorId, "DIGI_ISSUED")))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{priorId}/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var (plugin, _, reader) = BuildPlugin(handler);
        reader.SerialNumberToRequestId["ab:cd:ef"] = priorId;
        reader.ExpirationDateByRequestId[priorId] = DateTime.UtcNow.AddDays(50);

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string> { ["PriorCertSN"] = "ab:cd:ef" }
        };

        await plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(),
            productInfo, RequestFormat.PKCS10, EnrollmentType.RenewOrReissue);

        Assert.DoesNotContain(capturingFactory.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("Invalid RenewalWindowDays", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enroll_RenewOrReissueWithNoExpirationDataForThePriorCert_FallsBackToRevokingItAsBefore()
    {
        var priorId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var handler = BaseHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{priorId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(priorId, "DIGI_ISSUED")))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{priorId}/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var (plugin, _, reader) = BuildPlugin(handler);
        reader.SerialNumberToRequestId["ab:cd:ef"] = priorId;
        // No ExpirationDateByRequestId entry - GetExpirationDateByRequestId returns null.

        var productInfo = new EnrollmentProductInfo
        {
            ProductID = "SslDvGeotrust",
            ProductParameters = new Dictionary<string, string> { ["PriorCertSN"] = "ab:cd:ef" }
        };

        await plugin.Enroll(SampleCsr.Pem, "CN=test.mmcertdomain.com", new Dictionary<string, string[]>(),
            productInfo, RequestFormat.PKCS10, EnrollmentType.RenewOrReissue);

        Assert.Contains(handler.Requests, req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{priorId}/revoke"));
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
