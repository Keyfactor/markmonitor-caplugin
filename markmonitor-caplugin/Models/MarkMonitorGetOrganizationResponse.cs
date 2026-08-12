using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

/// <summary>
/// Represents an organization.
/// </summary>
public class MarkMonitorOrganizationResponse
{
    /// <summary>
    /// Gets or sets the date the organization was created.
    /// </summary>
    [JsonProperty("dateCreated")]
    public DateTime DateCreated { get; set; }

    /// <summary>
    /// Gets or sets the date the organization was last updated.
    /// </summary>
    [JsonProperty("dateUpdated")]
    public DateTime DateUpdated { get; set; }

    /// <summary>
    /// Gets or sets the name of the organization.
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the assumed name of the organization.
    /// </summary>
    [JsonProperty("assumedName")]
    public string AssumedName { get; set; }

    /// <summary>
    /// Gets or sets the first address line of the organization.
    /// </summary>
    [JsonProperty("address1")]
    public string Address1 { get; set; }

    /// <summary>
    /// Gets or sets the second address line of the organization.
    /// </summary>
    [JsonProperty("address2")]
    public string Address2 { get; set; }

    /// <summary>
    /// Gets or sets the city of the organization.
    /// </summary>
    [JsonProperty("city")]
    public string City { get; set; }

    /// <summary>
    /// Gets or sets the state of the organization.
    /// </summary>
    [JsonProperty("state")]
    public string State { get; set; }

    /// <summary>
    /// Gets or sets the country of the organization.
    /// </summary>
    [JsonProperty("country")]
    public string Country { get; set; }

    /// <summary>
    /// Gets or sets the zipcode of the organization.
    /// </summary>
    [JsonProperty("zipcode")]
    public string Zipcode { get; set; }

    /// <summary>
    /// Gets or sets the phone number of the organization.
    /// </summary>
    [JsonProperty("phone")]
    public string Phone { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the organization is active.
    /// </summary>
    [JsonProperty("active")]
    public bool Active { get; set; }

    /// <summary>
    /// Gets or sets the contacts of the organization.
    /// </summary>
    [JsonProperty("contacts")]
    public List<MarkMonitorGetContactResponse> Contacts { get; set; }

    /// <summary>
    /// Gets or sets the validations of the organization.
    /// </summary>
    [JsonProperty("validations")]
    public List<MarkMonitorOrgValidation> Validations { get; set; }

    /// <summary>
    /// Gets or sets the provider ID of the organization.
    /// </summary>
    [JsonProperty("providerId")]
    public int ProviderId { get; set; }

    /// <summary>
    /// Gets or sets the provider of the organization.
    /// </summary>
    [JsonProperty("provider")]
    public string Provider { get; set; }

    /// <summary>
    /// Gets or sets the ID of the organization.
    /// </summary>
    [JsonProperty("id")]
    public string Id { get; set; }
}

/// <summary>
/// Represents a validation within an organization.
/// </summary>
public class MarkMonitorOrgValidation
{
    /// <summary>
    /// Gets or sets the date the validation was created.
    /// </summary>
    [JsonProperty("dateCreated")]
    public DateTime DateCreated { get; set; }

    /// <summary>
    /// Gets or sets the date the validation was last updated.
    /// </summary>
    [JsonProperty("dateUpdated")]
    public DateTime DateUpdated { get; set; }

    /// <summary>
    /// Gets or sets the type of the validation.
    /// </summary>
    [JsonProperty("type")]
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the name of the validation.
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the date the validation was created by the provider.
    /// </summary>
    [JsonProperty("providerDateCreated")]
    public DateTime ProviderDateCreated { get; set; }

    /// <summary>
    /// Gets or sets the date until which the validation is valid.
    /// </summary>
    [JsonProperty("dateValidatedUntil")]
    public DateTime DateValidatedUntil { get; set; }

    /// <summary>
    /// Gets or sets the status of the validation.
    /// </summary>
    [JsonProperty("status")]
    public string Status { get; set; }

    /// <summary>
    /// Gets or sets the ID of the validation.
    /// </summary>
    [JsonProperty("id")]
    public string Id { get; set; }
}