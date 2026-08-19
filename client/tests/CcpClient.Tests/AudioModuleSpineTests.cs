using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-109 — SEVEN modules under one spine, and two of them cannot be seen at all.
///
/// <para><b>What is under test here, and what is deliberately NOT.</b> These facts drive the two
/// audio modules through a RECORDING presence, so the pacing, the probability, the clamps, the
/// stop-replace, the arm vocabulary and the dot are all decided deterministically with no device
/// anywhere. The claim that sound really reaches an output device is NOT made here — it is made in
/// <see cref="AudioCapabilityTests"/>, against the operating system, and the split is on purpose: a
/// fake presence that reported "playing" would be exactly the fake dial re-imposing its own clamp
/// that this port has rejected before.</para>
///
/// <para><b>The dot's fifth meaning is the sharp end</b>, and three facts pin it:
/// <see cref="WithNoRenderSessionTheModulesAreARMED_NotLive_EvenWithAFiringOnTheClock"/> (the OS
/// clause), <see cref="AnEmptyClipFolderIsDegradedButSTILLLive_TheSubliminalsAnswerNotThePinkFilterAnswer"/>
/// (the line between a missing CHANNEL and missing CONTENT), and
/// <see cref="BrainDrainsHalfRowIsLIVE_BecauseEverythingThisRowClaimsToBeIsRunning"/> (the subset
/// rule).</para>
///
/// <para><b>Zero wall-clock.</b> Everything rides the one injected clock the modules share.</para>
/// </summary>
public class AudioModuleSpineTests
{
    // =================================================================================
    //  seven modules, and the two new ones take the session
    // =================================================================================

    [Fact]
    public async Task PressingStartArmsAllTen_InWpfsOwnRackOrder()
    {
        await using var rig = await Rig.StartAsync();

        // WPF's group order is EFFECTS, GAMES & CARDS, IMMERSION, TIMING
        // (StudioTabView.xaml.cs:482-541). SP-110 opened GAMES & CARDS with Lock Card, so it sits
        // between the effects and the two IMMERSION rows — which is also StartEngine's own order
        // (the lock card at :206-209, then Mind Wipe :229-230 and Brain Drain :241-244, then the
        // ramp timer :265-269).
        //
        // SP-112 adds Bubble Count to that group and puts it FIRST in it, which is where the RACK
        // puts it (Bubble Pop, Bubble Count, Lock Card, Bouncing Text — :499-505) and NOT where
        // StartEngine does (the lock card at :206-209, then the bubble count at :212-215). The two
        // upstream orders disagree here, exactly as they do for Spiral Overlay and Pink Filter, and
        // D90 already settled which one this port takes: the rack's, because the rack is the order
        // the user has learned.
        //
        // SP-113 adds Bubble Pop and it goes FIRST in that group, which is where the rack puts it
        // (Add("bubbles", ...) at StudioTabView.xaml.cs:499, before bubblecount at :501 and lockcard
        // at :503). Nothing in StartEngine competes for its position: the ambient game is started
        // from the dashboard card and from SessionEngine.cs:444, not from StartEngine's effect
        // sequence, so the rack's order stands unopposed here.
        Assert.Equal(
            [
                FlashImagesEffect.EffectId, MandatoryVideoEffect.EffectId, SubliminalsEffect.EffectId,
                SpiralOverlayEffect.EffectId, BouncingTextEffect.EffectId,
                PinkFilterEffect.EffectId, BubblePopEffect.EffectId, BubbleCountEffect.EffectId,
                LockCardEffect.EffectId,
                MindWipeEffect.EffectId, BrainDrainEffect.EffectId, IntensityRampEffect.EffectId,
            ],
            rig.Engine.Effects.Select(e => e.Id));
    }

    [Fact]
    public async Task AnArmedAudioModuleIsLIVE_AndItsFiringsReallyReachTheAudioCapability()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableMindWipe(perHour: MindWipeSchedule.MaxPerHour);
        rig.Rolls.AlwaysWin();

        rig.Engine.Start();
        Assert.Equal(EffectDotState.Live, rig.MindWipe.Dot);
        Assert.IsType<CapabilityState.Available>(rig.Engine.ArmOutcomes[MindWipeEffect.EffectId]);

        rig.Clock.Advance(MindWipeSchedule.Window);

