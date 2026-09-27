using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1244: "images and bubble pops do not fire the toy" in Down the Rabbit Hole.
///
/// <para>The pop half was never broken - the page still barks <c>benign-popped</c> and the
/// director still maps it. The flash half was: the 2026-07 cutover moved every effect in-world
/// and the bridge stopped carrying fire-payload, so no verb in the table answered to a flash any
/// more. The page barks <c>effect-fired</c> now, and this pins that both halves resolve.</para>
/// </summary>
public class DtrhPayloadAccentTests
{
    [Fact]
    public void APoppedBubbleStillHasItsVerb()
    {
        Assert.True(DtrhHapticDirector.TryAccentFor("benign-popped", null, out var tier));
        Assert.Equal(1, tier);   // micro-event: coalesced into one swell with its neighbours
    }

    [Fact]
    public void AnInWorldFlashHasAVerbAgain()
    {
        Assert.True(DtrhHapticDirector.TryAccentFor("effect-fired", "flash", out var tier));
        Assert.Equal(1, tier);
    }

    [Theory]
    [InlineData("subliminal")]
    [InlineData("overlay")]
    [InlineData("glitch")]
    [InlineData("bouncingText")]
    public void TheLightPayloadsRideTheCoalescerWithThePops(string kind)
    {
        Assert.True(DtrhHapticDirector.TryAccentFor("effect-fired", kind, out var tier));
        Assert.Equal(1, tier);
    }

    [Theory]
    [InlineData("video")]
    [InlineData("gifCascade")]
    [InlineData("gifWash")]
    [InlineData("melt")]
    [InlineData("blackout")]
    public void TheHeaviesAreMomentsOfTheirOwn(string kind)
    {
        Assert.True(DtrhHapticDirector.TryAccentFor("effect-fired", kind, out var tier));
        Assert.Equal(2, tier);
    }

    [Fact]
    public void AnAudioPayloadIsNotAVisualAndHasNoAccent()
    {
        // The page does not bark for it either; this pins that a stale page cannot invent one.
        Assert.False(DtrhHapticDirector.TryAccentFor("effect-fired", "audio", out _));
    }

    [Fact]
    public void AnEventWithNoVerbIsSilent()
    {
        Assert.False(DtrhHapticDirector.TryAccentFor("boon-skipped", null, out _));
        Assert.False(DtrhHapticDirector.TryAccentFor("effect-fired", null, out _));
        Assert.False(DtrhHapticDirector.TryAccentFor(null, null, out _));
    }
}
