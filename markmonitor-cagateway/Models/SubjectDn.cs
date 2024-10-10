using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

public class SubjectDn
{
    [JsonProperty("C")] public string C { get; set; }

    [JsonProperty("L")] public string L { get; set; }

    [JsonProperty("O")] public string O { get; set; }

    [JsonProperty("CN")] public string Cn { get; set; }

    [JsonProperty("ST")] public string St { get; set; }

    [JsonProperty("emailAddress")] public string EmailAddress { get; set; }
}