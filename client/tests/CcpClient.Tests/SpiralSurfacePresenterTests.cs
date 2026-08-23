using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// How a MOVING effect reaches a surface, and how it keeps changing once it is there.
///
/// <para>The FOURTH consumer of the overlay capability and of <see cref="OverlaySurfaceSet"/>, and
/// the first one that has to keep working after the placement succeeded. Flash Images places up to
/// ten rectangles for six seconds; Subliminals places one card for a fifth of a second; Pink Filter
/// places one full-screen tint and leaves it. This places one full-screen layer, leaves it, and then
/// <b>repaints it twenty times a second for the length of the session</b>.</para>
///
/// <para>The two facts a reviewer should read first are
/// <see cref="TheLayerIsPresentedONCE_AndEveryFrameAfterThatIsAPaint"/> — because re-presenting per
/// frame is what this port's overlay explicitly must not do — and the four
/// <c>Running_…</c> facts, which are the dot's third meaning made mechanical.</para>
///
/// <para>Everything runs on an injected clock: not one wall-clock wait anywhere in this file.</para>
/// </summary>
public class SpiralSurfacePresenterTests
{
    private static readonly OverlayBounds Display = new(0, 0, 1920, 1080);

    private const string SpiralPath = @"C:\spirals\classic.gif";

    private static readonly SpiralPresentation Dial = new(10);

    // ---------------------------------------------------------------------------------
    //  one full-screen layer, at the module's own (reduced) opacity, click-through
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheSpiralCoversTheWholeDisplay_AtWpfsOwnReducedOpacity_AndPassesClicksThrough()
    {
        var rig = new Rig();

        var outcome = rig.Presenter.Engage(SpiralPath, Dial);

        Assert.IsType<CapabilityState.Available>(outcome);
        var presence = Assert.Single(rig.Presences);
        var request = Assert.Single(presence.Requests);

        // WPF sizes the spiral window to the screen's own bounds (OverlayService.cs:1709-1734) and
        // the IMAGE inside it carries `(opacity / 100.0) * 0.1` — "Very subtle opacity - 90%
        // reduction" (:1689-1690). A dial of 10 is therefore a layer at 1 %, not 10 %.
        Assert.Equal(Display, request.Bounds);
        Assert.Equal(0.01, request.Opacity, precision: 6);

        // WS_EX_TRANSPARENT | WS_EX_NOACTIVATE, IsHitTestVisible=false (OverlayService.cs:1734,
        // :1718). A full-screen layer up for a whole session that caught clicks would end the
        // user's ability to use their computer.
        Assert.True(request.ClickThrough);
        Assert.True(rig.Presenter.Showing);
        Assert.Equal(SpiralPath, rig.Presenter.CurrentPath);
    }

    // ---------------------------------------------------------------------------------
    //  THE FRAME PATH — the thing a moving module needed and the other three did not
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheLayerIsPresentedONCE_AndEveryFrameAfterThatIsAPaint()
    {
        var rig = new Rig(frames: 4);

        rig.Presenter.Engage(SpiralPath, Dial);
        for (var i = 0; i < 6; i++)
        {
            rig.Clock.Advance(SpiralFrameDelay.Default);
        }

        // ONE present, SEVEN paints. This is the whole reason OverlaySurfaceSet.Repaint exists:
        // IOverlayPresence.Present walks the OS's top-level z-order and asks the window manager's
        // hit test in BOTH polarities — with click-through momentarily cleared
        // (Overlay/Win32OverlayPresence.cs:547-576) — which the interface itself calls "right once
        // per placement and wrong per frame" (Overlay/IOverlayPresence.cs:80-85). Twenty times a
        // second it would be a full-screen window catching the user's clicks twenty times a second.
        var presence = Assert.Single(rig.Presences);
        Assert.Equal(1, presence.Calls.Count(c => c == "present"));
        Assert.Equal(7, presence.Calls.Count(c => c == "paint"));
        Assert.Equal("present", presence.Calls[0]);
        Assert.DoesNotContain("set-click-through", presence.Calls);
    }

