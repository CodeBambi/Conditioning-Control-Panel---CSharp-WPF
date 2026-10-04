using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Down the Rabbit Hole page learns the app's Motion setting from <c>init.motionLevel</c>.
/// These pin the three wire words the page's <c>shared/motion.js</c> accepts.
/// </summary>
public class DtrhMotionWireTests
{
    [Theory]
    [InlineData(MotionLevel.Full, "full")]
    [InlineData(MotionLevel.Reduced, "reduced")]
    [InlineData(MotionLevel.Off, "off")]
    public void EachSettingHasItsWireWord(MotionLevel setting, string word)
        => Assert.Equal(word, DtrhMotionWire.For(setting));

    /// <summary>A value the page does not know would make it fall back to the OS, so an
    /// out-of-range setting reads as full instead.</summary>
    [Fact]
    public void AnUnknownSettingReadsAsFull()
        => Assert.Equal("full", DtrhMotionWire.For((MotionLevel)99));
}
