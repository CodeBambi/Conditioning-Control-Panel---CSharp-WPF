using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// FIVE modules under one spine, and the fifth one draws nothing.
///
/// <para><b>The claim under test.</b> Every seam the port had proven — <c>OwnedSessionEffect</c>,
/// <c>OverlaySurfaceSet</c>, the dot's three states, a presenter owning the cadence — had only ever
/// been proven against modules from WPF's EFFECTS group that painted an overlay. These facts run a
/// module from the TIMING group beside them: it has no surface, is never "due", and its whole
/// observable output is the opacity numbers on the two modules next to it.</para>
///
/// <para><b>Two of them are the packet's traps, stated as assertions.</b>
/// <see cref="TheRampItselfPutsNOTHINGOnScreen_NoOverlayIsEverAcquiredForIt"/> is trap 2 — a module
/// that needs no surface must not acquire one — and
/// <see cref="ARampWithNothingLinkedIsARMEDNotLive_EvenThoughItsCadenceIsRunningInTheSameSession"/>
/// is trap 1, the dot refusing to say "running" about a module nobody can observe.</para>
///
/// <para><b>Zero wall-clock.</b> Everything rides the one injected clock the modules share.</para>
/// </summary>
public class NonDrawingEffectSpineTests
{
    // ---------------------------------------------------------------------------------
    //  the fifth module, running beside four that draw
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task PressingStartArmsAllFive_AndTheOneThatDrawsNothingHoldsTheOtherTwoModulesDials()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.EnableRamp(linkSpiral: true, linkPink: true, multiplier: 2.0);

        Assert.True(rig.Engine.Start());

        // The two paced modules are on the clock, the two continuous ones are on the screen, and the
        // fifth is holding the dials of two of the others. Four kinds of "running", one dot enum.
        Assert.True(rig.Flash.ScheduleArmed);
        Assert.True(rig.PinkPresence.Presents > 0);
        Assert.Equal(
            [PinkFilterEffect.EffectId, SpiralOverlayEffect.EffectId],
            rig.Ramp.HeldDials.Order(StringComparer.Ordinal));
        Assert.Equal(EffectDotState.Live, rig.Ramp.Dot);

