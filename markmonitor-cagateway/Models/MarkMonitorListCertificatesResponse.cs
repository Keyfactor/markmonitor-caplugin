using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

/// <summary>
/// Represents the response for a list of orders.
/// </summary>
public class MarkMonitorListOrdersResponse
{
    /// <summary>
    /// Gets or sets the content of the response.
    /// </summary>
    [JsonProperty("content")]
    public List<OrderContent> Content { get; set; }

    /// <summary>
    /// Gets or sets the pagination information.
    /// </summary>
    [JsonProperty("page")]
    public PageInfo Page { get; set; }
}