namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests;

public class MarkMonitorConfigTests
{
    [Fact]
    public void PageSize_DefaultsTo100()
    {
        Assert.Equal(100, new MarkMonitorConfig().PageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(250, 250)]
    [InlineData(500, 500)]
    [InlineData(501, 500)]
    [InlineData(10000, 500)]
    public void PageSize_ClampsOutOfRangeValues(int assigned, int expected)
    {
        var config = new MarkMonitorConfig { PageSize = assigned };

        Assert.Equal(expected, config.PageSize);
    }
}
