using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>An animated flash animates.</b>
///
/// <para>The pool has always accepted <c>.gif</c> (<see cref="FlashImagePool"/>) and the decoder has
/// always produced exactly one frame, so a user's animated asset was shown as a STILL — silently,
/// with nothing anywhere saying so. Upstream steps its frames on the flash heartbeat
/// (<c>Services/Flash/FlashService.cs:2126-2140</c>) and so does this now.</para>
///
/// <para><b>No second decoder was written for it.</b> The frame walk is the spiral's
/// (<see cref="GdiPlusSpiralFrameSource.OpenAnimation"/>): the first frame dimension, the frame
/// count, the <c>0x5100</c> delay property, <c>GdipImageSelectActiveFrame</c> and one reused buffer.
/// What is the flash's own is the DELAY LAW and the FIT, and the facts here pin both — including the
/// one that shows why the flash could not simply borrow the spiral's number.</para>
///
/// <para><b>What this does not cover.</b> GDI+ is a Windows library, so every decoder fact here
/// asserts the honest null off Windows rather than skipping, and nothing in this file is headed
/// evidence: it establishes what is decoded, what is repainted and when — never what a screen
/// composited.</para>
/// </summary>
public class FlashAnimationTests
{
    private const int Size = 8;

    // ---------------------------------------------------------------------------------
    //  the delay law, as a pure function: no image, no screen, no GDI+
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AClipOfNothingButZeroDelays_RunsAtAHundredMilliseconds_NotAtZeroAndNotAtTheFloor()
    {
        // Most of the web's GIFs declare 0, meaning "as fast as you can". Upstream samples only
        // durations above zero and falls back to 100 ms when there are none
        // (Services/Media/AnimatedWebp.cs:205-211), which is also what a browser shows them at.
        Assert.Equal(TimeSpan.FromMilliseconds(100), FlashFrameDelay.FromHundredths([0, 0, 0]));
        Assert.Equal(TimeSpan.FromMilliseconds(100), FlashFrameDelay.FromHundredths([]));
    }

    [Fact]
    public void TheDelayIsTheMEANOfTheFramesThatDeclaredOne_AndAZeroIsSkippedRatherThanAveragedIn()
    {
        // 10 and 20 hundredths are 100 ms and 200 ms; the 0 between them is not a sample, so the
        // mean is 150 and not 100 (AnimatedWebp.cs:205-209).
        Assert.Equal(TimeSpan.FromMilliseconds(150), FlashFrameDelay.FromHundredths([10, 0, 20]));
    }

    [Fact]
    public void AMeanBelowTheFloor_FallsBackToAHundred_RatherThanClampingToTheFloor()
    {
        // upstream: `if (avgMs < 20) avgMs = 100;` (AnimatedWebp.cs:211) — the fallback, not the
        // bound. A 10 ms clip is not shown at 20 ms, it is shown at 100 ms.
        Assert.Equal(TimeSpan.FromMilliseconds(100), FlashFrameDelay.FromHundredths([1]));
        Assert.Equal(TimeSpan.FromMilliseconds(20), FlashFrameDelay.FromHundredths([2]));
    }

    [Fact]
    public void ALongDeclaredDelayIsCLAMPED_AtUpstreamsTwoSeconds()
    {
        // `Math.Clamp(avgMs * step, 20, 2000)` (AnimatedWebp.cs:212). Four times the spiral's
        // ceiling, and the reason a slideshow GIF stays a slideshow.
        Assert.Equal(TimeSpan.FromMilliseconds(2000), FlashFrameDelay.FromHundredths([500]));
        Assert.Equal(TimeSpan.FromMilliseconds(1500), FlashFrameDelay.FromHundredths([150]));
    }

    [Fact]
    public void TheFlashsLawIsNotTheSpiralsLaw_AndOnAnOrdinaryFileTheyDisagreeTwelveFold()
    {
        // THE WHOLE REASON THE PROFILE EXISTS. A GIF declaring 60 hundredths per frame is a
        // 600 ms slideshow upstream (AnimatedWebp.cs:205-212) and would be a 50 ms strobe under the
        // spiral's law, which falls back for anything outside 20-500 ms
        // (OverlayService.cs:1548-1549). Borrowing the spiral's number would have animated the
        // user's flash twelve times too fast.
        Assert.Equal(TimeSpan.FromMilliseconds(600), FlashFrameDelay.FromHundredths([60, 60]));
        Assert.Equal(TimeSpan.FromMilliseconds(50), SpiralFrameDelay.FromHundredths(60));
    }