        Assert.Equal(1, rig.MindWipe.CueCount);
        var cue = Assert.Single(rig.Audio.Cues);
        Assert.Equal(MindWipeEffect.EffectId, cue.Slot);
        Assert.Equal(rig.MindWipeClip, cue.Path);
    }

    [Fact]
    public async Task StopSilencesTheModulesOwnSlot_AndOnlyItsOwn()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableMindWipe();
        rig.EnableBrainDrain();
        rig.Rolls.AlwaysWin();
        rig.Engine.Start();
        rig.Clock.Advance(MindWipeSchedule.Window);

        rig.Engine.QuickToggle(MindWipeEffect.EffectId);

        // Upstream's StopCurrentAudio is per-SERVICE, and each service owns one player field
        // (MindWipeService.cs:857-874 / BrainDrainService.cs:316-334). Here the slot is the module
        // id, so switching one module off can never silence the other's clip.
        Assert.Equal([MindWipeEffect.EffectId], rig.Audio.Silenced);
    }

    // =================================================================================
    //  THE DOT'S FIFTH MEANING — three facts, and each one is a different half of the rule
    // =================================================================================

    [Fact]
    public async Task WithNoRenderSessionTheModulesAreARMED_NotLive_EvenWithAFiringOnTheClock()
    {
        await using var rig = await Rig.StartAsync(rendering: false);
        rig.EnableMindWipe();
        rig.EnableBrainDrain();

        rig.Engine.Start();

        // The schedule really IS on the clock — the paced half of the dot is true — and the dot is
        // still Armed, because nothing this module plays can reach anybody. This is SP-105's rule
        // (a module whose whole output channel is gone is not running) applied to a channel nobody
        // can check by looking, and it is upstream's own gate: "endpoint down — stay quiet, don't
        // spin" (MindWipeService.cs:771, BrainDrainService.cs:211).
        Assert.True(rig.MindWipe.ScheduleArmed);
        Assert.True(rig.BrainDrain.ScheduleArmed);
        Assert.Equal(EffectDotState.Armed, rig.MindWipe.Dot);
        Assert.Equal(EffectDotState.Armed, rig.BrainDrain.Dot);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(
            rig.Engine.ArmOutcomes[MindWipeEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.AudioRenderUnavailable, refusal.Reason.Code);

        // And nothing is played, however many windows come due — a module that kept cueing into a
        // dead device is upstream's #778/#779 spin.
        rig.Rolls.AlwaysWin();
        for (var i = 0; i < 50; i++)
        {
            rig.Clock.Advance(MindWipeSchedule.Window);
        }

        Assert.Empty(rig.Audio.Cues);
        Assert.Equal(0, rig.MindWipe.CueCount);
    }

    [Fact]
    public async Task AnEmptyClipFolderIsDegradedButSTILLLive_TheSubliminalsAnswerNotThePinkFilterAnswer()
    {
        await using var rig = await Rig.StartAsync(withClips: false);
        rig.EnableMindWipe();
        rig.Rolls.AlwaysWin();

        rig.Engine.Start();

        // A POOL is content; a DEVICE is a channel. The module is genuinely running on a confirmed
        // render session, and dropping a clip in the folder starts producing sound with no re-arm —
        // which is exactly Subliminals over an empty phrase pool (Degraded arm, Live dot), and NOT
        // Pink Filter with a refused overlay (Degraded arm, Armed dot).
        var degraded = Assert.IsType<CapabilityState.Degraded>(
            rig.Engine.ArmOutcomes[MindWipeEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.AudioNoClip, degraded.Reason.Code);
        Assert.Equal(EffectDotState.Live, rig.MindWipe.Dot);

        rig.Clock.Advance(MindWipeSchedule.Window);

        // Nothing counted, nothing played, and the schedule keeps running — upstream's own outcome
        // (MindWipeService.cs:704-708 returns before the roll and the timer is untouched).
        Assert.Equal(0, rig.MindWipe.CueCount);
        Assert.Empty(rig.Audio.Cues);
        Assert.True(rig.MindWipe.ScheduleArmed);
    }

    [Fact]
    public async Task BrainDrainsHalfRowIsLIVE_BecauseEverythingThisRowClaimsToBeIsRunning()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableBrainDrain();
        rig.Rolls.AlwaysWin();

        rig.Engine.Start();
        rig.Clock.Advance(BrainDrainSchedule.NormalWindow);

        // THE FIFTH DOT MEANING'S SUBSET RULE. Two landed rules disagree here — SP-105's says a
        // module that cannot draw is Armed, the Subliminals rule says a module whose schedule is
        // really on the clock is Live — and this packet decides Live, on the ground that the dot is
        // scoped to what the ROW is. Everything this row claims to be is running and audible.
        Assert.Equal(EffectDotState.Live, rig.BrainDrain.Dot);
        Assert.Equal(1, rig.BrainDrain.CueCount);

        // And the scope is on the row where the user reads it — which is what MAKES the Live above
        // honest rather than an under-claim. If this title were ever changed to plain "Brain Drain",
        // the dot would become a lie, so the title is pinned here beside the dot it justifies.
        Assert.Equal("Brain Drain (audio half)", rig.BrainDrain.Title);
    }

    [Fact]
    public void BOTHClausesOfTheFifthDotMeaningAreLoadBearing_PinnedOnThePredicateItself()
    {
        // The sweep found the CLOCK clause open: no product path this file can construct leaves a
        // module armed, its generation live and its audio confirmed while its schedule is NOT on the
        // clock, so deleting `ScheduleArmed &&` was invisible. It is pinned directly instead — the
        // precedent MovingEffectSpineTests set for ReleaseIfStillOurs, and for the same reason: a
        // clause that can only fail on a rare interleaving should fail on EVERY run when it is
        // deleted, not on the runs where the thread pool obliges.
        var registry = new OperationRegistry();
        var boundary = new UiDispatchBoundary();
        boundary.Bind(new InlineDispatch());
        var infra = new ParticipantInfrastructure(registry, boundary, new NullSink());
        var signal = new EffectSignal(boundary, static () => true);
        var clock = new ManualSessionClock();
        var presence = new RecordingAudioPresence(rendering: true);
        var probe = new WorkIsRunningProbe(
            infra.OwnerFor("Probe"), signal, clock, new StubCuePool("clip.mp3", "folder"), presence);

        probe.SetEnabled(true);
        probe.Arm();

        // Both clauses true: on the clock AND the OS confirms. This is the only Live.
        Assert.True(probe.ScheduleArmed);
        Assert.True(presence.IsRendering);
        Assert.True(probe.ExposedWorkIsRunning);
        Assert.Equal(EffectDotState.Live, probe.Dot);

        // Drop ONLY the clock clause: still armed, still confirmed, nothing scheduled.
        probe.ForceReleaseWork();
        Assert.False(probe.ScheduleArmed);
        Assert.True(presence.IsRendering);
        Assert.False(probe.ExposedWorkIsRunning);
        Assert.Equal(EffectDotState.Armed, probe.Dot);
    }

    [Fact]
    public async Task ArmingAnEnabledAudioModuleOPENSTheDevice_AndArmingNoneNeverDoes()
    {
        // The sweep's M-t: deleting the device open from the arm survived, because the recording
        // presence reports a CONSTANT rendering state and the only fact that read Opens asserted it
        // was ZERO. Both directions are asserted here.
        await using var off = await Rig.StartAsync();
        off.Engine.Start();
        // Both audio modules ship OFF, and that is exactly why the device is opened at ARM rather
        // than at app start: a user who never enables either never gives this process a WASAPI
        // session or a row in their volume mixer.
        Assert.Equal(0, off.Audio.Opens);

        await using var on = await Rig.StartAsync();
        on.EnableMindWipe();
        on.Engine.Start();
        Assert.True(on.Audio.Opens > 0, "arming an enabled audio module asks the OS for a device");
    }

    // =================================================================================
    //  BRAIN DRAIN IS HALF A ROW, AND SAYS SO ON EVERY HEALTHY RUN
    // =================================================================================

    [Fact]
    public async Task BrainDrainNEVERArmsAvailable_TheMissingVisualHalfIsOnEveryRunNotOnlyOnFailures()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableBrainDrain();

        rig.Engine.Start();

        // Everything is healthy: confirmed render session, clips present, dial on. Every other
        // module in the rack reports a clean Available in this state. This one must not, because the
        // absence is a property of the BUILD rather than of the run.
        var degraded = Assert.IsType<CapabilityState.Degraded>(
            rig.Engine.ArmOutcomes[BrainDrainEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.BrainDrainVisualHalfAbsent, degraded.Reason.Code);
        Assert.Contains("AUDIO half of Brain Drain is armed", degraded.SurvivingSemantics, StringComparison.Ordinal);

        // The reason has to NAME the missing mechanism, not merely flag it: what it was, that
        // upstream's own panel calls it the VISUAL half, and that it is absent rather than broken.
        Assert.Contains("VISUAL half", degraded.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("blurs and melts", degraded.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("30 to 60 times a second", degraded.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("ABSENT here rather than broken", degraded.Reason.Detail, StringComparison.Ordinal);

        // Mind Wipe, in the same session, IS a whole row — so the Degraded above is this module's
        // own honesty and not something the audio capability does to everybody.
        rig.Engine.QuickToggle(MindWipeEffect.EffectId);
        Assert.IsType<CapabilityState.Available>(rig.Engine.ArmOutcomes[MindWipeEffect.EffectId]);
    }

    [Fact]
    public async Task BrainDrainsAudioRefusalIsNotBuriedUnderTheVisualHalfNotice()
    {
        // When the audio half itself cannot run, THAT is the fact the user needs; stacking the
        // permanent visual-half notice on top of it would bury the actionable one.
        await using var rig = await Rig.StartAsync(rendering: false);
        rig.EnableBrainDrain();

        rig.Engine.Start();

        var refusal = Assert.IsType<CapabilityState.Unavailable>(
            rig.Engine.ArmOutcomes[BrainDrainEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.AudioRenderUnavailable, refusal.Reason.Code);
    }

    // =================================================================================
    //  the numbers — upstream's arithmetic, kept exactly
    // =================================================================================

    [Theory]
    [InlineData(180, 0.5)]   // upstream's own stated expectation at the ceiling (MindWipeService.cs:733)
    [InlineData(360, 1.0)]   // above the clamp, shown here as the law rather than the dial
    [InlineData(36, 0.1)]
    [InlineData(6, 6.0 / 360.0)]
    public void MindWipesTriggerProbabilityIsPerHourOver360_NotOver120(int perHour, double expected)
    {
        // The divisor is 3600/10 — ten-second windows in an hour. Upstream's code comment says
        // "30-second window" (MindWipeService.cs:710) and its timer interval is 10 s
        // (:127-129); the ARITHMETIC at :734 is /360.0 and it is the behaviour. A port that
        // followed the comment would fire a third as often as the shipping app.
        Assert.Equal(expected, MindWipeSchedule.TriggerProbability(perHour), 10);
        Assert.Equal(TimeSpan.FromSeconds(10), MindWipeSchedule.Window);
    }

    [Theory]
    [InlineData(100, false, 100.0 / 100.0 / 12.0)] // 5 s window: twelve ticks a minute
    [InlineData(100, true, 100.0 / 100.0 / 120.0)] // 500 ms window: a hundred and twenty
    [InlineData(50, false, 50.0 / 100.0 / 12.0)]
    public void BrainDrainsProbabilityIsNormalisedPerMinute_SoHighRefreshChangesGrainNotRate(
        int intensity, bool highRefresh, double expected)
    {
        var window = BrainDrainSchedule.Window(highRefresh);
        Assert.Equal(expected, BrainDrainSchedule.TriggerProbability(intensity, window), 10);

        // The point of the normalisation, stated as an assertion: the same intensity produces the
        // same expected number of clips per minute in either mode (BrainDrainService.cs:183).
        var ticksPerMinute = 60.0 / window.TotalSeconds;
        Assert.Equal(intensity / 100.0, BrainDrainSchedule.TriggerProbability(intensity, window) * ticksPerMinute, 10);
    }

    [Fact]
    public void TheDialClampsAreUpstreams_AndTheyLiveInTheSettersWhereUpstreamPutsThem()
    {
        var mindWipe = new MindWipePresetDocument { PerHour = 5000, VolumePercent = 900 };
        Assert.Equal(MindWipeSchedule.MaxPerHour, mindWipe.PerHour);         // Math.Clamp(value, 1, 180)
        Assert.Equal(MindWipePresetDocument.MaxVolumePercent, mindWipe.VolumePercent);
        mindWipe.PerHour = -20;
        mindWipe.VolumePercent = -20;
        Assert.Equal(MindWipeSchedule.MinPerHour, mindWipe.PerHour);
        Assert.Equal(MindWipePresetDocument.MinVolumePercent, mindWipe.VolumePercent);

        var brainDrain = new BrainDrainPresetDocument { IntensityPercent = 5000 };
        Assert.Equal(BrainDrainSchedule.MaxIntensity, brainDrain.IntensityPercent); // Math.Clamp(value, 1, 100)
        brainDrain.IntensityPercent = 0;
        Assert.Equal(BrainDrainSchedule.MinIntensity, brainDrain.IntensityPercent);

        // Both ship OFF, which is what StartEngine gates on (MainWindow.StartStop.cs:229, :242).
        Assert.False(new MindWipePresetDocument().Enabled);
        Assert.False(new BrainDrainPresetDocument().Enabled);
    }

    [Fact]
    public async Task ALostRollFiresNOTHINGAndCountsNOTHING_AndTheScheduleKeepsRunning()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableMindWipe();
        rig.Rolls.AlwaysLose();

        rig.Engine.Start();
        for (var i = 0; i < 100; i++)
        {
            rig.Clock.Advance(MindWipeSchedule.Window);
        }

        // Upstream does not count a window that lost its roll — PlayAudioNow is simply not reached
        // (MindWipeService.cs:741-745) — and the timer is untouched, so the module is still running.
        Assert.Equal(0, rig.MindWipe.CueCount);
        Assert.Empty(rig.Audio.Cues);
        Assert.True(rig.MindWipe.ScheduleArmed);
        Assert.Equal(EffectDotState.Live, rig.MindWipe.Dot);
    }

    [Fact]
    public async Task TheWindowIsFIXED_TheDialMovesTheODDS_WhichIsNotHowTheOtherPacedModulesWork()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableMindWipe(perHour: 1);
        rig.Rolls.AlwaysWin();
        rig.Engine.Start();

        // Flash Images and Subliminals would space their next firing by the dial. This one does not:
        // at the FLOOR of its dial the next window is still ten seconds away, and the dial only
        // changed how likely that window is to produce a clip.
        rig.Clock.Advance(MindWipeSchedule.Window);
        Assert.Equal(1, rig.MindWipe.CueCount);

        rig.EnableMindWipe(perHour: MindWipeSchedule.MaxPerHour);
        rig.Clock.Advance(MindWipeSchedule.Window);
        Assert.Equal(2, rig.MindWipe.CueCount);
    }

    [Fact]
    public async Task BrainDrainsHighRefreshSwitchRePacesTheLiveSchedule_ItDoesNotWaitOutTheOldWindow()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableBrainDrain();
        rig.Rolls.AlwaysWin();
        rig.Engine.Start();

        rig.BrainDrain.SetHighRefresh(true);
        rig.Clock.Advance(BrainDrainSchedule.HighRefreshWindow);

        // The window IS the schedule here, so a switch that only took effect at the next firing
        // would leave a five-second tick running after the user asked for 500 ms.
        Assert.Equal(1, rig.BrainDrain.CueCount);
    }

    [Fact]
    public async Task TheCuesTypedOutcomeIsRemembered_SoAStartedClipTheOsStoppedConfirmingIsVisible()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableMindWipe();
        rig.Rolls.AlwaysWin();
        rig.Engine.Start();

        rig.Audio.NextCueOutcome = new CapabilityState.Degraded(
            "a player is attached to the mixer",
            new CapabilityReason(AudioReasonCodes.RenderSessionUnconfirmed, "the OS stopped confirming"));
        rig.Clock.Advance(MindWipeSchedule.Window);

        // Without this the state a capability trusting its own call would show as working would be
        // invisible to the user and to a bug report.
        var last = Assert.IsType<CapabilityState.Degraded>(rig.MindWipe.LastCue);
        Assert.Equal(AudioReasonCodes.RenderSessionUnconfirmed, last.Reason.Code);
    }

    // =================================================================================
    //  the five landed modules are untouched by any of this
    // =================================================================================

    [Fact]
    public async Task TheFiveLandedModulesStillArmExactlyAsBefore_WithNoAudioAnywhereNearThem()
    {
        await using var rig = await Rig.StartAsync();
        rig.Session.PinkFilterPreset.Mutate(p => p.Enabled = true);

        rig.Engine.Start();

        // Flash Images ships ON (AppSettings.cs:751-756) and Spiral Overlay ships ON (:2645), so both
        // arm Available; Subliminals and the ramp ship OFF, so their dial-off refusal is the landed
        // vocabulary. None of it may move because a capability none of them uses has arrived.
        Assert.IsType<CapabilityState.Available>(rig.Engine.ArmOutcomes[FlashImagesEffect.EffectId]);
        Assert.IsType<CapabilityState.Available>(rig.Engine.ArmOutcomes[SpiralOverlayEffect.EffectId]);
        Assert.IsType<CapabilityState.Available>(rig.Engine.ArmOutcomes[PinkFilterEffect.EffectId]);
        foreach (var id in new[] { SubliminalsEffect.EffectId, IntensityRampEffect.EffectId })
        {
            var refusal = Assert.IsType<CapabilityState.Unavailable>(rig.Engine.ArmOutcomes[id]);
            Assert.Equal(EffectReasonCodes.EffectDialOff, refusal.Reason.Code);
        }

        Assert.Equal(EffectDotState.Live, rig.Session.Flash.Dot);
        Assert.Equal(EffectDotState.Off, rig.Session.Subliminals.Dot);
        Assert.Empty(rig.Audio.Cues);

        // And no audio device was opened for a session with both audio modules switched off — the
        // reason the presence is opened at ARM rather than at app start.
        Assert.Equal(0, rig.Audio.Opens);
    }

    // =================================================================================

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _directory;

        private Rig(
            ApplicationHost host, SessionParticipant session, ManualSessionClock clock,
            RecordingAudioPresence audio, ScriptedRolls rolls, string mindWipeClip, string directory)
        {
            Host = host;
            Session = session;
            Clock = clock;
            Audio = audio;
            Rolls = rolls;
            MindWipeClip = mindWipeClip;
            _directory = directory;
        }

        public ApplicationHost Host { get; }

        public SessionParticipant Session { get; }

        public ManualSessionClock Clock { get; }

        public RecordingAudioPresence Audio { get; }

        public ScriptedRolls Rolls { get; }

        public string MindWipeClip { get; }

        public SessionEngine Engine => Session.Engine;

        public MindWipeEffect MindWipe => Session.MindWipe;

        public BrainDrainEffect BrainDrain => Session.BrainDrain;

        public static async Task<Rig> StartAsync(bool rendering = true, bool withClips = true)
        {
            var dir = Path.Combine(Path.GetTempPath(), "ccp-sp109m-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var spirals = SpiralLibrary.Folder(SessionParticipant.AssetsRootFor(dir));
            Directory.CreateDirectory(spirals);
            File.WriteAllBytes(Path.Combine(spirals, "classic.gif"), [0x00]);

            var mindWipeClip = Path.Combine(dir, "mindwipe.wav");
            var brainDrainClip = Path.Combine(dir, "braindrain.wav");
            File.WriteAllBytes(mindWipeClip, [0x00]);
            File.WriteAllBytes(brainDrainClip, [0x00]);

            var registry = new OperationRegistry();
            var log = new NullSink();
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            var infra = new ParticipantInfrastructure(registry, boundary, log);
            var clock = new ManualSessionClock();
            var audio = new RecordingAudioPresence(rendering);
            var rolls = new ScriptedRolls();

            var session = new SessionParticipant(
                infra, dir, clock, new StubPool(3), new NullFlashSurface(), new NullSubliminalSurface(),
                static () => true, new NullPinkFilterSurface(), new NullSpiralSurface(),
                audio,
                new StubCuePool(withClips ? mindWipeClip : null, Path.Combine(dir, "sounds", "mindwipe")),
                new StubCuePool(withClips ? brainDrainClip : null, Path.Combine(dir, "sounds", "braindrain")),
                // The roll, injected. A fact says "every window wins" or "every window loses" without
                // pinning a seed, and without any double re-deriving the module's probability.
                rolls.MindWipe,
                rolls.BrainDrain);

            var host = new ApplicationHost(log, [session], new StartupTrace(), registry, infra.UiDispatch);
            Assert.IsType<StartupOutcome.Success>(
                await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

            return new Rig(host, session, clock, audio, rolls, mindWipeClip, dir);
        }

        public void EnableMindWipe(int perHour = MindWipeSchedule.DefaultPerHour) =>
            Session.MindWipePreset.Mutate(p =>
            {
                p.Enabled = true;
                p.PerHour = perHour;
            });

        public void EnableBrainDrain(int intensity = BrainDrainSchedule.DefaultIntensity) =>
            Session.BrainDrainPreset.Mutate(p =>
            {
                p.Enabled = true;
                p.IntensityPercent = intensity;
            });

        public async ValueTask DisposeAsync()
        {
            await Host.ShutdownAsync();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// An audio presence that records what it was asked to do and never touches a device.
    ///
    /// <para><b>It deliberately does not decide anything the product should decide.</b> It reports
    /// whatever rendering state the fact asked for and hands back whatever outcome the fact set —
    /// it never re-derives a probability, never re-imposes a clamp, and never turns a cue into a
    /// success on its own. A double that made those decisions would be the fake dial this port has
    /// already been caught by twice.</para>
    /// </summary>
    private sealed class RecordingAudioPresence : IAudioPresence
    {
        private readonly bool _rendering;

        public RecordingAudioPresence(bool rendering) => _rendering = rendering;

        public List<AudioCue> Cues { get; } = [];

        public List<string> Silenced { get; } = [];

        public int Opens { get; private set; }

        public CapabilityState? NextCueOutcome { get; set; }

        public bool IsRendering => _rendering;

        public CapabilityState? LastOpen { get; private set; }

        public AudioRenderObservation LastObservation => _rendering
            ? new AudioRenderObservation(true, "Recording Endpoint", true, true, true, 0.5f, 1)
            : AudioRenderObservation.NotAsked;

        public CapabilityState Open()
        {
            Opens++;
            return LastOpen = _rendering
                ? new CapabilityState.Available("recording presence: the OS confirmed a render session")
                : new CapabilityState.Unavailable(new CapabilityReason(
                    AudioReasonCodes.NoRenderEndpoint, "recording presence: no render endpoint"));
        }

        public CapabilityState Cue(AudioCue cue)
        {
            Cues.Add(cue);
            return NextCueOutcome ?? new CapabilityState.Available("recording presence: cued");
        }

        public CapabilityState Silence(string slot)
        {
            Silenced.Add(slot);
            return new CapabilityState.Available("recording presence: silenced");
        }

        public AudioRenderObservation Observe() => LastObservation;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// A bare <see cref="AudioCueEffect"/> that exposes the dot's third input and the schedule
    /// release, so both conjuncts of the fifth dot meaning can be moved INDEPENDENTLY. It adds no
    /// behaviour of its own: every number it answers with is a constant, so nothing here can
    /// re-derive a decision the product should be making.
    /// </summary>
    private sealed class WorkIsRunningProbe : AudioCueEffect
    {
        private bool _enabled;

        public WorkIsRunningProbe(
            AsyncOperationOwner owner, EffectSignal signal, ISessionClock clock, IAudioCuePool pool,
            IAudioPresence presence)
            : base(owner, signal, clock, "probe-schedule", pool, presence)
        {
        }

        public override string Id => "probe";

        public override string Title => "Probe";

        public override bool Enabled => _enabled;

        /// <summary>The dot's third input, verbatim.</summary>
        public bool ExposedWorkIsRunning => WorkIsRunning;

        /// <summary>Drops the pending one-shot without disarming, which is the only way to hold one
        /// clause true and the other false.</summary>
        public void ForceReleaseWork() => ReleaseWork();

        public override void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            RaiseChanged();
        }

        protected override TimeSpan Window => TimeSpan.FromSeconds(10);

        protected override double TriggerProbability => 1.0;

        protected override float Volume => 1f;
    }

    /// <summary>A clip pool with one file, or none. No disk, so an empty pool is a state a fact
    /// chooses rather than a folder it has to arrange.</summary>
    private sealed class StubCuePool : IAudioCuePool
    {
        private readonly string? _clip;

        public StubCuePool(string? clip, string folder)
        {
            _clip = clip;
            Folder = folder;
        }

        public int ActiveCount => _clip is null ? 0 : 1;

        public string Folder { get; }

        public string? Draw() => _clip;
    }

    /// <summary>
    /// Makes the roll deterministic without pinning a seed, by replacing the module's
    /// <see cref="Random"/> with one whose <c>NextDouble</c> is scripted.
    ///
    /// <para>The probability itself is the PRODUCT's — this only decides which side of it the draw
    /// lands on, so a fact can say "every window wins" while the arithmetic under test stays the
    /// module's own.</para>
    /// </summary>
    private sealed class ScriptedRolls
    {
        public ScriptedRandom MindWipe { get; } = new();

        public ScriptedRandom BrainDrain { get; } = new();

        /// <summary>Every window wins its roll. The PROBABILITY is still the module's own — this
        /// only decides which side of it the draw lands on.</summary>
        public void AlwaysWin()
        {
            MindWipe.Value = 0.0;
            BrainDrain.Value = 0.0;
        }

        /// <summary>Every window loses its roll. 0.999999 is below no probability this port can
        /// produce (Mind Wipe tops out at 0.5, Brain Drain at 1/12).</summary>
        public void AlwaysLose()
        {
            MindWipe.Value = 0.999999;
            BrainDrain.Value = 0.999999;
        }
    }

    private sealed class ScriptedRandom : Random
    {
        public double Value { get; set; } = 0.5;

        public override double NextDouble() => Value;
    }

    // ---- the local doubles for the four modules this file is NOT about ----
    //
    // Each spine test file carries its own copies (ContinuousEffectSpineTests.cs:371-470 and its
    // siblings), because they are private to the fixture that uses them. Everything here draws
    // nothing and records nothing: this file's subject is audio, and the four drawing modules are
    // present only so the two new ones are exercised in a REAL rack rather than alone.

    private sealed class NullFlashSurface : IFlashSurface
    {
        public CapabilityState? LastPlacement => null;

        public void Show(IReadOnlyList<string> images)
        {
        }

        public void HideAll()
        {
        }
    }

    private sealed class NullSubliminalSurface : ISubliminalSurface
    {
        public CapabilityState? LastPlacement => null;

        public void Show(SubliminalCard card)
        {
        }

        public void HideAll()
        {
        }
    }

    private sealed class NullPinkFilterSurface : IPinkFilterSurface
    {
        public bool Showing { get; private set; }

        public CapabilityState? LastPlacement { get; private set; }

        public CapabilityState Engage(PinkFilterTint tint)
        {
            Showing = true;
            return LastPlacement = new CapabilityState.Available("null surface: engaged");
        }

        public void Withdraw() => Showing = false;
    }

    private sealed class NullSpiralSurface : ISpiralSurface
    {
        public bool Showing { get; private set; }

        public bool Running => Showing;

        public bool Engaged => Showing;

        public int FrameCount => Showing ? 1 : 0;

        public CapabilityState? LastPlacement { get; private set; }

        public CapabilityState Engage(string spiralPath, SpiralPresentation presentation)
        {
            Showing = true;
            return LastPlacement = new CapabilityState.Available("null surface: engaged");
        }

        public void Withdraw() => Showing = false;
    }

    private sealed class StubPool(int population) : IFlashImagePool
    {
        public IReadOnlyList<string> Draw(int count) =>
            [.. Enumerable.Range(0, Math.Min(count, population)).Select(i => $"image-{i}.png")];
    }

    private sealed class InlineDispatch : IUiDispatch
    {
        public void Post(Action action) => action();
    }

    private sealed class NullSink : ILogSink
    {
        public void Log(string message)
        {
        }
    }

    /// <summary>The manual clock, SP-098's shape. Zero wall-clock.</summary>
    private sealed class ManualSessionClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            lock (_timers)
            {
                _timers.Add(entry);
            }

            return new Handle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => t.Due <= UtcNow).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        return;
                    }

                    _timers.Remove(next);
                }

                next.Fire();
            }
        }

        private sealed class Entry
        {
            public DateTimeOffset Due { get; init; }

            public required Action Fire { get; init; }
        }

        private sealed class Handle(ManualSessionClock clock, Entry entry) : IDisposable
        {
            public void Dispose()
            {
                lock (clock._timers)
                {
                    clock._timers.Remove(entry);
                }
            }
        }
    }
}
