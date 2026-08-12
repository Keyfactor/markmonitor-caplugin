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
}
