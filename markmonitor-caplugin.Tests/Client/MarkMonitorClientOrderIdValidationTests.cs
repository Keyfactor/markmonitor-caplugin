using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientOrderIdValidationTests
{
    private const string NotAGuid = "../../etc/passwd";

    [Fact]
    public async Task GetSingleOrderAsync_WithNonGuidOrderId_ReturnsNullWithoutMakingARequest()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        var result = await client.GetSingleOrderAsync(NotAGuid);

        Assert.Null(result);
        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "GET", "/order/"));
    }

    [Fact]
    public async Task CancelCertificateAsync_WithNonGuidOrderId_ThrowsWithoutMakingARequest()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => client.CancelCertificateAsync(NotAGuid));

        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "PATCH", "/cancel"));
    }

    [Fact]
    public async Task RevokeCertificateAsync_WithNonGuidOrderId_ThrowsWithoutMakingARequest()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => client.RevokeCertificateAsync(NotAGuid));

        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "PATCH", "/revoke"));
    }
}
