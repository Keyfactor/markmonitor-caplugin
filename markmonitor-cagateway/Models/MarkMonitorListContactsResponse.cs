using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

/// <summary>
/// Represents the response for listing contacts.
/// </summary>
public class MarkMonitorListContactsResponse
{
    /// <summary>
    /// Gets or sets the content of the response.
    /// </summary>
    [JsonProperty("content")]
    public List<MarkMonitorGetContactResponse> Content { get; set; }

    /// <summary>
    /// Gets or sets the pagination information.
    /// </summary>
    [JsonProperty("page")]
    public MarkMonitorPageInfo Page { get; set; }
}