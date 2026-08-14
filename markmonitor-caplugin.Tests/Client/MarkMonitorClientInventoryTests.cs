// Copyright 2026 Keyfactor
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

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
        var client = handler.BuildClient();
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

    [Fact]
    public async Task GetCertificateInventoryAsync_CancelledWhilePagingThroughOrders_StopsRatherThanFetchingEveryRemainingPage()
    {
        // Regression test: the cancelToken passed in was never threaded into the paginated
        // ListCertificateOrdersAsync HTTP fetch loop - the only place it was ever consulted was
        // certificatesBuffer.Add(..., cancelToken), which only runs after every page has already been
        // downloaded. A Command-initiated sync cancellation issued while still paging couldn't take
        // effect until the entire (potentially very large) order list had already been fetched.
        var page2Gate = new TaskCompletionSource();
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrdersPage(SampleOrders.OrderWithCert(
                        "77777777-7777-7777-7777-777777777777", "DIGI_ISSUED"), totalPages: 2)))
            .WhenGated(req => FakeHttpMessageHandler.Is(req, "GET", "page=1"), page2Gate.Task,
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrdersPage(SampleOrders.OrderWithCert(
                        "88888888-8888-8888-8888-888888888888", "DIGI_ISSUED"), totalPages: 2)));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();
        using var cts = new CancellationTokenSource();

        var syncTask = client.GetCertificateInventoryAsync("", "", 100, buffer, cts.Token);
        await Task.Delay(50); // Let the first page complete and the second page's gated fetch start.
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => syncTask);
        // The gated (never-released) second page must not have been allowed to complete the sync.
        Assert.False(page2Gate.Task.IsCompleted);
    }

    [Fact]
    public async Task GetCertificateInventoryAsync_WithMultiplePages_AddsEachPageToTheBufferAsItArrivesRatherThanWaitingForAllPages()
    {
        // Regression test: ListCertificateOrdersAsync used to accumulate every page's orders into one
        // in-memory list before ever returning, so GetCertificateInventoryAsync's buffer-writing loop
        // saw zero throughput until the entire (potentially huge) order history had downloaded. Page 1
        // must reach the buffer before page 2's gated fetch is ever released.
        var page2Gate = new TaskCompletionSource();
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrdersPage(SampleOrders.OrderWithCert(
                        "77777777-7777-7777-7777-777777777777", "DIGI_ISSUED"), totalPages: 2)))
            .WhenGated(req => FakeHttpMessageHandler.Is(req, "GET", "page=1"), page2Gate.Task,
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrdersPage(SampleOrders.OrderWithCert(
                        "88888888-8888-8888-8888-888888888888", "DIGI_ISSUED"), totalPages: 2)));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        var syncTask = client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None);

        // Page 1's certificate must already be in the buffer while page 2 is still gated/pending -
        // proving the buffer is fed page-by-page, not only after the whole order history downloads.
        var firstFromBuffer = buffer.Take(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        Assert.Equal("77777777-7777-7777-7777-777777777777", firstFromBuffer.CARequestID);
        Assert.False(page2Gate.Task.IsCompleted);

        page2Gate.SetResult();
        var count = await syncTask;

        Assert.Equal(2, count);
        var second = buffer.Take(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        Assert.Equal("88888888-8888-8888-8888-888888888888", second.CARequestID);
    }
}
