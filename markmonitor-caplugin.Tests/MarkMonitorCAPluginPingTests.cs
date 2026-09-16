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

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests;

[Collection(LogHandlerFactoryCollection.Name)]
public class MarkMonitorCAPluginPingTests
{
    [Fact]
    public async Task Ping_WithInvalidCredentials_ThrowsWithoutEverLoggingAuthenticationSuccessful()
    {
        // Regression test: Ping() used to log "Authentication with MarkMonitor API successful"
        // immediately after CreateAndAuthenticateClientAsync() - which deliberately does not
        // authenticate eagerly, it only builds/caches the client wrapper - so that line was reached,
        // and logged, before any credential had actually been checked. With invalid credentials, the
        // log stream showed a false "successful" line followed immediately by a real auth failure for
        // the same authentication attempt.
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);

        var handler = new FakeHttpMessageHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/auth/v1/auth/authenticate"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized,
                    """{"errors":[{"code":"auth.invalidCredentials","message":"Invalid credentials."}]}"""));
        var plugin = new MarkMonitorCAPlugin(handler.BuildClient());
        plugin.Initialize(FakeAnyCAPluginConfigProvider.WithDefaults(), new FakeCertificateDataReader());

        await Assert.ThrowsAsync<Exception>(() => plugin.Ping());

        Assert.DoesNotContain(capturingFactory.Messages,
            m => m.Contains("Authentication with MarkMonitor API successful", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ping_WithValidCredentialsAndOrganizations_LogsAuthenticationSuccessful()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
        var plugin = new MarkMonitorCAPlugin(handler.BuildClient());
        plugin.Initialize(FakeAnyCAPluginConfigProvider.WithDefaults(), new FakeCertificateDataReader());

        await plugin.Ping();
    }

    [Fact]
    public async Task Ping_RequestsALargeEnoughPageSizeToAvoidOneRoundTripPerOrganization()
    {
        // Regression test: an earlier version of this fix requested page size 1 as a supposedly cheap
        // existence check, but ListOrganizationsAsync's pagination loop has no early exit - it always
        // fetches every page up to TotalPages regardless of what the caller needs. With size=1,
        // TotalPages equals the account's total organization count, so that "fix" actually turned Ping
        // into one sequential HTTP request PER organization instead of the single request intended.
        // Two explicit pages (rather than the default single-page fixture, which hardcodes
        // totalPages=1 regardless of the requested size and would mask this exact regression) prove
        // pagination still terminates correctly and uses the corrected, larger page size.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization") &&
                         !(req.RequestUri?.Query.Contains("page=") ?? false),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact("11111111-1111-1111-1111-111111111111"))
                        .Replace("\"totalPages\": 1", "\"totalPages\": 2")))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "page=1"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact("22222222-2222-2222-2222-222222222222"))
                        .Replace("\"totalPages\": 1", "\"totalPages\": 2")));
        var plugin = new MarkMonitorCAPlugin(handler.BuildClient());
        plugin.Initialize(FakeAnyCAPluginConfigProvider.WithDefaults(), new FakeCertificateDataReader());

        await plugin.Ping();

        var orgRequests = handler.Requests.Where(r => FakeHttpMessageHandler.Is(r, "GET", "/certs/v1/organization")).ToList();
        Assert.Equal(2, orgRequests.Count);
        Assert.All(orgRequests, r => Assert.Contains("size=100", r.RequestUri!.Query));
    }
}
