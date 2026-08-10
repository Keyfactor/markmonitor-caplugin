using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;
using Keyfactor.PKI.Enums.EJBCA;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientGetSingleOrderTests
{
    [Fact]
    public async Task GetSingleOrderAsync_ForIssuedOrder_ReturnsNonNullResultWithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order/55555555-5555-5555-5555-555555555555"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert("55555555-5555-5555-5555-555555555555", "DIGI_ISSUED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.GetSingleOrderAsync("55555555-5555-5555-5555-555555555555");

        Assert.NotNull(result);
        Assert.Equal("55555555-5555-5555-5555-555555555555", result.CARequestID);
        Assert.Equal((int)EndEntityStatus.GENERATED, result.Status);
        Assert.Null(result.RevocationDate);
    }

    [Fact]
    public async Task GetSingleOrderAsync_ForRevokedOrder_SetsRevocationDateFromDateValidUntil()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order/66666666-6666-6666-6666-666666666666"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert("66666666-6666-6666-6666-666666666666", "DIGI_REVOKED", "REVOKED", "2026-03-01T00:00:00Z")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.GetSingleOrderAsync("66666666-6666-6666-6666-666666666666");

        Assert.NotNull(result);
        Assert.Equal((int)EndEntityStatus.REVOKED, result.Status);
        Assert.Equal(new DateTime(2026, 3, 1), result.RevocationDate);
    }

    [Fact]
    public async Task GetSingleOrderAsync_WhenTheApiErrors_ThrowsInsteadOfReturningNull()
    {
        // Before this fix, any failure here (auth expiry, network blip, malformed body) was
        // logged and then swallowed to null - GetSingleRecord would log a false "retrieved
        // successfully" line right after the real error, and Command would see "not found"
        // instead of a real, actionable error.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order/77777777-7777-7777-7777-777777777777"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError,
                    """{"errors":[{"code":"request.genericError","message":"An unexpected error occurred."}]}"""));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<Exception>(
            () => client.GetSingleOrderAsync("77777777-7777-7777-7777-777777777777"));

        Assert.Contains("An unexpected error occurred", ex.Message);
    }

    [Fact]
    public async Task GetSingleOrderAsync_ForOrderWithNoCertYet_ReturnsAResultWithoutThrowing()
    {
        // Cert can be null for an order that hasn't progressed far enough yet (e.g. CREATED) - same
        // class of gap already fixed in GetCertificateInventoryAsync.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order/88888888-8888-8888-8888-888888888888"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithNullCert("88888888-8888-8888-8888-888888888888", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.GetSingleOrderAsync("88888888-8888-8888-8888-888888888888");

        Assert.NotNull(result);
        Assert.Equal("88888888-8888-8888-8888-888888888888", result.CARequestID);
        Assert.Null(result.Certificate);
        Assert.Null(result.RevocationDate);
    }

    [Fact]
    public async Task GetSingleOrderAsync_CalledRepeatedly_DoesNotAccumulateDuplicateAcceptHeaders()
    {
        // Regression test: FetchOrderAsync used to Add() an "application/json" Accept header on the
        // shared, cached HttpClient on every call with no preceding Remove() - since Accept is a
        // collection, not a single-value property, that appended a fresh duplicate entry per call,
        // unboundedly growing the header list (sent on every subsequent request) for the life of the
        // cached client. The Accept header is now set once, in the constructor.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order/99999999-9999-9999-9999-999999999999"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert("99999999-9999-9999-9999-999999999999", "DIGI_ISSUED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        for (var i = 0; i < 3; i++)
            await client.GetSingleOrderAsync("99999999-9999-9999-9999-999999999999");

        var orderRequests = handler.Requests.Where(r =>
            FakeHttpMessageHandler.Is(r, "GET", "/certs/v1/order/99999999-9999-9999-9999-999999999999"));
        Assert.All(orderRequests, r => Assert.Single(r.Headers.Accept));
    }
}
