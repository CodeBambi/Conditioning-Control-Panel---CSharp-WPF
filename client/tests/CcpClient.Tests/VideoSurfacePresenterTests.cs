using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Video;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The video PRESENTER: where the frame cadence lives, what it does when a clip ends, is
/// capped, or stops being held, and what it releases.
///
/// <para>Every seam is a stub: no window, no decoder, no file. That is deliberate and it is what
/// makes these facts about the presenter's own decisions — the OS-level half is
/// <see cref="VideoCapabilityTests"/>, and mixing the two would mean neither could be mutated on its
/// own.</para>
/// </summary>
public class VideoSurfacePresenterTests
{
    // ---------------------------------------------------------------------------------------
    //  The seam the SECOND consumer needed, and the refusal the FIRST one wanted
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void APAINTERSeesEVERYPictureIncludingTheFIRST_AndIsToldWhatTheOSSaidAboutTheClip()
    {
        using var rig = new Rig();
        rig.Clips.FrameInterval = TimeSpan.FromMilliseconds(50);
        var painter = new RecordingPainter();

        Assert.IsType<CapabilityState.Available>(
            rig.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => { }, painter));

        // THE FIRST PICTURE IS PAINTED TOO. Begin shows frame 0 itself, so a seam that only reached
        // the cadence would leave a clip's opening frame bare — and for a counting game that is a
        // picture the user is asked about and never saw drawn on.
        Assert.Equal(1, painter.Paints);
        Assert.Equal([TimeSpan.Zero], painter.Elapsed);

        // And the painter is told what the OS said about the clip, ONCE, before the first picture.
        // Nothing else can tell it: the clip is opened and closed inside this presenter.
        Assert.Equal(1, painter.Openings);
        Assert.Equal(TimeSpan.FromSeconds(1), painter.LastInfo.Duration);
        Assert.Equal(320, painter.LastInfo.Width);

        rig.Clock.AdvanceToNextDue();
        rig.Clock.AdvanceToNextDue();
        Assert.Equal(3, painter.Paints);

