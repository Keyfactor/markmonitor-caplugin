using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

/// <summary>
///     Represents a request to reissue a certificate.
/// </summary>
public class MarkMonitorReissueRequest
{
    /// <summary>
    ///     Gets or sets the certificate details.
    /// </summary>
    [JsonProperty("cert")]
    public MarkMonitorCertificate Cert { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether to ignore organization check.
    /// </summary>
    [JsonProperty("ignoreOrgCheck")]
    public bool IgnoreOrgCheck { get; set; }

    /// <summary>
    ///     Gets or sets the additional emails associated with the request.
    /// </summary>
    [JsonProperty("additionalEmails")]
    public List<string> AdditionalEmails { get; set; }
}