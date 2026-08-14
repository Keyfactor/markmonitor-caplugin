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

/// <summary>Builds minimal-but-valid MarkMonitor organization/contact JSON fragments for tests.</summary>
public static class SampleOrgs
{
    public const string DefaultOrgId = "11111111-1111-1111-1111-111111111111";
    public const string DefaultContactId = "22222222-2222-2222-2222-222222222222";

    public static string OrgWithContact(string orgId = DefaultOrgId, string orgName = "Test Org",
        string contactId = DefaultContactId, string contactType = "ORGANIZATION_CONTACT") =>
        $$"""
          {
            "id": "{{orgId}}",
            "name": "{{orgName}}",
            "provider": "DIGICERT",
            "providerId": 1,
            "contacts": [
              {
                "id": "{{contactId}}",
                "firstName": "Test",
                "lastName": "Contact",
                "email": "test.contact@example.com",
                "contactTypes": [{ "type": "{{contactType}}" }]
              }
            ]
          }
          """;

    public static string OrgsListResponse(string orgJson) =>
        $$"""
          {
            "content": [{{orgJson}}],
            "page": { "size": 1, "totalElements": 1, "totalPages": 1, "number": 0 }
          }
          """;

    /// <summary>Builds a multi-org list response, for exercising resolution against a search result
    /// that contains more than one candidate (e.g. a substring-name false positive).</summary>
    public static string OrgsListResponse(params string[] orgJsons) =>
        $$"""
          {
            "content": [{{string.Join(",", orgJsons)}}],
            "page": { "size": {{orgJsons.Length}}, "totalElements": {{orgJsons.Length}}, "totalPages": 1, "number": 0 }
          }
          """;
}