    // ---------------------------------------------------------------------------------
    //  a real GIF, through the shared GDI+ walk
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ARealAnimatedGifOpensAsAClip_AtTheFlashsOwnDelay()
    {
        using var file = new TempFlashImage(SlowTwoFrameGif(), ".gif");

        using var clip = new GdiPlusFlashAnimationSource().Open(file.Path, Size, Size);

        if (!GdiPlusRuntime.Available)
        {
            // Not a skip and not a silence: off Windows there is no GDI+, no overlay and no flash
            // on any screen, and the decoder says null rather than pretending.
            Assert.Null(clip);
            return;
        }

        Assert.NotNull(clip);
        Assert.Equal(2, clip.FrameCount);

        // The file declares 60 hundredths per frame. THE SAME BYTES through the spiral's profile
        // give 50 ms, which is what the previous fact says the two laws do — this is that
        // disagreement measured on a real file rather than on an array of ints.
        Assert.Equal(TimeSpan.FromMilliseconds(600), clip.FrameDelay);

        using var asSpiral = new GdiPlusSpiralFrameSource().Open(file.Path, Size, Size);
        Assert.Equal(TimeSpan.FromMilliseconds(50), asSpiral!.FrameDelay);
    }

    [Fact]
    public void TheFramesOfARealGifReallyDiffer_WhichIsTheOnlyThingThatMakesItMotion()
    {
        using var file = new TempFlashImage(SlowTwoFrameGif(), ".gif");
        using var clip = new GdiPlusFlashAnimationSource().Open(file.Path, Size, Size);
        if (!GdiPlusRuntime.Available)
        {
            Assert.Null(clip);
            return;
        }

        // Frame 1 is red everywhere; frame 2 is red over BLUE (see SlowTwoFrameGif). Sampled at the
        // SAME low point in both, so the comparison is between frames and not between places.
        var first = clip!.Render(0)!.ColourAt(Size / 2, Size - 1);
        var second = clip.Render(1)!.ColourAt(Size / 2, Size - 1);

        // Dominance and DIFFERENCE rather than equality, for the reason SpiralFrameSourceTests
        // states: the scale is bicubic, and pinning a channel value would pin the resampler's edge
        // policy — which at the last row of an 8-pixel upscale from two source rows is a long way
        // from 255. WHICH FRAME IS ON SCREEN is the behaviour, and that is what these compare.
        Assert.True((first & 0xFF) > ((first >> 16) & 0xFF), $"frame 1 should be red, got 0x{first:X6}");
        Assert.True(
            ((second >> 16) & 0xFF) > ((first >> 16) & 0xFF),
            $"frame 2 should bring blue where frame 1 had none, got 0x{first:X6} then 0x{second:X6}");

        // GdipImageSelectActiveFrame is what makes this a clip rather than a picture. Without it
        // both renders return the same pixels and the flash sits still while every counter in the
        // presenter says it is moving.
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AClipIsFITTEDToItsRectangleLikeTheStillBesideIt_AndNotCentreCroppedLikeTheSpiral()
    {
        // The second frame of this file is red over blue. Asked for a 4:1 rectangle:
        //   the FLASH profile stretches the whole 2x2 image into it     -> red row, blue row
        //   the SPIRAL profile takes UniformToFill's centred crop, which
        //   for a 1:1 source in a 4:1 box is the middle ROW only        -> red, red
        // A flash's rectangle was already sized to the source's aspect ratio by FlashGeometry.Size,
        // so cropping it would make a clip disagree with the still pictures beside it — and with the
        // still frame the very same surface was PLACED with.
        using var file = new TempFlashImage(SlowTwoFrameGif(), ".gif");
        using var clip = new GdiPlusFlashAnimationSource().Open(file.Path, 8, 2);
        if (!GdiPlusRuntime.Available)
        {
            Assert.Null(clip);
            return;
        }

        var frame = clip!.Render(1)!;
        var top = frame.ColourAt(4, 0);
        var bottom = frame.ColourAt(4, 1);

        Assert.True((top & 0xFF) > 200, $"expected a red top row, got 0x{top:X6}");
        Assert.True(((bottom >> 16) & 0xFF) > 200, $"expected a blue bottom row, got 0x{bottom:X6}");
    }

    [Fact]
    public void AStillGifIsAPictureAndOpensNoClipAtAll()
    {
        using var file = new TempFlashImage(SingleFrameGif(), ".gif");

        // Upstream's animated decode returns null below two frames (AnimatedWebp.cs:209) and the
        // caller falls through to the static path (FlashService.cs:955-956). A clip here would be a
        // timer repainting identical pixels for the whole life of the surface.
        Assert.Null(new GdiPlusFlashAnimationSource().Open(file.Path, Size, Size));
    }

    [Fact]
    public void OnlyGifIsEvenOpened_AndWebpStillDoesNotAnimateHere_WhichIsSaidRatherThanHidden()
    {
        Assert.True(GdiPlusFlashAnimationSource.MayAnimate("a.gif"));
        Assert.True(GdiPlusFlashAnimationSource.MayAnimate("A.GIF"));

        // A still is not asked about at all, so an ordinary flash costs not one extra decode.
        Assert.False(GdiPlusFlashAnimationSource.MayAnimate("a.png"));
        Assert.False(GdiPlusFlashAnimationSource.MayAnimate("a.jpg"));

        // AND WEBP IS FALSE ON PURPOSE. The pool accepts .webp (FlashImagePool) and upstream
        // animates it through SkiaSharp (FlashService.cs:903-928); GDI+ has no WebP codec at all, so
        // this build decodes neither its frames nor its first one. Recorded as a divergence — the
        // alternative would be a decoding dependency this build does not have.
        Assert.False(GdiPlusFlashAnimationSource.MayAnimate("a.webp"));
    }

    // ---------------------------------------------------------------------------------
    //  a surface really steps, on the injected clock
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AnAnimatedFlashIsRepaintedFrameByFrame_OnOneWindow()
    {
        var rig = new Rig(frameCount: 4, delay: TimeSpan.FromMilliseconds(600));

        rig.Presenter.Show(["clip.gif"]);
        var presence = Assert.Single(rig.Presences);
        Assert.Equal(1, rig.Presenter.ClipsOpened);
        Assert.Equal(1, presence.PresentCalls);
        Assert.Equal(1, presence.PaintCalls);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(600));
        rig.Clock.Advance(TimeSpan.FromMilliseconds(600));

        // Two advances, two repaints, and STILL ONE PRESENT. A frame advance is a paint and only a
        // paint: Present walks the OS's whole z-order and asks the hit test in both polarities
        // (Overlay/IOverlayPresence.cs:80-85), which is right once per placement and would be a
        // full-screen window catching the user's clicks several times a second here.
        Assert.Equal(1, presence.PresentCalls);
        Assert.Equal(3, presence.PaintCalls);
        Assert.Equal(2, rig.Presenter.ClipFramesShown);
        // The list starts at ONE, not zero: frame 0 was placed by the still decoder, which is
        // upstream's own order too — the window is spawned holding imageData.Frames[0] and the
        // heartbeat steps it from there (FlashService.cs:1240-1243, :2126-2140).
        Assert.Equal([1, 2], rig.Animations.Single().Rendered);
    }

