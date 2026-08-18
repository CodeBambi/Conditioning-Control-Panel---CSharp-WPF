using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #980 - "Just Drop media is showing as frozen/still images (including the spirals from
/// the app), on fullscreen and windowed alike."
///
/// <para>The web player honours <c>prefers-reduced-motion: reduce</c> by painting every canvas
/// layer at a frozen clock and serving the media Window one still poster that never advances.
/// Blink answers that media query on Windows from <c>SPI_GETCLIENTAREAANIMATION</c> - the same
/// "Animation effects" checkbox <c>MotionFx</c> reads - so a user who turned Windows animations
/// off got a session made of photographs.</para>
///
/// <para>These pin the rule the host now enforces: only the user's explicit in-app
/// <see cref="MotionLevel.Off"/> reduces a hosted page. The OS flag never reaches one, and
/// <see cref="MotionLevel.Reduced"/> - which is what MotionFx caps that OS flag to - does not
/// either, because it is a statement about chrome, not about content.</para>
/// </summary>
public class HostedPageMotionTests
{
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    public void AnythingShortOfOff_KeepsAHostedPageInMotion(MotionLevel setting)
        => Assert.Equal("--force-prefers-no-reduced-motion",
                        ChaosWebViewHost.PrefersReducedMotionArgument(setting));

    [Fact]
    public void OnlyTheExplicitOff_IsForwardedToThePage()
        => Assert.Equal("--force-prefers-reduced-motion",
                        ChaosWebViewHost.PrefersReducedMotionArgument(MotionLevel.Off));

    /// <summary>The switch is always emitted - never left to Chromium to decide - because
    /// "no argument" IS the bug: that is the state in which the Windows checkbox answers.</summary>
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public void TheHostAlwaysAnswers(MotionLevel setting)
    {
        var arg = ChaosWebViewHost.PrefersReducedMotionArgument(setting);
        Assert.StartsWith("--force-prefers-", arg);
        Assert.EndsWith("reduced-motion", arg);
    }
}