        // The elapsed time it is handed is the presenter's own INJECTED clock, one frame interval
        // per picture — never wall time.
        Assert.Equal(
            [TimeSpan.Zero, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100)],
            painter.Elapsed);

        // The paint really reaches the picture the capability is handed: the presence saw a frame
        // whose pixels the painter had already changed.
        Assert.True(painter.LastPaintedFrameWasShown);
    }

    [Fact]
    public void APAINTERIsOPTIONAL_AndTheFIRSTConsumerStillPassesNone()
    {
        using var rig = new Rig();

        // The seam the SECOND consumer added must not have changed what the first consumer does. Begin with no
        // painter plays exactly as it did before, and nothing in the presenter requires one.
        Assert.IsType<CapabilityState.Available>(rig.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => { }));
        rig.Clock.AdvanceToNextDue();
        Assert.Equal(2, rig.Presence.Shows);
    }

    [Fact]
    public void ASECONDBeginWhileAClipPlaysIsREFUSEDInType_RatherThanLEAKINGTheFirstClipsDecoder()
    {
        using var rig = new Rig();
        var firstEnded = 0;
        Assert.IsType<CapabilityState.Available>(
            rig.Presenter.Begin("first.mp4", TimeSpan.Zero, () => firstEnded++));
        Assert.Equal(1, rig.Clips.Opens);

        // THE COLLISION THE SECOND CONSUMER REVEALED. Both modules guard with "a clip is already
        // showing" in Compose, which runs on the CLOCK thread while this runs on the SURFACE
        // thread — so the guard cannot close the window between them. Before that seam this call
        // overwrote the first clip's handle and its end-callback: an open decoder leaked and the
        // first module believed it was still playing for ever.
        var second = rig.Presenter.Begin("second.mp4", TimeSpan.Zero, () => { });

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(second);
        Assert.Equal(VideoReasonCodes.VideoAlreadyPlaying, unavailable.Reason.Code);
        Assert.Contains("first.mp4", unavailable.Reason.Detail, StringComparison.Ordinal);

        // NOTHING was opened for the refused call, and the first clip is untouched: still playing,
        // still cadencing, still owning its own end-callback.
        Assert.Equal(1, rig.Clips.Opens);
        Assert.Equal("first.mp4", rig.Presenter.PlayingClip);
        rig.Clock.AdvanceToNextDue();
        Assert.Equal(2, rig.Presence.Shows);

        // And the first clip's ending still reaches the first caller, not the second.
        rig.Clock.AdvanceToNextDue();
        rig.Clock.AdvanceToNextDue();
        Assert.Equal(1, firstEnded);
    }

    [Fact]
    public void AfterAClipENDSTheSurfaceTakesTheNextOne_SoTheRefusalIsBUSYRatherThanBROKEN()
    {
        using var rig = new Rig();
        Assert.IsType<CapabilityState.Available>(rig.Presenter.Begin("first.mp4", TimeSpan.Zero, () => { }));
        rig.Presenter.End();

        // The positive control for the fact above: the refusal is about a clip being IN PROGRESS,
        // not about the surface having been used once. Without this, a Begin that always refused
        // would satisfy the previous fact perfectly.
        Assert.IsType<CapabilityState.Available>(rig.Presenter.Begin("second.mp4", TimeSpan.Zero, () => { }));
        Assert.Equal("second.mp4", rig.Presenter.PlayingClip);
    }

    /// <summary>A painter that records what it was shown and marks the picture, so a fact can prove
    /// the paint reached the frame the capability was afterwards handed.</summary>
    private sealed class RecordingPainter : IVideoFramePainter
    {
        public int Openings { get; private set; }

        public int Paints { get; private set; }

        public VideoClipInfo LastInfo { get; private set; }

        public List<TimeSpan> Elapsed { get; } = [];

        public bool LastPaintedFrameWasShown { get; private set; }

        public void Opening(VideoClipInfo clip)
        {
            Openings++;
            LastInfo = clip;
        }

        public void Paint(VideoFrame frame, TimeSpan elapsed)
        {
            Paints++;
            Elapsed.Add(elapsed);

            // Write a value the decoder never produces, then read it back through the frame's own
            // accessor — the same route the capability's blit takes.
            frame.Pixels[0] = 0xAB;
            frame.Pixels[1] = 0xCD;
            frame.Pixels[2] = 0xEF;
            LastPaintedFrameWasShown = frame.ColourAt(0, 0) == 0xABCDEF;
        }
    }

    [Fact]
    public void AClipThatDecodesNOTHING_TakesTheSurfaceStraightBackDOWN()
    {
        using var rig = new Rig();
        rig.Clips.Frames.Clear();

        var state = rig.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => { });

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(VideoReasonCodes.VideoClipHasNoPicture, unavailable.Reason.Code);

        // A BLACK RECTANGLE OVER THE USER'S DESKTOP WITH NO PICTURE IN IT is strictly worse than no
        // rectangle at all, so the surface comes down on the way out rather than being left for the
        // module to notice.
        Assert.Equal(1, rig.Presence.Withdrawals);
        Assert.False(rig.Presenter.Showing);

        // And the decoder is released: a clip whose surface refused is not showing and would still
        // hold an OPEN SOURCE READER.
        Assert.True(rig.Clips.LastClipDisposed);
        Assert.False(rig.Presenter.Engaged);
    }

    [Fact]
    public void AFirstPictureTheSurfaceREFUSES_AlsoTakesTheSurfaceBackDown()
    {
        using var rig = new Rig();
        rig.Presence.ShowResult = new CapabilityState.Unavailable(new CapabilityReason(
            VideoReasonCodes.VideoFrameNotHeld, "the OS's copy never carried it"));

        var state = rig.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => { });

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(VideoReasonCodes.VideoFrameNotHeld, unavailable.Reason.Code);

        // The SECOND way Begin can fail after the surface is already up, and it needs the same
        // teardown as the first: a placed surface holding nothing is a black rectangle over the
        // user's desktop. Nothing may be left behind — not the window and not the decoder.
        Assert.Equal(1, rig.Presence.Withdrawals);
        Assert.False(rig.Presenter.Showing);
        Assert.True(rig.Clips.LastClipDisposed);
        Assert.False(rig.Presenter.Engaged);
    }

    [Fact]
    public void TheFrameCadenceIsTheCLIPSOwnFrameRate_AndEveryAdvanceShowsTheNextPicture()
    {
        using var rig = new Rig();
        rig.Clips.FrameInterval = TimeSpan.FromMilliseconds(40);

        Assert.IsType<CapabilityState.Available>(rig.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => { }));
        Assert.Equal(1, rig.Presence.Shows);

        rig.Clock.AdvanceToNextDue();
        Assert.Equal(2, rig.Presence.Shows);
        rig.Clock.AdvanceToNextDue();
        Assert.Equal(3, rig.Presence.Shows);

        // The cadence is the CLIP's, not a constant of the presenter's: a 40 ms interval must not
        // produce the fallback's 80 ms spacing.
        Assert.NotEqual(VideoSurfacePresenter.FallbackFrameInterval, rig.Clock.LastInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(40), rig.Clock.LastInterval);
    }

    [Fact]
    public void AContainerWithNoFrameRateFallsBackRatherThanSpinningTheClock()
    {
        using var rig = new Rig();
        rig.Clips.FrameInterval = TimeSpan.Zero;

        rig.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => { });
        rig.Clock.AdvanceToNextDue();

        Assert.Equal(VideoSurfacePresenter.FallbackFrameInterval, rig.Clock.LastInterval);
    }

    [Fact]
    public void WhenTheClipRunsOUTTheSurfaceComesDownAndTheCallerIsTold()
    {
        using var rig = new Rig();
        var ended = 0;
        rig.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => ended++);

        for (var i = 0; i < 10 && rig.Presenter.Showing; i++)
        {
            rig.Clock.AdvanceToNextDue();
        }

        Assert.Equal(1, ended);
        Assert.False(rig.Presenter.Showing);
        Assert.Equal(1, rig.Presence.Withdrawals);
        Assert.True(rig.Clips.LastClipDisposed);
        Assert.Null(rig.Presenter.PlayingClip);
    }

    [Fact]
    public void THEMAXLENGTHCapEndsALongClipEarly_AndZeroMeansNoCapAtAll()
    {
        using var capped = new Rig();
        capped.Clips.FrameCount = 10_000;
        var cappedEnds = 0;
        capped.Presenter.Begin("clip.mp4", TimeSpan.FromSeconds(1), () => cappedEnds++);
        for (var i = 0; i < 40 && capped.Presenter.Showing; i++)
        {
            capped.Clock.AdvanceToNextDue();
        }

        Assert.Equal(1, cappedEnds);
        Assert.False(capped.Presenter.Showing);

        // Upstream's own encoding: 0 is OFF (VideoService.cs:5509-5510). The same clip with no cap
        // is still playing after the same number of beats.
        using var uncapped = new Rig();
        uncapped.Clips.FrameCount = 10_000;
        uncapped.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => { });
        for (var i = 0; i < 40; i++)
        {
            uncapped.Clock.AdvanceToNextDue();
        }

        Assert.True(uncapped.Presenter.Showing);
    }

    [Fact]
    public void ASurfaceThatSTOPSHoldingThePicture_EndsTheClipRatherThanFeedingADeadWindow()
    {
        using var rig = new Rig();
        rig.Clips.FrameCount = 100;
        var ended = 0;
        rig.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => ended++);

        rig.Presence.ShowResult = new CapabilityState.Unavailable(new CapabilityReason(
            VideoReasonCodes.VideoFrameNotHeld, "the OS's copy stopped carrying it"));
        rig.Clock.AdvanceToNextDue();

        Assert.Equal(1, ended);
        Assert.False(rig.Presenter.Showing);
        Assert.True(rig.Clips.LastClipDisposed);

        // And the refusal is REMEMBERED verbatim: it is the only place a user or a bug report learns
        // that a clip was decoding into a surface the operating system had stopped holding.
        var last = Assert.IsType<CapabilityState.Unavailable>(rig.Presenter.LastPlacement);
        Assert.Equal(VideoReasonCodes.VideoFrameNotHeld, last.Reason.Code);
    }

    [Fact]
    public void BOTHClausesOfRUNNINGAreLoadBearing()
    {
        using var rig = new Rig();
        rig.Clips.FrameCount = 100;
        rig.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => { });

        // Clause 1: the OS says the picture is moving. Clause 2: the frame advance is on the clock.
        Assert.True(rig.Presenter.Running);

        rig.Presence.MovingAnswer = false;
        Assert.False(rig.Presenter.Running);
        rig.Presence.MovingAnswer = true;
        Assert.True(rig.Presenter.Running);

        // Without the cadence clause a surface holding its last frame for ever reports itself alive.
        rig.Presenter.End();
        Assert.False(
            rig.Presenter.Running,
            "with the cadence torn down the presenter must not be Running, whatever the capability's "
            + "live read still answers");
    }

    [Fact]
    public void WithNoDisplayNothingIsOpenedAtAll_NotEvenTheDecoder()
    {
        using var rig = new Rig(display: null, useDefaultDisplay: false);

        var state = rig.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => { });

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(VideoReasonCodes.VideoNoDisplay, unavailable.Reason.Code);
        Assert.Equal(0, rig.Clips.Opens);
        Assert.Equal(0, rig.Presence.Presents);
    }

    /// <summary>
    /// <b>ONE decoder, ONE capability and ONE placement carry a whole clip</b> — the unified
    /// presentation contract's resource clause ("Monitor count must not multiply decoders, audio
    /// sessions, network downloads, or frame storage", <c>client/docs/architecture.md</c> A-003),
    /// which this port satisfies the only way a primary-display-only surface can: there is nothing
    /// to multiply BY. Upstream is the counter-example that makes the clause worth pinning — it
    /// builds one window and one <c>WriteableBitmap</c> per screen
    /// (<c>Services/Video/DualMonitorVideoService.cs:373-387</c>) and gates three-or-more monitors
    /// behind an opt-in because of "N independent decoders on high monitor counts"
    /// (<c>Services/Video/VideoService.cs:2035-2045</c>).
    ///
    /// <para><b>And the same count names a contract clause the port does NOT meet.</b> The display
    /// seam is read ONCE, before the decoder opens, and never again while the clip runs — so
    /// "Recompute targets when displays are added, removed, rotated, rearranged, or have scaling
    /// changed" (<c>client/docs/capability-inventory.md</c>, "Per-monitor geometry") is unimplemented:
    /// nothing in this port listens for a display change, and a clip that started before one keeps
    /// the rectangle it was given. That is asserted here rather than left as prose, so the lane that
    /// implements the recompute has to come through this fact and say so.</para>
    /// </summary>
    [Fact]
    public void ONEDecoderONECapabilityAndONEPlacementCarryTheWholeClip_AndTheDisplayIsNeverReReAD()
    {
        using var rig = new Rig();
        rig.Clips.FrameCount = 6;

        rig.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => { });
        rig.Clock.AdvanceToNextDue();
        rig.Clock.AdvanceToNextDue();
        rig.Clock.AdvanceToNextDue();

        Assert.Equal(1, rig.Clips.Opens);
        Assert.Equal(1, rig.PresencesBuilt);
        Assert.Equal(4, rig.Presence.Shows);
        Assert.Equal(1, rig.Presence.Presents);

        Assert.Equal(1, rig.DisplayReads);
    }

    [Fact]
    public void DisposeReleasesTheCadence_TheClip_AndTheCapability()
    {
        var rig = new Rig();
        rig.Clips.FrameCount = 100;
        rig.Presenter.Begin("clip.mp4", TimeSpan.Zero, () => { });
        Assert.True(rig.Presenter.Engaged);

        rig.Dispose();

        Assert.True(rig.Clips.LastClipDisposed);
        Assert.True(rig.Presence.Disposed);
        Assert.False(rig.Presenter.Engaged);
        Assert.Null(rig.Clock.PendingInterval);
    }

    // ---------------------------------------------------------------------------------------
    //  fixtures
    // ---------------------------------------------------------------------------------------

    private sealed class Rig : IDisposable
    {
        public Rig(VideoBounds? display = null, bool useDefaultDisplay = true)
        {
            Clock = new ManualClock();
            Presence = new StubPresence();
            Clips = new StubClipSource();
            var bounds = display ?? (useDefaultDisplay ? new VideoBounds(0, 0, 400, 240) : null);
            Presenter = new VideoSurfacePresenter(
                Clock,
                action => action(),
                () =>
                {
                    PresencesBuilt++;
                    return Presence;
                },
                Clips,
                () =>
                {
                    DisplayReads++;
                    return bounds;
                });
        }

        /// <summary>How many times the presenter asked its display seam where the surface may go.</summary>
        public int DisplayReads { get; private set; }

        /// <summary>How many video capabilities the presenter built.</summary>
        public int PresencesBuilt { get; private set; }

        public ManualClock Clock { get; }

        public StubPresence Presence { get; }

        public StubClipSource Clips { get; }

        public VideoSurfacePresenter Presenter { get; }

        public void Dispose() => Presenter.Dispose();
    }

    private sealed class StubPresence : IVideoPresence
    {
        public int Presents { get; private set; }

        public int Shows { get; private set; }

        public int Withdrawals { get; private set; }

        public bool Disposed { get; private set; }

        public bool MovingAnswer { get; set; } = true;

        public CapabilityState ShowResult { get; set; } = new CapabilityState.Available("the stub allowed it");

        public CapabilityState Present(VideoSurfaceRequest request)
        {
            Presents++;
            IsPresenting = true;
            return new CapabilityState.Available("the stub allowed it");
        }

        public CapabilityState Show(VideoFrame frame)
        {
            Shows++;
            LastShow = ShowResult;
            return ShowResult;
        }

        public CapabilityState Withdraw()
        {
            Withdrawals++;
            IsPresenting = false;
            return new CapabilityState.Available("the stub allowed it");
        }

        public VideoSurfaceObservation Observe() => VideoSurfaceObservation.NotAsked;

        public VideoDisplayObservation ObserveDisplay() => new(true, true, 1, true, true);

        public bool PictureIsMoving => MovingAnswer;

        public bool CanReachADisplay => true;

        public bool IsPresenting { get; private set; }

        public CapabilityState? LastShow { get; private set; }

        public VideoSurfaceObservation LastObservation => VideoSurfaceObservation.NotAsked;

        public int FramesHeld => Shows;

        public int FramesAdvanced => Shows;

        public void Dispose() => Disposed = true;
    }

    private sealed class StubClipSource : IVideoClipSource
    {
        public int Opens { get; private set; }

        public int FrameCount { get; set; } = 3;

        public TimeSpan FrameInterval { get; set; } = TimeSpan.FromMilliseconds(100);

        public List<int> Frames { get; } = [0, 1, 2];

        public bool LastClipDisposed { get; private set; }

        public bool MediaStackUsable => true;

        public CapabilityState Open(string path, out IVideoClip? clip)
        {
            Opens++;
            var count = Frames.Count == 0 ? 0 : FrameCount;
            var stub = new StubClip(count, FrameInterval, () => LastClipDisposed = true);
            LastClipDisposed = false;
            clip = stub;
            return new CapabilityState.Available("the stub opened it");
        }

        private sealed class StubClip(int frames, TimeSpan interval, Action onDisposed) : IVideoClip
        {
            public VideoClipInfo Info { get; } = new(true, 320, 240, interval, TimeSpan.FromSeconds(1), false);

            public int DecodedFrames { get; private set; }

            public bool Ended { get; private set; }

            public VideoFrame? ReadFrame()
            {
                if (DecodedFrames >= frames)
                {
                    Ended = true;
                    return null;
                }

                DecodedFrames++;
                return VideoFrame.Solid(320, 240, (byte)DecodedFrames, 0x40, 0x60);
            }

            public void Dispose() => onDisposed();
        }
    }

    private sealed class ManualClock : ISessionClock
    {
        private sealed class Entry
        {
            public DateTimeOffset Due;
            public required Action Fire;
            public bool Cancelled;
        }

        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

        /// <summary>The interval of the most recent schedule — the cadence, as a value a fact can pin.</summary>
        public TimeSpan LastInterval { get; private set; }

        public TimeSpan? PendingInterval =>
            _timers.Any(t => !t.Cancelled) ? LastInterval : null;

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            LastInterval = due;
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            _timers.Add(entry);
            return new Handle(entry);
        }

        public void AdvanceToNextDue()
        {
            var next = _timers.Where(t => !t.Cancelled).OrderBy(t => t.Due).FirstOrDefault();
            if (next is null)
            {
                return;
            }

            _timers.Remove(next);
            UtcNow = next.Due;
            next.Fire();
        }

        private sealed class Handle(Entry entry) : IDisposable
        {
            public void Dispose() => entry.Cancelled = true;
        }
    }
}
