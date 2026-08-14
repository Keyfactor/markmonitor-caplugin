using Newtonsoft.Json;
using Org.BouncyCastle.Crypto;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

/// <summary>
///     Represents a request to create an order.
/// </summary>
public class MarkMonitorCreateOrderRequest
{
    /// <summary>
    ///     Gets or sets the additional emails associated with the order.
    /// </summary>
    [JsonProperty("additionalEmails", NullValueHandling = NullValueHandling.Ignore)]
    public List<string> AdditionalEmails { get; set; }

    /// <summary>
    ///     Gets or sets the organization ID.
    /// </summary>
    [JsonProperty("organizationId", NullValueHandling = NullValueHandling.Ignore)]
    public Guid OrganizationId { get; set; }

    /// <summary>
    ///     Gets or sets the group ID.
    /// </summary>
    [JsonProperty("groupId", NullValueHandling = NullValueHandling.Ignore)]
    public Guid? GroupId { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether to skip the price.
    /// </summary>
    [JsonProperty("skipPrice", NullValueHandling = NullValueHandling.Ignore)]
    public bool SkipPrice { get; set; }

    /// <summary>
    ///     Gets or sets the contacts associated with the order.
    /// </summary>
    [JsonProperty("contacts", NullValueHandling = NullValueHandling.Ignore)]
    public List<MarkMonitorCreateOrderContact> Contacts { get; set; }

    /// <summary>
    ///     Gets or sets the comments for the order.
    /// </summary>
    [JsonProperty("comments", NullValueHandling = NullValueHandling.Ignore)]
    public string? Comments { get; set; }

    /// <summary>
    ///     Gets or sets the certificate details.
    /// </summary>
    [JsonProperty("cert")]
    public MarkMonitorOrderRequestCert Cert { get; set; }

    /// <summary>
    ///     Gets or sets the type of the certificate.
    /// </summary>
    [JsonProperty("certType")]
    public string CertType { get; set; }

    /// <summary>
    ///     Gets or sets the locale of the order.
    /// </summary>
    [JsonProperty("locale", NullValueHandling = NullValueHandling.Ignore)]
    public string Locale { get; set; }

    /// <summary>
    ///     Gets or sets the provider of the certificate.
    /// </summary>
    [JsonProperty("provider")]
    public string Provider { get; set; }

    /// <summary>
    /// Returns a string that represents the current object as JSON.
    /// </summary>
    public string JSONString()
    {
        var output = JsonConvert.SerializeObject(this, Formatting.Indented);
        return output;
    }
}

/// <summary>
/// Represents the request for a MarkMonitor order certificate.
/// </summary>
public class MarkMonitorOrderRequestCert
{
    /// <summary>
    /// Gets or sets the common name.
    /// </summary>
    [JsonProperty("commonName", NullValueHandling = NullValueHandling.Ignore)]
    public string CommonName { get; set; }

    /// <summary>
    /// Gets or sets the DNS names.
    /// </summary>
    [JsonProperty("dnsNames", NullValueHandling = NullValueHandling.Ignore)]
    public List<string> DnsNames { get; set; }

    /// <summary>
    /// Gets or sets the CSR.
    /// </summary>
    [JsonProperty("csr", NullValueHandling = NullValueHandling.Ignore)]
    public string Csr { get; set; }

    /// <summary>
    /// Gets or sets the server platform.
    /// </summary>
    [JsonProperty("serverPlatform", NullValueHandling = NullValueHandling.Ignore)]
    public string ServerPlatform { get; set; }

    /// <summary>
    /// Gets or sets the algorithm hash.
    /// </summary>
    [JsonProperty("algorithmHash", NullValueHandling = NullValueHandling.Ignore)]
    public string AlgorithmHash { get; set; }

    /// <summary>
    /// Gets or sets the root hash.
    /// </summary>
    [JsonProperty("rootHash", NullValueHandling = NullValueHandling.Ignore)]
    public string RootHash { get; set; }

    /// <summary>
    /// Gets or sets the organization units.
    /// </summary>
    [JsonProperty("organizationUnits", NullValueHandling = NullValueHandling.Ignore)]
    public List<string> OrganizationUnits { get; set; }

    /// <summary>
    /// Gets or sets the provider.
    /// </summary>
    [JsonProperty("provider", NullValueHandling = NullValueHandling.Ignore)]
    public string Provider { get; set; }

    /// <summary>
    /// Gets or sets the DCV method.
    /// </summary>
    [JsonProperty("dcvMethod", NullValueHandling = NullValueHandling.Ignore)]
    public string DcvMethod { get; set; }

    /// <summary>
    /// Gets or sets the DCV emails.
    /// </summary>
    [JsonProperty("dcvEmails", NullValueHandling = NullValueHandling.Ignore)]
    public List<DcvEmail> DcvEmails { get; set; }
}

/// <summary>
/// Represents a contact associated with the order.
/// </summary>
public class MarkMonitorCreateOrderContact
{
    /// <summary>
    /// Gets or sets the ID of the contact.
    /// </summary>
    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the contact types of the contact.
    /// </summary>
    [JsonProperty("contactTypes", NullValueHandling = NullValueHandling.Ignore)]
    public List<MarkMonitorContactType> ContactTypes { get; set; }
}