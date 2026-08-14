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

    private const int MinTimeoutSeconds = 1;
    private const int MaxTimeoutSeconds = 120;
    private int _timeoutSeconds = 120;

    /// <summary>
    /// The HTTP request timeout, in seconds, for calls to the MarkMonitor API. Clamped to
    /// [<see cref="MinTimeoutSeconds"/>, <see cref="MaxTimeoutSeconds"/>] in the setter - a value
    /// &lt;= 0 would otherwise crash <c>HttpClient.Timeout</c>'s own setter with an unhandled
    /// ArgumentOutOfRangeException (verified: .NET rejects non-positive timeouts), and the upper
    /// bound is capped at this field's own pre-existing hardcoded default so a misconfigured value
    /// can never make a slow-MarkMonitor scenario worse than before this field was configurable -
    /// notably bounding how long AuthenticateAsync can hold the shared auth lock across its 3 retry
    /// attempts. A property initializer (not just the annotation's DefaultValue) is required here so
    /// an existing saved CA connection - created before this field existed, and so missing it
    /// entirely from its stored JSON - still gets a sane timeout rather than 0.
    /// </summary>
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.TimeoutSeconds)]
    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => _timeoutSeconds = Math.Clamp(value, MinTimeoutSeconds, MaxTimeoutSeconds);
    }

    private const int MinPageSize = 1;
    private const int MaxPageSize = 500;
    private int _pageSize = 100;

    /// <summary>
    /// The number of certificate orders requested per page during synchronization. Clamped to
    /// [<see cref="MinPageSize"/>, <see cref="MaxPageSize"/>] in the setter, so both a
    /// JSON-deserialized value and a directly-assigned one are always sane - not to be confused
    /// with <c>MarkMonitorClient.NameResolutionPageSize</c>, an unrelated fixed page size used only
    /// for org/group name lookups.
    /// </summary>
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.PageSize)]
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, MinPageSize, MaxPageSize);
    }

    /// <summary>
    /// When true, bypasses the skip-unchanged sync optimization and re-emits every order on every
    /// synchronization, regardless of whether Command already has it at the same status.
    /// </summary>
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.ForceCompleteSync)]
    public bool ForceCompleteSync { get; set; }

    private const int MaxPickupRetries = 20;
    private int _pickupRetries = 5;

    /// <summary>
    /// How many times to poll a freshly-created order for issuance before falling back to returning
    /// it in its still-pending state. 0 disables polling entirely. Clamped to
    /// [0, <see cref="MaxPickupRetries"/>] - combined with <see cref="PickupDelaySeconds"/>'s own
    /// cap, this bounds Enroll's worst-case added latency (and, since polling runs before the
    /// enrollment dedup reservation resolves, how long a concurrent duplicate call can block behind
    /// it) to a fixed, sane ceiling rather than an operator-configurable unbounded one.
    /// </summary>
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.PickupRetries)]
    public int PickupRetries
    {
        get => _pickupRetries;
        set => _pickupRetries = Math.Clamp(value, 0, MaxPickupRetries);
    }

    private const int MaxPickupDelaySeconds = 60;
    private int _pickupDelaySeconds = 10;

    /// <summary>The delay between pickup polls, in seconds. Clamped to
    /// [0, <see cref="MaxPickupDelaySeconds"/>].</summary>
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.PickupDelaySeconds)]
    public int PickupDelaySeconds
    {
        get => _pickupDelaySeconds;
        set => _pickupDelaySeconds = Math.Clamp(value, 0, MaxPickupDelaySeconds);
    }
}