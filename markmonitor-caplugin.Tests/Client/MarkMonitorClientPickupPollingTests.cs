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

using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;
using Keyfactor.PKI.Enums.EJBCA;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

/// <summary>
/// Coverage for EnrollCertificateAsync's post-submit issuance polling - MarkMonitor always returns a
/// freshly-created order pending (never issued synchronously), so a fast-DCV product would otherwise
/// always report pending status back to Command even when it's about to issue within seconds.
/// </summary>
public class MarkMonitorClientPickupPollingTests
{
    private const string OrderId = "11111111-1111-1111-1111-111111111111";

    private static MarkMonitorConfig Config(int pickupRetries = 5, int pickupDelaySeconds = 10)
    {
        var config = SampleConfig.Default();
        config.PickupRetries = pickupRetries;
        config.PickupDelaySeconds = pickupDelaySeconds;
        return config;
    }

    private static int OrderFetchCount(FakeHttpMessageHandler handler) =>
        handler.Requests.Count(r => FakeHttpMessageHandler.Is(r, "GET", $"/certs/v1/order/{OrderId}"));

    private static FakeHttpMessageHandler BuildHandlerWithOrgAndCreate() =>
        new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted, SampleOrders.OrderWithCert(OrderId, "CREATED")));

    [Fact]
    public async Task EnrollCertificateAsync_WhenTheOrderIssuesOnASubsequentPoll_ReturnsGeneratedWithTheCertificate()
    {
        var handler = BuildHandlerWithOrgAndCreate()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{OrderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(OrderId, "CREATED")),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(OrderId, "CREATED")),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(OrderId, "DIGI_ISSUED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        Assert.Equal((int)EndEntityStatus.GENERATED, result.Status);
        Assert.NotNull(result.Certificate);
        Assert.Equal(3, OrderFetchCount(handler));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WhenIssuanceNeverCompletesWithinTheBudget_FallsBackToThePendingStatus()
    {
        var handler = BuildHandlerWithOrgAndCreate()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{OrderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(OrderId, "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config(pickupRetries: 3));

        Assert.Equal((int)EndEntityStatus.EXTERNALVALIDATION, result.Status);
        Assert.Equal(3, OrderFetchCount(handler));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WhenTheBudgetExhaustsWithStatusIssuedButNoCertBodyYet_ReportsInProcessNotAFalseGenerated()
    {
        // Regression test: if MarkMonitor's status flips to issued a moment before the cert body is
        // actually populated, IsPollingComplete correctly keeps polling (it requires both) - but if
        // the budget exhausts at exactly that moment, the order comes back with an "issued" status
        // and a null cert. Reporting GENERATED with Certificate=null would be an internally
        // inconsistent result no caller expects for a successful enrollment.
        var handler = BuildHandlerWithOrgAndCreate()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{OrderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderIssuedWithoutCertBody(OrderId)));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config(pickupRetries: 3));

        Assert.Equal((int)EndEntityStatus.INPROCESS, result.Status);
        Assert.Null(result.Certificate);
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithPickupRetriesZero_SkipsPollingEntirely()
    {
        var handler = BuildHandlerWithOrgAndCreate();
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config(pickupRetries: 0));

        Assert.Equal((int)EndEntityStatus.EXTERNALVALIDATION, result.Status);
        Assert.Equal(0, OrderFetchCount(handler));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WhenTheCreateOrderResponseItselfIsIssuedWithoutACertBody_ReportsInProcessNotAFalseGenerated()
    {
        // Same consistency check applies even with polling disabled entirely (PickupRetries=0) - the
        // inconsistency can in principle come straight from the order-create response itself, not
        // only from a poll response.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted, SampleOrders.OrderIssuedWithoutCertBody(OrderId)));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config(pickupRetries: 0));

        Assert.Equal((int)EndEntityStatus.INPROCESS, result.Status);
        Assert.Null(result.Certificate);
    }

    [Fact]
    public async Task EnrollCertificateAsync_WhenTheOrderReachesATerminalFailureWhilePolling_StopsPollingEarly()
    {
        var handler = BuildHandlerWithOrgAndCreate()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{OrderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(OrderId, "DIGI_FAILED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config(pickupRetries: 5));

        Assert.Equal((int)EndEntityStatus.FAILED, result.Status);
        // Stopped after the first poll observed the terminal state, not all 5 configured attempts.
        Assert.Equal(1, OrderFetchCount(handler));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WhenAPollAttemptFailsTransiently_RetriesOnTheNextAttemptInsteadOfFailingTheEnrollment()
    {
        var handler = BuildHandlerWithOrgAndCreate()
            .WhenAsync(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{OrderId}"),
                _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated connection reset")),
                _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated connection reset")),
                _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated connection reset")),
                _ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert(OrderId, "DIGI_ISSUED"))));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config(pickupRetries: 5));

        Assert.Equal((int)EndEntityStatus.GENERATED, result.Status);
    }

    [Fact]
    public async Task EnrollCertificateAsync_PollsUsingTheConfiguredDelayWithoutARealSleep()
    {
        var recordedDelays = new List<TimeSpan>();
        var handler = BuildHandlerWithOrgAndCreate()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{OrderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(OrderId, "CREATED")),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrders.OrderWithCert(OrderId, "DIGI_ISSUED")));
        var client = handler.BuildClient(delay: (delay, _) =>
        {
            recordedDelays.Add(delay);
            return Task.CompletedTask;
        });
        await client.AuthenticateAsync();

        await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(),
            Config(pickupRetries: 5, pickupDelaySeconds: 7));

        Assert.Equal(2, recordedDelays.Count);
        Assert.All(recordedDelays, d => Assert.Equal(TimeSpan.FromSeconds(7), d));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WhenAlreadyIssuedFromTheCreateOrderCall_DoesNotPollAtAll()
    {
        // A fast-issuing product (not observed for MarkMonitor today, but the check should still be
        // correct if it ever happens) shouldn't trigger any polling at all.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted, SampleOrders.OrderWithCert(OrderId, "DIGI_ISSUED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(), Config());

        Assert.Equal((int)EndEntityStatus.GENERATED, result.Status);
        Assert.Equal(0, OrderFetchCount(handler));
    }

    [Fact]
    public async Task EnrollCertificateAsync_WhenAPollAttemptFailsRepeatedly_EachOuterPollIsASingleHttpAttempt()
    {
        // Regression test: each poll's own order-fetch used to go through SendWithRetryAsync (up to
        // 3 attempts per poll), multiplying a single hung/slow poll far past the documented
        // PickupRetries*PickupDelaySeconds latency ceiling. It's now a single-attempt fetch, so a
        // failure is retried only by the OUTER polling loop (one more poll delay + attempt), not
        // absorbed 3-at-a-time inside a single outer attempt.
        var pollDelayCount = 0;
        var handler = BuildHandlerWithOrgAndCreate()
            .WhenAsync(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{OrderId}"),
                _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated connection reset")),
                _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated connection reset")),
                _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated connection reset")),
                _ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert(OrderId, "DIGI_ISSUED"))));
        var client = handler.BuildClient(delay: (_, _) =>
        {
            pollDelayCount++;
            return Task.CompletedTask;
        });
        await client.AuthenticateAsync();

        var result = await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", new Dictionary<string, string>(),
            Config(pickupRetries: 5));

        Assert.Equal((int)EndEntityStatus.GENERATED, result.Status);
        // 4 outer poll attempts (3 failures + 1 success), each a single HTTP request - if a poll
        // attempt still retried internally, the 3 failures would be absorbed within one outer
        // attempt (SendWithRetryAsync's own MaxRetryAttempts=3), needing only 2 outer attempts.
        Assert.Equal(4, pollDelayCount);
        Assert.Equal(4, OrderFetchCount(handler));
    }
}