    [Fact]
    public void TheFramesAdvanceOnTheGifsOwnDelay_AndLoopForever()
    {
        var rig = new Rig(frames: 3, frameDelay: TimeSpan.FromMilliseconds(80));

        rig.Presenter.Engage(SpiralPath, Dial);
        Assert.Equal(0, rig.Presenter.FrameIndex);

        // Nothing moves until the clip's own delay has passed — not the flash interval, not a
        // round number, the delay the FILE asked for.
        rig.Clock.Advance(TimeSpan.FromMilliseconds(79));
        Assert.Equal(0, rig.Presenter.FrameIndex);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, rig.Presenter.FrameIndex);

        // `(index + 1) % count` — WPF's own arithmetic (OverlayService.cs:1641), which LOOPS and
        // never stops on the last frame.
        rig.Clock.Advance(TimeSpan.FromMilliseconds(80));
        Assert.Equal(2, rig.Presenter.FrameIndex);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(80));
        Assert.Equal(0, rig.Presenter.FrameIndex);

        Assert.Equal([0, 1, 2, 0], rig.Animations.Single().Rendered);
    }

    [Fact]
    public void AStillSpiralGetsNoFrameTimerAtAll_BecauseWpfStartsNoneForOne()
    {
        var rig = new Rig(frames: 1);

        rig.Presenter.Engage(SpiralPath, Dial);

        // ONE timer on the clock, and it is the topmost cadence — not a frame advance. WPF's own
        // condition is `if (_spiralGifFrames.Count > 1 && …)` (OverlayService.cs:1370): a one-frame
        // spiral is a picture and a timer for it would be a tick that changes nothing, forever.
        Assert.Equal(1, rig.Clock.PendingCount);

        rig.Clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal([0], rig.Animations.Single().Rendered);
        Assert.True(rig.Presenter.Showing);
    }

    [Fact]
    public void AMovingSpiralArmsExactlyTwoTimers_TheBandAndTheFrame_AndNeitherIsALifetime()
    {
        var rig = new Rig(frames: 5);

        rig.Presenter.Engage(SpiralPath, Dial);

        // Two, and only two. A THIRD would be a per-surface lifetime, which a layer that is up
        // until the session ends must not have (the static module's nullable lifetime); a lifetime of "four
        // hours" is a timer that exists, that a stop has to cancel, and that fires in a session
        // nobody meant it to reach.
        Assert.Equal(2, rig.Clock.PendingCount);

        rig.Clock.Advance(TimeSpan.FromHours(4));

        Assert.True(rig.Presenter.Showing);
        Assert.Equal(0, Assert.Single(rig.Presences).WithdrawCalls);
    }

    [Fact]
    public void TheTopmostBandIsStillReAssertedOnWpfsOwnCadence_AlongsideTheFrames()
    {
        var rig = new Rig(frames: 2, frameDelay: TimeSpan.FromMilliseconds(100));

        rig.Presenter.Engage(SpiralPath, Dial);
        var presence = Assert.Single(rig.Presences);

        // TWO self-re-arming cadences on ONE clock, and they must not become one. Each Advance
        // fires each due cadence once (this clock moves time first and then fires, so a callback
        // that re-arms lands in the future), so four advances of the band's period is four kicks
        // and four frames.
        for (var i = 0; i < 4; i++)
        {
            rig.Clock.Advance(SpiralSurfacePresenter.TopmostCadence);
        }

        Assert.Equal(4, presence.ReassertCalls);
        Assert.Equal(5, presence.Calls.Count(c => c == "paint"));

        // WPF's periodic unconditional kick is ten ticks of its 500 ms reconcile loop
        // (OverlayService.cs:666-671), and it is the SAME constant the tint uses — a layer that is
        // up for a whole session loses the band whether or not its picture is moving.
        Assert.Equal(TimeSpan.FromSeconds(5), SpiralSurfacePresenter.TopmostCadence);
    }

    // ---------------------------------------------------------------------------------
    //  RUNNING — the dot's third meaning, in four facts
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Running_IsTrue_WhileTheLayerIsUpAndTheFramesAreAdvancing()
    {
        var rig = new Rig(frames: 4);

        rig.Presenter.Engage(SpiralPath, Dial);

        Assert.True(rig.Presenter.Showing);
        Assert.True(rig.Presenter.Running);

        rig.Clock.Advance(SpiralFrameDelay.Default);
        Assert.True(rig.Presenter.Running);
    }

    [Fact]
    public void Running_IsTrueForAStillSpiralThatNeverMoves_BecauseNothingMoreWasEverPromised()
    {
        var rig = new Rig(frames: 1);

        rig.Presenter.Engage(SpiralPath, Dial);
        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        // The OTHER frozen state, and the reason this property is not simply "is a frame due".
        // A single-frame file is upstream's own supported case; demanding motion from it would make
        // the dot lie in the opposite direction.
        Assert.True(rig.Presenter.Running);
        Assert.Equal(1, rig.Presenter.FrameCount);
    }

    [Fact]
    public void Running_GoesFalseWhileTheDecoderCannotProduceTheNextFrame_ThoughTheLayerIsStillUp()
    {
        var rig = new Rig(frames: 4);
        rig.Presenter.Engage(SpiralPath, Dial);
        Assert.True(rig.Presenter.Running);

        rig.Animations.Single().FailFrom = 1;
        rig.Clock.Advance(SpiralFrameDelay.Default);

        // THE STATE THIS MODULE ADDED. The window is on screen, the OS is perfectly happy, and the
        // picture has stopped. Neither a paced module's Live (a claim about the CLOCK) nor a static
        // continuous module's (a claim about the SCREEN) can see this.
        Assert.True(rig.Presenter.Showing);
        Assert.False(rig.Presenter.Running);

        // And it comes back by itself when a frame lands again — WPF's tick catches and keeps the
        // timer running (OverlayService.cs:1644-1648), so a transient decode failure recovers.
        rig.Animations.Single().FailFrom = null;
        rig.Clock.Advance(SpiralFrameDelay.Default);
        Assert.True(rig.Presenter.Running);
    }

    [Fact]
    public void Running_ThirdClauseIsAHeldHandle_TheSameGradeOfEvidenceAsAPacedModulesScheduleArmed()
    {
        var rig = new Rig(frames: 4);
        rig.Presenter.Engage(SpiralPath, Dial);
        Assert.True(rig.Presenter.Running);

        // THE BOUND ON THE CLAIM, ASSERTED RATHER THAN GLOSSED. Every timer is dropped AT THE
        // CLOCK, without telling the presenter — and Running does not notice, because its third
        // clause is "I still hold an advance handle", not "a callback will really arrive".
        //
        // That is exactly the grade of evidence PacedSessionEffect.ScheduleArmed has carried since
        // the session spine (it, too, is a held one-shot), and ISessionClock exposes nothing to ask. What
        // COVERS the real failure is the behavioural fact above,
        // Running_GoesFalseWhileTheDecoderCannotProduceTheNextFrame_…, which sees the picture stop
        // rather than the timer vanish. Written down here so nobody reads Running as stronger than
        // it is.
        rig.Clock.CancelAll();

        Assert.True(rig.Presenter.Showing);
        Assert.True(rig.Presenter.Running);
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    // ---------------------------------------------------------------------------------
    //  re-engaging, and what it must NOT do
    // ---------------------------------------------------------------------------------

    [Fact]
    public void MovingTheOpacityDial_ReusesTheWindowAndTheOpenClip_RatherThanRedecodingBoth()
    {
        var rig = new Rig(frames: 6);

        rig.Presenter.Engage(SpiralPath, Dial);
        rig.Clock.Advance(SpiralFrameDelay.Default);
        rig.Presenter.Engage(SpiralPath, new SpiralPresentation(50));

        // ONE window (WPF's UpdateSpiralOpacity changes the live window rather than building a
        // second, OverlayService.cs:446) and ONE decode: re-opening the file on every slider move
        // would re-decode a whole clip per pixel of travel.
        var presence = Assert.Single(rig.Presences);
        Assert.Equal(0.01, presence.Requests[0].Opacity, precision: 6);
        Assert.Equal(0.05, presence.Requests[1].Opacity, precision: 6);
        Assert.Single(rig.Animations);
        Assert.Equal(0, rig.Animations.Single().Disposals);

        // And the clip does not restart: a spiral that jumped back to frame 0 every time a dial
        // moved would stutter under the user's hand.
        Assert.Equal(1, rig.Presenter.FrameIndex);
    }

    [Fact]
    public void ChangingTheSpiralFile_ClosesTheOldClipAndStartsTheNewOneAtItsFirstFrame()
    {
        var rig = new Rig(frames: 6);

        rig.Presenter.Engage(SpiralPath, Dial);
        rig.Clock.Advance(SpiralFrameDelay.Default);
        Assert.Equal(1, rig.Presenter.FrameIndex);

        rig.Presenter.Engage(@"C:\spirals\other.gif", Dial);

        Assert.Equal(2, rig.Animations.Count);
        Assert.Equal(1, rig.Animations[0].Disposals);
        Assert.Equal(0, rig.Presenter.FrameIndex);
        Assert.Equal(@"C:\spirals\other.gif", rig.Presenter.CurrentPath);
    }

    // ---------------------------------------------------------------------------------
    //  refusals, kept verbatim
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AFileThisBuildCannotDecode_IsATypedRefusalAndNothingIsPlaced()
    {
        var rig = new Rig(frames: 0);

        var outcome = rig.Presenter.Engage(SpiralPath, Dial);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(EffectReasonCodes.SpiralNotDecoded, refusal.Reason.Code);
        Assert.Empty(rig.Presences);
        Assert.False(rig.Presenter.Showing);
        Assert.False(rig.Presenter.Running);

        // The DETAIL names the file and no more. A path is media the panel must not print.
        Assert.Contains("classic.gif", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\spirals", refusal.Reason.Detail, StringComparison.Ordinal);

        // NEITHER separator, and no drive letter — the leak this rejects is not a Windows one.
        // The first Linux run of the port failed exactly here: Path.GetFileName splits on '\'
        // only where the OS calls it a separator, so on Linux the whole absolute path went into
        // a sentence a user reads, drive letter and all, on a machine with no C: drive. The
        // presenter builds the display name with PortablePath.FileName now
        // (Effects/SpiralSurfacePresenter.cs), which gives the same answer on both platforms.
        Assert.DoesNotContain("\\", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("/", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("C:", refusal.Reason.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ABackendThatRefusesToPresent_IsReportedVerbatimAndNoFrameTimerIsLeftBehind()
    {
        var rig = new Rig(frames: 4) { PresentRefusal = OverlayReasonCodes.OverlayMechanismAbsent };

        var outcome = rig.Presenter.Engage(SpiralPath, Dial);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(OverlayReasonCodes.OverlayMechanismAbsent, refusal.Reason.Code);
        Assert.False(rig.Presenter.Showing);
        Assert.False(rig.Presenter.Running);

        // NOTHING on the clock. A module that armed a frame cadence over a surface the OS refused
        // would repaint a window that does not exist for the whole session — which is the Linux
        // path, where the backend refuses by design.
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    [Fact]
    public void APaintThatDoesNotHold_TakesTheLayerDownRatherThanLeavingAFrozenRectangle()
    {
        var rig = new Rig(frames: 4);
        rig.Presenter.Engage(SpiralPath, Dial);
        var presence = Assert.Single(rig.Presences);

        presence.PaintRefusal = OverlayReasonCodes.OverlayContentNotHeld;
        rig.Clock.Advance(SpiralFrameDelay.Default);

        // OverlaySurfaceSet.Repaint keeps Place's rule: a surface the OS confirms is on screen and
        // does NOT hold the frame is worse than no surface, because it is a rectangle of stale
        // pixels over the user's work.
        Assert.False(rig.Presenter.Showing);
        Assert.False(rig.Presenter.Running);
        Assert.Equal(1, presence.WithdrawCalls);

        // The frame cadence is gone at once — OnAdvance re-arms only while something is up. The one
        // timer still pending is the BAND's, which OverlaySurfaceSet re-arms only while a slot is
        // live, so it fires once, finds nothing, and stops. Asserted as one-then-zero rather than
        // as zero, because "no timer survives a failed frame" would be a claim this code does not
        // make and a reader would be entitled to believe it.
        Assert.Equal(1, rig.Clock.PendingCount);
        rig.Clock.Advance(SpiralSurfacePresenter.TopmostCadence);
        Assert.Equal(0, rig.Clock.PendingCount);
        Assert.Equal(0, presence.ReassertCalls);
        Assert.Equal(2, presence.Calls.Count(c => c == "paint"));
    }

    [Fact]
    public void AZeroOpacityDialIsRefusedRatherThanThrown_BecauseAnInvisibleSurfaceIsNotConstructible()
    {
        var rig = new Rig(frames: 4);

        var outcome = rig.Presenter.Engage(SpiralPath, new SpiralPresentation(0));

        // OverlaySurfaceRequest throws on opacity <= 0 by design. That exception must never escape
        // an arm, so the presenter answers in type — the module answers first, and this is the
        // second lock on the same door.
        var refusal = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(EffectReasonCodes.SpiralTransparent, refusal.Reason.Code);
        Assert.Empty(rig.Presences);
    }

    [Fact]
    public void NoDisplay_IsRecordedRatherThanGuessedAround()
    {
        var rig = new Rig(frames: 4) { Display = () => null };

        var outcome = rig.Presenter.Engage(SpiralPath, Dial);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(OverlayReasonCodes.OverlayNoDisplay, refusal.Reason.Code);
        Assert.False(rig.Presenter.Showing);
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    // ---------------------------------------------------------------------------------
    //  withdraw, which for this module is TWO things at once
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Withdraw_TakesTheLayerDownAndKillsTheFrameCadence_AndClosesTheClip()
    {
        var rig = new Rig(frames: 4);
        rig.Presenter.Engage(SpiralPath, Dial);
        rig.Clock.Advance(SpiralFrameDelay.Default);

        rig.Presenter.Withdraw();

        // A paced module's release drops a one-shot and leaves the screen alone; a static
        // continuous module's withdraws a surface and has no timer. This one has BOTH, and if the
        // timer outlived the surface a stopped session would repaint a window nobody can see.
        Assert.False(rig.Presenter.Showing);
        Assert.False(rig.Presenter.Running);
        Assert.False(rig.Presenter.Engaged);
        Assert.Equal(0, rig.Clock.PendingCount);
        Assert.Equal(1, rig.Animations.Single().Disposals);
        Assert.Equal(1, Assert.Single(rig.Presences).WithdrawCalls);

        // And no amount of clock brings any of it back.
        var paints = rig.Presences.Single().Calls.Count(c => c == "paint");
        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(paints, rig.Presences.Single().Calls.Count(c => c == "paint"));
    }

    [Fact]
    public void Engaged_IsTrueWhileATimerSurvivesASurfaceThatIsAlreadyDown()
    {
        var rig = new Rig(frames: 4);
        rig.Presenter.Engage(SpiralPath, Dial);

        Assert.True(rig.Presenter.Engaged);

        rig.Presenter.Withdraw();
        Assert.False(rig.Presenter.Engaged);
    }

    [Fact]
    public void ADisposedPresenter_RefusesInTypeAndPlacesNothing()
    {
        var rig = new Rig(frames: 4);
        rig.Presenter.Engage(SpiralPath, Dial);
        rig.Presenter.Dispose();

        var outcome = rig.Presenter.Engage(SpiralPath, Dial);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(OverlayReasonCodes.OverlayPresenceDisposed, refusal.Reason.Code);
        Assert.False(rig.Presenter.Showing);
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    // =====================================================================================

    private sealed class Rig
    {
        private readonly Lazy<SpiralSurfacePresenter> _presenter;

        public Rig(int frames = 2, TimeSpan? frameDelay = null)
        {
            Display = () => SpiralSurfacePresenterTests.Display;
            _presenter = new Lazy<SpiralSurfacePresenter>(() => new SpiralSurfacePresenter(
                Clock,
                action => action(),
                () =>
                {
                    var presence = new RecordingPresence { PresentRefusal = PresentRefusal };
                    Presences.Add(presence);
                    return presence;
                },
                new StubFrames(this, frames, frameDelay ?? SpiralFrameDelay.Default),
                () => Display()));
        }

        public ManualClock Clock { get; } = new();

        public List<RecordingPresence> Presences { get; } = [];

        public List<StubAnimation> Animations { get; } = [];

        public string? PresentRefusal { get; init; }

        public Func<OverlayBounds?> Display { get; init; }

        public SpiralSurfacePresenter Presenter => _presenter.Value;
    }

    /// <summary>A decoder that never touches a file: <paramref name="frames"/> of 0 is the
    /// "this build cannot decode it" outcome the real GDI+ source returns null for.</summary>
    private sealed class StubFrames(Rig rig, int frames, TimeSpan delay) : ISpiralFrameSource
    {
        public ISpiralAnimation? Open(string path, int width, int height)
        {
            if (frames <= 0)
            {
                return null;
            }

            var animation = new StubAnimation(frames, delay, width, height);
            rig.Animations.Add(animation);
            return animation;
        }
    }

    private sealed class StubAnimation(int frames, TimeSpan delay, int width, int height) : ISpiralAnimation
    {
        public int FrameCount => frames;

        public TimeSpan FrameDelay => delay;

        /// <summary>Every frame index this animation was asked for, in order.</summary>
        public List<int> Rendered { get; } = [];

        public int Disposals { get; private set; }

        /// <summary>From this index onwards, produce nothing — a decoder that stops working
        /// mid-clip, which is the only way to reach the frozen-but-present state.</summary>
        public int? FailFrom { get; set; }

        public OverlayFrame? Render(int index)
        {
            Rendered.Add(index);
            if (FailFrom is { } fail && index >= fail)
            {
                return null;
            }

            return OverlayFrame.Solid(width, height, (byte)index, (byte)index, (byte)index);
        }

        public void Dispose() => Disposals++;
    }

    /// <summary>An overlay that records what it was asked to do, in order, and never touches a
    /// screen. <see cref="PaintRefusal"/> is settable here (the tint's copy is init-only) because a
    /// moving module's interesting failure is a paint that stops holding PART WAY THROUGH.</summary>
    private sealed class RecordingPresence : IOverlayPresence
    {
        private OverlaySurfaceRequest? _current;

        public List<string> Calls { get; } = [];

        public List<OverlaySurfaceRequest> Requests { get; } = [];

        public List<OverlayFrame> Frames { get; } = [];

        public int WithdrawCalls { get; private set; }

        public int ReassertCalls { get; private set; }

        public string? PaintRefusal { get; set; }

        public string? PresentRefusal { get; init; }

        public bool IsPresenting => _current is not null;

        public CapabilityState Present(OverlaySurfaceRequest request)
        {
            Calls.Add("present");
            Requests.Add(request);
            if (PresentRefusal is not null)
            {
                return new CapabilityState.Unavailable(
                    new CapabilityReason(PresentRefusal, "recording presence: refused to present"));
            }

            _current = request;
            return new CapabilityState.Available("recording presence: placed");
        }

        public CapabilityState Paint(OverlayFrame frame)
        {
            Calls.Add("paint");
            Frames.Add(frame);
            return PaintRefusal is null
                ? new CapabilityState.Available("recording presence: painted")
                : new CapabilityState.Unavailable(new CapabilityReason(PaintRefusal, "recording presence: refused"));
        }

        public void Reassert()
        {
            Calls.Add("reassert");
            ReassertCalls++;
        }

        public CapabilityState SetClickThrough(bool clickThrough)
        {
            Calls.Add("set-click-through");
            return new CapabilityState.Available("recording presence: flipped");
        }

        public CapabilityState Withdraw()
        {
            Calls.Add("withdraw");
            WithdrawCalls++;
            _current = null;
            return new CapabilityState.Available("recording presence: withdrawn");
        }

        public void Dispose() => _current = null;
    }

    /// <summary>The manual clock, the session spine's shape, plus one thing a moving module needs: the ability
    /// to make every pending timer vanish without touching anything else, which is how the
    /// "the frames stopped arriving" state is reached deliberately. Zero wall-clock.</summary>
    private sealed class ManualClock : ISessionClock
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

            return new Handle(this, entry);
        }

        public void CancelAll()
        {
            lock (_timers)
            {
                _timers.Clear();
            }
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

        private sealed class Handle(ManualClock clock, Entry entry) : IDisposable
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
