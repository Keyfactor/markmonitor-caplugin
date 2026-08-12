using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

/// <summary>
/// Regression coverage for wiring Enroll's `san` dictionary (and any CSR-embedded SAN extension)
/// into the MarkMonitor order's `dnsNames` field - previously accepted and silently dropped, so a
/// multi-SAN enrollment issued CN-only.
/// </summary>
[Collection(LogHandlerFactoryCollection.Name)]
public class MarkMonitorClientSanTests
{
    private static FakeHttpMessageHandler BuildHandler(string orderId = "11111111-1111-1111-1111-111111111111") =>
        new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted, SampleOrders.OrderWithCert(orderId, "CREATED")));

    private static async Task<JObject> EnrollAndGetSentBody(string csrPem, string subject,
        Dictionary<string, string[]>? san, FakeHttpMessageHandler? handler = null)
    {
        handler ??= BuildHandler();
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await client.EnrollCertificateAsync(csrPem, subject, san, "SslDvGeotrust",
            new Dictionary<string, string>(), SampleConfig.Default());

        var orderRequest = handler.Requests.Single(req => FakeHttpMessageHandler.Is(req, "POST", "/order"));
        var body = await orderRequest.Content!.ReadAsStringAsync();
        return JObject.Parse(body);
    }

    private static List<string>? DnsNamesOf(JObject body) =>
        body["cert"]!["dnsNames"]?.Values<string>().Select(v => v!).ToList();

    [Fact]
    public async Task EnrollCertificateAsync_WithDnsSansInDictionary_PopulatesDnsNamesExcludingCn()
    {
        var san = new Dictionary<string, string[]> { ["Dns"] = ["www.mmcertdomain.com", "test.mmcertdomain.com"] };

        var body = await EnrollAndGetSentBody(SampleCsr.Pem, "CN=test.mmcertdomain.com", san);

        Assert.Equal(["www.mmcertdomain.com"], DnsNamesOf(body));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithDnsnameKeyCasing_IsAcceptedCaseInsensitively()
    {
        var san = new Dictionary<string, string[]> { ["dnsname"] = ["alt.mmcertdomain.com"] };

        var body = await EnrollAndGetSentBody(SampleCsr.Pem, "CN=test.mmcertdomain.com", san);

        Assert.Equal(["alt.mmcertdomain.com"], DnsNamesOf(body));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithEmptySanDictionaryAndNoCsrSans_OmitsDnsNamesFromTheRequest()
    {
        var body = await EnrollAndGetSentBody(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>());

        Assert.Null(body["cert"]!["dnsNames"]);
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithDuplicateDnsSans_DedupesThem()
    {
        var san = new Dictionary<string, string[]>
        {
            ["Dns"] = ["www.mmcertdomain.com", "WWW.mmcertdomain.com", "www.mmcertdomain.com"]
        };

        var body = await EnrollAndGetSentBody(SampleCsr.Pem, "CN=test.mmcertdomain.com", san);

        Assert.Equal(["www.mmcertdomain.com"], DnsNamesOf(body));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithNonDnsSanTypes_DropsThemAndLogsAWarning()
    {
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);
        var san = new Dictionary<string, string[]>
        {
            ["Dns"] = ["www.mmcertdomain.com"],
            ["IpAddress"] = ["10.0.0.1"],
            ["Email"] = ["admin@mmcertdomain.com"]
        };

        var body = await EnrollAndGetSentBody(SampleCsr.Pem, "CN=test.mmcertdomain.com", san);

        Assert.Equal(["www.mmcertdomain.com"], DnsNamesOf(body));
        var warning = Assert.Single(capturingFactory.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("non-DNS SAN", StringComparison.Ordinal));
        Assert.Contains("IpAddress", warning.Message);
        Assert.Contains("Email", warning.Message);
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithSansOnlyEmbeddedInTheCsrAndNoSanDictionaryAtAll_UnionsThemIntoDnsNames()
    {
        // `san` is genuinely null here - Command never populated SAN data at all for this request -
        // which is the only condition that falls back to the CSR's own SAN extension.
        var csrPem = SampleCsrWithSans.GeneratePem("test.mmcertdomain.com", "csr-san.mmcertdomain.com");

        var body = await EnrollAndGetSentBody(csrPem, "CN=test.mmcertdomain.com", null);

        Assert.Equal(["csr-san.mmcertdomain.com"], DnsNamesOf(body));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithSansInBothTheDictionaryAndTheCsr_UsesOnlyTheDictionaryAndIgnoresTheCsr()
    {
        // Regression test for a security concern raised in review: a non-null `san` dictionary -
        // even one that omits a domain the CSR itself carries - means Command's own enrollment
        // pattern/template ran and is authoritative for this request. The CSR is subscriber-generated
        // and outside Command's policy/RA control, so its own SAN extension must never be unioned in
        // (let alone let through unauthorized domains) once Command has supplied a real dictionary -
        // certinext-caplugin reverted the identical unconditional-union pattern for this exact reason.
        var csrPem = SampleCsrWithSans.GeneratePem("test.mmcertdomain.com", "csr-only.mmcertdomain.com",
            "shared.mmcertdomain.com");
        var san = new Dictionary<string, string[]> { ["Dns"] = ["dict-san.mmcertdomain.com", "shared.mmcertdomain.com"] };

        var body = await EnrollAndGetSentBody(csrPem, "CN=test.mmcertdomain.com", san);

        Assert.Equal(new HashSet<string> { "dict-san.mmcertdomain.com", "shared.mmcertdomain.com" },
            DnsNamesOf(body)!.ToHashSet());
        Assert.DoesNotContain("csr-only.mmcertdomain.com", DnsNamesOf(body)!);
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithANonNullEmptySanDictionaryAndCsrSans_IgnoresTheCsrSans()
    {
        // An empty-but-non-null dictionary means Command's enrollment pattern deliberately produced
        // no SANs for this request - that must be respected, not silently overridden by the CSR.
        var csrPem = SampleCsrWithSans.GeneratePem("test.mmcertdomain.com", "csr-san.mmcertdomain.com");

        var body = await EnrollAndGetSentBody(csrPem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>());

        Assert.Null(body["cert"]!["dnsNames"]);
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithCsrSanMatchingTheCnAndNoSanDictionary_ExcludesItFromDnsNames()
    {
        var csrPem = SampleCsrWithSans.GeneratePem("test.mmcertdomain.com", "test.mmcertdomain.com",
            "extra.mmcertdomain.com");

        var body = await EnrollAndGetSentBody(csrPem, "CN=test.mmcertdomain.com", null);

        Assert.Equal(["extra.mmcertdomain.com"], DnsNamesOf(body));
    }
}
