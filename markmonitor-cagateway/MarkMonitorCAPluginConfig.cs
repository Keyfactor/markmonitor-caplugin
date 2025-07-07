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

using Keyfactor.AnyGateway.Extensions;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor;

/// <summary>
/// Provides configuration and annotation details for the MarkMonitor CA Plugin.
/// </summary>
public class MarkMonitorCAPluginConfig
{
    /// <summary>
    /// Returns a dictionary of plugin configuration property annotations.
    /// </summary>
    /// <returns>Dictionary mapping property names to their configuration info.</returns>
    public static Dictionary<string, PropertyConfigInfo> GetPluginAnnotations()
    {
        return new Dictionary<string, PropertyConfigInfo>
        {
            [ConfigConstants.ApiKey] = new()
            {
                Comments = "The API Key for the MarkMonitor API",
                Hidden = true,
                DefaultValue = "",
                Type = "String"
            },
            [ConfigConstants.ApiUsername] = new()
            {
                Comments = "Username for the MarkMonitor API service account",
                Hidden = false,
                DefaultValue = "",
                Type = "String"
            },
            [ConfigConstants.ApiPassword] = new()
            {
                Comments = "Password for the MarkMonitor API service account",
                Hidden = true,
                DefaultValue = "",
                Type = "String"
            },
            [ConfigConstants.BaseUrl] = new()
            {
                Comments =
                    "The Base URL for the MarkMonitor API - Usually either https://api.markmonitor.com",
                Hidden = false,
                DefaultValue = "https://api.markmonitor.com",
                Type = "String"
            },
            [ConfigConstants.OrgName] = new()
            {
                Comments =
                    "The name of the MarkMonitor Organization to use for the API calls (ex: MarkMonitor). You can also use the Organization ID in GUID format.",
                Hidden = false,
                DefaultValue = "",
                Type = "String"
            },
            [ConfigConstants.Enabled] = new()
            {
                Comments =
                    "Flag to Enable or Disable gateway functionality. Disabling is primarily used to allow creation of the CA prior to configuration information being available.",
                Hidden = false,
                DefaultValue = true,
                Type = "Boolean"
            }
        };
    }

    /// <summary>
    /// Returns a dictionary of template parameter annotations for certificate enrollment.
    /// </summary>
    /// <returns>Dictionary mapping template parameter names to their configuration info.</returns>
    public static Dictionary<string, PropertyConfigInfo> GetTemplateParameterAnnotations()
    {
        return new Dictionary<string, PropertyConfigInfo>
        {
            [EnrollmentConfigConstants.CertificateValidityInYears] = new()
            {
                Comments = "Number of years the certificate will be valid for",
                Hidden = false,
                DefaultValue = "1",
                Type = "Number"
            },
            [EnrollmentConfigConstants.Email] = new()
            {
                Comments = "Email address of the requestor",
                Hidden = false,
                DefaultValue = "",
                Type = "String"
            },
            [EnrollmentConfigConstants.OrganizationName] = new()
            {
                Comments = "Name of the organization to be validated against",
                Hidden = false,
                DefaultValue = "",
                Type = "String"
            }
        };
    }

    /// <summary>
    /// Contains constant keys for plugin configuration properties.
    /// </summary>
    public class ConfigConstants
    {
        /// <summary>
        /// The API key property name.
        /// </summary>
        public const string ApiKey = "ApiKey";
        /// <summary>
        /// The API password property name.
        /// </summary>
        public const string ApiPassword = "Password";
        /// <summary>
        /// The API username property name.
        /// </summary>
        public const string ApiUsername = "Username";
        /// <summary>
        /// The base URL property name.
        /// </summary>
        public const string BaseUrl = "BaseUrl";
        /// <summary>
        /// The organization name property name.
        /// </summary>
        public const string OrgName = "OrgId";
        /// <summary>
        /// The enabled flag property name.
        /// </summary>
        public const string Enabled = "Enabled";
    }

    /// <summary>
    /// Represents the configuration values for the MarkMonitor CA Plugin.
    /// </summary>
    public class Config
    {
        /// <summary>
        /// The API key used to authenticate with the MarkMonitor API.
        /// </summary>
        [JsonProperty(ConfigConstants.ApiKey)]
        public string ApiKey { get; set; }

        /// <summary>
        /// The password for the MarkMonitor API service account.
        /// </summary>
        [JsonProperty(ConfigConstants.ApiPassword)]
        public string ApiPassword { get; set; }

        /// <summary>
        /// The username for the MarkMonitor API service account.
        /// </summary>
        [JsonProperty(ConfigConstants.ApiUsername)]
        public string ApiUsername { get; set; }

        /// <summary>
        /// The base URL for the MarkMonitor API.
        /// </summary>
        [JsonProperty(ConfigConstants.BaseUrl)]
        public string BaseUrl { get; set; }

        /// <summary>
        /// The organization name or ID used in MarkMonitor operations.
        /// </summary>
        [JsonProperty(ConfigConstants.OrgName)]
        public string OrgName { get; set; }

        /// <summary>
        /// Indicates whether the MarkMonitor CA Plugin is enabled.
        /// </summary>
        [JsonProperty(ConfigConstants.Enabled)]
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// Contains constant keys for enrollment configuration parameters.
    /// </summary>
    public static class EnrollmentConfigConstants
    {
        /// <summary>
        /// The email address parameter name.
        /// </summary>
        public const string Email = "Email";
        /// <summary>
        /// The organization name parameter name.
        /// </summary>
        public const string OrganizationName = "OrganizationName";
        /// <summary>
        /// The certificate validity in years parameter name.
        /// </summary>
        public const string CertificateValidityInYears = "CertificateValidityInYears";
    }
}