using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

/// <summary>
/// Represents the certificate details.
/// </summary>
public class MarkMonitorCertificate
{
    [JsonProperty("dateCreated")] public DateTime DateCreated { get; set; }

    [JsonProperty("dateUpdated")] public DateTime DateUpdated { get; set; }

    [JsonProperty("commonName")] public string CommonName { get; set; }

    [JsonProperty("dnsNames")] public List<string> DnsNames { get; set; }

    [JsonProperty("providerDateCreated")] public DateTime ProviderDateCreated { get; set; }

    [JsonProperty("dateValidFrom")] public DateTime DateValidFrom { get; set; }

    [JsonProperty("dateValidUntil")] public DateTime DateValidUntil { get; set; }

    [JsonProperty("daysRemaining")] public int DaysRemaining { get; set; }

    [JsonProperty("csr")] public string Csr { get; set; }

    [JsonProperty("serverPlatform")] public string ServerPlatform { get; set; }

    [JsonProperty("algorithmHash")] public string AlgorithmHash { get; set; }

    [JsonProperty("rootHash")] public string RootHash { get; set; }

    [JsonProperty("organizationUnits")] public List<string> OrganizationUnits { get; set; }

    [JsonProperty("providerId")] public int ProviderId { get; set; }

    [JsonProperty("serialNumber")] public string SerialNumber { get; set; }

    [JsonProperty("revokeStatus")] public string RevokeStatus { get; set; }

    [JsonProperty("provider")] public string Provider { get; set; }

    [JsonProperty("dcvMethod")] public string DcvMethod { get; set; }

    [JsonProperty("dcvEmails")] public List<DcvEmail> DcvEmails { get; set; }

    [JsonProperty("intermediateCert")] public string IntermediateCert { get; set; }

    [JsonProperty("endEntityCert")] public string EndEntityCert { get; set; }

    [JsonProperty("rootCert")] public string RootCert { get; set; }

    [JsonProperty("id")] public string Id { get; set; }
}

/// <summary>
/// Represents pagination information.
/// </summary>
public class PageInfo
{
    [JsonProperty("size")] public int Size { get; set; }

    [JsonProperty("totalElements")] public int TotalElements { get; set; }

    [JsonProperty("totalPages")] public int TotalPages { get; set; }

    [JsonProperty("number")] public int Number { get; set; }
}