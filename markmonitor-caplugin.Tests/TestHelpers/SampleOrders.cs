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

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

/// <summary>Builds minimal-but-valid MarkMonitor order JSON fragments for tests.</summary>
public static class SampleOrders
{
    public static string OrderWithCert(string id, string status, string? revokeStatus = null,
        string dateValidUntil = "2027-01-01T00:00:00Z", string certType = "SSL_DV_GEOTRUST",
        string? organizationId = SampleOrgs.DefaultOrgId) =>
        $$"""
          {
            "id": "{{id}}",
            "certType": "{{certType}}",
            "status": "{{status}}",
            "organizationId": {{(organizationId == null ? "null" : $"\"{organizationId}\"")}},
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

    /// <summary>An order whose status has already flipped to issued but whose cert body hasn't been
    /// populated yet - the race PollForIssuanceAsync's own completion check (and, if the poll budget
    /// exhausts at exactly this moment, EnrollCertificateAsync's own result-consistency check) guards
    /// against, as distinct from OrderWithNullCert's "cert is entirely absent" pending state.</summary>
    public static string OrderIssuedWithoutCertBody(string id) =>
        $$"""
          {
            "id": "{{id}}",
            "certType": "SSL_DV_GEOTRUST",
            "status": "DIGI_ISSUED",
            "organizationId": "{{SampleOrgs.DefaultOrgId}}",
            "cert": {
              "commonName": "test.mmcertdomain.com",
              "csr": "-----BEGIN CERTIFICATE REQUEST-----\nMII...\n-----END CERTIFICATE REQUEST-----",
              "endEntityCert": null,
              "revokeStatus": null,
              "dateValidUntil": "2027-01-01T00:00:00Z",
              "daysRemaining": 200
            }
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
