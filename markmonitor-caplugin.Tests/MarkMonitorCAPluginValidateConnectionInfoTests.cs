using Keyfactor.AnyGateway.Extensions;

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
        var plugin = new MarkMonitorCAPlugin();

        var ex = await Assert.ThrowsAsync<AnyCAValidationException>(() =>
            plugin.ValidateCAConnectionInfo(ValidConnectionInfo("http://mm-api.internal")));

        Assert.Contains("https://", ex.Message);
    }

    [Theory]
    [InlineData("https://api.markmonitor.com")]
    [InlineData("HTTPS://api.markmonitor.com")]
    public async Task ValidateCAConnectionInfo_WithHttpsBaseUrl_DoesNotThrow(string baseUrl)
    {
        var plugin = new MarkMonitorCAPlugin();

        await plugin.ValidateCAConnectionInfo(ValidConnectionInfo(baseUrl));
    }
}
