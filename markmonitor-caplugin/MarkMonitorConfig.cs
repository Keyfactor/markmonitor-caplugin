// Copyright 2025 Keyfactor
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

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor;

/// <summary>
/// Represents the configuration settings required for the MarkMonitor CA Plugin.
/// </summary>
public class MarkMonitorConfig
{
    /// <summary>
    /// The API key used to authenticate with the MarkMonitor API.
    /// </summary>
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.ApiKey)]
    public string ApiKey { get; set; }

    /// <summary>
    /// The password for the MarkMonitor API service account.
    /// </summary>
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.ApiPassword)]
    public string ApiPassword { get; set; }

    /// <summary>
    /// The username for the MarkMonitor API service account.
    /// </summary>
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.ApiUsername)]
    public string ApiUsername { get; set; }

    /// <summary>
    /// The base URL for the MarkMonitor API.
    /// </summary>
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.BaseUrl)]
    public string BaseUrl { get; set; }

    /// <summary>
    /// The organization name used in MarkMonitor operations.
    /// </summary>
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.OrgName)]
    public string OrgName { get; set; }

    /// <summary>
    /// Indicates whether the MarkMonitor CA Plugin is enabled.
    /// </summary>
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.Enabled)]
    public bool Enabled { get; set; }

    /// <summary>
    /// The client identifier for the MarkMonitor API client.
    /// </summary>
    public string MarkMonitorApiClient { get; set; }
}