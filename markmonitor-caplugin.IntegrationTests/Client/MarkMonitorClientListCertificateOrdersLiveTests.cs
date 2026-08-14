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

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.IntegrationTests.Client;

// Hits the real MarkMonitor API using credentials from the environment (see .env / TestConsole/.env).
// Skips automatically when those variables aren't set, so it stays out of normal CI/unit runs.
public class MarkMonitorClientListCertificateOrdersLiveTests
{
    [Fact]
    public async Task ListCertificateOrdersAsync_WithLiveCredentials_ReturnsOrders()
    {
        if (!LiveApiCredentials.TryGet(out var baseUrl, out var apiToken, out var username, out var password))
        {
            // No live credentials in the environment; nothing to verify.
            return;
        }

        using var client = new MarkMonitorClient(baseUrl, apiToken, username, password);
        await client.AuthenticateAsync();

        var orders = await client.ListCertificateOrdersAsync(0, "", "", 100);

        // Mirrors TestConsole's TestListCertificateOrders, which treats an empty result as a
        // test-setup problem ("no certificates found, please add some to run this test") rather
        // than a valid outcome.
        Assert.NotEmpty(orders);

        foreach (var order in orders)
        {
            Assert.False(string.IsNullOrWhiteSpace(order.Id));
            Assert.False(string.IsNullOrWhiteSpace(order.Status));
        }
    }
}
