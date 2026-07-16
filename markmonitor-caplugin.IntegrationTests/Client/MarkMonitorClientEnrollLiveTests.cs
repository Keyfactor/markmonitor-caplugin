using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;
using Keyfactor.PKI.Enums.EJBCA;
using Keyfactor.PKI.PEM;
using TestConsole.Helpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.IntegrationTests.Client;

// Hits the real MarkMonitor API using credentials from the environment (see .env / TestConsole/.env)
// and creates real, billable MarkMonitor orders. Skips automatically when those variables aren't
// set, so it stays out of normal CI/unit runs.
//
// Unlike TestConsole/Program.cs's EnrollCertificateAsync loop (which supports
// MARKMONITOR_SKIP_CLEANUP for deliberate manual-inspection runs), every order created here is
// always cleaned up (see OrderCleanup) - this suite is meant to run unattended and repeatedly, so
// leaving created orders in place is never an option.
public class MarkMonitorClientEnrollLiveTests
{
    [Fact]
    public async Task EnrollCertificateAsync_WithRsaCsr_CreatesAndCleansUpOrder()
    {
        await RunEnrollTest(CsrGenerator.KeyType.RSA);
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithEccCsr_CreatesAndCleansUpOrder()
    {
        await RunEnrollTest(CsrGenerator.KeyType.ECC);
    }

    private static async Task RunEnrollTest(CsrGenerator.KeyType keyType)
    {
        if (!LiveApiCredentials.TryGet(out var baseUrl, out var apiToken, out var username, out var password))
        {
            // No live credentials in the environment; nothing to verify.
            return;
        }

        using var client = new MarkMonitorClient(baseUrl, apiToken, username, password);
        await client.AuthenticateAsync();

        var orgs = await client.ListOrganizationsAsync(0, 0);
        Assert.NotEmpty(orgs);

        var csrGenerator = new CsrGenerator();
        var generatedCsrs = await csrGenerator.GenerateCsrs(1, keyType);
        var (csr, _, _) = generatedCsrs[0];
        var csrPem = PemUtilities.DERToPEM(csr.GetEncoded(), PemUtilities.PemObjectType.CertRequest);
        var commonName = csr.GetCertificationRequestInfo().Subject.GetValueList()[0];

        var randomNumberOfEmails = new Random().Next(1, 4);
        var additionalEmails = await EmailAddressGenerator.GenerateRandomEmailsAsync(randomNumberOfEmails);

        var config = new MarkMonitorConfig
        {
            ApiKey = apiToken,
            ApiUsername = username,
            ApiPassword = password,
            BaseUrl = baseUrl,
            OrgName = orgs[0].Name,
            Enabled = true
        };
        var productParams = new Dictionary<string, string>
        {
            ["additionalEmails"] = string.Join(",", additionalEmails)
        };

        EnrollmentResult? enrollResult = null;
        try
        {
            enrollResult = await client.EnrollCertificateAsync(csrPem, $"CN={commonName}",
                new Dictionary<string, string[]>(), CertOrderTypes.SslDvGeotrust.ToString(), productParams, config);

            Assert.NotNull(enrollResult);
            Assert.False(string.IsNullOrWhiteSpace(enrollResult.CARequestID));
            // The test org used for these live runs requires manual email DCV approval, so the
            // order will be pending (EXTERNALVALIDATION), not issued, by the time this returns -
            // the real assertion is that the order was accepted at all rather than rejected/failed.
            Assert.NotEqual((int)EndEntityStatus.FAILED, enrollResult.Status);
        }
        finally
        {
            if (enrollResult != null)
            {
                await OrderCleanup.CleanUpOrderAsync(client, enrollResult.CARequestID);
            }
        }
    }
}
