using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.IntegrationTests.Client;

// Hits the real MarkMonitor API using credentials from the environment (see .env / TestConsole/.env).
// Skips automatically when those variables aren't set, so it stays out of normal CI/unit runs.
public class MarkMonitorClientListOrgsLiveTests
{
    [Fact]
    public async Task ListOrganizationsAsync_WithLiveCredentials_ReturnsOrgsWithValidations()
    {
        if (!LiveApiCredentials.TryGet(out var baseUrl, out var apiToken, out var username, out var password))
        {
            // No live credentials in the environment; nothing to verify.
            return;
        }

        using var client = new MarkMonitorClient(baseUrl, apiToken, username, password);
        await client.AuthenticateAsync();

        var orgs = await client.ListOrganizationsAsync(0, 0);

        // Mirrors TestConsole's TestListOrgs, which treats an empty result as a test-setup problem
        // ("no organizations found, please add some to run this test") rather than a valid outcome.
        Assert.NotEmpty(orgs);

        foreach (var org in orgs)
        {
            Assert.False(string.IsNullOrWhiteSpace(org.Id));

            // TestListOrgs additionally logs each org's validations (name/type) - assert their shape
            // is well-formed rather than just that the call didn't throw.
            foreach (var validation in org.Validations ?? new List<MarkMonitorOrgValidation>())
            {
                Assert.False(string.IsNullOrWhiteSpace(validation.Name));
                Assert.False(string.IsNullOrWhiteSpace(validation.Type));
            }
        }
    }
}
