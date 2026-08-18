using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-098 — the conditioning session spine, and the one effect running under it.
///
/// <para><b>The subject of this file is not a flag.</b> WPF's <c>START</c> starts real work and
/// its stop really stops it (<c>MainWindow/MainWindow.StartStop.cs:159-410</c>). Every fact
/// below therefore drives the REAL participant, the REAL operation registry and the REAL
/// persisted preset, and observes work — flashes that came due, generations that terminated,
/// timers left on a clock — rather than <c>Running</c>.</para>
///
/// <para><b>Zero wall-clock.</b> The effect's pacing runs on an injected
/// <see cref="ISessionClock"/> advanced by hand. Nothing here sleeps, polls a real clock or
/// waits out an interval; the one wait that exists is on a deterministic completion signal,
/// through the approved <see cref="TestWait"/> helper.</para>
/// </summary>
public class SessionSpineTests
{
    // ---------------------------------------------------------------------------------
    //  the work really starts
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task PressingStart_ArmsTheSchedule_AndTheClockMakesRealFlashesComeDue()
    {
        await using var rig = await Rig.StartAsync(imageCount: 4);

        Assert.Equal(0, rig.Flash.FlashCount);
        Assert.False(rig.Flash.ScheduleArmed);

        Assert.True(rig.Engine.Start());

        // Arming schedules the first flash SYNCHRONOUSLY, as WPF's FlashService.Start does
        // (Services/Flash/FlashService.cs:352) — so the caller's very next look at the clock
        // already sees it. Nothing has fired yet.
        Assert.True(rig.Flash.ScheduleArmed);
        Assert.Equal(0, rig.Flash.FlashCount);

        rig.Clock.Advance(rig.LongestInterval);
        Assert.Equal(1, rig.Flash.FlashCount);

        rig.Clock.Advance(rig.LongestInterval);
        Assert.Equal(2, rig.Flash.FlashCount);

        // The draw is the dial's, and the draw really happened: WPF takes SimultaneousImages
        // independent picks per firing (FlashService.cs:2647-2652).
        Assert.Equal(SessionPresetDocument.DefaultImagesPerFlash, rig.Flash.Last!.ImagesDrawn);
        Assert.Equal(2 * SessionPresetDocument.DefaultImagesPerFlash, rig.Pool.TotalDrawn);
    }

    // ---------------------------------------------------------------------------------
    //  THE BITE: stop really stops
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task AfterStop_NoAmountOfClockMakesTheEffectWorkAgain()
    {
        await using var rig = await Rig.StartAsync(imageCount: 4);
        rig.Engine.Start();

        rig.Clock.Advance(rig.LongestInterval);
        rig.Clock.Advance(rig.LongestInterval);
        var flashesAtStop = rig.Flash.FlashCount;
        var drawsAtStop = rig.Pool.TotalDrawn;
        Assert.Equal(2, flashesAtStop);

        Assert.True(rig.Engine.Stop());

        // The one-shot is torn down BY the stop, synchronously: "the cancellation callback will
        // get there eventually" is not the guarantee a panic button owes anyone.
        Assert.False(rig.Flash.ScheduleArmed);
        Assert.Equal(0, rig.Clock.PendingCount);

        // Ten more windows of clock. If stop leaked — a surviving timer, a live generation, a
        // re-arm at the tail of a firing — every one of these advances would produce a flash.
        for (var i = 0; i < 10; i++)
        {
            rig.Clock.Advance(rig.LongestInterval);
        }

        Assert.Equal(flashesAtStop, rig.Flash.FlashCount);
        Assert.Equal(drawsAtStop, rig.Pool.TotalDrawn);
        Assert.NotEqual(EffectDotState.Live, rig.Flash.Dot);
    }

    [Fact]
    public async Task Stop_TerminatesTheOwnedOperation_Cancelled_WithNothingLeftOutstanding()
    {
        await using var rig = await Rig.StartAsync(imageCount: 2);
        rig.Engine.Start();
        rig.Clock.Advance(rig.LongestInterval);

        var completion = rig.Flash.Completion;
        Assert.NotNull(completion);

        rig.Engine.Stop();

        // The schedule is a REGISTERED operation, so "did it really stop" is a question the
        // registry answers (async-lifecycle-fault-contract §1/§2), not a boolean on the effect.
        await TestWait.Until(completion!, "the flash schedule's owned completion after stop");
        Assert.IsType<OperationOutcome.Cancelled>(await completion!);
        await TestWait.Until(
            () => rig.Registry.OutstandingOperations == 0,
            "every owned operation to leave the registry after stop",
            () => $"outstanding={rig.Registry.OutstandingOperations}",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, rig.Registry.UnobservedOperations);
    }

