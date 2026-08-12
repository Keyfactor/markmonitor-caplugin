using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

/// <summary>
/// Represents the response for listing groups from the MarkMonitor Auth API.
/// </summary>
public class MarkMonitorListGroupsResponse
{
    /// <summary>
    /// Gets or sets the groups returned.
    /// </summary>
    [JsonProperty("groups")]
    public List<MarkMonitorGroup> Groups { get; set; }

    /// <summary>
    /// Gets or sets the pagination information.
    /// </summary>
    [JsonProperty("page")]
    public MarkMonitorPageInfo MarkMonitorPage { get; set; }
}

/// <summary>
/// Represents a MarkMonitor group that an order can be associated with.
/// </summary>
public class MarkMonitorGroup
{
    /// <summary>
    /// Gets or sets the date the group was created.
    /// </summary>
    [JsonProperty("dateCreated")]
    public DateTime DateCreated { get; set; }

    /// <summary>
    /// Gets or sets the date the group was last updated.
    /// </summary>
    [JsonProperty("dateUpdated")]
    public DateTime DateUpdated { get; set; }

    /// <summary>
    /// Gets or sets the name of the group.
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the description of the group.
    /// </summary>
    [JsonProperty("description")]
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the ID of the group.
    /// </summary>
    [JsonProperty("id")]
    public string Id { get; set; }
}
