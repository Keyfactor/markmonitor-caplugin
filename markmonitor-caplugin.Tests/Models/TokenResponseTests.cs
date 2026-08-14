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
