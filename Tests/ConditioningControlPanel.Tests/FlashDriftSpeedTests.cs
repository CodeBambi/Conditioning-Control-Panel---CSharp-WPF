using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1265: the Drift and Bounce speed dial. 1.0 is the authored speed, and the setting
/// never leaves 0.25..3 whatever a hand-edited settings file says.
/// </summary>
public class FlashDriftSpeedTests
{
    [Fact]
    public void Defaults_to_the_authored_speed()
        => Assert.Equal(1.0, new AppSettings().FlashDriftSpeed);

    [Theory]
    [InlineData(0.0, 0.25)]
    [InlineData(-4.0, 0.25)]
    [InlineData(0.25, 0.25)]
    [InlineData(1.75, 1.75)]
    [InlineData(3.0, 3.0)]
    [InlineData(9.0, 3.0)]
    public void Clamps_into_range(double given, double expected)
        => Assert.Equal(expected, new AppSettings { FlashDriftSpeed = given }.FlashDriftSpeed);

    [Fact]
    public void Not_a_number_reads_as_normal_speed()
        => Assert.Equal(1.0, new AppSettings { FlashDriftSpeed = double.NaN }.FlashDriftSpeed);

    [Fact]
    public void A_loaded_file_is_clamped_too()
    {
        var s = JsonConvert.DeserializeObject<AppSettings>("{\"FlashDriftSpeed\": 12}")!;
        Assert.Equal(3.0, s.FlashDriftSpeed);
    }
}
