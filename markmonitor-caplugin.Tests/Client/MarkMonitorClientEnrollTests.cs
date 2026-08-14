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
        Enabled = true,
        PickupRetries = 0 // disable post-submit polling - not what these tests exercise
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

    [Fact]
    public async Task EnrollCertificateAsync_WhenEccCsrUsesExplicitCurveParameters_ThrowsWithoutSubmittingTheOrder()
    {
        // MarkMonitor silently fails an order for an ECC CSR whose key uses explicit curve
        // parameters instead of a named-curve OID reference (confirmed against a live sandbox: the
        // order reaches DIGI_FAILED in under a second, with no reason surfaced anywhere in the API).
        // Reject it at enrollment time with an actionable error instead.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.EnrollCertificateAsync(SampleEccCsrs.ExplicitCurvePem, "CN=test.mmcertdomain.com",
                new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config()));

        Assert.Contains("named curve", ex.Message);
        Assert.DoesNotContain(handler.Requests, req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WhenEccCsrUsesANamedCurve_EnrollsSuccessfully()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("55555555-5555-5555-5555-555555555555", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.EnrollCertificateAsync(SampleEccCsrs.NamedCurvePem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        Assert.NotNull(result);
        Assert.Contains(handler.Requests, req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithANumericOrderTypeForAnUndefinedEnumValue_ThrowsWithoutSubmittingTheOrder()
    {
        // Regression test: Enum.Parse<CertOrderTypes> alone "succeeds" for any numeric string that
        // fits the underlying int type, even with no member defined for that value (CertOrderTypes
        // has 12 members, values 0-11) - Enum.IsDefined is the check that actually enforces membership.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
                new Dictionary<string, string[]>(), "20", new Dictionary<string, string>(), Config()));

        Assert.Contains("20", ex.Message);
        Assert.DoesNotContain(handler.Requests, req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"));
    }
}
