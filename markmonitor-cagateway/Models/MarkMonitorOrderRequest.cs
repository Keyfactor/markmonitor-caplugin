using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

/// <summary>
/// Represents a request to create an order.
/// </summary>
public class MarkMonitorCreateOrderRequest
{
    /// <summary>
    /// Gets or sets the additional emails associated with the order.
    /// </summary>
    [JsonProperty("additionalEmails")]
    public List<string> AdditionalEmails { get; set; }

    /// <summary>
    /// Gets or sets the organization ID.
    /// </summary>
    [JsonProperty("organizationId")]
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Gets or sets the group ID.
    /// </summary>
    [JsonProperty("groupId")]
    public Guid GroupId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to skip the price.
    /// </summary>
    [JsonProperty("skipPrice")]
    public bool SkipPrice { get; set; }

    /// <summary>
    /// Gets or sets the contacts associated with the order.
    /// </summary>
    [JsonProperty("contacts")]
    public List<Contact> Contacts { get; set; }

    /// <summary>
    /// Gets or sets the comments for the order.
    /// </summary>
    [JsonProperty("comments")]
    public string Comments { get; set; }

    /// <summary>
    /// Gets or sets the certificate details.
    /// </summary>
    [JsonProperty("cert")]
    public Certificate Cert { get; set; }

    /// <summary>
    /// Gets or sets the type of the certificate.
    /// </summary>
    [JsonProperty("certType")]
    public string CertType { get; set; }

    /// <summary>
    /// Gets or sets the locale of the order.
    /// </summary>
    [JsonProperty("locale")]
    public string Locale { get; set; }

    /// <summary>
    /// Gets or sets the provider of the certificate.
    /// </summary>
    [JsonProperty("provider")]
    public string Provider { get; set; }
}

/// <summary>
/// Represents a contact associated with the order.
/// </summary>
public class Contact
{
    /// <summary>
    /// Gets or sets the ID of the contact.
    /// </summary>
    [JsonProperty("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the contact types.
    /// </summary>
    [JsonProperty("contactTypes")]
    public List<ContactType> ContactTypes { get; set; }
}

/// <summary>
/// Represents a contact type.
/// </summary>
public class ContactType
{
    /// <summary>
    /// Gets or sets the type of the contact.
    /// </summary>
    [JsonProperty("type")]
    public string Type { get; set; }
}

/// <summary>
/// Represents the certificate details.
/// </summary>
public class Certificate
{
    /// <summary>
    /// Gets or sets the common name of the certificate.
    /// </summary>
    [JsonProperty("commonName")]
    public string CommonName { get; set; }

    /// <summary>
    /// Gets or sets the DNS names associated with the certificate.
    /// </summary>
    [JsonProperty("dnsNames")]
    public List<string> DnsNames { get; set; }

    /// <summary>
    /// Gets or sets the CSR of the certificate.
    /// </summary>
    [JsonProperty("csr")]
    public string Csr { get; set; }

    /// <summary>
    /// Gets or sets the server platform.
    /// </summary>
    [JsonProperty("serverPlatform")]
    public string ServerPlatform { get; set; }

    /// <summary>
    /// Gets or sets the algorithm hash.
    /// </summary>
    [JsonProperty("algorithmHash")]
    public string AlgorithmHash { get; set; }

    /// <summary>
    /// Gets or sets the root hash.
    /// </summary>
    [JsonProperty("rootHash")]
    public string RootHash { get; set; }

    /// <summary>
    /// Gets or sets the organization units.
    /// </summary>
    [JsonProperty("organizationUnits")]
    public List<string> OrganizationUnits { get; set; }

    /// <summary>
    /// Gets or sets the provider of the certificate.
    /// </summary>
    [JsonProperty("provider")]
    public string Provider { get; set; }

    /// <summary>
    /// Gets or sets the DCV method.
    /// </summary>
    [JsonProperty("dcvMethod")]
    public string DcvMethod { get; set; }

    /// <summary>
    /// Gets or sets the DCV emails.
    /// </summary>
    [JsonProperty("dcvEmails")]
    public List<DcvEmail> DcvEmails { get; set; }
}
