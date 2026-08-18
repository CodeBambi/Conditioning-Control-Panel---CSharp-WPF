using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;
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
    // =====================================================================================
    //  SP-105 final review — the CONTINUOUS module's live-state line, one sentence per state
    // =====================================================================================

    /// <summary>
    /// The state a sentence is written for, so the theory rows read as states rather than as
    /// argument tuples. Each one is a situation a user can really be in.
    /// </summary>
    public enum PinkLine
    {
        /// <summary>Dial off. No session involved either way.</summary>
        Off,

        /// <summary>Dial on, no session yet.</summary>
        ArmedIdle,

        /// <summary>Dial on, no session yet, and the opacity is at zero.</summary>
        ArmedIdleTransparent,

        /// <summary>Session running, opacity at zero: the user's own dial is why nothing is drawn.</summary>
        RunningTransparent,

        /// <summary>Session running and the SURFACE refused. Every Linux session is this one.</summary>
        RunningRefused,

        /// <summary>Session running, nothing up, and nothing has recorded a refusal to name.</summary>
        RunningUnexplained,

        /// <summary>Session running and the tint is confirmed on screen.</summary>
        Live,
    }

    [Theory]
    [InlineData(PinkLine.Off)]
    [InlineData(PinkLine.ArmedIdle)]
    [InlineData(PinkLine.ArmedIdleTransparent)]
    [InlineData(PinkLine.RunningTransparent)]
    [InlineData(PinkLine.RunningRefused)]
    [InlineData(PinkLine.RunningUnexplained)]
    [InlineData(PinkLine.Live)]
    public void TheContinuousModulesLine_SaysADIFFERENTTrueThingInEveryStateAUserCanBeIn(PinkLine state)
    {
        var text = PinkLineFor(state);

        // No line may be blank, and no two states may share a sentence: a line that collapsed two
        // situations into one wording is exactly how "Nothing is drawn until the session starts"
        // came to be shown to a user whose session was already running.
        Assert.False(string.IsNullOrWhiteSpace(text));
        foreach (var other in Enum.GetValues<PinkLine>())
        {
            if (other != state)
            {
                Assert.NotEqual(PinkLineFor(other), text);
            }
        }

        // And the load-bearing half: only a state with NO session running may tell the user to
        // start one, and every running state must say so.
        var running = state is PinkLine.RunningTransparent or PinkLine.RunningRefused
            or PinkLine.RunningUnexplained or PinkLine.Live;
        if (running)
        {
            Assert.StartsWith("Running", text, StringComparison.Ordinal);
            Assert.DoesNotContain("until the session starts", text, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("Running", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ARunningSessionWhoseOverlayRefused_NamesTheSURFACE_AndNeverTellsTheUserToStartASession()
    {
        // THE regression this pass exists for. On Linux the overlay backend refuses by design, so
        // this is not an edge case there — it is every session, for its whole length.
        var text = PinkLineFor(PinkLine.RunningRefused);

        Assert.Contains("Running, but nothing is on your screen", text, StringComparison.Ordinal);
        Assert.Contains("overlay surface", text, StringComparison.Ordinal);
        Assert.Contains(OverlayReasonCodes.OverlayMechanismAbsent, text, StringComparison.Ordinal);

        // The sentence the old single Armed arm produced here, named so a revert is visible.
        Assert.DoesNotContain("Nothing is drawn until the session starts", text, StringComparison.Ordinal);
        Assert.NotEqual(PinkLineFor(PinkLine.ArmedIdle), text);
    }

    [Fact]
    public void ARunningSessionWithTheOpacityAtZero_BlamesTheDIAL_AndOffersTheRemedy()
    {
        // The other running-but-blank case, and its cause is the user's own, not the platform's —
        // so it must not borrow the surface's wording.
        var text = PinkLineFor(PinkLine.RunningTransparent);

        Assert.Contains("opacity is at 0%", text, StringComparison.Ordinal);
        Assert.Contains("Move the slider up", text, StringComparison.Ordinal);
        Assert.DoesNotContain("overlay surface", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNothingRecorded_TheRunningLineStillRefusesToInventACause()
    {
        // A running session with nothing up and no recorded refusal is not reachable on today's
        // product path, and the line still has to be true rather than a guess.
        var text = PinkLineFor(PinkLine.RunningUnexplained);

        Assert.Contains("Running, but nothing is on your screen", text, StringComparison.Ordinal);
        Assert.DoesNotContain("(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("opacity", text, StringComparison.Ordinal);
    }

    private static string PinkLineFor(PinkLine state)
    {
        var visible = new PinkFilterTint(255, 105, 180, 10);
        var transparent = visible with { OpacityPercent = 0 };
        var refused = new CapabilityState.Unavailable(new CapabilityReason(
            OverlayReasonCodes.OverlayMechanismAbsent, "no Linux overlay backend is implemented"));
        var placed = new CapabilityState.Available("win32 overlay: placed");

        return state switch
        {
            PinkLine.Off => StudioPage.DescribePinkFilterState(
                EffectDotState.Off, visible, sessionRunning: false, placement: null),
            PinkLine.ArmedIdle => StudioPage.DescribePinkFilterState(
                EffectDotState.Armed, visible, sessionRunning: false, placement: null),
            PinkLine.ArmedIdleTransparent => StudioPage.DescribePinkFilterState(
                EffectDotState.Armed, transparent, sessionRunning: false, placement: null),
            PinkLine.RunningTransparent => StudioPage.DescribePinkFilterState(
                EffectDotState.Armed, transparent, sessionRunning: true, placement: null),
            PinkLine.RunningRefused => StudioPage.DescribePinkFilterState(
                EffectDotState.Armed, visible, sessionRunning: true, placement: refused),
            PinkLine.RunningUnexplained => StudioPage.DescribePinkFilterState(
                EffectDotState.Armed, visible, sessionRunning: true, placement: placed),
            _ => StudioPage.DescribePinkFilterState(
                EffectDotState.Live, visible, sessionRunning: true, placement: placed),
        };
    }

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
