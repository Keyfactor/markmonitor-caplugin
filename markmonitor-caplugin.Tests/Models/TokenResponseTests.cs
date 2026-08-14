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

using Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Models;

public class TokenResponseTests
{
    [Fact]
    public void Deserialize_RealApiShape_PopulatesExpiresIn()
    {
        // The real MarkMonitor /auth/v1/auth/authenticate response (confirmed against the live
        // sandbox) uses "expiresIn" (camelCase), not "expires_in". A mismatch here means ExpiresIn
        // silently deserializes to 0, which makes every token look instantly expired to any code
        // (like MarkMonitorClient's expiry tracking) that relies on it.
        const string json = """{"token":"abc123","expiresIn":3600}""";

        var result = JsonConvert.DeserializeObject<TokenResponse>(json);

        Assert.NotNull(result);
        Assert.Equal("abc123", result.BearerToken);
        Assert.Equal(3600, result.ExpiresIn);
    }
}
