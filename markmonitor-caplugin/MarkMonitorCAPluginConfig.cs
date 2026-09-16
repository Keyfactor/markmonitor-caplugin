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

using Keyfactor.AnyGateway.Extensions;

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
            },
            [ConfigConstants.TimeoutSeconds] = new()
            {
                Comments = "The HTTP request timeout, in seconds, for calls to the MarkMonitor API. Default is 120.",
                Hidden = false,
                DefaultValue = 120,
                Type = "Number"
            },
            [ConfigConstants.PageSize] = new()
            {
                Comments =
                    "The number of certificate orders requested per page during synchronization (1-500). Default is 100.",
                Hidden = false,
                DefaultValue = 100,
                Type = "Number"
            },
            [ConfigConstants.ForceCompleteSync] = new()
            {
                Comments =
                    "When true, bypasses the skip-unchanged optimization and re-emits every order on every synchronization. Default is false.",
                Hidden = false,
                DefaultValue = false,
                Type = "Boolean"
            },
            [ConfigConstants.PickupRetries] = new()
            {
                Comments =
                    "How many times to poll a freshly-created order for issuance before returning it in its still-pending state. 0 disables polling. Default is 5.",
                Hidden = false,
                DefaultValue = 5,
                Type = "Number"
            },
            [ConfigConstants.PickupDelaySeconds] = new()
            {
                Comments = "The delay, in seconds, between issuance pickup polls. Default is 10.",
                Hidden = false,
                DefaultValue = 10,
                Type = "Number"
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
            [EnrollmentConfigConstants.AdditionalEmails] = new()
            {
                Comments =
                    "List of 0 or more comma separated email addresses to send the certificate to via email after generation.",
                Hidden = false,
                DefaultValue = "",
                Type = "String"
            },
            [EnrollmentConfigConstants.MarkmonitorGroup] = new()
            {
                Comments = "The name or GUID of a Markmonitor group to use for the certificate request.",
                Hidden = false,
                DefaultValue = "",
                Type = "String"
            },
            [EnrollmentConfigConstants.MarkmonitorContact] = new()
            {
                Comments =
                    "The name or GUID of a Markmonitor contact to use for the certificate request. Will use default Markmonitor organization contact if not specified.",
                Hidden = false,
                DefaultValue = "",
                Type = "String"
            },
            [EnrollmentConfigConstants.DCVMethod] = new()
            {
                Comments =
                    "The method to use for Domain Control Validation (DCV). Valid values are EMAIL, DNS_CNAME_TOKEN, HTTP_TOKEN, DNS_TXT_TOKEN. Default is EMAIL.",
                Hidden = false,
                DefaultValue = "EMAIL",
                Type = "String"
            },
            [EnrollmentConfigConstants.Comments] = new()
            {
                Comments = "Comments to attach to the MarkMonitor order. Default is \"Requested via Keyfactor Command\".",
                Hidden = false,
                DefaultValue = "Requested via Keyfactor Command",
                Type = "String"
            },
            [EnrollmentConfigConstants.Locale] = new()
            {
                Comments = "Locale to use for the MarkMonitor order. Default is \"en\".",
                Hidden = false,
                DefaultValue = "en",
                Type = "String"
            },
            [EnrollmentConfigConstants.Provider] = new()
            {
                Comments = "The certificate provider to use for the order. Default is \"DIGICERT\" (currently the only provider MarkMonitor's API supports).",
                Hidden = false,
                DefaultValue = "DIGICERT",
                Type = "String"
            },
            [EnrollmentConfigConstants.RenewalWindowDays] = new()
            {
                Comments =
                    "For a RenewOrReissue enrollment, how many days before its expiration a prior certificate must be within before it is revoked after being replaced. Outside this window, the prior certificate is left unrevoked and the request is treated like a plain new issuance. Default is 90.",
                Hidden = false,
                DefaultValue = 90,
                Type = "Number"
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
        /// <summary>
        /// The HTTP request timeout (seconds) property name.
        /// </summary>
        public const string TimeoutSeconds = "TimeoutSeconds";
        /// <summary>
        /// The sync page size property name.
        /// </summary>
        public const string PageSize = "PageSize";
        /// <summary>
        /// The force-complete-sync flag property name.
        /// </summary>
        public const string ForceCompleteSync = "ForceCompleteSync";
        /// <summary>
        /// The pickup poll retry count property name.
        /// </summary>
        public const string PickupRetries = "PickupRetries";
        /// <summary>
        /// The pickup poll delay (seconds) property name.
        /// </summary>
        public const string PickupDelaySeconds = "PickupDelaySeconds";
    }

    /// <summary>
    /// Contains constant keys for enrollment configuration parameters.
    /// </summary>
    public static class EnrollmentConfigConstants
    {
        /// <summary>
        /// The additional emails parameter name.
        /// </summary>
        public const string AdditionalEmails = "AdditionalEmails";
        /// <summary>
        /// The MarkMonitor group parameter name.
        /// </summary>
        public const string MarkmonitorGroup = "MarkmonitorGroup";
        /// <summary>
        /// The MarkMonitor contact parameter name.
        /// </summary>
        public const string MarkmonitorContact = "MarkmonitorContact";
        /// <summary>
        /// The domain control validation method parameter name.
        /// </summary>
        public const string DCVMethod = "DCVMethod";
        /// <summary>
        /// The order comments parameter name.
        /// </summary>
        public const string Comments = "comments";
        /// <summary>
        /// The order locale parameter name.
        /// </summary>
        public const string Locale = "locale";
        /// <summary>
        /// The certificate provider parameter name.
        /// </summary>
        public const string Provider = "provider";
        /// <summary>
        /// The renewal window (days) parameter name.
        /// </summary>
        public const string RenewalWindowDays = "RenewalWindowDays";
    }
}