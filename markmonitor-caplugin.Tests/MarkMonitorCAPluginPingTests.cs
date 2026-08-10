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
}
