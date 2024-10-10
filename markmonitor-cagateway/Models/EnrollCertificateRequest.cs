using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

public class EnrollCertificateRequest
{
    [JsonProperty("ca_id")] public int CaId { get; set; }

    [JsonProperty("cert_type")] public string CertType { get; set; }

    [JsonProperty("file_csr_encoding")] public string CsrEncoding { get; set; }

    [JsonProperty("issue_cert")] public bool IssueCert { get; set; }

    [JsonProperty("days")] public int Days { get; set; }

    [JsonProperty("file_csr")] public string Csr { get; set; }
}