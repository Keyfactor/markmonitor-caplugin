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
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.IntegrationTests.Client;

// Hits the real MarkMonitor API using credentials from the environment (see .env / TestConsole/.env).
// Skips automatically when those variables aren't set, so it stays out of normal CI/unit runs.
public class MarkMonitorClientListOrgsLiveTests
{
    [Fact]
    public async Task ListOrganizationsAsync_WithLiveCredentials_ReturnsOrgsWithValidations()
    {
        if (!LiveApiCredentials.TryGet(out var baseUrl, out var apiToken, out var username, out var password))
        {
            // No live credentials in the environment; nothing to verify.
            return;
        }

        using var client = new MarkMonitorClient(baseUrl, apiToken, username, password);
        await client.AuthenticateAsync();

        var orgs = await client.ListOrganizationsAsync(0, 0);

        // Mirrors TestConsole's TestListOrgs, which treats an empty result as a test-setup problem
        // ("no organizations found, please add some to run this test") rather than a valid outcome.
        Assert.NotEmpty(orgs);

        foreach (var org in orgs)
        {
            Assert.False(string.IsNullOrWhiteSpace(org.Id));

            // TestListOrgs additionally logs each org's validations (name/type) - assert their shape
            // is well-formed rather than just that the call didn't throw. `name` is not always
            // present on real data - a DV-type validation, for example, comes back with only `type`
            // and no `name` field at all - so only `type` is guaranteed non-empty.
            foreach (var validation in org.Validations ?? new List<MarkMonitorOrgValidation>())
            {
                Assert.False(string.IsNullOrWhiteSpace(validation.Type));
            }
        }
    }
}
