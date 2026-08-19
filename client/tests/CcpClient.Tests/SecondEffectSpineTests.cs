using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-101 — TWO modules under one spine, and the three template hazards SP-098's review named.
///
/// <para><b>What this file is really testing.</b> Not subliminals. The claim under test is that the
/// session spine SP-098 built for one effect is a template rather than an accident: that a second
/// module with a different pacing period, a different default, a different pool and a different
/// counting rule runs under the same engine, the same clock, the same operation registry and the
/// same stop, without either module changing to accommodate the other. Every fact below fails if
/// the two modules share state they should not, or fail to share machinery they should.</para>
///
/// <para><b>Zero wall-clock.</b> Both modules pace on the same injected clock, advanced by hand.</para>
/// </summary>
public class SecondEffectSpineTests
{
    // ---------------------------------------------------------------------------------
    //  two modules, one session
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task PressingStart_ArmsBothModules_AndTheClockMakesBothKindsOfWorkComeDue()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals();

        Assert.True(rig.Engine.Start());

        // Both schedules are on the clock the instant START returns — WPF's services schedule
        // their first tick synchronously (FlashService.cs:352, SubliminalService.cs:105) — and they
        // are TWO timers, not one shared one.
        Assert.True(rig.Flash.ScheduleArmed);
        Assert.True(rig.Subliminals.ScheduleArmed);
        Assert.Equal(2, rig.Clock.PendingCount);

        rig.Clock.Advance(rig.LongestFlashInterval);

