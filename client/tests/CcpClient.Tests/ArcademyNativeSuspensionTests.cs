using System.Text.Json;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Features.Arcademy;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Video;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Slice 5 of the Arcademy row: NATIVE-STATE SUSPENSION — the class yields while a native video
/// covers the screen (<c>ArcademyHostService.SeedNativeState</c> <c>:409-440</c>,
/// <c>OnVideoStarted</c> <c>:1714</c>, <c>OnVideoEnded</c> <c>:1716-1730</c>,
/// <c>HookVideoEvents</c> <c>:1648-1671</c>).
///
/// <para><b>The direction is upstream's and it is worth naming, because it is the opposite of the
/// obvious reading.</b> The Arcademy does not stop the app's media; the CLASS is what stops. A
/// mandatory video "fully covers the class" (<c>:1644-1647</c>), so the host tells the page to
/// drop every effect and pause, and lifts it when the video is over.</para>
///
/// <para><b>Half of these facts drive the REAL video module.</b>
/// <see cref="ArcademyNativeSuspension"/> exists to be wrong in one specific way — hooking a
/// signal that never fires, which is upstream's own defect at <c>:1650-1656</c> — and a
/// hand-written producer double could not catch it. So the wire is exercised against a real
/// <see cref="MandatoryVideoEffect"/> over a recording surface: the clip really starts, the module
/// really raises what it raises, and the frames are whatever came of that.</para>
///
/// <para><b>What these facts are NOT.</b> No page receives them here: this assembly has no browser
/// and this build has no Arcademy window. They pin WHICH FRAME goes out, in what order, and
/// against which native state — never that a class visibly froze, that a picture was on a screen,
/// or that any pixel changed. The page-side half — the real payload's shell running its own
/// freeze path on these frames — is <see cref="ArcademyBootHandshakeTests"/>'s, and the surface
/// here is a double that draws nothing.</para>
/// </summary>
public sealed class ArcademyNativeSuspensionTests : IDisposable
{
    private readonly List<string> _log = [];
    private readonly List<object> _posted = [];
    private readonly string _dir;
    private readonly OperationRegistry _registry = new();
    private readonly PersistenceStore<ArcademySettingsDocument> _store;

    public ArcademyNativeSuspensionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-arcademy-suspend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new PersistenceStore<ArcademySettingsDocument>(
            _registry.OwnerFor(nameof(ArcademyNativeSuspensionTests)),
            new SinkAdapter(_log),
            Path.Combine(_dir, ArcademySettingsDocument.FileName),
            ArcademySettingsDocument.CurrentSchemaVersion);
        _ = _store.StartAsync(TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        try
        {
            _ = _store.StopAsync();
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception)
        {
            // best-effort teardown
        }
    }

    // ==================================================================================
    // The boot seed: what a page that opened OVER a running video is told.
    // ==================================================================================

    /// <summary>
    /// A page that boots while a video already covers the screen is told so, at the TAIL of the
    /// handshake (<c>:396-402</c>). Upstream's stated failure is precise: <c>init</c> is a snapshot
    /// of settings, every other producer is edge-driven, so without this seed the page "never heard
    /// about it and dealt a board over the video" (<c>:410-413</c>).
    /// </summary>
    [Fact]
    public void TheBootSeed_TellsAPageThatOpenedOverAVideo_AfterInitAndFullscreen()
    {
        var session = NewSession();
        session.NativeStateOwnsScreen = () => true;

        session.Ready();

        // The ORDER is the fact. A suspend ahead of init reaches no handler at all — boot.js
        // registers its own only as it imports (arcademy/boot.js:195) — so a seed that overtook
        // the handshake would be silently dropped by the page rather than early.
        Assert.Equal(["init", "fullscreen", "suspend"], Types());
        var seed = Frame(2);
        Assert.True(seed.GetProperty("on").GetBoolean());
        Assert.Equal(ArcademySession.VideoSuspendReason, seed.GetProperty("reason").GetString());
        Assert.Contains(_log, l => l.Contains("seeding suspend", StringComparison.Ordinal));
    }

    /// <summary>The other half, and the one a seed that fires unconditionally would break: with
    /// nothing owning the screen the handshake is exactly what it was before slice 5 — a page that
    /// opens on a quiet desktop is not frozen on arrival.</summary>
    [Fact]
    public void TheBootSeed_SaysNothing_WhenNoNativeStateOwnsTheScreen()
    {
        var session = NewSession();

        session.Ready();

        Assert.Equal(["init", "fullscreen"], Types());
    }