    [Fact]
    public void AStillImageOpensNoClipAndArmsNoFrameTimer()
    {
        var rig = new Rig(frameCount: 0, delay: TimeSpan.FromMilliseconds(600));

        rig.Presenter.Show(["picture.png"]);

        Assert.Equal(0, rig.Presenter.ClipsOpened);
        Assert.Equal(0, rig.Presenter.LiveClips);

        // The only timers on the clock are the surface's LIFETIME and the topmost cadence. A frame
        // advance for a picture would be a tick that changes nothing until the flash expires.
        Assert.Equal(2, rig.Clock.PendingCount);
    }

    [Fact]
    public void TheFrameIsDerivedFromELAPSEDTime_SoALateTickDropsFramesRatherThanRunningTheClipSlow()
    {
        var rig = new Rig(frameCount: 8, delay: TimeSpan.FromMilliseconds(100));
        rig.Presenter.Show(["clip.gif"]);

        // One tick, arriving 400 ms late. Upstream's heartbeat computes
        // `(int)(elapsed / frameDelay) % count` from the window's START time
        // (FlashService.cs:2128-2130), so the clip is where it should be by now — frame 4 — and the
        // three it missed are gone. An incrementing index would show frame 1 here and would run the
        // whole clip four times slow for the rest of the surface's life.
        rig.Clock.Advance(TimeSpan.FromMilliseconds(400));

        Assert.Equal([4], rig.Animations.Single().Rendered);
    }

