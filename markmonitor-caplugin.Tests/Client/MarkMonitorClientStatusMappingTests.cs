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
using Keyfactor.PKI.Enums.EJBCA;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientStatusMappingTests
{
    private static async Task<int> GetMappedStatus(string markMonitorStatus)
    {
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/order/11111111-1111-1111-1111-111111111111"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    SampleOrders.OrderWithCert("11111111-1111-1111-1111-111111111111", markMonitorStatus)));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var result = await client.GetSingleOrderAsync("11111111-1111-1111-1111-111111111111");
        Assert.NotNull(result);
        return result.Status;
    }

    [Fact]
    public async Task CreatedStatus_MapsToExternalValidation_NotInitialized()
    {
        // github.com/Keyfactor/markmonitor-caplugin/issues/2, verified against a real
        // AnyGatewayREST + Command deployment: INITIALIZED (20) is not what the gateway framework
        // treats as "accepted, still pending" - only EXTERNALVALIDATION (90) is. Mapping CREATED to
        // INITIALIZED caused the gateway to report a hard enrollment failure for an order that had
        // actually been created successfully at MarkMonitor and was simply awaiting DCV/issuance.
        var status = await GetMappedStatus("CREATED");

        Assert.Equal((int)EndEntityStatus.EXTERNALVALIDATION, status);
    }

    [Theory]
    [InlineData("DIGI_PENDING", EndEntityStatus.INPROCESS)]
    [InlineData("DIGI_PROCESSING", EndEntityStatus.INPROCESS)]
    [InlineData("DIGI_ISSUED", EndEntityStatus.GENERATED)]
    [InlineData("DIGI_REVOKED", EndEntityStatus.REVOKED)]
    [InlineData("DIGI_FAILED", EndEntityStatus.FAILED)]
    [InlineData("DIGI_CANCELED", EndEntityStatus.CANCELLED)]
    [InlineData("DIGI_REJECTED", EndEntityStatus.CANCELLED)]
    public async Task OtherStatuses_MapAsExpected(string markMonitorStatus, EndEntityStatus expected)
    {
        var status = await GetMappedStatus(markMonitorStatus);

        Assert.Equal((int)expected, status);
    }
}
