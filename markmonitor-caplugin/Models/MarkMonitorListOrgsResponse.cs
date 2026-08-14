using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

/// <summary>
/// Represents the response for listing organizations.
/// </summary>
public class MarkMonitorListOrgsResponse
{
    /// <summary>
    /// Gets or sets the content of the response.
    /// </summary>
    [JsonProperty("content")]
    public List<MarkMonitorOrganizationResponse> Content { get; set; }

    /// <summary>
    /// Gets or sets the pagination information.
    /// </summary>
    [JsonProperty("page")]
    public MarkMonitorPageInfo MarkMonitorPage { get; set; }
}