    [Fact]
    public void TheClipLOOPS_ForAsLongAsTheSurfaceIsUp()
    {
        var rig = new Rig(frameCount: 3, delay: TimeSpan.FromMilliseconds(100));
        rig.Presenter.Show(["clip.gif"]);

        for (var i = 0; i < 3; i++)
        {
            rig.Clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        // `% Frames.Count` (FlashService.cs:2130): the clip never stops on its last frame.
        Assert.Equal([1, 2, 0], rig.Animations.Single().Rendered);
    }

    // ---------------------------------------------------------------------------------
    //  nothing outlives its surface
    // ---------------------------------------------------------------------------------

    [Fact]
    public void WhenTheSurfacesLifetimeExpires_TheClipIsClosedAndItsTimerIsGone()
    {
        var rig = new Rig(frameCount: 4, delay: TimeSpan.FromMilliseconds(600));
        rig.Presenter.Show(["clip.gif"]);
        var animation = rig.Animations.Single();

        // The surface retires itself on the SET's own lifetime timer and tells nobody, so the clip
        // notices at its next advance. A GDI+ image handle and a pinned buffer per stranded clip is
        // a leak with a session's worth of flashes behind it.
        rig.Clock.Advance(FlashSurfacePresenter.SurfaceLifetime);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(600));

        Assert.Equal(0, rig.Presenter.LiveClips);
        Assert.True(animation.Disposed);
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
    }

    [Fact]
    public void HideAllClosesEveryClip_WhichIsTheHalfOfStopTheUserCanSee()
    {
        var rig = new Rig(frameCount: 4, delay: TimeSpan.FromMilliseconds(600));
        rig.Presenter.Show(["one.gif", "two.gif"]);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(FlashSurfacePresenter.StaggerMilliseconds));

        // One clip per SURFACE, which is upstream's own shape: the frame list and the start time
        // live on the window (FlashService.cs:1240-1243).
        Assert.Equal(2, rig.Presenter.LiveClips);
        Assert.Equal(2, rig.Animations.Count);

        rig.Presenter.HideAll();

        Assert.Equal(0, rig.Presenter.LiveClips);
        Assert.All(rig.Animations, a => Assert.True(a.Disposed));

