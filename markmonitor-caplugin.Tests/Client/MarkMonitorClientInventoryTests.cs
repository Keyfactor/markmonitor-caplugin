using System.Collections.Concurrent;
using System.Net;
using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientInventoryTests
{
    [Fact]
    public async Task GetCertificateInventoryAsync_WhenOneOrderHasNoCertYet_SkipsItButKeepsTheRest()
    {
        // A page containing one order with cert:null (plausible for a CREATED/DIGI_NEEDS_CSR order)
        // used to NRE partway through the foreach, get swallowed by the outer catch, and silently
        // drop every remaining order on that page - Synchronize() would report success while quietly
        // losing certificates from Command's inventory.
        var ordersJson = string.Join(",", new[]
        {
            SampleOrders.OrderWithNullCert("99999999-9999-9999-9999-999999999999", "CREATED"),
            SampleOrders.OrderWithCert("77777777-7777-7777-7777-777777777777", "DIGI_ISSUED"),
            SampleOrders.OrderWithCert("88888888-8888-8888-8888-888888888888", "DIGI_ISSUED")
        });
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrdersPage(ordersJson)));
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        var count = await client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None);

        Assert.Equal(2, count);
        var collected = buffer.ToList();
        Assert.Equal(2, collected.Count);
        Assert.DoesNotContain(collected, c => c.CARequestID == "99999999-9999-9999-9999-999999999999");
        Assert.Contains(collected, c => c.CARequestID == "77777777-7777-7777-7777-777777777777");
        Assert.Contains(collected, c => c.CARequestID == "88888888-8888-8888-8888-888888888888");
    }
}
