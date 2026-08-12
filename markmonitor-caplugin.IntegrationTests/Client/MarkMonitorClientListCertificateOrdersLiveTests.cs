using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.IntegrationTests.Client;

// Hits the real MarkMonitor API using credentials from the environment (see .env / TestConsole/.env).
// Skips automatically when those variables aren't set, so it stays out of normal CI/unit runs.
public class MarkMonitorClientListCertificateOrdersLiveTests
{
    [Fact]
    public async Task ListCertificateOrdersAsync_WithLiveCredentials_ReturnsOrders()
    {
        if (!LiveApiCredentials.TryGet(out var baseUrl, out var apiToken, out var username, out var password))
        {
            // No live credentials in the environment; nothing to verify.
            return;
        }

        using var client = new MarkMonitorClient(baseUrl, apiToken, username, password);
        await client.AuthenticateAsync();

        var orders = await client.ListCertificateOrdersAsync(0, "", "", 100);

        // Mirrors TestConsole's TestListCertificateOrders, which treats an empty result as a
        // test-setup problem ("no certificates found, please add some to run this test") rather
        // than a valid outcome.
        Assert.NotEmpty(orders);

        foreach (var order in orders)
        {
            Assert.False(string.IsNullOrWhiteSpace(order.Id));
            Assert.False(string.IsNullOrWhiteSpace(order.Status));
        }
    }
}
