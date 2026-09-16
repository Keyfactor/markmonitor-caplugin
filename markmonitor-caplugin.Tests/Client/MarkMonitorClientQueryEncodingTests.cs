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

using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientQueryEncodingTests
{
    [Fact]
    public async Task ListGroupsAsync_WithSpecialCharactersInName_UrlEncodesTheQueryValue()
    {
        // MarkmonitorGroup is a template parameter supplied by whoever requests the certificate
        // through Command, not just an admin - an unescaped "&"/"#"/space could inject extra query
        // parameters or corrupt the request line.
        const string groupName = "R&D Team #1";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/auth/v1/group"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    """{"groups":[],"page":{"size":0,"totalElements":0,"totalPages":0,"number":0}}"""));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await client.ListGroupsAsync(0, 0, groupName);

        var groupRequest = Assert.Single(handler.Requests, req => FakeHttpMessageHandler.Is(req, "GET", "/group"));
        var query = groupRequest.RequestUri!.Query;
        Assert.DoesNotContain("R&D Team #1", query);
        Assert.Contains(Uri.EscapeDataString(groupName), query);
    }

    [Fact]
    public async Task ListOrganizationsAsync_WithSpecialCharactersInName_UrlEncodesTheQueryValue()
    {
        const string orgName = "Acme & Sons";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, SampleOrgs.OrgsListResponse("")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        await client.ListOrganizationsAsync(0, 1, orgName);

        var orgRequest =
            Assert.Single(handler.Requests, req => FakeHttpMessageHandler.Is(req, "GET", "/organization"));
        var query = orgRequest.RequestUri!.Query;
        Assert.DoesNotContain("Acme & Sons", query);
        Assert.Contains(Uri.EscapeDataString(orgName), query);
    }
}
