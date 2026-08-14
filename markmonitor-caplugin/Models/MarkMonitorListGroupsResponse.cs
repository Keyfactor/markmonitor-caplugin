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