        // And no advance survives it: a stopped session leaves no timer behind.
        var pending = rig.Clock.PendingCount;
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, pending);
    }

    [Fact]
    public void ARepaintTheOsRefuses_TakesTheSurfaceDownAndTheClipWithIt()
    {
        var rig = new Rig(frameCount: 4, delay: TimeSpan.FromMilliseconds(600));
        rig.Presenter.Show(["clip.gif"]);
        var presence = Assert.Single(rig.Presences);

        presence.PaintRefusal = OverlayReasonCodes.OverlayContentNotHeld;
        rig.Clock.Advance(TimeSpan.FromMilliseconds(600));

        // OverlaySurfaceSet.Repaint keeps Place's rule — a surface the OS confirms is on screen and
        // does NOT hold the frame is a rectangle of stale pixels over the user's work — so it comes
        // down, and the clip stops with it rather than stepping a window that is gone.
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(0, rig.Presenter.LiveClips);
        Assert.True(rig.Animations.Single().Disposed);
        Assert.Equal(0, rig.Presenter.ClipFramesShown);
    }

    // ---------------------------------------------------------------------------------
    //  rig
    // ---------------------------------------------------------------------------------

    private sealed class Rig
    {
        private readonly Lazy<FlashSurfacePresenter> _presenter;

        public Rig(int frameCount, TimeSpan delay)
        {
            _presenter = new Lazy<FlashSurfacePresenter>(() => new FlashSurfacePresenter(
                Clock,
                action => action(),
                () =>
                {
                    var presence = new RecordingPresence();
                    Presences.Add(presence);
                    return presence;
                },
                new StubFrames(),
                () => new OverlayBounds(0, 0, 1920, 1080),
                new Random(1),
                animations: new StubAnimations(this, frameCount, delay)));
        }

        public ManualClock Clock { get; } = new();

        public List<RecordingPresence> Presences { get; } = [];

        public List<StubAnimation> Animations { get; } = [];

        public FlashSurfacePresenter Presenter => _presenter.Value;
    }

    /// <summary>A still decoder: every path renders one flat frame at the size the geometry asks
    /// for, so the PLACEMENT is real and only the clip is stubbed.</summary>
    private sealed class StubFrames : IFlashFrameSource
    {
        public OverlayFrame? Render(string path, Func<int, int, (int Width, int Height)> targetSize)
        {
            var (width, height) = targetSize(400, 300);
            return OverlayFrame.Solid(width, height, 0x20, 0x30, 0x40);
        }
    }

    /// <summary>Opens a clip for anything but <c>.png</c>, so a test picks a still or a clip by file
    /// name. <c>frameCount</c> of zero means this build opens nothing at all.</summary>
    private sealed class StubAnimations(Rig rig, int frameCount, TimeSpan delay) : IFlashAnimationSource
    {
        public ISpiralAnimation? Open(string path, int width, int height)
        {
            if (frameCount <= 1 || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var animation = new StubAnimation(frameCount, delay, width, height);
            rig.Animations.Add(animation);
            return animation;
        }
    }

    private sealed class StubAnimation(int frames, TimeSpan delay, int width, int height) : ISpiralAnimation
    {
        public List<int> Rendered { get; } = [];

        public bool Disposed { get; private set; }

        public int FrameCount => frames;

        public TimeSpan FrameDelay => delay;

        public OverlayFrame? Render(int index)
        {
            Rendered.Add(index);
            return OverlayFrame.Solid(width, height, 0x10, 0x20, 0x30);
        }

        public void Dispose() => Disposed = true;
    }

    /// <summary>An overlay that records what it was asked to do and never touches a screen.</summary>
    private sealed class RecordingPresence : IOverlayPresence
    {
        private OverlaySurfaceRequest? _current;

        public int PresentCalls { get; private set; }

        public int PaintCalls { get; private set; }

        public string? PaintRefusal { get; set; }

        public bool IsPresenting => _current is not null;

        public CapabilityState Present(OverlaySurfaceRequest request)
        {
            PresentCalls++;
            _current = request;
            return new CapabilityState.Available("recording presence: placed");
        }

        public CapabilityState Paint(OverlayFrame frame)
        {
            PaintCalls++;
            return PaintRefusal is null
                ? new CapabilityState.Available("recording presence: painted")
                : new CapabilityState.Unavailable(new CapabilityReason(PaintRefusal, "recording presence: refused"));
        }

        public void Reassert()
        {
        }

        public CapabilityState SetClickThrough(bool clickThrough) =>
            new CapabilityState.Available("recording presence: flipped");

        public CapabilityState Withdraw()
        {
            _current = null;
            return new CapabilityState.Available("recording presence: withdrawn");
        }

        public void Dispose() => _current = null;
    }

    /// <summary>The manual clock, in the shape every module test shares. Zero wall-clock.</summary>
    private sealed class ManualClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

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

    private sealed class TempFlashImage : IDisposable
    {
        public TempFlashImage(byte[] bytes, string extension)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "ccp-flash-anim-" + Guid.NewGuid().ToString("N") + extension);
            File.WriteAllBytes(Path, bytes);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
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
    /// A 2x2 GIF89a with two frames — frame 1 all red, frame 2 all blue — each declaring
    /// <b>60 hundredths</b> of a second. The bytes are
    /// <see cref="SpiralFrameSourceTests"/>'s hand-built clip with one field changed, and that field
    /// is the point: 60 hundredths is inside the flash's 20-2000 ms clamp and outside the spiral's
    /// 20-500 ms window, so the same file measures the two laws apart.
    /// </summary>
    private static byte[] SlowTwoFrameGif() =>
    [
        // "GIF89a"
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
        // logical screen: 2x2, global colour table of 2 entries, background 0, aspect 0
        0x02, 0x00, 0x02, 0x00, 0xF0, 0x00, 0x00,
        // the table: red, blue
        0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF,
        // graphic control extension: no transparency, delay 0x003C = 60 hundredths
        0x21, 0xF9, 0x04, 0x00, 0x3C, 0x00, 0x00, 0x00,
        // image descriptor at 0,0 sized 2x2, no local colour table
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x02, 0x00, 0x00,
        // LZW min code size 2, one sub-block of 4 bytes: CLEAR,0,CLEAR,0,CLEAR,0,CLEAR,0,EOI
        0x02, 0x04, 0x04, 0x41, 0x10, 0x05, 0x00,
        // second frame, same delay
        0x21, 0xF9, 0x04, 0x00, 0x3C, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x02, 0x00, 0x00,
        // CLEAR,0,CLEAR,0,CLEAR,1,CLEAR,1,EOI - the TOP row red and the BOTTOM row blue, which is
        // what makes the fit visible: a stretch keeps both rows and a centre-crop keeps one.
        0x02, 0x04, 0x04, 0xC1, 0x30, 0x05, 0x00,
        // trailer
        0x3B,
    ];

    /// <summary>The same file with one frame and no graphic control extension: a picture.</summary>
    private static byte[] SingleFrameGif() =>
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
        0x02, 0x00, 0x02, 0x00, 0xF0, 0x00, 0x00,
        0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x02, 0x00, 0x00,
        0x02, 0x04, 0x04, 0x41, 0x10, 0x05, 0x00,
        0x3B,
    ];
}
