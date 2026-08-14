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
