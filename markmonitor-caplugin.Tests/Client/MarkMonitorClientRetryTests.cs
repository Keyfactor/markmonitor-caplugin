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
using System.Net.Http.Headers;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

/// <summary>
/// Coverage for SendWithRetryAsync (transient 5xx/429/network-failure retry with backoff) and the
/// configurable HttpClient timeout - and, just as importantly, that CreateCertificateOrder's
/// order-create POST deliberately does NOT go through that retry path (see its own comment).
/// </summary>
[Collection(LogHandlerFactoryCollection.Name)]
public class MarkMonitorClientRetryTests
{
    private static int OrgRequestCount(FakeHttpMessageHandler handler) =>
        handler.Requests.Count(r => FakeHttpMessageHandler.Is(r, "GET", "/certs/v1/organization"));

    [Fact]
    public async Task ListOrganizationsAsync_WhenATransient500IsFollowedBySuccess_RetriesAndSucceeds()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError,
                    """{"errors":[{"code":"request.genericError","message":"Transient failure"}]}"""),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.ListOrganizationsAsync();

        Assert.NotEmpty(result);
        Assert.Equal(2, OrgRequestCount(handler));
    }

    [Fact]
    public async Task ListOrganizationsAsync_WhenEveryAttemptReturns500_ThrowsAfterExactlyMaxRetryAttempts()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError,
                    """{"errors":[{"code":"request.genericError","message":"Persistent failure"}]}"""));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<Exception>(() => client.ListOrganizationsAsync());

        Assert.Contains("Persistent failure", ex.Message);
        Assert.Equal(3, OrgRequestCount(handler));
    }

    [Fact]
    public async Task ListOrganizationsAsync_WhenTheApiReturns400_DoesNotRetry()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest,
                    """{"errors":[{"code":"request.badRequest","message":"Bad request"}]}"""));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<Exception>(() => client.ListOrganizationsAsync());

        Assert.Contains("Bad request", ex.Message);
        Assert.Equal(1, OrgRequestCount(handler));
    }

    [Fact]
    public async Task ListOrganizationsAsync_WhenEveryAttemptThrowsANetworkError_ThrowsAfterExactlyMaxRetryAttempts()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .WhenAsync(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated connection reset")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ListOrganizationsAsync());

        Assert.Equal(3, OrgRequestCount(handler));
    }

    [Fact]
    public async Task ListOrganizationsAsync_On429WithRetryAfterHeader_WaitsForTheAdvertisedDuration()
    {
        var recordedDelays = new List<TimeSpan>();
        var rateLimited = FakeHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, "{}");
        rateLimited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                rateLimited,
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
        var client = handler.BuildClient(delay: (delay, _) =>
        {
            recordedDelays.Add(delay);
            return Task.CompletedTask;
        });
        await client.AuthenticateAsync();

        var result = await client.ListOrganizationsAsync();

        Assert.NotEmpty(result);
        Assert.Equal(2, OrgRequestCount(handler));
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(recordedDelays));
    }

    [Fact]
    public async Task ListOrganizationsAsync_On429WithAnExcessiveRetryAfterHeader_CapsTheDelay()
    {
        // Regression test: Retry-After is server-controlled input - a misbehaving/compromised
        // endpoint returning an enormous value must not be trusted verbatim, since that delay can
        // execute while _authLock is held (the auth call path) with no way to cancel it.
        var recordedDelays = new List<TimeSpan>();
        var rateLimited = FakeHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, "{}");
        rateLimited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromDays(1));
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                rateLimited,
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
        var client = handler.BuildClient(delay: (delay, _) =>
        {
            recordedDelays.Add(delay);
            return Task.CompletedTask;
        });
        await client.AuthenticateAsync();

        var result = await client.ListOrganizationsAsync();

        Assert.NotEmpty(result);
        Assert.Equal(TimeSpan.FromSeconds(120), Assert.Single(recordedDelays));
    }

    [Fact]
    public async Task ListOrganizationsAsync_On500WithoutRetryAfter_BacksOffExponentiallyWithJitter()
    {
        var recordedDelays = new List<TimeSpan>();
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
        var client = handler.BuildClient(delay: (delay, _) =>
        {
            recordedDelays.Add(delay);
            return Task.CompletedTask;
        });
        await client.AuthenticateAsync();

        await client.ListOrganizationsAsync();

        Assert.Equal(2, recordedDelays.Count);
        // Nominal 1s/2s ±25% jitter.
        Assert.InRange(recordedDelays[0].TotalSeconds, 0.75, 1.25);
        Assert.InRange(recordedDelays[1].TotalSeconds, 1.5, 2.5);
    }

    [Fact]
    public async Task CreateCertificateOrder_WhenTheApiReturns500_DoesNotRetryTheOrderCreatePost()
    {
        // The order-create POST must never be retried at this layer - a transport/5xx failure here
        // is ambiguous about whether MarkMonitor already created the order, and EnrollCertificateAsync's
        // dedup reservation (not this client-level retry) is what handles a caller-level retry safely.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError,
                    """{"errors":[{"code":"request.genericError","message":"Transient failure"}]}"""),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("11111111-1111-1111-1111-111111111111", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<Exception>(() => client.CreateCertificateOrder(new()
        {
            Cert = new() { CommonName = "test.mmcertdomain.com" },
            CertType = "SSL_DV_GEOTRUST",
            Provider = "DIGICERT"
        }));

        Assert.Equal(1, handler.Requests.Count(r => FakeHttpMessageHandler.Is(r, "POST", "/certs/v1/order")));
    }

    [Fact]
    public void Constructor_WithATimeoutSecondsValue_FlowsItToTheUnderlyingHttpClient()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler,
            timeoutSeconds: 45);

        Assert.Equal(TimeSpan.FromSeconds(45), client.HttpTimeout);
    }

    [Fact]
    public void Constructor_WithNoTimeoutSecondsValue_DefaultsTo120Seconds()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);

        Assert.Equal(TimeSpan.FromSeconds(120), client.HttpTimeout);
    }
}
