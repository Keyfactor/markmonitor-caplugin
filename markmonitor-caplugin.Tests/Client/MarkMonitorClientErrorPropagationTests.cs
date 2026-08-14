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

[Collection(LogHandlerFactoryCollection.Name)]
public class MarkMonitorClientErrorPropagationTests
{
    [Fact]
    public async Task ListOrganizationsAsync_WhenTheApiErrors_ThrowsInsteadOfReturningNull()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError,
                    """{"errors":[{"code":"request.genericError","message":"An unexpected error occurred."}]}"""));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<Exception>(() => client.ListOrganizationsAsync());

        Assert.Contains("An unexpected error occurred", ex.Message);
    }

    [Fact]
    public async Task ListCertificateOrdersAsync_WhenTheApiErrors_ThrowsInsteadOfReturningNull()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError,
                    """{"errors":[{"code":"request.genericError","message":"An unexpected error occurred."}]}"""));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<Exception>(() => client.ListCertificateOrdersAsync(0, "", "", 100));

        Assert.Contains("An unexpected error occurred", ex.Message);
    }

    [Fact]
    public async Task GetCertificateInventoryAsync_WhenListingOrdersFails_PropagatesTheRealErrorAndCompletesTheBuffer()
    {
        // Before this fix, a failure here was swallowed and GetCertificateInventoryAsync returned 0
        // - Synchronize() would report "0 certificates synced" as if the sync had genuinely found
        // nothing, rather than failing loudly on a real API error.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError,
                    """{"errors":[{"code":"request.genericError","message":"An unexpected error occurred."}]}"""));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var buffer = new BlockingCollection<AnyCAPluginCertificate>();

        await Assert.ThrowsAsync<Exception>(() =>
            client.GetCertificateInventoryAsync("", "", 100, buffer, CancellationToken.None));

        // The buffer must still be marked complete (in a `finally`) even on failure, or a consumer
        // blocked on GetConsumingEnumerable() would hang forever.
        Assert.True(buffer.IsAddingCompleted);
    }

    [Fact]
    public async Task ListGroupsAsync_WhenTheApiErrors_StillReturnsNullRatherThanThrowing()
    {
        // Unlike the org/order listing methods above, group resolution is an optional, best-effort
        // lookup - EnrollCertificateAsync deliberately proceeds without a group when this fails, so
        // this one method is expected to keep swallowing rather than throwing.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/auth/v1/group"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError,
                    """{"errors":[{"code":"request.genericError","message":"An unexpected error occurred."}]}"""));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.ListGroupsAsync(0, 0, "Engineering");

        Assert.Null(result);
    }

    [Fact]
    public async Task SendAndLogAsync_WhenTheUnderlyingCallFailsBeforeAnyResponse_StillLogsMethodAndUrl()
    {
        // Regression test: SendAndLogAsync only logged method/URL/status/elapsed-time after send()
        // returned successfully - a transport-level failure (timeout, DNS failure, connection refused/
        // reset, TLS failure) never produces a response at all, so that log line never ran, leaving
        // only whichever generic, URL-less message an enclosing caller's own catch happened to log.
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);

        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .WhenAsync(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated connection reset")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ListOrganizationsAsync());

        Assert.Contains(capturingFactory.Messages,
            m => m.Contains("GET", StringComparison.Ordinal) &&
                 m.Contains("/certs/v1/organization", StringComparison.Ordinal) &&
                 m.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }
}
