using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.IntegrationTests.Client;

// Hits the real MarkMonitor API using credentials from the environment (see .env / TestConsole/.env).
// Skips automatically when those variables aren't set, so it stays out of normal CI/unit runs.
public class MarkMonitorClientAuthenticateLiveTests
{
    [Fact]
    public async Task AuthenticateAsync_WithLiveCredentials_Succeeds()
    {
        if (!LiveApiCredentials.TryGet(out var baseUrl, out var apiToken, out var username, out var password))
        {
            // No live credentials in the environment; nothing to verify.
            return;
        }

        using var client = new MarkMonitorClient(baseUrl, apiToken, username, password);

        // AuthenticateAsync throws on a failed authentication, so "did not throw" is the real
        // assertion here - mirroring TestConsole's TestAuthenticate.
        var exception = await Record.ExceptionAsync(() => client.AuthenticateAsync());

        Assert.Null(exception);
    }
}
