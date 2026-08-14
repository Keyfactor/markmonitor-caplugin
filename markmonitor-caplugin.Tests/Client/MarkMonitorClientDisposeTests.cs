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
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientDisposeTests
{
    [Fact]
    public async Task Dispose_DisposesTheUnderlyingHttpClient()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = handler.BuildClient();

        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.AuthenticateAsync());
    }
}