    // ==================================================================================
    // The edges, and the order the restore has to keep.
    // ==================================================================================

    /// <summary>The whole round trip in the vocabulary the page reads: freeze on the video
    /// (<c>:1714</c>), lift when it ends (<c>:1726</c>), both carrying <c>"video"</c> — the reason
    /// that tells the page's own overlay this suspend has a natural end and needs no Resume button
    /// (<c>arcademy/shell/shell.js:1224-1231</c>).</summary>
    [Fact]
    public void AVideoStarting_FreezesTheClass_AndItsEndLiftsTheFreeze()
    {
        var session = BootedSession();

        session.NativeVideoChanged(playing: true);
        session.NativeVideoChanged(playing: false);

        Assert.Equal(["suspend", "suspend"], Types());
        Assert.True(Frame(0).GetProperty("on").GetBoolean());
        Assert.Equal("video", Frame(0).GetProperty("reason").GetString());
        Assert.False(Frame(1).GetProperty("on").GetBoolean());
        Assert.Equal("video", Frame(1).GetProperty("reason").GetString());
    }

    /// <summary>
    /// <b>THE RESTORE ORDER, and it is asymmetric on purpose</b> (<c>:1720-1723</c>). The freeze is
    /// unconditional — a video covers the class whatever else is true — but the LIFT is refused
    /// while a panic press stands, because "a video ending is not them asking to be put back in a
    /// class. It lifts on their resume-request and nowhere else". Restoring in the other order
    /// un-freezes a class the user hit the emergency stop on.
    /// </summary>
    [Fact]
    public void APanicPress_OutranksTheVideosOwnLift()
    {
        var session = BootedSession();

        session.NativeVideoChanged(playing: true);
        session.PanicPress();
        _posted.Clear();

        session.NativeVideoChanged(playing: false);

        Assert.Empty(_posted);
        Assert.Contains(_log, l => l.Contains("panic suspend still stands", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same rule from the page's side, through the wire's own predicate: the page may ASK to
    /// come back from a panic freeze, and the host holds that request while a video still owns the
    /// screen (<c>:359-364</c>) — "un-freezing here would drop a class back on top of a video the
    /// user is supposed to be watching". When the clip really goes away, the same request is
    /// granted.
    /// </summary>
    [Fact]
    public void AResumeRequest_IsHeldWhileTheVideoStillCoversTheScreen()
    {
        using var rig = new VideoRig();
        var session = BootedSession();
        using var wire = new ArcademyNativeSuspension(rig.Effect, session);
        rig.PlayOneClip();
        session.PanicPress();
        _posted.Clear();

        session.Handle("""{"type":"resume-request","reason":"panic"}""");

        Assert.Empty(_posted);
        Assert.Contains(_log, l => l.Contains("resume-request held", StringComparison.Ordinal));

        // The clip ends: the predicate is live, so the very same request now lands.
        rig.EndTheClip();
        _posted.Clear();
        session.Handle("""{"type":"resume-request","reason":"panic"}""");

        Assert.Single(_posted);
        Assert.False(Frame(0).GetProperty("on").GetBoolean());
        Assert.Equal("panic", Frame(0).GetProperty("reason").GetString());
    }

    // ==================================================================================
    // The wire, against the REAL video module.
    // ==================================================================================

    /// <summary>
    /// The real module, the real signals, and the level test that turns them into edges. A clip
    /// that really starts freezes the class once; a <c>Changed</c> that moved no picture — a dial
    /// write, a re-arm — crosses nothing; and the clip ending lifts it.
    /// </summary>
    [Fact]
    public void TheWire_CrossesTheBridgeOnlyWhenTheRealModulesPictureAppearsOrGoes()
    {
        using var rig = new VideoRig();
        var session = BootedSession();
        using var wire = new ArcademyNativeSuspension(rig.Effect, session);

        rig.PlayOneClip();
        Assert.True(rig.Effect.Playing);
        Assert.True(wire.Covering);
        Assert.Equal(["suspend"], Types());
        Assert.True(Frame(0).GetProperty("on").GetBoolean());

        // A dial write with the clip still up: the module raises Changed, the picture did not move.
        rig.Effect.SetMaxSeconds(30);
        Assert.Equal(["suspend"], Types());

        rig.EndTheClip();
        Assert.False(wire.Covering);
        Assert.Equal(["suspend", "suspend"], Types());
        Assert.False(Frame(1).GetProperty("on").GetBoolean());
    }

    /// <summary>
    /// <b>A clip that never appeared must not freeze a class.</b> The module raises <c>Fired</c> on
    /// BOTH arms of its delivery, including the one where the video capability refused the
    /// placement (<c>Effects/MandatoryVideoEffect.cs:287-310</c>) — and a wire that treated the
    /// firing as the event would suspend a class over a video nobody can see.
    /// </summary>
    [Fact]
    public void TheWire_FreezesNothing_WhenTheSurfaceRefusedTheClip()
    {
        using var rig = new VideoRig();
        var session = BootedSession();
        using var wire = new ArcademyNativeSuspension(rig.Effect, session);
        rig.Surface.BeginResult = new CapabilityState.Unavailable(
            new CapabilityReason("test.no-surface", "the double refused the placement"));

        rig.PlayOneClip();

        Assert.Equal(1, rig.Fires);          // the module really did deliver a firing
        Assert.False(rig.Effect.Playing);    // and nothing is on screen
        Assert.False(wire.Covering);
        Assert.Empty(_posted);
    }

    /// <summary>
    /// Teardown, which upstream does at the TOP of <c>DisposeAll</c> (<c>:2014</c>) ahead of the
    /// meta flush and the host's own disposal: after the wire is gone, a video ending posts
    /// nothing, and the session's live predicate is back to the honest default rather than left
    /// pointing at a module nobody is watching any more — a predicate stuck true would hold every
    /// future panic resume with nothing alive to lift it.
    ///
    /// <para>And the state upstream's own defect left behind (<c>:1650-1656</c>: a teardown that
    /// returned early "left the flag true and the NEXT launch's HookVideoEvents(true) refused to
    /// subscribe — the Arcademy then never suspended for a mandatory video for the rest of the app
    /// session"): a FRESH wire over the same module suspends again.</para>
    /// </summary>
    [Fact]
    public void DisposingTheWire_UnhooksAndRestoresThePredicate_AndAFreshOneWorksAgain()
    {
        using var rig = new VideoRig();
        var session = BootedSession();
        var wire = new ArcademyNativeSuspension(rig.Effect, session);
        rig.PlayOneClip();
        _posted.Clear();

        wire.Dispose();
        rig.EndTheClip();

        Assert.Empty(_posted);
        Assert.False(session.NativeStateOwnsScreen());

        using var second = new ArcademyNativeSuspension(rig.Effect, session);
        rig.PlayOneClip();

        Assert.Equal(["suspend"], Types());
        Assert.True(Frame(0).GetProperty("on").GetBoolean());
        Assert.True(session.NativeStateOwnsScreen());
    }

    // ==================================================================================
    // fixtures
    // ==================================================================================

    private ArcademySession NewSession() =>
        new(_store, new ArcademyAppFacts(), _posted.Add, new SinkAdapter(_log));

    /// <summary>A session that has completed its handshake, with the handshake frames cleared so a
    /// fact reads only what suspension produced.</summary>
    private ArcademySession BootedSession()
    {
        var session = NewSession();
        session.Ready();
        _posted.Clear();
        return session;
    }

    private IReadOnlyList<string> Types() =>
        [.. _posted.Select(f => Parse(f).GetProperty("type").GetString() ?? "")];

    private JsonElement Frame(int index) => Parse(_posted[index]);

    private static JsonElement Parse(object frame) =>
        JsonDocument.Parse(ArcademyProtocol.SerializeForPage(frame)).RootElement.Clone();

    /// <summary>
    /// A REAL <see cref="MandatoryVideoEffect"/> over a recording surface and a manual clock. The
    /// module is the product's own — the point of these facts is that the wire hooks signals this
    /// module really raises — and only the surface, the pool and the clock are doubles.
    /// </summary>
    private sealed class VideoRig : IDisposable
    {
        private readonly OperationRegistry _registry = new();
        private readonly string _directory;
        private bool _armed;

        public VideoRig()
        {
            _directory = Path.Combine(Path.GetTempPath(), "ccp-arcademy-video-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            Clock = new ManualClock();
            Surface = new RecordingVideoSurface();
            Pool = new StubVideoPool(Path.Combine(_directory, "videos"));
            Pool.Clips.Add(Path.Combine(_directory, "videos", "clip.mp4"));
            Preset = new PersistenceStore<MandatoryVideoPresetDocument>(
                _registry.OwnerFor("VideoPreset"), new NullLog(),
                Path.Combine(_directory, MandatoryVideoPresetDocument.FileName),
                MandatoryVideoPresetDocument.CurrentSchemaVersion);
            Preset.Mutate(p =>
            {
                p.Enabled = true;
                p.PerHour = MandatoryVideoSchedule.DefaultPerHour;
            });

            Effect = new MandatoryVideoEffect(
                _registry.OwnerFor("MandatoryVideo"),
                new EffectSignal(boundary, static () => true),
                Clock,
                Preset,
                Pool,
                Surface,
                new Random(11));
            Effect.Fired += _ => Fires++;
        }

        public ManualClock Clock { get; }

        public RecordingVideoSurface Surface { get; }

        public StubVideoPool Pool { get; }

        public PersistenceStore<MandatoryVideoPresetDocument> Preset { get; }

        public MandatoryVideoEffect Effect { get; }

        /// <summary>How many firings the module really delivered.</summary>
        public int Fires { get; private set; }

        /// <summary>Arm if needed and run the schedule forward to the next firing, which is where
        /// the module starts a clip.</summary>
        public void PlayOneClip()
        {
            if (!_armed)
            {
                _armed = true;
                Effect.Arm();
            }

            Clock.AdvanceToNextDue();
        }

        /// <summary>What the presenter does when the clip finishes: take the picture down, then
        /// tell the module (<c>Effects/VideoSurfacePresenter.cs</c>).</summary>
        public void EndTheClip() => Surface.RaiseEnded();

        public void Dispose()
        {
            Effect.Disarm();
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

    /// <summary>A surface that records and never touches a window or a decoder. It MIRRORS the
    /// product where the state transitions matter: <c>Begin</c> marks the surface showing only when
    /// it succeeded, exactly as <c>Effects/VideoSurfacePresenter.cs</c> does.</summary>
    private sealed class RecordingVideoSurface : IVideoSurface
    {
        private Action? _onEnded;

        public CapabilityState BeginResult { get; set; } = new CapabilityState.Available("the double allowed it");

        public bool Showing { get; private set; }

        public bool Running => Showing;

        public bool Engaged => Showing;

        public bool CanReachADisplay => true;

        public int FramesDecoded => 0;

        public int FramesHeld => 0;

        public int FramesAdvanced => 0;

        public string? PlayingClip { get; private set; }

        public CapabilityState? LastPlacement { get; private set; }

        public VideoSurfaceObservation LastObservation => VideoSurfaceObservation.NotAsked;

        public CapabilityState Begin(
            string clipPath, TimeSpan maxLength, Action onEnded, IVideoFramePainter? painter = null)
        {
            _onEnded = onEnded;
            LastPlacement = BeginResult;
            if (BeginResult is CapabilityState.Available)
            {
                Showing = true;
                PlayingClip = clipPath;
            }

            return BeginResult;
        }

        public void End()
        {
            Showing = false;
            PlayingClip = null;
        }

        public void RaiseEnded()
        {
            var ended = _onEnded;
            End();
            ended?.Invoke();
        }
    }

    private sealed class StubVideoPool(string folder) : IVideoClipPool
    {
        public List<string> Clips { get; } = [];

        public int ActiveCount => Clips.Count;

        public string Folder => folder;

        public string? Draw() => Clips.Count == 0 ? null : Clips[0];
    }

    /// <summary>Time only moves when a fact moves it.</summary>
    private sealed class ManualClock : ISessionClock
    {
        private sealed class Entry
        {
            public DateTimeOffset Due;

            public required Action Fire;

            public bool Cancelled;
        }

        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

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

            return new Handle(entry);
        }

        public void AdvanceToNextDue()
        {
            if (NextDue is { } due)
            {
                Advance(due - UtcNow);
            }
        }

        public void Advance(TimeSpan by)
        {
            var target = UtcNow + by;
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => !t.Cancelled && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is not null)
                    {
                        _timers.Remove(next);
                    }
                }

                if (next is null)
                {
                    UtcNow = target;
                    return;
                }

                UtcNow = next.Due;
                next.Fire();
            }
        }

        private sealed class Handle(Entry entry) : IDisposable
        {
            public void Dispose() => entry.Cancelled = true;
        }
    }

    private sealed class InlineDispatch : IUiDispatch
    {
        public void Post(Action action) => action();
    }

    private sealed class NullLog : ILogSink
    {
        public void Log(string message)
        {
        }
    }

    private sealed class SinkAdapter(List<string> lines) : ILogSink
    {
        public void Log(string message)
        {
            lock (lines)
            {
                lines.Add(message);
            }
        }
    }
}
