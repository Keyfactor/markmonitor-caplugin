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
