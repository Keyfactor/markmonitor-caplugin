// Copyright 2024 Keyfactor
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

public class MarkMonitorCAPluginConfig
{
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
            // [EnrollmentConfigConstants.OrganizationAddress] = new()
            // {
            //     Comments = "Address of the organization to be validated against",
            //     Hidden = false,
            //     DefaultValue = "",
            //     Type = "String"
            // },
            // [EnrollmentConfigConstants.OrganizationCity] = new()
            // {
            //     Comments = "City of the organization to be validated against",
            //     Hidden = false,
            //     DefaultValue = "",
            //     Type = "String"
            // },
            // [EnrollmentConfigConstants.OrganizationState] = new()
            // {
            //     Comments = "Full state name of the organization to be validated against",
            //     Hidden = false,
            //     DefaultValue = "",
            //     Type = "String"
            // },
            // [EnrollmentConfigConstants.OrganizationCountry] = new()
            // {
            //     Comments = "2 character abbreviation of the country of the organization to be validated against",
            //     Hidden = false,
            //     DefaultValue = "",
            //     Type = "String"
            // },
            // [EnrollmentConfigConstants.OrganizationPhone] = new()
            // {
            //     Comments = "Phone number of the organization to be validated against",
            //     Hidden = false,
            //     DefaultValue = "",
            //     Type = "String"
            // },
            // [EnrollmentConfigConstants.RegistrationAgent] = new()
            // {
            //     Comments =
            //         "Registration agent name assigned to the organization when its documents were filed for registration",
            //     Hidden = false,
            //     DefaultValue = "",
            //     Type = "String"
            // },
            // [EnrollmentConfigConstants.RegistrationNumber] = new()
            // {
            //     Comments =
            //         "Registration number assigned to the organization when its documents were filed for registration",
            //     Hidden = false,
            //     DefaultValue = "",
            //     Type = "String"
            // },
            // [EnrollmentConfigConstants.RootCAType] = new()
            // {
            //     Comments =
            //         "The certificate's root CA - Depending on certificate expiration date, SHA_1 not be allowed. Will default to SHA_2 if expiration date exceeds sha1 allowed date. Options are MarkMonitor_SHA_1, MarkMonitor_SHA_2, STARFIELD_SHA_1, or STARFIELD_SHA_2.",
            //     Hidden = false,
            //     DefaultValue = "MarkMonitor_SHA_2",
            //     Type = "String"
            // }
        };
    }

    public class ConfigConstants
    {
        public const string ApiKey = "ApiKey";
        public const string ApiPassword = "Password";
        public const string ApiUsername = "Username";
        public const string BaseUrl = "BaseUrl";
        public const string OrgName = "OrgId";
        public const string Enabled = "Enabled";
    }

    public class Config
    {
        [JsonProperty(ConfigConstants.ApiKey)] public string ApiKey { get; set; }

        [JsonProperty(ConfigConstants.ApiPassword)]
        public string ApiPassword { get; set; }

        [JsonProperty(ConfigConstants.ApiUsername)]
        public string ApiUsername { get; set; }

        [JsonProperty(ConfigConstants.BaseUrl)]
        public string BaseUrl { get; set; }

        [JsonProperty(ConfigConstants.OrgName)]
        public string OrgName { get; set; }

        [JsonProperty(ConfigConstants.Enabled)]
        public bool Enabled { get; set; }
    }

    public static class EnrollmentConfigConstants
    {
        // public const string LastName = "LastName";
        // public const string FirstName = "FirstName";
        public const string Email = "Email";
        // public const string Phone = "Phone";

        public const string OrganizationName = "OrganizationName";
        // public const string OrganizationAddress = "OrganizationAddress";
        // public const string OrganizationCity = "OrganizationCity";
        // public const string OrganizationState = "OrganizationState";
        // public const string OrganizationCountry = "OrganizationCountry";
        // public const string OrganizationPhone = "OrganizationPhone";

        // public const string JobTitle = "JobTitle";
        // public const string RegistrationAgent = "RegistrationAgent";
        // public const string RegistrationNumber = "RegistrationNumber";
        //
        // public const string RootCAType = "RootCAType";
        // public const string SlotSize = "SlotSize";
        public const string CertificateValidityInYears = "CertificateValidityInYears";
    }
}