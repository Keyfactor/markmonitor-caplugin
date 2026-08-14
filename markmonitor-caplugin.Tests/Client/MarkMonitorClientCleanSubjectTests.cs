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

public class MarkMonitorClientCleanSubjectTests
{
    private static async Task<string> EnrollAndGetSentCommonName(string subject)
    {
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

        await client.EnrollCertificateAsync(SampleCsr.Pem, subject, new Dictionary<string, string[]>(),
            "SslDvGeotrust", new Dictionary<string, string>(), config);

        var orderRequest = handler.Requests.Single(req => FakeHttpMessageHandler.Is(req, "POST", "/order"));
        var body = await orderRequest.Content!.ReadAsStringAsync();
        return JObject.Parse(body)["cert"]!["commonName"]!.Value<string>()!;
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithCommaEscapedInCommonName_DoesNotTruncateAtTheEscapedComma()
    {
        // Plain IndexOf(",") string-slicing would cut this off at "Doe" instead of the real CN
        // "Doe, John" - X509Name's RFC 2253 parser handles the backslash escape correctly.
        var commonName = await EnrollAndGetSentCommonName("CN=Doe\\, John,O=Acme");

        Assert.Equal("Doe, John", commonName);
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithSimpleCommonName_StillWorks()
    {
        var commonName = await EnrollAndGetSentCommonName("CN=test.mmcertdomain.com");

        Assert.Equal("test.mmcertdomain.com", commonName);
    }

    [Fact]
    public async Task EnrollCertificateAsync_WithMultiRdnSubject_ExtractsJustTheCommonName()
    {
        var commonName = await EnrollAndGetSentCommonName("CN=test.mmcertdomain.com,O=Acme,C=US");

        Assert.Equal("test.mmcertdomain.com", commonName);
    }
}
