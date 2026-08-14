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

using Keyfactor.AnyGateway.Extensions;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests;

public class MarkMonitorCAPluginValidateProductInfoTests
{
    [Theory]
    [InlineData("SslDvGeotrust")]
    [InlineData("SslOvBasic")]
    [InlineData("SslEvSecuresitePro")]
    public async Task ValidateProductInfo_WithAValidProductId_DoesNotThrow(string productId)
    {
        var plugin = new MarkMonitorCAPlugin();
        var productInfo = new EnrollmentProductInfo { ProductID = productId };

        await plugin.ValidateProductInfo(productInfo, new Dictionary<string, object>());
    }

    [Fact]
    public async Task ValidateProductInfo_WithAnInvalidProductId_ThrowsAndListsTheValidValues()
    {
        var plugin = new MarkMonitorCAPlugin();
        var productInfo = new EnrollmentProductInfo { ProductID = "NotARealProduct" };

        var ex = await Assert.ThrowsAsync<AnyCAValidationException>(() =>
            plugin.ValidateProductInfo(productInfo, new Dictionary<string, object>()));

        Assert.Contains("NotARealProduct", ex.Message);
        Assert.Contains("SslDvGeotrust", ex.Message);
    }

    [Fact]
    public async Task ValidateProductInfo_WithAnEmptyProductId_Throws()
    {
        var plugin = new MarkMonitorCAPlugin();
        var productInfo = new EnrollmentProductInfo { ProductID = "" };

        await Assert.ThrowsAsync<AnyCAValidationException>(() =>
            plugin.ValidateProductInfo(productInfo, new Dictionary<string, object>()));
    }

    [Fact]
    public async Task ValidateProductInfo_WithANumericStringForAnUndefinedEnumValue_Throws()
    {
        // Regression test: Enum.TryParse<CertOrderTypes> alone "succeeds" for any numeric string
        // that fits the underlying int type, even with no member defined for that value (CertOrderTypes
        // has 12 members, values 0-11) - Enum.IsDefined is the check that actually enforces membership.
        var plugin = new MarkMonitorCAPlugin();
        var productInfo = new EnrollmentProductInfo { ProductID = "20" };

        var ex = await Assert.ThrowsAsync<AnyCAValidationException>(() =>
            plugin.ValidateProductInfo(productInfo, new Dictionary<string, object>()));

        Assert.Contains("20", ex.Message);
        Assert.Contains("SslDvGeotrust", ex.Message);
    }
}
