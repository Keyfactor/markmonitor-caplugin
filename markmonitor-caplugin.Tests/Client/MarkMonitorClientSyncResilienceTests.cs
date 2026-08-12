using System.Collections.Concurrent;
using System.Net;
using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;
using Keyfactor.PKI.Enums.EJBCA;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

/// <summary>
/// Coverage for GetCertificateInventoryAsync's per-record isolation, error-rate circuit breaker,
/// and skip-unchanged optimization added to close the "one bad record aborts the whole sync" and
/// "every sync re-emits every order" gaps.
/// </summary>
public class MarkMonitorClientSyncResilienceTests
{
    private const string GoodId = "77777777-7777-7777-7777-777777777777";

    [Fact]
    public async Task GetCertificateInventoryAsync_WhenOneRecordThrowsDuringProcessing_SkipsItButKeepsTheRest()
    {
        // Simulates the realistic "bad record" failure surface: a downstream ICertificateDataReader
        // lookup failure (e.g. a transient database error) for one specific order, rather than a
        // malformed API response - MarkMonitorOrder's DateValidUntil/status fields are already
        // strongly-typed DateTime/string, so a genuinely malformed value would fail JSON
        // deserialization of the whole page, not per-record processing.
        var badId = "99999999-9999-9999-9999-999999999999";
        var otherGoodId = "88888888-8888-8888-8888-888888888888";
        var reader = new FakeCertificateDataReader();
        reader.ThrowForRequestIds.Add(badId);
        var ordersJson = string.Join(",", new[]
        {
            SampleOrders.OrderWithCert(GoodId, "DIGI_ISSUED"),
            SampleOrders.OrderWithCert(badId, "DIGI_ISSUED"),
            SampleOrders.OrderWithCert(otherGoodId, "DIGI_ISSUED")
        });
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrdersPage(ordersJson)));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        var count = await client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None, reader);

        Assert.Equal(2, count);
        var collected = buffer.ToList();
        Assert.Equal(2, collected.Count);
        Assert.DoesNotContain(collected, c => c.CARequestID == badId);
        Assert.Contains(collected, c => c.CARequestID == GoodId);
        Assert.Contains(collected, c => c.CARequestID == otherGoodId);
    }

    [Fact]
    public async Task GetCertificateInventoryAsync_WhenErrorRateExceeds25PercentAfter50Records_AbortsTheSync()
    {
        // 50 good records first (ratio stays 0% - the breaker isn't evaluated below the 50-record
        // sample size floor), then enough bad ones that the cumulative error rate crosses 25% - the
        // 67th record observed (17 bad / 67 = 25.37%) is where this trips.
        var reader = new FakeCertificateDataReader();
        var records = new List<string>();
        for (var i = 0; i < 50; i++)
            records.Add(SampleOrders.OrderWithCert(Guid.NewGuid().ToString(), "DIGI_ISSUED"));
        for (var i = 0; i < 20; i++)
        {
            var badId = Guid.NewGuid().ToString();
            reader.ThrowForRequestIds.Add(badId);
            records.Add(SampleOrders.OrderWithCert(badId, "DIGI_ISSUED"));
        }
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrdersPage(string.Join(",", records))));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        var ex = await Assert.ThrowsAsync<Exception>(
            () => client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None, reader));

        Assert.Contains("Aborting synchronization", ex.Message);
        Assert.True(buffer.IsAddingCompleted);
    }

    [Fact]
    public async Task GetCertificateInventoryAsync_WhenBelow50Records_DoesNotTripTheCircuitBreakerEvenIfEveryOneFails()
    {
        var reader = new FakeCertificateDataReader();
        var records = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            var badId = Guid.NewGuid().ToString();
            reader.ThrowForRequestIds.Add(badId);
            records.Add(SampleOrders.OrderWithCert(badId, "DIGI_ISSUED"));
        }
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrdersPage(string.Join(",", records))));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        var count = await client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None, reader);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetCertificateInventoryAsync_WhenARecordIsUnchanged_SkipsReEmittingIt()
    {
        var reader = new FakeCertificateDataReader();
        reader.RequestIdToStatus[GoodId] = (int)EndEntityStatus.GENERATED; // DIGI_ISSUED maps to GENERATED
        // SampleOrders.OrderWithCert's default dateValidUntil - matching this proves "truly unchanged"
        // (same status AND same expiration), not just a status coincidence.
        reader.ExpirationDateByRequestId[GoodId] = DateTime.Parse("2027-01-01T00:00:00Z").ToUniversalTime();
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrdersPage(SampleOrders.OrderWithCert(GoodId, "DIGI_ISSUED"))));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        var count = await client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None, reader);

        Assert.Equal(0, count);
        Assert.Empty(buffer.ToList());
    }

    [Fact]
    public async Task GetCertificateInventoryAsync_WhenStatusMatchesButExpirationDiffers_StillEmitsIt()
    {
        // Regression test: an out-of-band MarkMonitor reissue of the same order ID round-trips
        // DIGI_ISSUED -> DIGI_REISSUE_PENDING -> DIGI_ISSUED - if a sync only observes the order
        // before and after that round-trip, status alone looks unchanged even though the certificate
        // (and its expiration) is new. Comparing expiration too catches this.
        var reader = new FakeCertificateDataReader();
        reader.RequestIdToStatus[GoodId] = (int)EndEntityStatus.GENERATED;
        reader.ExpirationDateByRequestId[GoodId] = DateTime.Parse("2026-06-01T00:00:00Z").ToUniversalTime();
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    // Default dateValidUntil (2027-01-01) differs from the reader's stored 2026-06-01 -
                    // simulating a reissued certificate with a new validity window at the same status.
                    SampleOrders.OrdersPage(SampleOrders.OrderWithCert(GoodId, "DIGI_ISSUED"))));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        var count = await client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None, reader);

        Assert.Equal(1, count);
        Assert.Contains(buffer.ToList(), c => c.CARequestID == GoodId);
    }

    [Fact]
    public async Task GetCertificateInventoryAsync_WhenNoExpirationDataForTheRequestId_StillEmitsIt()
    {
        // Command not (yet) tracking an expiration for this request ID must not be treated as a
        // false match - erring toward re-emitting rather than skipping when uncertain.
        var reader = new FakeCertificateDataReader();
        reader.RequestIdToStatus[GoodId] = (int)EndEntityStatus.GENERATED;
        // No ExpirationDateByRequestId entry - GetExpirationDateByRequestId returns null.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrdersPage(SampleOrders.OrderWithCert(GoodId, "DIGI_ISSUED"))));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        var count = await client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None, reader);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetCertificateInventoryAsync_WhenARecordsStatusChanged_StillEmitsIt()
    {
        var reader = new FakeCertificateDataReader();
        reader.RequestIdToStatus[GoodId] = (int)EndEntityStatus.INPROCESS; // was pending, now issued
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrdersPage(SampleOrders.OrderWithCert(GoodId, "DIGI_ISSUED"))));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        var count = await client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None, reader);

        Assert.Equal(1, count);
        Assert.Contains(buffer.ToList(), c => c.CARequestID == GoodId);
    }

    [Fact]
    public async Task GetCertificateInventoryAsync_WhenNoCertificateDataReaderIsSupplied_AlwaysEmits()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrdersPage(SampleOrders.OrderWithCert(GoodId, "DIGI_ISSUED"))));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        var count = await client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetCertificateInventoryAsync_WithForceCompleteSyncTrue_ReEmitsEvenAnUnchangedRecord()
    {
        var reader = new FakeCertificateDataReader();
        reader.RequestIdToStatus[GoodId] = (int)EndEntityStatus.GENERATED;
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrdersPage(SampleOrders.OrderWithCert(GoodId, "DIGI_ISSUED"))));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        var count = await client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None, reader,
            forceCompleteSync: true);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetCertificateInventoryAsync_WithAPageLargerThanTheConcurrencyLimit_CountsEveryOutcomeExactlyOnce()
    {
        // Regression test for concurrent per-record processing (records within a page are now
        // processed with bounded concurrency instead of one at a time): a page bigger than the
        // concurrency limit (10) exercises multiple concurrent batches, so this asserts the
        // Interlocked counters aren't racing/double-counting/dropping increments under that load.
        var reader = new FakeCertificateDataReader();
        var records = new List<string>();
        var emittedIds = new List<string>();
        var skippedIds = new List<string>();
        var erroredIds = new List<string>();

        for (var i = 0; i < 15; i++)
        {
            var id = Guid.NewGuid().ToString();
            emittedIds.Add(id);
            records.Add(SampleOrders.OrderWithCert(id, "DIGI_ISSUED"));
        }
        for (var i = 0; i < 15; i++)
        {
            var id = Guid.NewGuid().ToString();
            skippedIds.Add(id);
            reader.RequestIdToStatus[id] = (int)EndEntityStatus.GENERATED;
            reader.ExpirationDateByRequestId[id] = DateTime.Parse("2027-01-01T00:00:00Z").ToUniversalTime();
            records.Add(SampleOrders.OrderWithCert(id, "DIGI_ISSUED"));
        }
        for (var i = 0; i < 10; i++)
        {
            var id = Guid.NewGuid().ToString();
            erroredIds.Add(id);
            reader.ThrowForRequestIds.Add(id);
            records.Add(SampleOrders.OrderWithCert(id, "DIGI_ISSUED"));
        }

        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrdersPage(string.Join(",", records))));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        var count = await client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None, reader);

        Assert.Equal(15, count);
        var collectedIds = buffer.ToList().Select(c => c.CARequestID).ToHashSet();
        Assert.Equal(new HashSet<string>(emittedIds), collectedIds);
        Assert.DoesNotContain(collectedIds, id => skippedIds.Contains(id) || erroredIds.Contains(id));
    }
}
