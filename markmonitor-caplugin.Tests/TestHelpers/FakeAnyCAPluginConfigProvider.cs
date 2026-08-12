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
                [MarkMonitorCAPluginConfig.ConfigConstants.Enabled] = true,
                // 0 disables Enroll's post-submit issuance polling by default here - tests that
                // specifically exercise polling opt in explicitly rather than every other test needing
                // to stub a GET /certs/v1/order/{id} route it doesn't otherwise care about.
                [MarkMonitorCAPluginConfig.ConfigConstants.PickupRetries] = 0
            }
        };
}