    [Fact]
    public async Task HostTeardown_WithASessionStillRunning_KillsTheScheduleAndLeavesNothingUnobserved()
    {
        await using var rig = await Rig.StartAsync(imageCount: 2);
        rig.Engine.Start();
        rig.Clock.Advance(rig.LongestInterval);
        var flashes = rig.Flash.FlashCount;
        Assert.Equal(1, flashes);

        // The user never pressed STOP; the app is closing. Teardown's cancel-and-drain is the
        // path that has to kill the schedule, and it is a DIFFERENT path from Stop.
        await rig.Host.ShutdownAsync();

        for (var i = 0; i < 5; i++)
        {
            rig.Clock.Advance(rig.LongestInterval);
        }

        Assert.Equal(flashes, rig.Flash.FlashCount);
        Assert.Equal(0, rig.Registry.OutstandingOperations);
        Assert.Equal(0, rig.Registry.UnobservedOperations);
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    // ---------------------------------------------------------------------------------
    //  the flag follows the work; it never leads it
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TheRunningFlagFollowsTheWork_OnBothEdges()
    {
        await using var rig = await Rig.StartAsync(imageCount: 1);
        var probe = new OrderProbeEffect();
        var engine = new SessionEngine([probe], rig.Session.Preset);
        probe.Engine = engine;

        engine.Start();
        // WPF flips _isRunning/IsEngineRunning AFTER every service has started
        // (MainWindow.StartStop.cs:268-269): at the moment an effect is armed, the session
        // does not yet claim to be running.
        Assert.False(probe.RunningWhenArmed);
        Assert.True(engine.Running);

        engine.Stop();
        // ...and it stops the work BEFORE clearing the flag (:305 "Stop flash first", :385-387):
        // at the moment an effect is disarmed, the session still claims to be running.
        Assert.True(probe.RunningWhenDisarmed);
        Assert.False(engine.Running);
    }

    [Fact]
    public async Task StartAndStopAreIdempotent_AndArmingTwiceStartsNoSecondGeneration()
    {
        await using var rig = await Rig.StartAsync(imageCount: 2);

        Assert.True(rig.Engine.Start());
        var completion = rig.Flash.Completion;
        Assert.NotNull(completion);
        Assert.False(rig.Engine.Start());              // already running: WPF never reaches StartEngine

        // And the effect's own guard, driven directly: a second arm must not begin a second
        // generation (that is the re-entrant double-toggle defect the StatusTicker precedent
        // names), and must not leave two timers racing on the clock.
        rig.Flash.Arm();
        Assert.Same(completion, rig.Flash.Completion);
        Assert.Equal(1, rig.Clock.PendingCount);

        Assert.True(rig.Engine.Stop());
        Assert.False(rig.Engine.Stop());               // stopping a stopped session is a no-op
        Assert.False(rig.Engine.Running);
        rig.Flash.Disarm();                            // disarming a disarmed effect is a no-op too
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    // ---------------------------------------------------------------------------------
    //  the dot tells the truth
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TheDotHasThreeStates_AndEachOneIsTrueWhenItIsShown()
    {
        await using var rig = await Rig.StartAsync(imageCount: 2);

        // Armed: the dial is on, but no session owns it, so nothing is scheduled.
        Assert.True(rig.Flash.Enabled);
        Assert.Equal(EffectDotState.Armed, rig.Flash.Dot);
        Assert.False(rig.Flash.ScheduleArmed);

        // Live: an owned generation and a real timer on the clock.
        rig.Engine.Start();
        Assert.Equal(EffectDotState.Live, rig.Flash.Dot);
        Assert.True(rig.Flash.ScheduleArmed);

        // Off: the module's own dial. It beats "running", because a module switched off will
        // do nothing whether or not a session is up.
        rig.Engine.QuickToggle(FlashImagesEffect.EffectId);
        Assert.Equal(EffectDotState.Off, rig.Flash.Dot);

        rig.Engine.QuickToggle(FlashImagesEffect.EffectId);
        Assert.Equal(EffectDotState.Live, rig.Flash.Dot);

        rig.Engine.Stop();
        Assert.Equal(EffectDotState.Armed, rig.Flash.Dot);
    }

    [Fact]
    public async Task AModuleWhoseDialIsOff_ArmsNothing_HoweverLongTheSessionRuns()
    {
        await using var rig = await Rig.StartAsync(imageCount: 4);
        rig.Session.Preset.Mutate(p => p.FlashEnabled = false);

        rig.Engine.Start();

        // WPF's service still reports itself running here and schedules nothing: ScheduleNextFlash
        // returns at `if (!settings.FlashEnabled)` (FlashService.cs:541-546). The port says so
        // with the dot instead of with a lie.
        Assert.True(rig.Engine.Running);
        Assert.Equal(EffectDotState.Off, rig.Flash.Dot);
        Assert.False(rig.Flash.ScheduleArmed);

        for (var i = 0; i < 10; i++)
        {
            rig.Clock.Advance(rig.LongestInterval);
        }

        Assert.Equal(0, rig.Flash.FlashCount);
        Assert.Equal(0, rig.Pool.TotalDrawn);
    }

    // ---------------------------------------------------------------------------------
    //  the rack row's second gesture
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task QuickToggle_OffMidSession_StopsTheWork_AndOnAgainRestartsIt()
    {
        await using var rig = await Rig.StartAsync(imageCount: 3);
        rig.Engine.Start();
        rig.Clock.Advance(rig.LongestInterval);
        Assert.Equal(1, rig.Flash.FlashCount);

        // WPF's body for a rack key: flip the persisted flag, then — only while the engine is
        // running — stop the live service (MainWindow/MainWindow.Presets.cs:1250).
        Assert.True(rig.Engine.QuickToggle(FlashImagesEffect.EffectId));
        Assert.False(rig.Session.Preset.Current.FlashEnabled);
        Assert.False(rig.Flash.ScheduleArmed);

        for (var i = 0; i < 5; i++)
        {
            rig.Clock.Advance(rig.LongestInterval);
        }

        Assert.Equal(1, rig.Flash.FlashCount);

        // Back on, mid-session. THE DIVERGENCE, and it is deliberate: WPF's own path is dead
        // here — its Start() returns at `if (_isRunning) return;` (FlashService.cs:347), so a
        // module that was off when the engine started can never be armed by turning it on. The
        // port does what the app's own onboarding card promises the gesture does.
        Assert.True(rig.Engine.QuickToggle(FlashImagesEffect.EffectId));
        Assert.True(rig.Session.Preset.Current.FlashEnabled);
        Assert.True(rig.Flash.ScheduleArmed);

        rig.Clock.Advance(rig.LongestInterval);
        Assert.Equal(2, rig.Flash.FlashCount);
    }

    [Fact]
    public async Task QuickToggle_WithNoSessionRunning_WritesTheDialAndStartsNothing()
    {
        await using var rig = await Rig.StartAsync(imageCount: 3);

        Assert.True(rig.Engine.QuickToggle(FlashImagesEffect.EffectId));

        Assert.False(rig.Session.Preset.Current.FlashEnabled);
        Assert.False(rig.Engine.Running);
        Assert.False(rig.Flash.ScheduleArmed);
        Assert.Equal(EffectDotState.Off, rig.Flash.Dot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("spiral")]
    [InlineData("Flash")]
    public async Task QuickToggle_ForARowWithNoEffect_IsASilentNoOp(string? id)
    {
        await using var rig = await Rig.StartAsync(imageCount: 1);
        var before = rig.Session.Preset.Current.FlashEnabled;

        // WPF's `default: return` (MainWindow.Presets.cs:1259) and its unhandled rack rows
        // (StudioTabView.xaml.cs:659). The id is case-SENSITIVE on purpose: it is a dispatch
        // identity, not display text, so "Flash" is a different row from "flash".
        Assert.False(rig.Engine.QuickToggle(id));
        Assert.Equal(before, rig.Session.Preset.Current.FlashEnabled);
    }

    [Fact]
    public async Task TheDialSurvivesARestart_ThroughTheRealPersistedPreset()
    {
        var dir = Rig.NewDirectory();
        await using (var rig = await Rig.StartAsync(imageCount: 1, directory: dir))
        {
            rig.Engine.QuickToggle(FlashImagesEffect.EffectId);   // flash off
            rig.Session.Preset.Mutate(p => p.FlashesPerHour = 77);
            await rig.Session.FlushAsync(TimeSpan.FromSeconds(5));
        }

        await using var reopened = await Rig.StartAsync(imageCount: 1, directory: dir);
        Assert.False(reopened.Session.Preset.Current.FlashEnabled);
        Assert.Equal(77, reopened.Session.Preset.Current.FlashesPerHour);
        Assert.Equal(EffectDotState.Off, reopened.Flash.Dot);
    }

    // ---------------------------------------------------------------------------------
    //  the empty pool is an outcome, not a failure
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task AFlashOverAnEmptyPool_ComesDueAndDrawsNothing_WithoutFaultingTheSession()
    {
        await using var rig = await Rig.StartAsync(imageCount: 0);
        rig.Engine.Start();

        rig.Clock.Advance(rig.LongestInterval);

        // WPF's empty pool returns an empty list and the flash shows nothing
        // (FlashService.cs:2589-2593, :585-597 — the dead end its own comment calls the most
        // common one). It is not an error, the schedule keeps running, and the surface is
        // entitled to say the folder is empty.
        Assert.Equal(1, rig.Flash.FlashCount);
        Assert.True(rig.Flash.Last!.PoolWasEmpty);
        Assert.Equal(0, rig.Flash.Last!.ImagesDrawn);
        Assert.Equal(EffectDotState.Live, rig.Flash.Dot);

        rig.Clock.Advance(rig.LongestInterval);
        Assert.Equal(2, rig.Flash.FlashCount);
    }

    [Fact]
    public async Task MovingTheFrequencyDialMidSession_RePacesThePendingFlash()
    {
        await using var rig = await Rig.StartAsync(imageCount: 2);
        rig.Session.Preset.Mutate(p => p.FlashesPerHour = 1);   // one an hour: a long wait
        rig.Engine.Start();

        var slowDue = rig.Clock.NextDue;
        Assert.NotNull(slowDue);

        rig.Session.Preset.Mutate(p => p.FlashesPerHour = SessionPresetDocument.MaxFlashesPerHour);
        rig.Flash.RefreshSchedule();

        // WPF's frequency slider calls RefreshSchedule (Features/FlashFeatureControl.xaml.cs:186
        // -> FlashService.cs:527-531) so the change takes effect NOW instead of after the old
        // interval expires. The pending flash moved forward, and there is still exactly one.
        Assert.Equal(1, rig.Clock.PendingCount);
        Assert.True(rig.Clock.NextDue < slowDue);
    }

    // ---------------------------------------------------------------------------------
    //  rig
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The REAL participant on a REAL registry and a REAL persisted preset, with only the two
    /// seams the spine declares — the clock and the image pool — replaced. Nothing here is a
    /// double of the session, the engine, the effect or the store.
    /// </summary>
    private sealed class Rig : IAsyncDisposable
    {
        private readonly bool _ownsDirectory;

        private Rig(ApplicationHost host, OperationRegistry registry, SessionParticipant session,
            ManualSessionClock clock, CountingPool pool, string directory, bool ownsDirectory)
        {
            Host = host;
            Registry = registry;
            Session = session;
            Clock = clock;
            Pool = pool;
            Directory = directory;
            _ownsDirectory = ownsDirectory;
        }

        public ApplicationHost Host { get; }

        public OperationRegistry Registry { get; }

        public SessionParticipant Session { get; }

        public ManualSessionClock Clock { get; }

        public CountingPool Pool { get; }

        public string Directory { get; }

        public SessionEngine Engine => Session.Engine;

        public FlashImagesEffect Flash => Session.Flash;

        /// <summary>An advance guaranteed to reach the next flash whatever the ±30% draw was
        /// (<c>FlashSchedule.MaximumInterval</c>) — the deterministic alternative to pinning the RNG.</summary>
        public TimeSpan LongestInterval =>
            FlashSchedule.MaximumInterval(Session.Preset.Current.FlashesPerHour) + TimeSpan.FromSeconds(1);

        public static string NewDirectory() =>
            Path.Combine(Path.GetTempPath(), "ccp-sp098-" + Guid.NewGuid().ToString("N"));

        public static async Task<Rig> StartAsync(int imageCount, string? directory = null)
        {
            var dir = directory ?? NewDirectory();
            System.IO.Directory.CreateDirectory(dir);
            var registry = new OperationRegistry();
            var log = new CollectingLog();
            var infra = new ParticipantInfrastructure(registry, new UiDispatchBoundary(), log);
            var clock = new ManualSessionClock();
            var pool = new CountingPool(imageCount);
            var session = new SessionParticipant(infra, dir, clock, pool);
            var host = new ApplicationHost(log, [session], new StartupTrace(), registry, infra.UiDispatch);

            var outcome = await host.StartParticipantsAsync(TestContext.Current.CancellationToken);
            Assert.IsType<StartupOutcome.Success>(outcome);

            return new Rig(host, registry, session, clock, pool, dir, ownsDirectory: directory is null);
        }

        public async ValueTask DisposeAsync()
        {
            await Host.ShutdownAsync();
            if (!_ownsDirectory)
            {
                // A caller that named the directory is reusing it (the restart round-trip):
                // deleting it here would delete the file the next rig is about to read.
                return;
            }

            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not a test outcome.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class CollectingLog : ILogSink
    {
        public List<string> Lines { get; } = [];

        public void Log(string message)
        {
            lock (Lines)
            {
                Lines.Add(message);
            }
        }
    }

    /// <summary>
    /// A pool with a known population and a memory of every draw. Not a filesystem: the spine's
    /// proofs must not depend on one (<see cref="FlashImagePoolTests"/> owns the real folder).
    /// </summary>
    private sealed class CountingPool(int population) : IFlashImagePool
    {
        private readonly string[] _images =
            Enumerable.Range(0, population).Select(i => $"image-{i}.png").ToArray();

        public int TotalDrawn { get; private set; }

        public int DrawCalls { get; private set; }

        public IReadOnlyList<string> Draw(int count)
        {
            DrawCalls++;
            if (_images.Length == 0)
            {
                return [];
            }

            var drawn = Enumerable.Range(0, count).Select(i => _images[i % _images.Length]).ToArray();
            TotalDrawn += drawn.Length;
            return drawn;
        }
    }

    /// <summary>
    /// An effect that records what the SESSION claimed at the exact moment it was armed and
    /// disarmed. It is the only way to assert the ordering WPF's start/stop bodies encode
    /// without reaching into private state.
    /// </summary>
    private sealed class OrderProbeEffect : ISessionEffect
    {
        public SessionEngine? Engine { get; set; }

        public string Id => "order-probe";

        public string Title => "Order probe";

        public bool Enabled => true;

        public EffectDotState Dot => EffectDotState.Off;

        public Task<OperationOutcome>? Completion => null;

        public bool RunningWhenArmed { get; private set; }

        public bool RunningWhenDisarmed { get; private set; }

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public void SetEnabled(bool enabled)
        {
        }

        public CapabilityState Arm()
        {
            RunningWhenArmed = Engine?.Running ?? false;
            return new CapabilityState.Available("the order probe took the session");
        }

        public void Disarm() => RunningWhenDisarmed = Engine?.Running ?? false;
    }

    /// <summary>
    /// The manual clock (the <c>SoundArbitrationTests.ManualClock</c> shape, SP-043): due
    /// timers fire in due order inside <see cref="Advance"/>, and a timer scheduled by a
    /// callback fires in the same pass if it is already due. Zero wall-clock.
    /// </summary>
    private sealed class ManualSessionClock : ISessionClock
    {
        private sealed class Entry
        {
            public DateTimeOffset Due;
            public required Action Fire;
            public bool Cancelled;
        }

        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

        /// <summary>Live (uncancelled) timers. After a stop this must be zero.</summary>
        public int PendingCount
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Count(t => !t.Cancelled);
                }
            }
        }

        /// <summary>When the soonest live timer is due, or null when none is.</summary>
        public DateTimeOffset? NextDue
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Where(t => !t.Cancelled).Select(t => (DateTimeOffset?)t.Due).Min();
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
                    next = _timers.Where(t => !t.Cancelled && t.Due <= UtcNow).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        return;
                    }

                    _timers.Remove(next);
                }

                next.Fire();
            }
        }

        private sealed class CancelHandle(ManualSessionClock clock, Entry entry) : IDisposable
        {
            public void Dispose()
            {
                lock (clock._timers)
                {
                    entry.Cancelled = true;
                    clock._timers.Remove(entry);
                }
            }
        }
    }
}
