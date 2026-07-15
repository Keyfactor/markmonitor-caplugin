using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientDisposeTests
{
    [Fact]
    public async Task Dispose_DisposesTheUnderlyingHttpClient()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);

        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.AuthenticateAsync());
    }
}
