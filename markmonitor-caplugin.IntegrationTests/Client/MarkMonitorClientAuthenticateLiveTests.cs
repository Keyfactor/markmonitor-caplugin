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
public class MarkMonitorClientAuthenticateLiveTests
{
    [Fact]
    public async Task AuthenticateAsync_WithLiveCredentials_Succeeds()
    {
        if (!LiveApiCredentials.TryGet(out var baseUrl, out var apiToken, out var username, out var password))
        {
            // No live credentials in the environment; nothing to verify.
            return;
        }

        using var client = new MarkMonitorClient(baseUrl, apiToken, username, password);

        // AuthenticateAsync throws on a failed authentication, so "did not throw" is the real
        // assertion here - mirroring TestConsole's TestAuthenticate.
        var exception = await Record.ExceptionAsync(() => client.AuthenticateAsync());

        Assert.Null(exception);
    }
}
