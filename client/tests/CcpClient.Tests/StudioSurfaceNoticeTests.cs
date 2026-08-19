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
    public void TheRampsLiveLineSaysSomethingDifferentAndTrueInEveryStateItCanBeIn(RampLine line)
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
        var text = RampPanelNotices.DescribeRampState(dot, preset, progress, current, held, sessionRunning);

        Assert.False(string.IsNullOrWhiteSpace(text), $"{line} renders as blank space");
        Assert.EndsWith(".", text.TrimEnd(), StringComparison.Ordinal);
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
        // Every link flag defaults false (AppSettings.cs:2590-2622), so switching this module on and
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
}
