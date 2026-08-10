using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;
using Keyfactor.PKI.Enums.EJBCA;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests;

[Collection(LogHandlerFactoryCollection.Name)]
public class MarkMonitorCAPluginRevokeTests
{
    private const string OrderId = "11111111-1111-1111-1111-111111111111";

    private static FakeHttpMessageHandler BaseHandler() =>
        new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "GET", $"/certs/v1/order/{OrderId}"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert(OrderId, "DIGI_ISSUED", organizationId: SampleOrgs.DefaultOrgId)))
            .When(req => FakeHttpMessageHandler.Is(req, "PATCH", $"/certs/v1/order/{OrderId}/revoke"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

    [Fact]
    public async Task Revoke_WithOrgNameConfigured_RevokesSuccessfully()
    {
        var handler = BaseHandler();
        var plugin = new MarkMonitorCAPlugin(handler.BuildClient());
        plugin.Initialize(FakeAnyCAPluginConfigProvider.WithDefaults(), new FakeCertificateDataReader());

        var status = await plugin.Revoke(OrderId, "aabbcc", 0);

        Assert.Equal((int)EndEntityStatus.REVOKED, status);
    }

    [Fact]
    public async Task Revoke_WithBlankOrgNameConfigured_ThrowsWithoutMakingARequest()
    {
        // A blank/missing OrgId must never silently skip the cross-org ownership check on a live
        // connector instance - Initialize() doesn't re-validate the deserialized config, so this is
        // the only guard against a misconfigured OrgId reverting Revoke to "trust the caller".
        var handler = BaseHandler();
        var plugin = new MarkMonitorCAPlugin(handler.BuildClient());
        var configProvider = FakeAnyCAPluginConfigProvider.WithDefaults();
        configProvider.CAConnectionData[MarkMonitorCAPluginConfig.ConfigConstants.OrgName] = "";
        plugin.Initialize(configProvider, new FakeCertificateDataReader());

        await Assert.ThrowsAsync<Exception>(() => plugin.Revoke(OrderId, "aabbcc", 0));

        Assert.DoesNotContain(handler.Requests, r => FakeHttpMessageHandler.Is(r, "PATCH", "/revoke"));
    }

    [Fact]
    public async Task Revoke_WithBlankOrgNameConfigured_LogsTheFailureBeforeThrowing()
    {
        // Regression test: Revoke()'s catch block used to rethrow a wrapped exception with no
        // _logger call at all - every other terminating path in this class (Enroll, GetSingleRecord,
        // Synchronize, Ping) logs on exception, but a rejected Revoke left zero trace in the plugin's
        // own logs that the attempt was ever made or why it was refused.
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);

        var handler = BaseHandler();
        var plugin = new MarkMonitorCAPlugin(handler.BuildClient());
        var configProvider = FakeAnyCAPluginConfigProvider.WithDefaults();
        configProvider.CAConnectionData[MarkMonitorCAPluginConfig.ConfigConstants.OrgName] = "";
        plugin.Initialize(configProvider, new FakeCertificateDataReader());

        await Assert.ThrowsAsync<Exception>(() => plugin.Revoke(OrderId, "aabbcc", 0));

        Assert.Contains(capturingFactory.Messages,
            m => m.Contains("Revoke failed", StringComparison.OrdinalIgnoreCase) &&
                 m.Contains(OrderId, StringComparison.Ordinal));
    }
}
