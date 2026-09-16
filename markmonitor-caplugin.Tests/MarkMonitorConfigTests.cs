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

    [Fact]
    public void TimeoutSeconds_DefaultsTo120()
    {
        Assert.Equal(120, new MarkMonitorConfig().TimeoutSeconds);
    }

    [Theory]
    [InlineData(0, 1)] // 0 would otherwise crash HttpClient.Timeout's own setter
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(60, 60)]
    [InlineData(120, 120)]
    [InlineData(121, 120)] // never allowed above this field's own pre-existing hardcoded default
    [InlineData(10000, 120)]
    public void TimeoutSeconds_ClampsOutOfRangeValues(int assigned, int expected)
    {
        var config = new MarkMonitorConfig { TimeoutSeconds = assigned };

        Assert.Equal(expected, config.TimeoutSeconds);
    }

    [Fact]
    public void PickupRetries_DefaultsTo5()
    {
        Assert.Equal(5, new MarkMonitorConfig().PickupRetries);
    }

    [Theory]
    [InlineData(0, 0)] // 0 is a valid, deliberate "disable polling" value
    [InlineData(-5, 0)]
    [InlineData(5, 5)]
    [InlineData(20, 20)]
    [InlineData(21, 20)]
    [InlineData(10000, 20)]
    public void PickupRetries_ClampsOutOfRangeValues(int assigned, int expected)
    {
        var config = new MarkMonitorConfig { PickupRetries = assigned };

        Assert.Equal(expected, config.PickupRetries);
    }

    [Fact]
    public void PickupDelaySeconds_DefaultsTo10()
    {
        Assert.Equal(10, new MarkMonitorConfig().PickupDelaySeconds);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(10, 10)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    [InlineData(10000, 60)]
    public void PickupDelaySeconds_ClampsOutOfRangeValues(int assigned, int expected)
    {
        var config = new MarkMonitorConfig { PickupDelaySeconds = assigned };

        Assert.Equal(expected, config.PickupDelaySeconds);
    }
}
