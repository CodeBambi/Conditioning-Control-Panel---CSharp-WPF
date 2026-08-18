using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-101 — the one sentence on the Studio module panel that had gone false.
///
/// <para><b>What was wrong.</b> Through SP-098 the panel said, in fixed words, that showing the
/// images over your other windows "is not ported yet". SP-100 landed the overlay and made that
/// false on Windows while leaving it exactly right on Linux, and no fixed sentence can be both.
/// SP-100's own closing note recorded it as a consequence outside its File Scope and named the
/// requirement: the replacement has to say both halves.</para>
///
/// <para><b>What replaced it, and why these facts are about the SHAPE of the answer.</b> The line
/// is derived from the surface presenter's last typed <see cref="CapabilityState"/>, verbatim — so
/// it asserts nothing about the platform, and on a build whose overlay refuses it repeats the
/// backend's own reason and manual gate rather than a summary of them. A platform check here would
/// be a second opinion about a capability that already answers for itself, and second opinions are
/// how the first sentence came to be wrong.</para>
/// </summary>
public class StudioSurfaceNoticeTests
{
    [Fact]
    public void BeforeAnythingIsAttempted_ItNamesTheMechanism_AndClaimsNothingAboutTheScreen()
    {
        var text = StudioPage.DescribeSurface(null);

        // Said BEFORE the user presses START: they must not have to press it and watch to find out
        // how this effect reaches the screen.
        Assert.Contains("always-on-top", text, StringComparison.Ordinal);
        Assert.Contains("click-through", text, StringComparison.Ordinal);
        Assert.Contains("Nothing has been drawn yet", text, StringComparison.Ordinal);

        // ...and it is a claim about THIS SESSION, not about the surface: nothing has asked the OS
        // anything yet, so nothing may be said about what it would answer.
        Assert.DoesNotContain("not ported", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Linux", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenTheSurfaceReallyPlacedAFlash_ItSaysSo_AndDoesNotStillCallItUnported()
    {
        var text = StudioPage.DescribeSurface(new CapabilityState.Available("win32 overlay: placed"));

        Assert.Contains("was placed", text, StringComparison.Ordinal);
        Assert.Contains("above your other windows", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not ported", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing was drawn", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OnABuildWhoseOverlayRefuses_TheBACKENDsOwnWordsReachTheUser_ManualGateIncluded()
    {
        // The real Linux refusal, not a double of it (OverlayPresenceFactory.CreateFor). Its detail
        // names the route AND the manual gate, and both have to survive the trip to the panel: a
        // user on Linux who is told only "unavailable" has been told nothing they can act on.
        var refusal = OverlayPresenceFactory.CreateFor(OverlayHostPlatform.Linux).Withdraw();
        var reason = Assert.IsType<CapabilityState.Unavailable>(refusal).Reason;

        var text = StudioPage.DescribeSurface(refusal);

        Assert.StartsWith("Nothing was drawn on screen:", text, StringComparison.Ordinal);
        Assert.Contains(reason.Detail, text, StringComparison.Ordinal);
        Assert.Contains("override-redirect", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoDisplayEnumerated_TheReasonIsTheOnesTheSurfaceRecorded_NotAGuess()
    {
        var refusal = new CapabilityState.Unavailable(new CapabilityReason(
            OverlayReasonCodes.OverlayNoDisplay,
            "the operating system enumerated no display, so there is no rectangle a flash could legally be placed on"));

        var text = StudioPage.DescribeSurface(refusal);

        Assert.Contains("enumerated no display", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTypedStateHasWords_SoNoOutcomeCanReachTheUserAsBlankSpace()
    {
        var reason = new CapabilityReason("some-code", "the detail a bug report quotes");
        CapabilityState[] states =
        [
            new CapabilityState.Available("placed"),
            new CapabilityState.Unavailable(reason),
            new CapabilityState.Degraded("half of it holds", reason),
            new CapabilityState.PermissionRequired(reason),
            new CapabilityState.DependencyMissing("some component", reason),
            new CapabilityState.Faulted(reason),
        ];

        Assert.NotEmpty(states);
        Assert.All(states, state => Assert.False(
            string.IsNullOrWhiteSpace(StudioPage.DescribeSurface(state)),
            $"{state.GetType().Name} renders as blank space on the module panel"));

        // ...and every state that is NOT Available carries the reason's own detail through, because
        // that detail is what a user acts on and what a bug report quotes.
        Assert.All(states.Where(s => s is not CapabilityState.Available), state =>
            Assert.Contains(reason.Detail, StudioPage.DescribeSurface(state), StringComparison.Ordinal));
    }
}
