using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

[Collection(LogHandlerFactoryCollection.Name)]
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

    [Fact]
    public async Task EnrollCertificateAsync_WithCrLfInGroupAndDcvMethodParams_SanitizesThemInLogOutput()
    {
        // Regression test (CWE-117), extending the Subject/CommonName sanitization to the other
        // requester-controlled, free-text enrollment template parameters (MarkmonitorGroup, DCVMethod,
        // Comments, Locale, Provider) - a full-review round found the first fix only covered
        // Subject/CommonName, leaving the same forged-log-line vector open through these fields.
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);

        const string maliciousGroup = "no-such-group\r\n2026-08-10 09:00:00 [INF] FAKE forged log line";
        const string maliciousDcvMethod = "BOGUS\r\n2026-08-10 09:00:00 [INF] FAKE forged log line";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/auth/v1/group"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"groups":[],"page":{"totalPages":1}}"""))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("22222222-2222-2222-2222-222222222222", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var productParams = new Dictionary<string, string>
        {
            [MarkMonitorCAPluginConfig.EnrollmentConfigConstants.MarkmonitorGroup] = maliciousGroup,
            [MarkMonitorCAPluginConfig.EnrollmentConfigConstants.DCVMethod] = maliciousDcvMethod
        };

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", productParams, SampleConfig.Default());

        Assert.NotNull(result);
        Assert.DoesNotContain(capturingFactory.Messages, m => m.Contains("\r\n", StringComparison.Ordinal));
        Assert.Contains(capturingFactory.Messages,
            m => m.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase) &&
                 m.Contains("\\r\\n", StringComparison.Ordinal));
        Assert.Contains(capturingFactory.Messages,
            m => m.Contains("Invalid DCVMethod", StringComparison.OrdinalIgnoreCase) &&
                 m.Contains("\\r\\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnrollCertificateAsync_ResolvingGroupByName_RequestsAPageSizeLargeEnoughToAvoidOneRoundTripPerMatch()
    {
        // Regression test: ResolveGroupIdAsync used to request the server default page size (no
        // `size=` param at all) instead of the larger page size ResolveOrganizationIdAsync already
        // uses for the identical fuzzy-match, filter-by-exact-name pattern - risking one sequential
        // HTTP round-trip per fuzzy-matching group instead of one round-trip total.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/auth/v1/group"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"groups":[],"page":{"totalPages":1}}"""))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("33333333-3333-3333-3333-333333333333", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var productParams = new Dictionary<string, string>
        {
            [MarkMonitorCAPluginConfig.EnrollmentConfigConstants.MarkmonitorGroup] = "some-group"
        };

        await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", productParams, SampleConfig.Default());

        var groupRequest = Assert.Single(handler.Requests, req => FakeHttpMessageHandler.Is(req, "GET", "/auth/v1/group"));
        Assert.Contains("size=100", groupRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task EnrollCertificateAsync_CalledTwiceWithTheSameGroupName_OnlyResolvesTheGroupOnce()
    {
        // Regression test: resolving a named MarkmonitorGroup used to make a fresh MarkMonitor API
        // call on every single enrollment, even though a resolved group name never maps to a
        // different GUID later - a real cost at steady enrollment volume. A resolved group GUID is
        // now cached for this client's lifetime.
        const string groupId = "44444444-4444-4444-4444-444444444444";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/auth/v1/group"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    $$"""{"groups":[{"id":"{{groupId}}","name":"some-group"}],"page":{"totalPages": 1} }"""))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("55555555-5555-5555-5555-555555555555", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var productParams = new Dictionary<string, string>
        {
            [MarkMonitorCAPluginConfig.EnrollmentConfigConstants.MarkmonitorGroup] = "some-group"
        };

        await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test1.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", productParams, SampleConfig.Default());
        await client.EnrollCertificateAsync(SampleCsr2.Pem, "CN=test2.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", productParams, SampleConfig.Default());

        Assert.Single(handler.Requests, req => FakeHttpMessageHandler.Is(req, "GET", "/auth/v1/group"));
    }
}
