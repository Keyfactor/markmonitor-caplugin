using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientAuthenticateTests
{
    [Fact]
    public async Task AuthenticateAsync_WithFakeSuccessResponse_Succeeds()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);

        await client.AuthenticateAsync();

        var authRequest = Assert.Single(handler.Requests);
        Assert.Equal("POST", authRequest.Method.Method);
        Assert.Contains("/auth/v1/auth/authenticate", authRequest.RequestUri!.ToString());
    }
}
