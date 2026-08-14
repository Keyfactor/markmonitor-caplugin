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

using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests;

public class MarkMonitorCAPluginClientCachingTests
{
    [Fact]
    public async Task CreateAndAuthenticateClientAsync_CalledTwice_ReturnsTheSameClientInstance()
    {
        // Before the fix, every IAnyCAPlugin call (Enroll/Revoke/Ping/GetSingleRecord) built a brand
        // new MarkMonitorClient - and therefore a brand new HttpClient/HttpClientHandler and a fresh
        // authentication round-trip - from scratch. This asserts the plugin now hands back the same
        // client across multiple calls instead.
        var plugin = new MarkMonitorCAPlugin();
        plugin.Initialize(FakeAnyCAPluginConfigProvider.WithDefaults(), new FakeCertificateDataReader());

        var first = await plugin.CreateAndAuthenticateClientAsync();
        var second = await plugin.CreateAndAuthenticateClientAsync();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task CreateAndAuthenticateClientAsync_CalledConcurrently_OnlyBuildsOneClient()
    {
        var plugin = new MarkMonitorCAPlugin();
        plugin.Initialize(FakeAnyCAPluginConfigProvider.WithDefaults(), new FakeCertificateDataReader());

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => plugin.CreateAndAuthenticateClientAsync()));

        Assert.All(results, r => Assert.Same(results[0], r));
    }
}
