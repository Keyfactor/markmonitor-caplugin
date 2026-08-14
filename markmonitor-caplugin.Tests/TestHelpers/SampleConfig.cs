using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

/// <summary>Builds a MarkMonitorConfig matching the fake handler's base URL/credentials.</summary>
public static class SampleConfig
{
    public static MarkMonitorConfig Default(string orgName = "Test Org") => new()
    {
        BaseUrl = "https://api.markmonitor.test",
        ApiKey = "key",
        ApiUsername = "user",
        ApiPassword = "pass",
        OrgName = orgName,
        Enabled = true,
        // 0 disables Enroll's post-submit issuance polling by default here - tests that specifically
        // exercise polling opt in explicitly rather than every other enroll test needing to stub a
        // GET /certs/v1/order/{id} route it doesn't otherwise care about.
        PickupRetries = 0
    };
}