        Assert.IsType<CapabilityState.Available>(rig.Engine.ArmOutcomes[IntensityRampEffect.EffectId]);
    }

    [Fact]
    public async Task TheRampDrivesTheREALModulesDials_AndTheirOwnPanelsWouldSeeExactlyThat()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.EnableRamp(linkPink: true, multiplier: 2.0, durationMinutes: 60);

        var basePink = rig.Session.PinkFilterPreset.Current.OpacityPercent;
        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        // The number on the Pink Filter module's own panel really moved, because the ramp wrote the
        // Pink Filter module's own persisted dial — not a copy, not a shadow value.
        var halfway = rig.Session.PinkFilterPreset.Current.OpacityPercent;
        Assert.True(halfway > basePink, $"the tint climbed from {basePink} to {halfway}");
        Assert.Equal(halfway, rig.PinkFilter.Tint.OpacityPercent);
        Assert.Equal(basePink, rig.Ramp.BaseValueFor(PinkFilterEffect.EffectId));
    }

    [Fact]
    public async Task TheRampReTintsTheLIVEOverlay_SoTheClimbIsSeenWithoutRestartingTheSession()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.EnableRamp(linkPink: true, multiplier: 3.0, durationMinutes: 60);

        rig.Engine.Start();
        var presentsAtStart = rig.PinkPresence.Presents;

        for (var i = 0; i < 900; i++)
        {
            rig.Clock.Advance(IntensityRampEffect.TickInterval);
        }

        // WPF's opacity write reaches the LIVE window through the overlay service's reconcile
        // (OverlayService.cs:434-437). Here it reaches it because the ramp asks the MODULE to
        // re-apply, which re-places the tint on the surface that is already up. A ramp that only
        // wrote the document would leave the user staring at the opacity they started with.
        Assert.True(
            rig.PinkPresence.Presents > presentsAtStart,
            "the live tint was re-placed while the session ran");

        // And it is not re-placed twice a second: only when the integer really moves (D96).
        Assert.True(
            rig.PinkPresence.Presents - presentsAtStart < 60,
            $"{rig.PinkPresence.Presents - presentsAtStart} re-places over 900 samples");
    }

    // ---------------------------------------------------------------------------------
    //  TRAP 2 — no surface, and none acquired for symmetry
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TheRampItselfPutsNOTHINGOnScreen_NoOverlayIsEverAcquiredForIt()
    {
        await using var rig = await Rig.StartAsync();

        // ONLY the ramp is on. Both drawing modules that could hold a surface are off — Spiral
        // Overlay is the one ported module that ships ON (AppSettings.cs:2645), so it has to be
        // switched off by hand here — and the ramp is linked to both of them. If the module reached
        // for an overlay to look like its siblings, this is where it would show.
        rig.Session.SpiralPreset.Mutate(p => p.Enabled = false);
        rig.EnableRamp(linkSpiral: true, linkPink: true, multiplier: 3.0);

        rig.Engine.Start();
        for (var i = 0; i < 200; i++)
        {
            rig.Clock.Advance(IntensityRampEffect.TickInterval);
        }

        Assert.Empty(rig.PinkPresence.Calls);
        Assert.Empty(rig.SpiralPresence.Calls);
        Assert.False(rig.Session.PinkFilterSurface.Showing);
        Assert.False(rig.Session.SpiralSurface.Showing);

        // It IS running, and it is running with nothing on any screen: it holds both dials, and the
        // two modules that own them are switched off and stay switched off.
        Assert.Equal(EffectDotState.Live, rig.Ramp.Dot);
        Assert.Equal(2, rig.Ramp.HeldDials.Count);
    }

    // ---------------------------------------------------------------------------------
    //  TRAP 1 — what the dot may say when there is nothing to look at
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task ARampWithNothingLinkedIsARMEDNotLive_EvenThoughItsCadenceIsRunningInTheSameSession()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.EnableRamp(multiplier: 3.0);          // enabled, and nothing linked to it

        rig.Engine.Start();

        // The session is genuinely running and so is this module's 2-second sample. A dot derived
        // the way a PACED module's is — "a callback is on the clock" — would say Live here about a
        // module that will change nothing, anywhere, for the whole session.
        Assert.True(rig.Engine.Running);
        Assert.Equal(EffectDotState.Live, rig.PinkFilter.Dot);
        Assert.Equal(EffectDotState.Armed, rig.Ramp.Dot);

        var degraded = Assert.IsType<CapabilityState.Degraded>(
            rig.Engine.ArmOutcomes[IntensityRampEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.RampNoLinkedDial, degraded.Reason.Code);

        var opacity = rig.Session.PinkFilterPreset.Current.OpacityPercent;
        for (var i = 0; i < 400; i++)
        {
            rig.Clock.Advance(IntensityRampEffect.TickInterval);
        }

        Assert.Equal(EffectDotState.Armed, rig.Ramp.Dot);
        Assert.Equal(opacity, rig.Session.PinkFilterPreset.Current.OpacityPercent);
    }

    // ---------------------------------------------------------------------------------
    //  giving it back, on every route out
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task StopGivesTheUsersOwnOpacityBack_SoNoFlushCanEverWriteARampedValue()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.EnableSpiral();
        rig.Session.PinkFilterPreset.Mutate(p => p.OpacityPercent = 12);
        rig.Session.SpiralPreset.Mutate(p => p.OpacityPercent = 21);
        rig.EnableRamp(linkSpiral: true, linkPink: true, multiplier: 2.0);

        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(2));
        Assert.True(rig.Session.PinkFilterPreset.Current.OpacityPercent > 12);
        Assert.True(rig.Session.SpiralPreset.Current.OpacityPercent > 21);

        Assert.True(rig.Engine.Stop());

        // The DOCUMENTS are back where the user left them, which is what the persistence flush will
        // later write. Upstream reaches the same outcome by a different route — every exit path calls
        // StopEngine before SaveSettings, and StopEngine -> StopRampTimer IS the restore
        // (MainWindow.StartStop.cs:388 -> :437-479) — so this is parity, not a fix. D95.
        Assert.Equal(12, rig.Session.PinkFilterPreset.Current.OpacityPercent);
        Assert.Equal(21, rig.Session.SpiralPreset.Current.OpacityPercent);
        Assert.Empty(rig.Ramp.HeldDials);
    }

    [Fact]
    public async Task TheRampsTHIRDDialIsFlashOpacity_AndItIsHeldDrivenAndGivenBackLikeTheOtherTwo()
    {
        // D93 said flash opacity's link was absent because it had "no dial on any ported
        // panel"; the Visuals row is that panel, so this is the condition being discharged rather
        // than waived. The dial's Reapply is a NO-OP (D174 — a live flash keeps the alpha it was
        // placed with), so the whole of custody has to rest on Write, and that is what this checks.
        // The base is SIXTY on purpose. WPF's arm is min(base * currentMult, 100)
        // (MainWindow/MainWindow.StartStop.cs:508): the cap is a CEILING, not a target, so a base
        // of 30 at 2.0x finishes at 60 and never reaches it. At 60 the climb is unclamped halfway
        // (90) and clamped at the end (120 -> 100), which is the only way one run exercises both.
        await using var rig = await Rig.StartAsync();
        rig.Session.VisualsPreset.Mutate(p => p.FlashOpacityPercent = 60);
        rig.Session.RampPreset.Mutate(p =>
        {
            p.Enabled = true;
            p.LinkFlashOpacity = true;
            p.Multiplier = 2.0;
            p.DurationMinutes = 60;
        });

        rig.Engine.Start();
        Assert.Contains(FlashImagesEffect.EffectId, rig.Ramp.HeldDials);
        Assert.Equal(60, rig.Ramp.BaseValueFor(FlashImagesEffect.EffectId));

        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        // The Visuals panel's own number really moved, and so did the reading the NEXT flash will
        // be drawn with — the ramp wrote the module's own persisted dial, not a shadow copy.
        var halfway = rig.Session.VisualsPreset.Current.FlashOpacityPercent;
        Assert.Equal(90, halfway);
        Assert.Equal(halfway, rig.Session.Visuals.Draw().OpacityPercent);

        // A full climb lands exactly on the ceiling rather than past it — the multiply would give
        // 120, and both WPF's own min and the document's clamp put it at 100.
        rig.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(VisualsPresetDocument.MaxFlashOpacityPercent,
            rig.Session.VisualsPreset.Current.FlashOpacityPercent);

        Assert.True(rig.Engine.Stop());
        Assert.Equal(60, rig.Session.VisualsPreset.Current.FlashOpacityPercent);
        Assert.Empty(rig.Ramp.HeldDials);
    }

    [Fact]
    public async Task HostTeardownGivesTheDialsBackToo_WithoutGoingThroughStopAtAll()
    {
        var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.Session.PinkFilterPreset.Mutate(p => p.OpacityPercent = 12);
        rig.EnableRamp(linkPink: true, multiplier: 3.0);
        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(36, rig.Session.PinkFilterPreset.Current.OpacityPercent);

        await rig.Host.ShutdownAsync();

        // Two independent routes reach this — the host cancels every generation before participants
        // stop, and the participant's own stop runs Engine.Stop() — and either alone must leave the
        // user's number where they left it.
        Assert.Equal(12, rig.Session.PinkFilterPreset.Current.OpacityPercent);
        Assert.NotEqual(EffectDotState.Live, rig.Ramp.Dot);
        await rig.DisposeAsync();
    }

    // ---------------------------------------------------------------------------------
    //  the rack's second gesture, on a module with nothing to see
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TheQuickToggleSwitchesTheRampOnMidSession_AndItTakesCustodyImmediately()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.Session.PinkFilterPreset.Mutate(p => p.OpacityPercent = 12);
        rig.Session.RampPreset.Mutate(p =>
        {
            p.LinkPinkFilterOpacity = true;
            p.Multiplier = 3.0;
        });

        rig.Engine.Start();
        Assert.Equal(EffectDotState.Off, rig.Ramp.Dot);
        Assert.Empty(rig.Ramp.HeldDials);

        Assert.True(rig.Engine.QuickToggle(IntensityRampEffect.EffectId));

        Assert.Equal(EffectDotState.Live, rig.Ramp.Dot);
        Assert.Equal(12, rig.Ramp.BaseValueFor(PinkFilterEffect.EffectId));

        rig.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(36, rig.Session.PinkFilterPreset.Current.OpacityPercent);
    }

    [Fact]
    public async Task TheQuickToggleSwitchesItOffMidSession_AndTheDialComesBackAtOnce()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.Session.PinkFilterPreset.Mutate(p => p.OpacityPercent = 12);
        rig.EnableRamp(linkPink: true, multiplier: 3.0);

        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(36, rig.Session.PinkFilterPreset.Current.OpacityPercent);

        Assert.True(rig.Engine.QuickToggle(IntensityRampEffect.EffectId));

        // WPF's own ramp quick-toggle is INERT mid-session — it flips the flag and the running timer
        // ignores it entirely (StudioTabView.xaml.cs:539-541 -> IntensityRampFeatureControl.xaml.cs:82-89,
        // which writes and saves and touches no timer), so upstream's dot goes dark while the ramp
        // keeps ramping. Here the dial is the authority the whole spine already gives it, so the
        // ramp really stops and really gives the dial back. D94.
        Assert.Equal(EffectDotState.Off, rig.Ramp.Dot);
        Assert.Equal(12, rig.Session.PinkFilterPreset.Current.OpacityPercent);
        Assert.Empty(rig.Ramp.HeldDials);
    }

    // ---------------------------------------------------------------------------------
    //  the first module that can END a session
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TheRampCanENDTheSession_WhichNoModuleBeforeItCouldDo()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.Session.PinkFilterPreset.Mutate(p => p.OpacityPercent = 12);
        rig.EnableRamp(linkPink: true, multiplier: 2.0, durationMinutes: 30);
        rig.Session.RampPreset.Mutate(p => p.EndSessionOnComplete = true);

        rig.Engine.Start();
        Assert.True(rig.Engine.Running);

        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        // WPF: `if (progress >= 1.0 && settings.EndSessionOnRampComplete && !sessionActive)` ->
        // StopEngine() (MainWindow.StartStop.cs:547-555). The whole session goes down, every module
        // with it, and the dials the ramp held come back on the way.
        Assert.False(rig.Engine.Running);
        Assert.False(rig.Flash.ScheduleArmed);
        Assert.False(rig.Session.PinkFilterSurface.Showing);
        Assert.Equal(12, rig.Session.PinkFilterPreset.Current.OpacityPercent);
    }

    [Fact]
    public async Task ARampThatCompletesWithoutTheSwitchLeavesTheSessionRunning_HoldingItsDialsAtTheTop()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.Session.PinkFilterPreset.Mutate(p => p.OpacityPercent = 12);
        rig.EnableRamp(linkPink: true, multiplier: 2.0, durationMinutes: 30);

        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(4));

        // EndSessionOnRampComplete ships OFF (AppSettings.cs:2624-2629), so the default outcome is a
        // ramp that finishes and a session that keeps going. And the finished ramp is STILL Live: it
        // is still holding the user's dial and still owes it back, which is exactly why "Live means
        // it will change a moment from now" is the wrong rule for this module.
        Assert.True(rig.Engine.Running);
        Assert.Equal(24, rig.Session.PinkFilterPreset.Current.OpacityPercent);
        Assert.Equal(1.0, rig.Ramp.Progress, 6);
        Assert.Equal(EffectDotState.Live, rig.Ramp.Dot);

        rig.Engine.Stop();
        Assert.Equal(12, rig.Session.PinkFilterPreset.Current.OpacityPercent);
    }

    // ---------------------------------------------------------------------------------
    //  the four landed modules, unchanged
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TheFourLandedModulesRunExactlyAsBefore_WithTheFifthOneRunningBesideThem()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals();
        rig.EnablePinkFilter();
        rig.EnableSpiral();
        rig.EnableRamp(linkSpiral: true, linkPink: true, multiplier: 2.0);

        rig.Engine.Start();
        for (var i = 0; i < 40; i++)
        {
            rig.Clock.Advance(rig.LongestFlashInterval);
        }

        Assert.True(rig.Flash.FlashCount >= 40);
        Assert.True(rig.Subliminals.SubliminalCount > 0);
        Assert.Equal(EffectDotState.Live, rig.Flash.Dot);
        Assert.Equal(EffectDotState.Live, rig.Subliminals.Dot);
        Assert.Equal(EffectDotState.Live, rig.PinkFilter.Dot);
        Assert.Equal(EffectDotState.Live, rig.Spiral.Dot);
        Assert.True(rig.Session.SpiralSurface.Showing);
        Assert.True(rig.Session.PinkFilterSurface.Showing);
    }

    // =====================================================================================

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _directory;

        private Rig(
            ApplicationHost host, SessionParticipant session, ManualSessionClock clock,
            RecordingPresence spiralPresence, RecordingPresence pinkPresence, string directory)
        {
            Host = host;
            Session = session;
            Clock = clock;
            SpiralPresence = spiralPresence;
            PinkPresence = pinkPresence;
            _directory = directory;
        }

        public ApplicationHost Host { get; }

        public SessionParticipant Session { get; }

        public ManualSessionClock Clock { get; }

        public RecordingPresence SpiralPresence { get; }

        public RecordingPresence PinkPresence { get; }

        public SessionEngine Engine => Session.Engine;

        public FlashImagesEffect Flash => Session.Flash;

        public SubliminalsEffect Subliminals => Session.Subliminals;

        public PinkFilterEffect PinkFilter => Session.PinkFilter;

        public SpiralOverlayEffect Spiral => Session.Spiral;

        public IntensityRampEffect Ramp => Session.Ramp;

        public TimeSpan LongestFlashInterval =>
            FlashSchedule.MaximumInterval(Session.Preset.Current.FlashesPerHour) + TimeSpan.FromSeconds(1);

        public static async Task<Rig> StartAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ccp-sp108-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var spirals = SpiralLibrary.Folder(SessionParticipant.AssetsRootFor(dir));
            Directory.CreateDirectory(spirals);
            File.WriteAllBytes(Path.Combine(spirals, "classic.gif"), [0x00]);

            var registry = new OperationRegistry();
            var log = new NullSink();
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            var infra = new ParticipantInfrastructure(registry, boundary, log);
            var clock = new ManualSessionClock();
            var spiralPresence = new RecordingPresence();
            var pinkPresence = new RecordingPresence();

            var spiralSurface = new SpiralSurfacePresenter(
                clock, action => action(), () => spiralPresence, new StubSpiralFrames(),
                () => new OverlayBounds(0, 0, 1920, 1080));
            var pinkSurface = new PinkFilterSurfacePresenter(
                clock, action => action(), () => pinkPresence,
                () => new OverlayBounds(0, 0, 1920, 1080));

            var session = new SessionParticipant(
                infra, dir, clock, new StubPool(3), new NullFlashSurface(), new NullSubliminalSurface(),
                static () => true, pinkSurface, spiralSurface);
            var host = new ApplicationHost(log, [session], new StartupTrace(), registry, infra.UiDispatch);

            Assert.IsType<StartupOutcome.Success>(
                await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

            return new Rig(host, session, clock, spiralPresence, pinkPresence, dir);
        }

        public void EnableSubliminals() => Session.SubliminalPreset.Mutate(p => p.Enabled = true);

        public void EnablePinkFilter() => Session.PinkFilterPreset.Mutate(p => p.Enabled = true);

        /// <summary>Spiral Overlay is the one ported module that ships ON (AppSettings.cs:2645), so
        /// this is a no-op that says so rather than a state change.</summary>
        public void EnableSpiral() => Session.SpiralPreset.Mutate(p => p.Enabled = true);

        public void EnableRamp(
            bool linkSpiral = false, bool linkPink = false, double multiplier = 1.0,
            int durationMinutes = 60) =>
            Session.RampPreset.Mutate(p =>
            {
                p.Enabled = true;
                p.LinkSpiralOpacity = linkSpiral;
                p.LinkPinkFilterOpacity = linkPink;
                p.Multiplier = multiplier;
                p.DurationMinutes = durationMinutes;
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

    /// <summary>An overlay that records what it was asked to do and never touches a screen.</summary>
    private sealed class RecordingPresence : IOverlayPresence
    {
        private OverlaySurfaceRequest? _current;

        public List<string> Calls { get; } = [];

        public int Presents { get; private set; }

        public bool IsPresenting => _current is not null;

        public CapabilityState Present(OverlaySurfaceRequest request)
        {
            Calls.Add("present");
            Presents++;
            _current = request;
            return new CapabilityState.Available("recording presence: placed");
        }

        public CapabilityState Paint(OverlayFrame frame)
        {
            Calls.Add("paint");
            return new CapabilityState.Available("recording presence: painted");
        }

        public void Reassert() => Calls.Add("reassert");

        public CapabilityState SetClickThrough(bool clickThrough)
        {
            Calls.Add("set-click-through");
            return new CapabilityState.Available("recording presence: flipped");
        }

        public CapabilityState Withdraw()
        {
            Calls.Add("withdraw");
            _current = null;
            return new CapabilityState.Available("recording presence: withdrawn");
        }

        public void Dispose() => _current = null;
    }

    private sealed class StubSpiralFrames : ISpiralFrameSource
    {
        public ISpiralAnimation? Open(string path, int width, int height) =>
            new StubAnimation(width, height);

        private sealed class StubAnimation(int width, int height) : ISpiralAnimation
        {
            public int FrameCount => 4;

            public TimeSpan FrameDelay => SpiralFrameDelay.Default;

            public OverlayFrame? Render(int index) =>
                OverlayFrame.Solid(width, height, (byte)index, (byte)index, (byte)index);

            public void Dispose()
            {
            }
        }
    }

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

    /// <summary>The manual clock, in the shape every module test shares. Zero wall-clock.</summary>
    private sealed class ManualSessionClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

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

            return new CancelHandle(this, entry);
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
            public DateTimeOffset Due;

            public required Action Fire;
        }

        private sealed class CancelHandle(ManualSessionClock clock, Entry entry) : IDisposable
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
