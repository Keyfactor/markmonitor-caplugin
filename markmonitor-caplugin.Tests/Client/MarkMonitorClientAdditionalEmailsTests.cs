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
using Newtonsoft.Json.Linq;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientAdditionalEmailsTests
{
    [Fact]
    public async Task EnrollCertificateAsync_WithSpaceAndCommaSeparatedEmails_DoesNotSendEmptyEntries()
    {
        // "a@b.com, c@d.com".Replace(" ", ",").Split(',') used to yield ["a@b.com", "", "c@d.com"] -
        // an empty string sent to MarkMonitor as one of the additional emails.
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrgs.OrgsListResponse(SampleOrgs.OrgWithContact())))
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/certs/v1/order"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Accepted,
                    SampleOrders.OrderWithCert("11111111-1111-1111-1111-111111111111", "CREATED")));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();
        var config = SampleConfig.Default();
        var productParams = new Dictionary<string, string> { ["additionalEmails"] = "a@b.com, c@d.com" };

        await client.EnrollCertificateAsync(SampleCsr.Pem, "CN=test.mmcertdomain.com",
            new Dictionary<string, string[]>(), "SslDvGeotrust", productParams, config);

        var orderRequest = Assert.Single(handler.Requests, req => FakeHttpMessageHandler.Is(req, "POST", "/order"));
        var body = await orderRequest.Content!.ReadAsStringAsync();
        var sentEmails = JObject.Parse(body)["additionalEmails"]!.Values<string>().ToList();
        Assert.Equal(new[] { "a@b.com", "c@d.com" }, sentEmails);
    }
}
