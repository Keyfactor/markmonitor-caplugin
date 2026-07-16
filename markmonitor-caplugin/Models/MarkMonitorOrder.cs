using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

/// <summary>
///     Represents the content of an order.
/// </summary>
public class OrderContent
{
    /// <summary>
    ///     Gets or sets the date the order was created.
    /// </summary>
    [JsonProperty("dateCreated")]
    public DateTime DateCreated { get; set; }

    /// <summary>
    ///     Gets or sets the date the order was last updated.
    /// </summary>
    [JsonProperty("dateUpdated")]
    public DateTime DateUpdated { get; set; }

    /// <summary>
    ///     Gets or sets the comments for the order.
    /// </summary>
    [JsonProperty("comments")]
    public string Comments { get; set; }

    /// <summary>
    ///     Gets or sets the additional emails associated with the order.
    /// </summary>
    [JsonProperty("additionalEmails")]
    public List<string> AdditionalEmails { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether renewal notifications are disabled.
    /// </summary>
    [JsonProperty("renewalNotificationsDisabled")]
    public bool RenewalNotificationsDisabled { get; set; }

    /// <summary>
    ///     Gets or sets the locale of the order.
    /// </summary>
    [JsonProperty("locale")]
    public string Locale { get; set; }

    /// <summary>
    ///     Gets or sets the certificate details.
    /// </summary>
    [JsonProperty("cert")]
    public MarkMonitorCertificate Cert { get; set; }

    /// <summary>
    ///     Gets or sets the type of the certificate.
    /// </summary>
    [JsonProperty("certType")]
    public string CertType { get; set; }

    /// <summary>
    ///     Gets or sets the price details of the order.
    /// </summary>
    [JsonProperty("price")]
    public Price Price { get; set; }

    /// <summary>
    ///     Gets or sets the group ID associated with the order.
    /// </summary>
    [JsonProperty("groupId")]
    public string GroupId { get; set; }

    /// <summary>
    ///     Gets or sets the organization ID that owns this order.
    /// </summary>
    [JsonProperty("organizationId")]
    public string OrganizationId { get; set; }

    /// <summary>
    ///     Gets or sets the contacts associated with the order.
    /// </summary>
    [JsonProperty("contacts")]
    public List<string> Contacts { get; set; }

    /// <summary>
    ///     Gets or sets the provider ID.
    /// </summary>
    [JsonProperty("providerId")]
    public int ProviderId { get; set; }

    /// <summary>
    ///     Gets or sets the provider name.
    /// </summary>
    [JsonProperty("provider")]
    public string Provider { get; set; }

    /// <summary>
    ///     Gets or sets the status of the order.
    /// </summary>
    [JsonProperty("status")]
    public string Status { get; set; }

    /// <summary>
    ///     Gets or sets the ID of the order.
    /// </summary>
    [JsonProperty("id")]
    public string Id { get; set; }

    /// <summary>
    ///     Gets or sets the history of the order.
    /// </summary>
    [JsonProperty("history")]
    public List<History> History { get; set; }
}

/// <summary>
///     Represents the price details of an order.
/// </summary>
public class Price
{
    [JsonProperty("certType")] public string CertType { get; set; }

    [JsonProperty("provider")] public string Provider { get; set; }

    [JsonProperty("term")] public int Term { get; set; }

    [JsonProperty("totalPrice")] public decimal TotalPrice { get; set; }

    [JsonProperty("products")] public List<Product> Products { get; set; }

    [JsonProperty("errorMessage")] public string ErrorMessage { get; set; }
}

/// <summary>
///     Represents a product in the price details.
/// </summary>
public class Product
{
    [JsonProperty("product")] public string ProductName { get; set; }

    [JsonProperty("item")] public string Item { get; set; }

    [JsonProperty("price")] public decimal Price { get; set; }
}

/// <summary>
///     Represents a DCV email.
/// </summary>
public class DcvEmail
{
    [JsonProperty("dnsName")] public string DnsName { get; set; }

    [JsonProperty("emailDomain")] public string EmailDomain { get; set; }

    [JsonProperty("email")] public string Email { get; set; }
}

/// <summary>
///     Represents the history of an order.
/// </summary>
public class History
{
    [JsonProperty("dateCreated")] public DateTime DateCreated { get; set; }

    [JsonProperty("userName")] public string UserName { get; set; }

    [JsonProperty("text")] public string Text { get; set; }

    [JsonProperty("id")] public string Id { get; set; }
}