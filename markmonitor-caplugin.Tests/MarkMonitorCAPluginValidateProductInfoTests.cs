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
}
