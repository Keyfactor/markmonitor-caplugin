using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

public class TokenResponse
{
    [JsonProperty("token")] public string BearerToken { get; set; }

    [JsonProperty("expiresIn")] public int ExpiresIn { get; set; }
}