using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Input;
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

    // =====================================================================================
    //  SP-106 — the MOVING module's live-state line, where "on screen and stopped" is a sentence
    // =====================================================================================

    /// <summary>Every situation a user of the moving module can really be in.</summary>
    public enum SpiralLine
    {
        /// <summary>Dial off. No session involved either way.</summary>
        Off,

        /// <summary>Armed, no session, everything ready.</summary>
        ArmedIdle,

        /// <summary>Armed, no session, and the opacity dial is at zero.</summary>
        ArmedIdleTransparent,

        /// <summary>Armed, no session, and the library has nothing in it.</summary>
        ArmedIdleNoSpiral,

        /// <summary>Session running, opacity at zero, nothing placed.</summary>
        RunningTransparent,

        /// <summary>Session running, nothing in the library, nothing placed.</summary>
        RunningNoSpiral,

        /// <summary>Session running and the overlay backend refused (every Linux session).</summary>
        RunningRefused,

        /// <summary>Session running, nothing up, and nothing recorded to explain it.</summary>
        RunningUnexplained,

        /// <summary>Session running, the layer IS on screen, and it has stopped turning.</summary>
        RunningFrozen,

        /// <summary>Session running and a single-frame spiral is up, exactly as asked.</summary>
        LiveStill,

        /// <summary>Session running and the spiral is turning.</summary>
        LiveTurning,
    }

    [Theory]
    [InlineData(SpiralLine.Off)]
    [InlineData(SpiralLine.ArmedIdle)]
    [InlineData(SpiralLine.ArmedIdleTransparent)]
    [InlineData(SpiralLine.ArmedIdleNoSpiral)]
    [InlineData(SpiralLine.RunningTransparent)]
    [InlineData(SpiralLine.RunningNoSpiral)]
    [InlineData(SpiralLine.RunningRefused)]
    [InlineData(SpiralLine.RunningUnexplained)]
    [InlineData(SpiralLine.RunningFrozen)]
    [InlineData(SpiralLine.LiveStill)]
    [InlineData(SpiralLine.LiveTurning)]
    public void TheMovingModulesLine_SaysADIFFERENTTrueThingInEveryStateAUserCanBeIn(SpiralLine state)
    {
        var text = SpiralLineFor(state);

        Assert.False(string.IsNullOrWhiteSpace(text));
        foreach (var other in Enum.GetValues<SpiralLine>())
        {
            if (other != state)
            {
                Assert.NotEqual(SpiralLineFor(other), text);
            }
        }

        // SP-105's final-review rule, carried to the fourth module: only a state with NO session
        // running may tell the user to start one, and every running state must say so. This module
        // has ELEVEN states against the tint's seven, which is exactly why the rule is asserted
        // mechanically rather than read.
        var running = state is not (SpiralLine.Off or SpiralLine.ArmedIdle
            or SpiralLine.ArmedIdleTransparent or SpiralLine.ArmedIdleNoSpiral);
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
    public void ALayerThatIsOnScreenAndHasStoppedTurning_SaysSO_AndIsNotConfusedWithAStillSpiral()
    {
        // THE STATE THIS MODULE ADDED, and the reason the dot needed a third meaning. Two situations
        // look identical on the user's screen — a motionless picture — and only one of them is the
        // module working.
        var frozen = SpiralLineFor(SpiralLine.RunningFrozen);
        var still = SpiralLineFor(SpiralLine.LiveStill);

        Assert.Contains("STOPPED TURNING", frozen, StringComparison.Ordinal);
        Assert.Contains("frozen", frozen, StringComparison.Ordinal);

        // The healthy one must not borrow any of that vocabulary, and must say the file is the
        // reason rather than leaving the user to suspect a fault.
        Assert.DoesNotContain("STOPPED", still, StringComparison.Ordinal);
        Assert.DoesNotContain("frozen", still, StringComparison.Ordinal);
        Assert.Contains("still frame", still, StringComparison.Ordinal);
        Assert.Contains("not a fault", still, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunningSessionWithNoSpiralInTheLibrary_BlamesTheLIBRARY_AndNotTheSurface()
    {
        // The first-run state for most users, because this port bundles no spiral (D86). It must not
        // read like a platform failure.
        var text = SpiralLineFor(SpiralLine.RunningNoSpiral);

        Assert.Contains("no spiral", text, StringComparison.Ordinal);
        Assert.DoesNotContain("overlay surface", text, StringComparison.Ordinal);
        Assert.DoesNotContain("opacity", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMovingModulesRefusedLine_NamesTheSurfaceAndItsCode_TheSameWayTheTintsDoes()
    {
        var text = SpiralLineFor(SpiralLine.RunningRefused);

        Assert.Contains("Running, but nothing is on your screen", text, StringComparison.Ordinal);
        Assert.Contains("overlay surface", text, StringComparison.Ordinal);
        Assert.Contains(OverlayReasonCodes.OverlayMechanismAbsent, text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLibraryLine_NamesTheFOLDERWhenEmptyAndTheFILENAMEWhenNot_AndNeverTheFullPath()
    {
        var folder = Path.Combine("C:", "data", "assets", "spirals");

        var empty = StudioPage.DescribeSpiralLibrary(null, folder);
        Assert.Contains(folder, empty, StringComparison.Ordinal);
        Assert.Contains(".gif", empty, StringComparison.Ordinal);

        // A file name, never its path: the media-logging rule the DTRH manifest holds applies to
        // what a panel prints as much as to what a log writes.
        var drawing = StudioPage.DescribeSpiralLibrary(Path.Combine(folder, "classic.gif"), folder);
        Assert.Contains("classic.gif", drawing, StringComparison.Ordinal);
        Assert.DoesNotContain(folder, drawing, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMovingModulesSurfaceLine_UsesThePresentTense_BecauseItsLayerStays()
    {
        // The two paced modules place something that is gone a moment later, so their line is about
        // the LAST one; this one places something that stays, so its line is about NOW — the same
        // choice the tint's line makes.
        Assert.Contains("Nothing has been drawn yet", StudioPage.DescribeSpiralSurface(null), StringComparison.Ordinal);
        Assert.Contains(
            "The spiral is on an always-on-top overlay surface",
            StudioPage.DescribeSpiralSurface(new CapabilityState.Available("placed")),
            StringComparison.Ordinal);
        Assert.Contains(
            "Nothing is drawn on screen",
            StudioPage.DescribeSpiralSurface(new CapabilityState.Unavailable(
                new CapabilityReason(OverlayReasonCodes.OverlayMechanismAbsent, "no backend here"))),
            StringComparison.Ordinal);
    }

    private static string SpiralLineFor(SpiralLine state)
    {
        var visible = new SpiralPresentation(10);
        var transparent = new SpiralPresentation(0);
        var refused = new CapabilityState.Unavailable(new CapabilityReason(
            OverlayReasonCodes.OverlayMechanismAbsent, "no Linux overlay backend is implemented"));
        var placed = new CapabilityState.Available("win32 overlay: placed");

        return state switch
        {
            SpiralLine.Off => StudioPage.DescribeSpiralState(
                EffectDotState.Off, visible, hasSpiral: true, showing: false, frameCount: 0,
                sessionRunning: false, placement: null),
            SpiralLine.ArmedIdle => StudioPage.DescribeSpiralState(
                EffectDotState.Armed, visible, hasSpiral: true, showing: false, frameCount: 0,
                sessionRunning: false, placement: null),
            SpiralLine.ArmedIdleTransparent => StudioPage.DescribeSpiralState(
                EffectDotState.Armed, transparent, hasSpiral: true, showing: false, frameCount: 0,
                sessionRunning: false, placement: null),
            SpiralLine.ArmedIdleNoSpiral => StudioPage.DescribeSpiralState(
                EffectDotState.Armed, visible, hasSpiral: false, showing: false, frameCount: 0,
                sessionRunning: false, placement: null),
            SpiralLine.RunningTransparent => StudioPage.DescribeSpiralState(
                EffectDotState.Armed, transparent, hasSpiral: true, showing: false, frameCount: 0,
                sessionRunning: true, placement: null),
            SpiralLine.RunningNoSpiral => StudioPage.DescribeSpiralState(
                EffectDotState.Armed, visible, hasSpiral: false, showing: false, frameCount: 0,
                sessionRunning: true, placement: null),
            SpiralLine.RunningRefused => StudioPage.DescribeSpiralState(
                EffectDotState.Armed, visible, hasSpiral: true, showing: false, frameCount: 0,
                sessionRunning: true, placement: refused),
            SpiralLine.RunningUnexplained => StudioPage.DescribeSpiralState(
                EffectDotState.Armed, visible, hasSpiral: true, showing: false, frameCount: 0,
                sessionRunning: true, placement: placed),
            SpiralLine.RunningFrozen => StudioPage.DescribeSpiralState(
                EffectDotState.Armed, visible, hasSpiral: true, showing: true, frameCount: 12,
                sessionRunning: true, placement: placed),
            SpiralLine.LiveStill => StudioPage.DescribeSpiralState(
                EffectDotState.Live, visible, hasSpiral: true, showing: true, frameCount: 1,
                sessionRunning: true, placement: placed),
            _ => StudioPage.DescribeSpiralState(
                EffectDotState.Live, visible, hasSpiral: true, showing: true, frameCount: 12,
                sessionRunning: true, placement: placed),
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

    // =====================================================================================
    //  SP-108 — the panel that has NO surface line, and what it says instead
    // =====================================================================================

    /// <summary>The state the Intensity Ramp's live line is written for. Every row is a situation a
    /// user can really be in, and two of them look alike and are not.</summary>
    public enum RampLine
    {
        /// <summary>Dial off. No session involved either way.</summary>
        Off,

        /// <summary>Dial on, no session yet, and something is linked.</summary>
        ReadyWithALink,

        /// <summary>Dial on, no session yet, and NOTHING is linked — the state a freshly enabled ramp
        /// is in, because every link ships off.</summary>
        ReadyWithNoLink,

        /// <summary>Session running and the ramp holds nothing, because nothing is linked.</summary>
        RunningHoldingNothing,

        /// <summary>Session running, dials held, and the multiplier is at its 1.0 floor.</summary>
        RunningAtNeutralGain,

        /// <summary>Session running and climbing.</summary>
        Climbing,

        /// <summary>The climb finished and the ramp is still holding what it took.</summary>
        Finished,
    }

    [Theory]
    [InlineData(RampLine.Off)]
    [InlineData(RampLine.ReadyWithALink)]
    [InlineData(RampLine.ReadyWithNoLink)]
    [InlineData(RampLine.RunningHoldingNothing)]
    [InlineData(RampLine.RunningAtNeutralGain)]
    [InlineData(RampLine.Climbing)]
    [InlineData(RampLine.Finished)]
    public void TheRampsLiveLineSaysADIFFERENTTrueThingInEveryStateItCanBeIn(RampLine line)
    {
        var text = RampLineFor(line);

        // No line may be blank, and NO TWO STATES MAY SHARE A SENTENCE. Without this loop a
        // DescribeRampState that returned one constant would pass every row of this theory — which
        // is the bug SP-105 shipped, a review caught, and the sibling theory above has guarded
        // against ever since. Caught here at code review before it landed.
        Assert.False(string.IsNullOrWhiteSpace(text), $"{line} renders as blank space");
        Assert.EndsWith(".", text.TrimEnd(), StringComparison.Ordinal);
        foreach (var other in Enum.GetValues<RampLine>())
        {
            if (other != line)
            {
                Assert.NotEqual(RampLineFor(other), text);
            }
        }

        // And the load-bearing half, in this module's own currency: only a state with NO session
        // running may tell the user to start one, and every running state must say it is running —
        // INCLUDING the finished one, because a ramp at full progress still holds the user's dials
        // and its dot still reads Live.
        var running = line is RampLine.RunningHoldingNothing or RampLine.RunningAtNeutralGain
            or RampLine.Climbing or RampLine.Finished;
        if (running)
        {
            Assert.StartsWith("Running", text, StringComparison.Ordinal);
            Assert.DoesNotContain("When a session starts", text, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("Running", text, StringComparison.Ordinal);
        }
    }

    private static string RampLineFor(RampLine line)
    {
        var preset = new IntensityRampPresetDocument { Enabled = true, DurationMinutes = 60 };
        var dot = EffectDotState.Live;
        var progress = 0.5;
        var current = 2.0;
        var held = 2;

        switch (line)
        {
            case RampLine.Off:
                preset.Enabled = false;
                dot = EffectDotState.Off;
                held = 0;
                break;
            case RampLine.ReadyWithALink:
                preset.LinkSpiralOpacity = true;
                preset.Multiplier = 3.0;
                dot = EffectDotState.Armed;
                held = 0;
                break;
            case RampLine.ReadyWithNoLink:
                preset.Multiplier = 3.0;
                dot = EffectDotState.Armed;
                held = 0;
                break;
            case RampLine.RunningHoldingNothing:
                preset.Multiplier = 3.0;
                dot = EffectDotState.Armed;
                held = 0;
                break;
            case RampLine.RunningAtNeutralGain:
                preset.Multiplier = IntensityRampPresetDocument.MinMultiplier;
                current = 1.0;
                break;
            case RampLine.Climbing:
                preset.LinkPinkFilterOpacity = true;
                preset.Multiplier = 3.0;
                break;
            case RampLine.Finished:
                preset.LinkPinkFilterOpacity = true;
                preset.Multiplier = 3.0;
                progress = 1.0;
                current = 3.0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(line));
        }

        var sessionRunning = line is not (RampLine.Off or RampLine.ReadyWithALink or RampLine.ReadyWithNoLink);
        return RampPanelNotices.DescribeRampState(dot, preset, progress, current, held, sessionRunning);
    }

    [Fact]
    public void ARampLinkedToNothingBUTTHEFLASHOpacityIsStillLinked_WhichSp117sThirdLinkMadeReachable()
    {
        // SP-117's sweep survivor M-ao, closed. RampPanelNotices' anyLink predicate decides whether
        // this panel tells the user "nothing is linked to it yet"; before this fact, every case that
        // exercised it linked the spiral or the tint, so dropping the third link from the predicate
        // changed no measured outcome — and a user who had linked ONLY flash opacity would have been
        // told their ramp does nothing.
        var flashOnly = new IntensityRampPresetDocument
        {
            Enabled = true,
            LinkFlashOpacity = true,
            Multiplier = 3.0,
        };

        var text = RampPanelNotices.DescribeRampState(
            EffectDotState.Armed, flashOnly, progress: 0.0, currentMultiplier: 1.0, heldCount: 0,
            sessionRunning: false);

        Assert.Contains("the effects you have linked climb to", text, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing is linked to it yet", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTwoStatesThatLookAlikeSayDifferentThings_BecauseOneIsRunningAndTheOtherIsNot()
    {
        // THE PAIR THIS SENTENCE EXISTS FOR. A ramp holding nothing and a ramp holding two dials at
        // 1.0x are both "nothing is climbing" — and the first will never do anything while the second
        // starts the moment the multiplier moves and still owes the user their dials back. A line
        // that described them the same way would make the dot's two states unreadable.
        var nothingLinked = new IntensityRampPresetDocument { Enabled = true, Multiplier = 3.0 };
        var neutralGain = new IntensityRampPresetDocument
        {
            Enabled = true,
            LinkPinkFilterOpacity = true,
            Multiplier = IntensityRampPresetDocument.MinMultiplier,
        };

        var holdingNothing = RampPanelNotices.DescribeRampState(
            EffectDotState.Armed, nothingLinked, 0.4, 1.8, 0, sessionRunning: true);
        var atNeutral = RampPanelNotices.DescribeRampState(
            EffectDotState.Live, neutralGain, 0.4, 1.0, 1, sessionRunning: true);

        Assert.Contains("holding nothing", holdingNothing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nothing has to be put back", holdingNothing, StringComparison.Ordinal);

        Assert.Contains("1 dial", atNeutral, StringComparison.Ordinal);
        Assert.Contains("goes back", atNeutral, StringComparison.Ordinal);
        Assert.DoesNotContain("holding nothing", atNeutral, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFreshlyEnabledRampIsToldWhatToDoNext_BecauseEveryLinkShipsOff()
    {
        // Every link flag defaults false (AppSettings.cs:2589-2621), so switching this module on and
        // walking away is the state a first-time user really lands in, and "nothing happened" is not
        // an acceptable answer to it.
        var preset = new IntensityRampPresetDocument { Enabled = true, Multiplier = 2.0 };

        var text = RampPanelNotices.DescribeRampState(
            EffectDotState.Armed, preset, 0, 1.0, 0, sessionRunning: false);

        Assert.Contains("nothing is linked", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Link to ramp", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCustodyLineNamesEveryBorrowedDialAndBothOfItsNumbers()
    {
        // The line this module has INSTEAD of a surface line. "Your spiral opacity says 27 and you
        // set it to 10" is the question a user looking at another module's panel mid-session will
        // have, and the answer must not be "the ramp, probably".
        var text = RampPanelNotices.DescribeRampCustody(
            2,
            [
                new RampDialHold("Spiral Overlay opacity", 10, 27),
                new RampDialHold("Pink Filter opacity", 12, 24),
            ]);

        Assert.Contains("Spiral Overlay opacity 10% → 27%", text, StringComparison.Ordinal);
        Assert.Contains("Pink Filter opacity 12% → 24%", text, StringComparison.Ordinal);
        Assert.Contains("goes back to the first number", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCustodyLineTellsAnEmptyHoldFromABuildWithNothingToDrive()
    {
        // Two different nothings: "the ramp has not borrowed anything yet" is a fact about this
        // session, and "this build gives it no dials" is a fact about the composition. A single
        // sentence for both would hide a typed refusal behind a shrug.
        var noHold = RampPanelNotices.DescribeRampCustody(2, []);
        var noDials = RampPanelNotices.DescribeRampCustody(0, []);

        Assert.Contains("Holding nothing", noHold, StringComparison.Ordinal);
        Assert.Contains("no dials to drive", noDials, StringComparison.Ordinal);
        Assert.NotEqual(noHold, noDials);
    }

    // =====================================================================================
    //  SP-109 — the AUDIO panels' lines. Four states for a module nobody can SEE.
    // =====================================================================================

    /// <summary>The state an audio sentence is written for. Every one is a situation a user can
    /// really be in, and three of them look identical from inside the process.</summary>
    public enum AudioLine
    {
        /// <summary>Dial off. No session involved either way.</summary>
        Off,

        /// <summary>Dial on, no session. The ordinary armed state.</summary>
        ArmedNoSession,

        /// <summary>Session running and the OS reports no render session for this process. The
        /// module is genuinely not running, and it must NOT be told to start a session.</summary>
        RunningButSilent,

        /// <summary>Session running, audio confirmed, and this module's schedule is not on the
        /// clock — a cancelled generation, or a repaint between the switch-on and the arm.</summary>
        RunningNotScheduled,

        /// <summary>Really running, nothing played yet.</summary>
        LiveNoCuesYet,

        /// <summary>Really running, with cues behind it.</summary>
        LiveWithCues,
    }

    [Theory]
    [InlineData(AudioLine.Off, EffectDotState.Off, false, false, 0)]
    [InlineData(AudioLine.ArmedNoSession, EffectDotState.Armed, false, false, 0)]
    [InlineData(AudioLine.RunningButSilent, EffectDotState.Armed, true, false, 0)]
    [InlineData(AudioLine.RunningNotScheduled, EffectDotState.Armed, true, true, 0)]
    [InlineData(AudioLine.LiveNoCuesYet, EffectDotState.Live, true, true, 0)]
    [InlineData(AudioLine.LiveWithCues, EffectDotState.Live, true, true, 3)]
    public void EveryAudioPanelState_GetsItsOwnSentence_AndNoTwoAreTheSame(
        AudioLine state, EffectDotState dot, bool sessionRunning, bool rendering, int cueCount)
    {
        var last = cueCount == 0 ? null : new AudioCueEvent(cueCount, DateTimeOffset.UnixEpoch);
        var line = AudioPanelNotices.DescribeCueState(dot, "clip", cueCount, last, sessionRunning, rendering);

        Assert.NotEmpty(line);

        var expected = state switch
        {
            AudioLine.Off => "Switched off",
            AudioLine.ArmedNoSession => "Armed. Nothing plays until the session starts.",
            AudioLine.RunningButSilent => "Running, but silent",
            AudioLine.RunningNotScheduled => "not scheduled right now",
            _ => "Running: the next clip is on the clock.",
        };
        Assert.Contains(expected, line, StringComparison.Ordinal);

        // THE CLAUSE THAT CAUGHT A REAL DEFECT. SP-105's finding was that telling a user to start a
        // session they have already started is the failure mode of a one-sentence Armed. Two of the
        // six rows here have a session running, and neither may say it.
        if (sessionRunning)
        {
            Assert.DoesNotContain("until the session starts", line, StringComparison.Ordinal);
        }

        // And the count only appears once something has really played.
        Assert.Equal(cueCount > 0, line.Contains("played so far", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSixAudioSentencesAreSixDIFFERENTSentences()
    {
        // The negative control for the theory above: a switch that collapsed two states onto one
        // sentence would satisfy every row individually and still tell the user nothing.
        var lines = new[]
        {
            AudioPanelNotices.DescribeCueState(EffectDotState.Off, "clip", 0, null, false, false),
            AudioPanelNotices.DescribeCueState(EffectDotState.Armed, "clip", 0, null, false, false),
            AudioPanelNotices.DescribeCueState(EffectDotState.Armed, "clip", 0, null, true, false),
            AudioPanelNotices.DescribeCueState(EffectDotState.Armed, "clip", 0, null, true, true),
            AudioPanelNotices.DescribeCueState(EffectDotState.Live, "clip", 0, null, true, true),
            AudioPanelNotices.DescribeCueState(
                EffectDotState.Live, "clip", 3, new AudioCueEvent(3, DateTimeOffset.UnixEpoch), true, true),
        };

        Assert.Equal(6, lines.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheClipPoolLineNamesTheFolderWhenItIsEmpty_AndCountsWhenItIsNot()
    {
        var empty = AudioPanelNotices.DescribeClipPool(0, @"C:\assets\sounds\mindwipe");
        var one = AudioPanelNotices.DescribeClipPool(1, @"C:\assets\sounds\mindwipe");
        var many = AudioPanelNotices.DescribeClipPool(4, @"C:\assets\sounds\mindwipe");

        // The port's standing answer to "where do I put them" — the shape the flash panel uses.
        Assert.Contains(@"C:\assets\sounds\mindwipe", empty, StringComparison.Ordinal);
        Assert.Contains(".mp3, .wav or .ogg", empty, StringComparison.Ordinal);
        // And it promises the LATE DROP, which is the pool behaviour AudioCuePoolTests pins.
        Assert.Contains("without restarting", empty, StringComparison.Ordinal);

        Assert.Contains("1 clip in", one, StringComparison.Ordinal);
        Assert.Contains("4 clips in", many, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAudioCapabilityLineQuotesTheCapability_AndNeverInventsASentenceAboutThePlatform()
    {
        // Before anything is asked: names the MECHANISM and says nothing was asked. A fact about
        // this session, not a claim about the machine.
        var nothingAsked = AudioPanelNotices.DescribeAudioCapability(null, AudioRenderObservation.NotAsked);
        Assert.Contains("Nothing has been asked of the operating system yet.", nothingAsked, StringComparison.Ordinal);

        // A refusal repeats the capability's OWN detail verbatim — a Linux run therefore reads its
        // own manual gate here rather than a summary somebody wrote about Linux.
        var refused = AudioPanelNotices.DescribeAudioCapability(
            new CapabilityState.Unavailable(new CapabilityReason("audio-render-readback-absent", "GATE-TEXT-VERBATIM")),
            AudioRenderObservation.NotAsked);
        Assert.Contains("GATE-TEXT-VERBATIM", refused, StringComparison.Ordinal);

        // A confirmed run appends the OS's own measurement AND the ceiling on it, in the same
        // sentence, so the number can never be read as proof that anybody heard anything.
        var confirmed = AudioPanelNotices.DescribeAudioCapability(
            new CapabilityState.Available("OS-DETAIL-VERBATIM"),
            new AudioRenderObservation(true, "Speakers", true, true, true, 0.4049835f, 6));
        Assert.Contains("OS-DETAIL-VERBATIM", confirmed, StringComparison.Ordinal);
        Assert.Contains("0.405", confirmed, StringComparison.Ordinal);
        Assert.Contains("does NOT prove your speakers were on", confirmed, StringComparison.Ordinal);

        // No meter, no number — "we could not read one" must never render as "we read zero".
        var noMeter = AudioPanelNotices.DescribeAudioCapability(
            new CapabilityState.Available("OS-DETAIL-VERBATIM"),
            new AudioRenderObservation(true, "Speakers", true, true, false, 0f, 6));
        Assert.DoesNotContain("metered", noMeter, StringComparison.Ordinal);
    }

    // =================================================================================
    //  SP-110 — the one resolution value that is reached by TWO opposite causes
    // =================================================================================

    [Fact]
    public void ARefusedCardSaysWHICHRefusalItWas_BecauseOneValueIsReachedByTwoOppositeCauses()
    {
        // THE DEFECT CLASS SP-105 AND SP-109 EACH SHIPPED ONCE: a sentence true for one branch and
        // false for another that reaches it. LockCardResolution.Refused is reached BOTH when the
        // operating system would not give the card the keyboard AND when it did and only the ink
        // read-back refused. A single sentence naming the first is a lie about the second — and the
        // second is the state a code review found this packet leaving on screen, so it is the one a
        // user is most likely to be reading about.
        //
        // The capability's own typed outcome is what tells them apart, so it is what decides the
        // words: this is the same rule the audio panel follows when it quotes the OS rather than
        // composing a sentence about a platform.
        var refusedForFocus = InputPanelNotices.DescribeCardState(
            EffectDotState.Live, cardCount: 1, last: null, sessionRunning: true, canReachAUser: true,
            prompting: false, holdsTheInput: false, LockCardResolution.Refused,
            new CapabilityState.Unavailable(new CapabilityReason(
                InputReasonCodes.InputNotCaptured, "the OS kept the foreground")));

        var refusedForInk = InputPanelNotices.DescribeCardState(
            EffectDotState.Live, cardCount: 1, last: null, sessionRunning: true, canReachAUser: true,
            prompting: false, holdsTheInput: false, LockCardResolution.Refused,
            new CapabilityState.Degraded("the card holds the keyboard", new CapabilityReason(
                InputReasonCodes.InputPromptNotInked, "no ink")));

        Assert.Contains("would not give it the keyboard", refusedForFocus, StringComparison.Ordinal);
        Assert.DoesNotContain("gave it the keyboard", refusedForFocus, StringComparison.Ordinal);

        Assert.Contains("gave it the keyboard", refusedForInk, StringComparison.Ordinal);
        Assert.DoesNotContain("would not give it the keyboard", refusedForInk,
            StringComparison.Ordinal);
        Assert.NotEqual(refusedForFocus, refusedForInk);

        // And the COUNT does not overclaim either: a card that was asked for and refused never
        // stayed on the user's screen, so the sentence says "asked for" rather than "shown" — the
        // same line LockCardEffect.CardCount's own doc draws.
        Assert.Contains("asked for so far", refusedForFocus, StringComparison.Ordinal);
        Assert.DoesNotContain("shown so far", refusedForFocus, StringComparison.Ordinal);

        // The three endings that are NOT ambiguous keep their own words, so this split cannot have
        // been bought by making every ending say the same thing.
        var solved = InputPanelNotices.DescribeCardState(
            EffectDotState.Live, 1, null, true, true, false, false, LockCardResolution.Solved, null);
        var dismissed = InputPanelNotices.DescribeCardState(
            EffectDotState.Live, 1, null, true, true, false, false, LockCardResolution.Dismissed, null);
        var withdrawn = InputPanelNotices.DescribeCardState(
            EffectDotState.Live, 1, null, true, true, false, false, LockCardResolution.Withdrawn, null);

        Assert.Contains("typed the last one out in full", solved, StringComparison.Ordinal);
        Assert.Contains("closed the last one with Esc", dismissed, StringComparison.Ordinal);
        Assert.Contains("taken down when the session stopped", withdrawn, StringComparison.Ordinal);
        Assert.Equal(4, new[] { solved, dismissed, withdrawn, refusedForInk }.Distinct().Count());
    }

    // =================================================================================
    //  SP-112 - the row with TWO channels, and one sentence per state that can differ
    // =================================================================================

    [Fact]
    public void EACHArmedSTATEOfTheTwoChannelRowGetsItsOwnSentence()
    {
        // THE SAME DEFECT CLASS, on a row that can be Armed for FIVE different reasons. A single
        // "not running" sentence would be false about four of them, and two of the five are the
        // states the two capabilities' own dot meanings exist for - a picture that stopped moving
        // and a keyboard that went elsewhere.
        string Line(
            bool sessionRunning, bool display, bool user, bool playing, bool asking) =>
            BubbleCountPanelNotices.DescribeGameState(
                EffectDotState.Armed, playedCount: 0, last: null, sessionRunning, display, user,
                playing, asking, BubbleCountResolution.None);

        var noSession = Line(false, true, true, false, false);
        var noDisplay = Line(true, false, true, false, false);
        var noUser = Line(true, true, false, false, false);
        var neither = Line(true, false, false, false, false);
        var frozen = Line(true, true, true, true, false);
        var unfocused = Line(true, true, true, false, true);
        var unscheduled = Line(true, true, true, false, false);

        Assert.Contains("until the session starts", noSession, StringComparison.Ordinal);
        Assert.Contains("nothing it plays can reach a screen", noDisplay, StringComparison.Ordinal);
        Assert.Contains("could never ask you the count", noUser, StringComparison.Ordinal);
        Assert.Contains("neither half of this game can reach you", neither, StringComparison.Ordinal);
        Assert.Contains("STOPPED changing", frozen, StringComparison.Ordinal);
        Assert.Contains("given the keyboard to another window", unfocused, StringComparison.Ordinal);
        Assert.Contains("not scheduled right now", unscheduled, StringComparison.Ordinal);

        // NONE of the five running states tells a user to start a session they already started -
        // the exact message SP-105 had to split apart.
        foreach (var line in new[] { noDisplay, noUser, neither, frozen, unfocused, unscheduled })
        {
            Assert.DoesNotContain("until the session starts", line, StringComparison.Ordinal);
        }

        // Seven states, seven sentences.
        Assert.Equal(
            7,
            new[] { noSession, noDisplay, noUser, neither, frozen, unfocused, unscheduled }
                .Distinct().Count());
    }

    [Fact]
    public void ALIVERowSaysWHICHHalfOfTheGameIsRunning_AndTheENDINGSAreAllDifferent()
    {
        string Live(bool playing, bool asking, BubbleCountResolution resolution, int played) =>
            BubbleCountPanelNotices.DescribeGameState(
                EffectDotState.Live, played, new BubbleCountEvent(3, default, BubbleCountDifficulty.Easy),
                sessionRunning: true, canReachADisplay: true, canReachAUser: true, playing, asking,
                resolution);

        Assert.Contains("Counting now", Live(true, false, BubbleCountResolution.None, 0), StringComparison.Ordinal);
        Assert.Contains("Asking you now", Live(false, true, BubbleCountResolution.None, 0), StringComparison.Ordinal);
        Assert.Contains("next game is on the clock", Live(false, false, BubbleCountResolution.None, 0), StringComparison.Ordinal);

        var counted = Live(false, false, BubbleCountResolution.Counted, 4);
        var missed = Live(false, false, BubbleCountResolution.Missed, 4);
        var dismissed = Live(false, false, BubbleCountResolution.Dismissed, 4);
        var withdrawn = Live(false, false, BubbleCountResolution.Withdrawn, 4);
        var refused = Live(false, false, BubbleCountResolution.Refused, 4);
        var abandoned = Live(false, false, BubbleCountResolution.Abandoned, 4);

        Assert.Contains("got the last count right", counted, StringComparison.Ordinal);
        Assert.Contains("used up every try", missed, StringComparison.Ordinal);
        Assert.Contains("closed the last question with Esc", dismissed, StringComparison.Ordinal);
        Assert.Contains("taken down when the session stopped", withdrawn, StringComparison.Ordinal);
        Assert.Contains("would not give it the keyboard", refused, StringComparison.Ordinal);

        // ABANDONED IS NOT MISSED, and the sentence has to keep them apart: a user who was never
        // asked must not be told they failed. That distinction is this module's own and it is the
        // one a panel is most likely to blur.
        Assert.Contains("could not really be watched", abandoned, StringComparison.Ordinal);
        Assert.DoesNotContain("used up every try", abandoned, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong", abandoned, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            6, new[] { counted, missed, dismissed, withdrawn, refused, abandoned }.Distinct().Count());

        // The count says "started", which is what it counts: games that really began a clip.
        Assert.Contains("4 games started so far", counted, StringComparison.Ordinal);
    }

    [Fact]
    public void THECLOSINGLineQuotesBOTHCapabilities_AndNeitherHalfSpeaksForTheOther()
    {
        // The only panel on the page that quotes two capabilities, because it is the only row that
        // needs two. Each half reports its OWN typed outcome; a single sentence would be false
        // about one of them whenever they disagree - which is exactly the interesting case.
        var videoRefused = BubbleCountPanelNotices.DescribeBothCapabilities(
            new CapabilityState.Unavailable(new CapabilityReason("video-no-display", "no display here")),
            new CapabilityState.Available("the OS gave the card the keyboard"));

        Assert.Contains("Video: no clip played. no display here", videoRefused, StringComparison.Ordinal);
        Assert.Contains("Question: the operating system gave it the keyboard", videoRefused, StringComparison.Ordinal);

        var inputRefused = BubbleCountPanelNotices.DescribeBothCapabilities(
            new CapabilityState.Available("the OS is holding the picture"),
            new CapabilityState.Unavailable(new CapabilityReason("input-not-captured", "the OS kept the foreground")));

        Assert.Contains("Video: the operating system is holding the picture", inputRefused, StringComparison.Ordinal);
        Assert.Contains("Question: none was shown. the OS kept the foreground", inputRefused, StringComparison.Ordinal);

        // Before anything has been asked, BOTH halves say nobody asked - a different fact from "the
        // answer was no", and the distinction the NotAsked observations exist to hold.
        var cold = BubbleCountPanelNotices.DescribeBothCapabilities(null, null);
        Assert.Contains("Video: nothing has been asked", cold, StringComparison.Ordinal);
        Assert.Contains("Question: nothing has been asked", cold, StringComparison.Ordinal);
    }

    [Fact]
    public void THECLIPPoolLineNamesTheSHAREDFolder_SoNobodyGoesLookingForASecondOne()
    {
        var empty = BubbleCountPanelNotices.DescribeClipPool(0, @"C:\media\videos");
        Assert.Contains("No clip to play", empty, StringComparison.Ordinal);
        Assert.Contains(@"C:\media\videos", empty, StringComparison.Ordinal);
        Assert.Contains("same folder Mandatory Video plays from", empty, StringComparison.Ordinal);

        var full = BubbleCountPanelNotices.DescribeClipPool(3, @"C:\media\videos");
        Assert.Contains("3 clips", full, StringComparison.Ordinal);
        Assert.Contains("own shuffled order", full, StringComparison.Ordinal);

        // One clip is not "1 clips".
        Assert.Contains("1 clip in", BubbleCountPanelNotices.DescribeClipPool(1, "x"), StringComparison.Ordinal);
    }

    [Fact]
    public void THEDIFFICULTYLineStatesUpstreamsOwnRate_InTheUnitsAUserThinksIn()
    {
        var easy = BubbleCountPanelNotices.DescribeDifficulty(BubbleCountDifficulty.Easy);
        var hard = BubbleCountPanelNotices.DescribeDifficulty(BubbleCountDifficulty.Hard);

        // 3 and 8 per thirty seconds are upstream's own numbers
        // (Windows/BubbleCountWindow.xaml.cs:1139-1145), stated rather than hidden behind a word.
        Assert.Contains("about 3 bubbles every 30 seconds", easy, StringComparison.Ordinal);
        Assert.Contains("about 8 bubbles every 30 seconds", hard, StringComparison.Ordinal);
        Assert.Contains("Three tries", easy, StringComparison.Ordinal);
    }
}
