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
/// Represents a contact associated with the order.
/// </summary>
public class MarkMonitorGetContactResponse
{
    /// <summary>
    /// Gets or sets the date the contact was created.
    /// </summary>
    [JsonProperty("dateCreated")]
    public DateTime DateCreated { get; set; }

    /// <summary>
    /// Gets or sets the date the contact was last updated.
    /// </summary>
    [JsonProperty("dateUpdated")]
    public DateTime DateUpdated { get; set; }

    /// <summary>
    /// Gets or sets the first name of the contact.
    /// </summary>
    [JsonProperty("firstName")]
    public string FirstName { get; set; }

    /// <summary>
    /// Gets or sets the last name of the contact.
    /// </summary>
    [JsonProperty("lastName")]
    public string LastName { get; set; }

    /// <summary>
    /// Gets or sets the job title of the contact.
    /// </summary>
    [JsonProperty("jobTitle")]
    public string JobTitle { get; set; }

    /// <summary>
    /// Gets or sets the email address of the contact.
    /// </summary>
    [JsonProperty("email")]
    public string Email { get; set; }

    /// <summary>
    /// Gets or sets the phone number of the contact.
    /// </summary>
    [JsonProperty("phone")]
    public string Phone { get; set; }

    /// <summary>
    /// Gets or sets the organization ID of the contact.
    /// </summary>
    [JsonProperty("organizationId")]
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Gets or sets the phone extension of the contact.
    /// </summary>
    [JsonProperty("phoneExtension")]
    public string PhoneExtension { get; set; }

    /// <summary>
    /// Gets or sets the provider of the contact.
    /// </summary>
    [JsonProperty("provider")]
    public string Provider { get; set; }

    /// <summary>
    /// Gets or sets the contact types of the contact.
    /// </summary>
    [JsonProperty("contactTypes")]
    public List<MarkMonitorContactType> ContactTypes { get; set; }

    /// <summary>
    /// Gets or sets the ID of the contact.
    /// </summary>
    [JsonProperty("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the history of the contact.
    /// </summary>
    [JsonProperty("history")]
    public List<MarkMonitorContactHistory> History { get; set; }
}

/// <summary>
///     Represents a contact type.
/// </summary>
public class MarkMonitorContactType
{
    /// <summary>
    ///     Gets or sets the type of the contact.
    /// </summary>
    [JsonProperty("type")]
    public string Type { get; set; }
}

/// <summary>
/// Represents the history of a contact.
/// </summary>
public class MarkMonitorContactHistory
{
    /// <summary>
    /// Gets or sets the date the history entry was created.
    /// </summary>
    [JsonProperty("dateCreated")]
    public DateTime DateCreated { get; set; }

    /// <summary>
    /// Gets or sets the username associated with the history entry.
    /// </summary>
    [JsonProperty("userName")]
    public string UserName { get; set; }

    /// <summary>
    /// Gets or sets the text of the history entry.
    /// </summary>
    [JsonProperty("text")]
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the ID of the history entry.
    /// </summary>
    [JsonProperty("id")]
    public Guid Id { get; set; }
}