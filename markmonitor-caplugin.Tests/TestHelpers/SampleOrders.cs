namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

/// <summary>Builds minimal-but-valid MarkMonitor order JSON fragments for tests.</summary>
public static class SampleOrders
{
    public static string OrderWithCert(string id, string status, string? revokeStatus = null,
        string dateValidUntil = "2027-01-01T00:00:00Z", string certType = "SSL_DV_GEOTRUST") =>
        $$"""
          {
            "id": "{{id}}",
            "certType": "{{certType}}",
            "status": "{{status}}",
            "cert": {
              "commonName": "test.mmcertdomain.com",
              "csr": "-----BEGIN CERTIFICATE REQUEST-----\nMII...\n-----END CERTIFICATE REQUEST-----",
              "endEntityCert": "-----BEGIN CERTIFICATE-----\nMII...\n-----END CERTIFICATE-----",
              "revokeStatus": {{(revokeStatus == null ? "null" : $"\"{revokeStatus}\"")}},
              "dateValidUntil": "{{dateValidUntil}}",
              "daysRemaining": 200
            }
          }
          """;

    public static string OrderWithNullCert(string id, string status) =>
        $$"""
          {
            "id": "{{id}}",
            "certType": "SSL_DV_GEOTRUST",
            "status": "{{status}}",
            "cert": null
          }
          """;

    public static string OrdersPage(string content, int totalPages = 1) =>
        $$"""
          {
            "content": [{{content}}],
            "page": { "size": 100, "totalElements": 1, "totalPages": {{totalPages}}, "number": 0 }
          }
          """;
}
