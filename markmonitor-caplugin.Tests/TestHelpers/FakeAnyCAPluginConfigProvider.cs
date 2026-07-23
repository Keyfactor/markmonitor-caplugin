using Keyfactor.AnyGateway.Extensions;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

public class FakeAnyCAPluginConfigProvider : IAnyCAPluginConfigProvider
{
    public Dictionary<string, object> CAConnectionData { get; set; } = new();

    public static FakeAnyCAPluginConfigProvider WithDefaults(string baseUrl = "https://api.markmonitor.test") =>
        new()
        {
            CAConnectionData = new Dictionary<string, object>
            {
                [MarkMonitorCAPluginConfig.ConfigConstants.ApiKey] = "test-api-key",
                [MarkMonitorCAPluginConfig.ConfigConstants.ApiUsername] = "test-user",
                [MarkMonitorCAPluginConfig.ConfigConstants.ApiPassword] = "test-password",
                [MarkMonitorCAPluginConfig.ConfigConstants.BaseUrl] = baseUrl,
                [MarkMonitorCAPluginConfig.ConfigConstants.OrgName] = "Test Org",
                [MarkMonitorCAPluginConfig.ConfigConstants.Enabled] = true
            }
        };
}
