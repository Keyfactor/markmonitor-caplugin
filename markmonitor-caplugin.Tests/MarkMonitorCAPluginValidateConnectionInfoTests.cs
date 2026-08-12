using System.Net;
using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests;

public class MarkMonitorCAPluginValidateConnectionInfoTests
{
    private static Dictionary<string, object> ValidConnectionInfo(string baseUrl = "https://api.markmonitor.com") =>
        new()
        {
            [MarkMonitorCAPluginConfig.ConfigConstants.ApiKey] = "key",
            [MarkMonitorCAPluginConfig.ConfigConstants.ApiUsername] = "user",
            [MarkMonitorCAPluginConfig.ConfigConstants.ApiPassword] = "pass",
            [MarkMonitorCAPluginConfig.ConfigConstants.BaseUrl] = baseUrl,
            [MarkMonitorCAPluginConfig.ConfigConstants.OrgName] = "Test Org"
        };

    [Fact]
    public async Task ValidateCAConnectionInfo_WithHttpBaseUrl_Throws()
    {
        // Fails the aggregated field checks before ever attempting a live call, so no client
        // injection is needed here.
        var plugin = new MarkMonitorCAPlugin();

        var ex = await Assert.ThrowsAsync<AnyCAValidationException>(() =>
            plugin.ValidateCAConnectionInfo(ValidConnectionInfo("http://mm-api.internal")));

        Assert.Contains("https://", ex.Message);
    }

    [Theory]
    [InlineData("https://api.markmonitor.com")]
    [InlineData("HTTPS://api.markmonitor.com")]
    public async Task ValidateCAConnectionInfo_WithHttpsBaseUrlAndWorkingCredentials_DoesNotThrow(string baseUrl)
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())));
        var plugin = new MarkMonitorCAPlugin(handler.BuildClient());

        await plugin.ValidateCAConnectionInfo(ValidConnectionInfo(baseUrl));
    }

    [Fact]
    public async Task ValidateCAConnectionInfo_WhenAuthenticationFails_ThrowsASanitizedErrorWithoutTheRawResponse()
    {
        var handler = new FakeHttpMessageHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/auth/v1/auth/authenticate"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized,
                    """{"errors":[{"code":"auth.invalidCredentials","message":"Invalid API key or credentials - secret-token-xyz"}]}"""));
        var plugin = new MarkMonitorCAPlugin(handler.BuildClient());

        var ex = await Assert.ThrowsAsync<AnyCAValidationException>(() =>
            plugin.ValidateCAConnectionInfo(ValidConnectionInfo()));

        Assert.Contains("Authentication failed", ex.Message);
        Assert.DoesNotContain("secret-token-xyz", ex.Message);
        Assert.DoesNotContain("auth.invalidCredentials", ex.Message);
    }

    [Fact]
    public async Task ValidateCAConnectionInfo_WhenListingOrganizationsFails_ThrowsASanitizedError()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError,
                    """{"errors":[{"code":"request.genericError","message":"An unexpected error occurred."}]}"""));
        var plugin = new MarkMonitorCAPlugin(handler.BuildClient());

        var ex = await Assert.ThrowsAsync<AnyCAValidationException>(() =>
            plugin.ValidateCAConnectionInfo(ValidConnectionInfo()));

        Assert.Contains("listing organizations failed", ex.Message);
        Assert.DoesNotContain("An unexpected error occurred", ex.Message);
    }

    [Fact]
    public async Task ValidateCAConnectionInfo_WithTimeoutSecondsZero_ThrowsASanitizedErrorRatherThanCrashing()
    {
        // Regression test: TimeoutSeconds=0 used to reach HttpClient.Timeout's own setter unguarded,
        // which .NET throws ArgumentOutOfRangeException for - propagating as a raw unhandled
        // exception instead of this method's designed sanitized AnyCAValidationException. No client
        // is injected here on purpose, since that's the only path that reaches the real
        // (non-test-seam) transient-client construction where TimeoutSeconds actually gets used. An
        // unroutable address (a closed local port) makes the live call fail fast and predictably;
        // what matters is which exception type surfaces, not why the call failed.
        var plugin = new MarkMonitorCAPlugin();
        var connectionInfo = ValidConnectionInfo("https://127.0.0.1:1");
        connectionInfo[MarkMonitorCAPluginConfig.ConfigConstants.TimeoutSeconds] = 0;

        var ex = await Assert.ThrowsAsync<AnyCAValidationException>(() =>
            plugin.ValidateCAConnectionInfo(connectionInfo));

        Assert.Contains("Authentication failed", ex.Message);
    }

    [Fact]
    public async Task ValidateCAConnectionInfo_WhenNoOrganizationsAreVisible_ThrowsASanitizedError()
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrgs.OrgsListResponse()));
        var plugin = new MarkMonitorCAPlugin(handler.BuildClient());

        var ex = await Assert.ThrowsAsync<AnyCAValidationException>(() =>
            plugin.ValidateCAConnectionInfo(ValidConnectionInfo()));

        Assert.Contains("listing organizations failed", ex.Message);
    }

    [Fact]
    public async Task ValidateCAConnectionInfo_WithAMalformedNumericField_ThrowsASanitizedErrorRatherThanCrashing()
    {
        // Regression test: the connectionInfo-to-MarkMonitorConfig deserialization used to sit
        // outside both inner try/catch blocks, so a non-numeric value for one of the Number-typed
        // fields threw a raw, unlogged JsonSerializationException instead of this method's designed
        // sanitized AnyCAValidationException. No client is injected here on purpose, since that's
        // the only path that reaches the real (non-test-seam) deserialization.
        var plugin = new MarkMonitorCAPlugin();
        var connectionInfo = ValidConnectionInfo();
        connectionInfo[MarkMonitorCAPluginConfig.ConfigConstants.PageSize] = "not-a-number";

        var ex = await Assert.ThrowsAsync<AnyCAValidationException>(() =>
            plugin.ValidateCAConnectionInfo(connectionInfo));

        Assert.Contains("could not be parsed", ex.Message);
    }

    [Fact]
    public async Task ValidateCAConnectionInfo_WithEnabledFalse_SkipsTheLiveConnectivityCheck()
    {
        // Regression test: Enabled's own documented purpose is letting an admin save the CA
        // connector before real MarkMonitor credentials are available. The new live-connectivity
        // check used to run unconditionally, breaking that pre-existing, documented workflow for a
        // connector saved with placeholder credentials while disabled. No client is injected and an
        // unroutable address is used on purpose - the assertion is that no live call is even
        // attempted, not that one succeeds.
        var plugin = new MarkMonitorCAPlugin();
        var connectionInfo = ValidConnectionInfo("https://127.0.0.1:1");
        connectionInfo[MarkMonitorCAPluginConfig.ConfigConstants.Enabled] = false;

        await plugin.ValidateCAConnectionInfo(connectionInfo);
    }

    [Fact]
    public async Task ValidateCAConnectionInfo_WithEnabledTrueOrAbsent_StillPerformsTheLiveConnectivityCheck()
    {
        var plugin = new MarkMonitorCAPlugin();
        var connectionInfo = ValidConnectionInfo("https://127.0.0.1:1");
        connectionInfo[MarkMonitorCAPluginConfig.ConfigConstants.Enabled] = true;

        await Assert.ThrowsAsync<AnyCAValidationException>(() =>
            plugin.ValidateCAConnectionInfo(connectionInfo));
    }
}
