using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Video;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-111 — the video PRESENTER: where the frame cadence lives, what it does when a clip ends, is
/// capped, or stops being held, and what it releases.
///
/// <para>Every seam is a stub: no window, no decoder, no file. That is deliberate and it is what
/// makes these facts about the presenter's own decisions — the OS-level half is
/// <see cref="VideoCapabilityTests"/>, and mixing the two would mean neither could be mutated on its
/// own.</para>
/// </summary>
public class VideoSurfacePresenterTests
{
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
                Clock, action => action(), () => Presence, Clips, () => bounds);
        }

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