        Assert.Equal(1, rig.Flash.FlashCount);
        Assert.Equal(1, rig.Subliminals.SubliminalCount);
        Assert.Single(rig.Surface.Cards);
    }

    [Fact]
    public async Task TheTwoModulesPaceIndependently_TheFastOneRunsWhileTheSlowOneHasNotComeDueAtAll()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals();
        rig.Engine.Start();

        // Ten subliminal windows is at most 166 s of clock; Flash Images' own floor at its default
        // dial is 252 s (FlashService.cs:549-554), so the flash cannot have come due even once.
        for (var i = 0; i < 10; i++)
        {
            rig.Clock.Advance(rig.LongestSubliminalInterval);
        }

        // A second module that inherited the first's pacing law — the exact copy-paste this packet
        // exists to catch — would show these two counts moving together.
        Assert.Equal(10, rig.Subliminals.SubliminalCount);
        Assert.Equal(0, rig.Flash.FlashCount);
        Assert.Equal(10, rig.Surface.Cards.Count);
    }

    // ---------------------------------------------------------------------------------
    //  the difference that proves the template flexed: an empty pool is not a firing
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task ASubliminalOverAnEmptyPool_CountsNOTHING_ButTheScheduleKeepsRunning()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals(phrases: []);
        rig.Engine.Start();

        for (var i = 0; i < 5; i++)
        {
            rig.Clock.Advance(rig.LongestSubliminalInterval);
        }

        // THE difference from Flash Images, and the reason the shared base allows a firing to
        // produce nothing: WPF's FlashSubliminal returns at "No active subliminal texts"
        // (SubliminalService.cs:207-212) BEFORE the counter and the event at :611-612, while
        // Timer_Tick re-schedules regardless (:189-201).
        Assert.Equal(0, rig.Subliminals.SubliminalCount);
        Assert.Null(rig.Subliminals.Last);
        Assert.Empty(rig.Surface.Cards);

        // ...and it is still running, which is the half that would be lost if "nothing to show"
        // were treated as a stop.
        Assert.True(rig.Subliminals.ScheduleArmed);
        Assert.Equal(EffectDotState.Live, rig.Subliminals.Dot);
    }

    [Fact]
    public async Task TheSameEmptyPoolOnFlashImages_STILLCountsAFlash_SoTheTwoRulesReallyDiffer()
    {
        // The control for the fact above. If a later refactor "tidied" the two modules into one
        // counting rule, one of these two tests would go red — which is the whole point of having
        // both.
        await using var rig = await Rig.StartAsync(imageCount: 0);
        rig.EnableSubliminals(phrases: []);
        rig.Engine.Start();

        rig.Clock.Advance(rig.LongestFlashInterval);

        Assert.Equal(1, rig.Flash.FlashCount);
        Assert.True(rig.Flash.Last!.PoolWasEmpty);
        Assert.Equal(0, rig.Subliminals.SubliminalCount);
    }

    // ---------------------------------------------------------------------------------
    //  HAZARD 1: Arm() can refuse, and the two modules use the type differently
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task ArmingAModuleWhoseDialIsOff_SaysSoInType_InsteadOfBeingIndistinguishableFromSuccess()
    {
        await using var rig = await Rig.StartAsync();

        // Subliminals ships OFF (AppSettings.cs:1234), so a cold START arms it and it schedules
        // nothing. Before SP-101 that was literally the same observation as a successful arm.
        rig.Engine.Start();

        var flash = Assert.IsType<CapabilityState.Available>(rig.Engine.ArmOutcomes[FlashImagesEffect.EffectId]);
        Assert.Contains("flash", flash.Detail, StringComparison.Ordinal);

        var subliminal = Assert.IsType<CapabilityState.Unavailable>(rig.Engine.ArmOutcomes[SubliminalsEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.EffectDialOff, subliminal.Reason.Code);

        // And the session can NAME the holes rather than merely having them, in rack order.
        // SP-105 added a third module that also ships off (Pink Filter,
        // CCP.Core/Models/AppSettings.cs:3726) and SP-108 a fourth (Intensity Ramp, :2575-2580), so
        // this list has grown twice — the FACT is unchanged and the assertion is stronger each time:
        // three modules declined the same session for the same reason and all three are named, which
        // is the whole point of a typed arm.
        Assert.Equal(
            [SubliminalsEffect.EffectId, PinkFilterEffect.EffectId, IntensityRampEffect.EffectId],
            rig.Engine.ArmRefusals.Select(r => r.Id));
    }

    [Fact]
    public async Task AModuleThatIsPacedButCannotShowAnything_ArmsDEGRADED_NamingWhichHalfHolds()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals(phrases: []);

        rig.Engine.Start();

        // Not Unavailable: the schedule really is armed and really is on the clock. Not Available
        // either: every firing will be a no-op. Degraded is the only honest one of the three, and
        // it is what made the typed return load-bearing on the day it landed rather than a seat
        // kept warm for a module that does not exist yet.
        var state = Assert.IsType<CapabilityState.Degraded>(rig.Engine.ArmOutcomes[SubliminalsEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.SubliminalNoActivePhrase, state.Reason.Code);
        Assert.Contains("paced", state.SurvivingSemantics, StringComparison.Ordinal);
        Assert.True(rig.Subliminals.ScheduleArmed);

        // Flash Images cannot produce this state at all — its empty pool is a flash. Two modules,
        // two different vocabularies out of one typed return.
        Assert.IsType<CapabilityState.Available>(rig.Engine.ArmOutcomes[FlashImagesEffect.EffectId]);
    }

    [Fact]
    public async Task PuttingAPhraseBackInThePool_TurnsTheDegradedArmIntoAnAvailableOne()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals(phrases: []);
        rig.Engine.Start();
        Assert.IsType<CapabilityState.Degraded>(rig.Engine.ArmOutcomes[SubliminalsEffect.EffectId]);

        rig.Session.SubliminalPreset.Mutate(p => p.Phrases = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["GOOD GIRL"] = true,
        });
        Assert.IsType<CapabilityState.Available>(rig.Subliminals.Arm());

        rig.Clock.Advance(rig.LongestSubliminalInterval);
        Assert.Equal(1, rig.Subliminals.SubliminalCount);
    }

    // ---------------------------------------------------------------------------------
    //  HAZARD 2: Changed is the producer's duty now, not fifteen consumers'
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheSignal_RaisesInline_WhenThereIsNoUiToMarshalTo()
    {
        var boundary = new UiDispatchBoundary();
        var signal = new EffectSignal(boundary, onSignalThread: () => throw new InvalidOperationException(
            "the thread test must not be consulted before the boundary is bound — a process with no "
            + "Avalonia runtime would fault on it"));

        var raised = 0;
        signal.Raise(() => raised++);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void TheSignal_RaisesInlineOnTheSignalThread_AndPostsFromEverywhereElse()
    {
        var dispatch = new CountingDispatch();
        var boundary = new UiDispatchBoundary();
        boundary.Bind(dispatch);
        var onSignalThread = true;
        var signal = new EffectSignal(boundary, () => onSignalThread);

        var raised = 0;
        signal.Raise(() => raised++);

        // Already on the UI thread: inline, because deferring a synchronous gesture by a dispatcher
        // turn would change observable ordering for no safety gain. This is the rule the two
        // hand-written consumers implemented; it now lives once.
        Assert.Equal(1, raised);
        Assert.Equal(0, dispatch.Posts);

        onSignalThread = false;
        signal.Raise(() => raised++);

        Assert.Equal(2, raised);
        Assert.Equal(1, dispatch.Posts);
    }

    [Fact]
    public async Task EveryChangedNotificationArrivesThroughTheBoundary_WhenTheMoverIsNotTheUiThread()
    {
        var dispatch = new CountingDispatch();
        await using var rig = await Rig.StartAsync(dispatch: dispatch, onSignalThread: () => false);
        rig.EnableSubliminals();

        var changes = 0;
        var insideAPost = 0;
        rig.Engine.Changed += () =>
        {
            changes++;
            if (dispatch.InsideAPost)
            {
                insideAPost++;
            }
        };

        rig.Engine.Start();
        rig.Clock.Advance(rig.LongestSubliminalInterval);
        rig.Engine.QuickToggle(SubliminalsEffect.EffectId);
        rig.Engine.Stop();

        // Exact, not "at least": a single notification raised up the mover's own thread — which is
        // what every module did before SP-101, and what the fifteenth would have gone on doing —
        // makes these two numbers differ.
        Assert.True(changes > 0, "the session raised no Changed at all, so this fact proved nothing");
        Assert.Equal(changes, insideAPost);
    }

    // ---------------------------------------------------------------------------------
    //  HAZARD 3: a superseded one-shot must not fire, and must not touch the live schedule
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ASupersededOneShotFiringLate_NeitherFires_NorStealsTheLiveSchedulesSlot()
    {
        // The race the CompareExchange closes, driven directly. A frequency change re-schedules
        // while the previous one-shot's callback is already in flight on a pool thread; a real
        // timer cannot be summoned into that state on demand, and a test that waited for it to
        // happen by chance would be a wall-clock test — so the clock here keeps every callback it
        // was ever handed and the fact replays the superseded one.
        var lab = new EffectLab(new ReplayClock());
        var clock = lab.Clock;
        lab.Effect.SetEnabled(true);
        lab.Effect.Arm();

        Assert.Equal(1, clock.Count);
        lab.Effect.RefreshSchedule();
        Assert.Equal(2, clock.Count);

        clock.Replay(0);

        // Nothing fired: a schedule the user's own slider move replaced must not still arrive at
        // the old pace. And the LIVE schedule is untouched — before SP-101 the stale callback
        // nulled the pending slot, so the dot said Armed while a firing sat on the clock, and the
        // next Disarm found nothing to dispose and left the timer behind.
        Assert.Equal(0, lab.Effect.SubliminalCount);
        Assert.True(lab.Effect.ScheduleArmed);
        Assert.Equal(EffectDotState.Live, lab.Effect.Dot);

        // ...the live one still works,
        clock.Replay(1);
        Assert.Equal(1, lab.Effect.SubliminalCount);

        // ...and stop still reaches every handle this module ever took out.
        lab.Effect.Disarm();
        Assert.False(lab.Effect.ScheduleArmed);
        Assert.Equal(0, clock.LiveHandles);
    }

    [Fact]
    public void AOneShotThatIsCancelledWhileTheClockIsStillHandingBackItsHandle_IsDisposedAnyway()
    {
        // The other half of the same window: the token is published to the pending slot BEFORE the
        // clock is asked, so a cancellation landing in between finds it — and ScheduledFire.Attach
        // disposes the handle the instant it arrives. Without that, a stop could leave a live timer
        // that nothing owns.
        var token = new ScheduledFire();
        var handle = new CountingHandle();

        token.Dispose();
        token.Attach(handle);

        Assert.False(token.Alive);
        Assert.Equal(1, handle.Disposals);
    }

    // ---------------------------------------------------------------------------------
    //  stop really stops — both of them
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task AfterStop_NoAmountOfClockMakesEitherModuleWorkAgain()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals();
        rig.Engine.Start();
        rig.Clock.Advance(rig.LongestFlashInterval);

        var flashes = rig.Flash.FlashCount;
        var subliminals = rig.Subliminals.SubliminalCount;
        Assert.True(flashes > 0 && subliminals > 0);

        Assert.True(rig.Engine.Stop());

        Assert.False(rig.Flash.ScheduleArmed);
        Assert.False(rig.Subliminals.ScheduleArmed);
        Assert.Equal(0, rig.Clock.PendingCount);
        // The half of stop a user can SEE: the card comes off the screen.
        Assert.Equal(1, rig.Surface.HideAllCalls);

        for (var i = 0; i < 10; i++)
        {
            rig.Clock.Advance(rig.LongestFlashInterval);
        }

        Assert.Equal(flashes, rig.Flash.FlashCount);
        Assert.Equal(subliminals, rig.Subliminals.SubliminalCount);
    }

    [Fact]
    public async Task Stop_TerminatesBothOwnedOperations_Cancelled_WithNothingLeftOutstanding()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals();
        rig.Engine.Start();
        rig.Clock.Advance(rig.LongestSubliminalInterval);

        var flash = rig.Flash.Completion;
        var subliminal = rig.Subliminals.Completion;
        Assert.NotNull(flash);
        Assert.NotNull(subliminal);
        Assert.NotSame(flash, subliminal);

        rig.Engine.Stop();

        // Each module owns its OWN generation and its own registered operation: two schedules, two
        // terminal outcomes, one stop. A second module that shared the first's generation would
        // pass every counting fact above and fail here.
        await TestWait.Until(flash!, "the flash schedule's owned completion after stop");
        await TestWait.Until(subliminal!, "the subliminal schedule's owned completion after stop");
        Assert.IsType<OperationOutcome.Cancelled>(await flash!);
        Assert.IsType<OperationOutcome.Cancelled>(await subliminal!);
        await TestWait.Until(
            () => rig.Registry.OutstandingOperations == 0,
            "every owned operation to leave the registry after stop",
            () => $"outstanding={rig.Registry.OutstandingOperations}",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, rig.Registry.UnobservedOperations);
    }

    [Fact]
    public async Task HostTeardown_WithBothModulesRunning_KillsBothSchedules()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals();
        rig.Engine.Start();
        rig.Clock.Advance(rig.LongestSubliminalInterval);
        var subliminals = rig.Subliminals.SubliminalCount;
        Assert.True(subliminals > 0);

        // The user never pressed STOP; the app is closing. A DIFFERENT path from Stop.
        await rig.Host.ShutdownAsync();

        for (var i = 0; i < 5; i++)
        {
            rig.Clock.Advance(rig.LongestFlashInterval);
        }

        Assert.Equal(subliminals, rig.Subliminals.SubliminalCount);
        Assert.Equal(0, rig.Registry.OutstandingOperations);
        Assert.Equal(0, rig.Registry.UnobservedOperations);
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    // ---------------------------------------------------------------------------------
    //  the rack grammar reaches the second module through the SAME entry
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task QuickToggle_ReachesTheSecondModuleByItsOwnRackKey_AndNotTheFirstOne()
    {
        await using var rig = await Rig.StartAsync();
        rig.Engine.Start();

        // WPF's rack key for this row is "subliminal" (StudioTabView.xaml.cs:486, and the same key
        // MainWindow.Presets.cs:1252 switches on). Flipping it must not touch the flash row.
        Assert.True(rig.Engine.QuickToggle(SubliminalsEffect.EffectId));
        Assert.True(rig.Session.SubliminalPreset.Current.Enabled);
        Assert.True(rig.Subliminals.ScheduleArmed);
        Assert.True(rig.Session.Preset.Current.FlashEnabled);
        Assert.True(rig.Flash.ScheduleArmed);

        rig.Clock.Advance(rig.LongestSubliminalInterval);
        Assert.Equal(1, rig.Subliminals.SubliminalCount);

        // ...and off again mid-session stops it, and records why the module is no longer running.
        Assert.True(rig.Engine.QuickToggle(SubliminalsEffect.EffectId));
        Assert.False(rig.Subliminals.ScheduleArmed);
        Assert.Equal(EffectDotState.Off, rig.Subliminals.Dot);
        var state = Assert.IsType<CapabilityState.Unavailable>(rig.Engine.ArmOutcomes[SubliminalsEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.EffectDialOff, state.Reason.Code);

        rig.Clock.Advance(rig.LongestSubliminalInterval);
        Assert.Equal(1, rig.Subliminals.SubliminalCount);
    }

    [Fact]
    public async Task TheSecondModulesDialsSurviveARestart_ThroughItsOwnPersistedDocument()
    {
        var dir = Rig.NewDirectory();
        await using (var rig = await Rig.StartAsync(directory: dir))
        {
            rig.Engine.QuickToggle(SubliminalsEffect.EffectId);   // subliminals on
            rig.Session.SubliminalPreset.Mutate(p =>
            {
                p.PerMinute = 21;
                p.DurationFrames = 9;
                p.OpacityPercent = 45;
            });
            await rig.Session.FlushAsync(TimeSpan.FromSeconds(5));
        }

        await using var reopened = await Rig.StartAsync(directory: dir);

        // One document per module: the module's own file carries its own dials, and Flash Images'
        // are untouched by anything written here.
        Assert.True(reopened.Session.SubliminalPreset.Current.Enabled);
        Assert.Equal(21, reopened.Session.SubliminalPreset.Current.PerMinute);
        Assert.Equal(9, reopened.Session.SubliminalPreset.Current.DurationFrames);
        Assert.Equal(45, reopened.Session.SubliminalPreset.Current.OpacityPercent);
        Assert.Equal(EffectDotState.Armed, reopened.Subliminals.Dot);
        Assert.True(reopened.Session.Preset.Current.FlashEnabled);

        // ...in its OWN file, whose bytes carry its own dials and nothing else's. A missing file
        // throws here rather than being a predicate an assertion could hide behind.
        var written = await File.ReadAllTextAsync(
            Path.Combine(dir, SubliminalPresetDocument.FileName), TestContext.Current.CancellationToken);
        Assert.Contains("\"perMinute\": 21", written, StringComparison.Ordinal);
        Assert.DoesNotContain("flashesPerHour", written, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------
    //  what reaches the surface
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TheCardHandedToTheSurface_CarriesThePhraseAndTheModulesOwnDials()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals(phrases: ["ONE TRUE PHRASE"]);
        rig.Session.SubliminalPreset.Mutate(p =>
        {
            p.OpacityPercent = 55;
            p.DurationFrames = 10;
        });
        rig.Engine.Start();

        rig.Clock.Advance(rig.LongestSubliminalInterval);

        var card = Assert.Single(rig.Surface.Cards);
        Assert.Equal("ONE TRUE PHRASE", card.Text);
        Assert.Equal(55, card.OpacityPercent);
        Assert.Equal(SubliminalsEffect.CardLifetime(10), card.Lifetime);

        // ...and the EVENT a subscriber sees carries the count and the duration and NOT the words:
        // the same content-free rule FlashEvent holds.
        var fired = rig.Subliminals.Last!;
        Assert.Equal(1, fired.Ordinal);
        Assert.Equal(SubliminalsEffect.HoldMilliseconds(10), fired.HeldMilliseconds);
        Assert.DoesNotContain("PHRASE", fired.ToString(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------
    //  rig
    // ---------------------------------------------------------------------------------

    /// <summary>The REAL participant, engine, both modules and both persisted stores, with only the
    /// clock, the flash pool and the subliminal surface substituted.</summary>
    private sealed class Rig : IAsyncDisposable
    {
        private readonly bool _ownsDirectory;

        private Rig(ApplicationHost host, OperationRegistry registry, SessionParticipant session,
            ManualSessionClock clock, RecordingSubliminalSurface surface, string directory, bool ownsDirectory)
        {
            Host = host;
            Registry = registry;
            Session = session;
            Clock = clock;
            Surface = surface;
            Directory = directory;
            _ownsDirectory = ownsDirectory;
        }

        public ApplicationHost Host { get; }

        public OperationRegistry Registry { get; }

        public SessionParticipant Session { get; }

        public ManualSessionClock Clock { get; }

        public RecordingSubliminalSurface Surface { get; }

        public string Directory { get; }

        public SessionEngine Engine => Session.Engine;

        public FlashImagesEffect Flash => Session.Flash;

        public SubliminalsEffect Subliminals => Session.Subliminals;

        public TimeSpan LongestFlashInterval =>
            FlashSchedule.MaximumInterval(Session.Preset.Current.FlashesPerHour) + TimeSpan.FromSeconds(1);

        public TimeSpan LongestSubliminalInterval =>
            SubliminalSchedule.MaximumInterval(Session.SubliminalPreset.Current.PerMinute) + TimeSpan.FromSeconds(1);

        public static string NewDirectory() =>
            Path.Combine(Path.GetTempPath(), "ccp-sp101-" + Guid.NewGuid().ToString("N"));

        public static async Task<Rig> StartAsync(
            int imageCount = 3,
            string? directory = null,
            IUiDispatch? dispatch = null,
            Func<bool>? onSignalThread = null)
        {
            var dir = directory ?? NewDirectory();
            System.IO.Directory.CreateDirectory(dir);
            var registry = new OperationRegistry();
            var log = new NullSink();
            var boundary = new UiDispatchBoundary();
            boundary.Bind(dispatch ?? new CountingDispatch());
            var infra = new ParticipantInfrastructure(registry, boundary, log);
            var clock = new ManualSessionClock();
            var surface = new RecordingSubliminalSurface();
            var session = new SessionParticipant(
                infra, dir, clock, new StubPool(imageCount), new NullFlashSurface(), surface,
                // This project has no Avalonia runtime to ask, which is exactly why EffectSignal
                // takes the predicate rather than reaching for the dispatcher itself.
                onSignalThread ?? (static () => true));
            var host = new ApplicationHost(log, [session], new StartupTrace(), registry, infra.UiDispatch);

            Assert.IsType<StartupOutcome.Success>(
                await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

            return new Rig(host, registry, session, clock, surface, dir, ownsDirectory: directory is null);
        }

        /// <summary>Switch the module on, and optionally replace its phrase pool. Writes the real
        /// persisted document, exactly as the rack's quick-toggle does.</summary>
        public void EnableSubliminals(IReadOnlyList<string>? phrases = null)
        {
            Session.SubliminalPreset.Mutate(p =>
            {
                p.Enabled = true;
                if (phrases is null)
                {
                    return;
                }

                var pool = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var phrase in phrases)
                {
                    pool[phrase] = true;
                }

                // An explicitly EMPTY pool is a pool with everything switched off — the document's
                // own "an absent pool means the shipped defaults" rule must not resurrect them.
                if (pool.Count == 0)
                {
                    pool["EVERY PHRASE IS UNCHECKED"] = false;
                }

                p.Phrases = pool;
            });
        }

        public async ValueTask DisposeAsync()
        {
            await Host.ShutdownAsync();
            if (!_ownsDirectory)
            {
                // A caller that named the directory is reusing it (the restart round-trip).
                return;
            }

            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>The Subliminals module alone, on a clock the test drives callback by callback. Used
    /// only where the SESSION would be noise: the hazard-3 race is a property of one module's
    /// schedule, and a second module's timers on the same clock would obscure which handle is
    /// whose.</summary>
    private sealed class EffectLab
    {
        public EffectLab(ReplayClock clock)
        {
            Clock = clock;
            var registry = new OperationRegistry();
            var path = Path.Combine(Path.GetTempPath(), "ccp-sp101-lab-" + Guid.NewGuid().ToString("N") + ".json");
            Preset = new PersistenceStore<SubliminalPresetDocument>(
                registry.OwnerFor("LabSubliminalPreset"), new NullSink(), path,
                SubliminalPresetDocument.CurrentSchemaVersion);
            Effect = new SubliminalsEffect(
                registry.OwnerFor("LabSubliminals"),
                new EffectSignal(new UiDispatchBoundary()),
                clock,
                new SubliminalPhrasePool(Preset, new Random(5)),
                Preset);
        }

        public ReplayClock Clock { get; }

        public PersistenceStore<SubliminalPresetDocument> Preset { get; }

        public SubliminalsEffect Effect { get; }
    }

    /// <summary>Runs posted delegates immediately, counts them, and records that the delegate is
    /// running INSIDE one — which is how a fact tells "went through the boundary" from "ran on
    /// whatever thread called it".</summary>
    private sealed class CountingDispatch : IUiDispatch
    {
        public int Posts { get; private set; }

        public bool InsideAPost { get; private set; }

        public void Post(Action action)
        {
            Posts++;
            var outer = InsideAPost;
            InsideAPost = true;
            try
            {
                action();
            }
            finally
            {
                InsideAPost = outer;
            }
        }
    }

    private sealed class CountingHandle : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class NullSink : ILogSink
    {
        public void Log(string message)
        {
        }
    }

    private sealed class StubPool(int population) : IFlashImagePool
    {
        private readonly string[] _images =
            Enumerable.Range(0, population).Select(i => $"image-{i}.png").ToArray();

        public IReadOnlyList<string> Draw(int count) =>
            _images.Length == 0 ? [] : [.. Enumerable.Range(0, count).Select(i => _images[i % _images.Length])];
    }

    private sealed class NullFlashSurface : IFlashSurface
    {
        public CapabilityState? LastPlacement => null;

        public void Show(IReadOnlyList<string> paths)
        {
        }

        public void HideAll()
        {
        }
    }

    /// <summary>A subliminal surface that records the cards it was handed and never touches a screen.</summary>
    private sealed class RecordingSubliminalSurface : ISubliminalSurface
    {
        public List<SubliminalCard> Cards { get; } = [];

        public int HideAllCalls { get; private set; }

        public CapabilityState? LastPlacement { get; private set; }

        public void Show(SubliminalCard card)
        {
            Cards.Add(card);
            LastPlacement = new CapabilityState.Available("recording surface: shown");
        }

        public void HideAll() => HideAllCalls++;
    }

    /// <summary>The manual clock, SP-098's shape: due timers fire in due order inside
    /// <see cref="Advance"/>. Zero wall-clock.</summary>
    private sealed class ManualSessionClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

        public int PendingCount
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Count;
                }
            }
        }

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

    /// <summary>
    /// A clock that REMEMBERS every callback it was ever handed, including the ones whose handle has
    /// been disposed, so a fact can replay a superseded one-shot on demand.
    /// </summary>
    private sealed class ReplayClock : ISessionClock
    {
        private readonly List<Entry> _entries = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

        /// <summary>Every callback ever scheduled, spent or not.</summary>
        public int Count
        {
            get
            {
                lock (_entries)
                {
                    return _entries.Count;
                }
            }
        }

        /// <summary>Handles that have not been disposed. After a disarm this must be zero.</summary>
        public int LiveHandles
        {
            get
            {
                lock (_entries)
                {
                    return _entries.Count(e => !e.Disposed);
                }
            }
        }

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            lock (_entries)
            {
                _entries.Add(entry);
            }

            return new Handle(entry);
        }

        /// <summary>Invoke one recorded callback, disposed or not — the in-flight stale timer.</summary>
        public void Replay(int index)
        {
            Entry entry;
            lock (_entries)
            {
                entry = _entries[index];
            }

            UtcNow = entry.Due;
            entry.Fire();
        }

        private sealed class Entry
        {
            public DateTimeOffset Due { get; init; }

            public required Action Fire { get; init; }

            public bool Disposed { get; set; }
        }

        private sealed class Handle(Entry entry) : IDisposable
        {
            public void Dispose() => entry.Disposed = true;
        }
    }
}
