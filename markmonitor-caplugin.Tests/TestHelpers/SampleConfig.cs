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
