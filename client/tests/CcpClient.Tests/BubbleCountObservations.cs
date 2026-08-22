using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Video;

namespace CcpClient.Tests;

/// <summary>
/// This module's one expensive real-desktop run, and it exists to answer ONE question the module's own
/// facts cannot: <b>does the video capability hold a picture this process COMPOSED, as readily as
/// one its decoder produced?</b>
///
/// <para>Nothing here is new capability code. A real <see cref="Win32VideoPresence"/> is handed a
/// real decoded frame that a real <see cref="BubbleCountRun"/> has painted a bubble into, and the
/// operating system is asked back — through the capability's own read-back and, independently,
/// through <see cref="FlashPixelProbe.RenderWindow"/> (<c>PrintWindow</c>, a path the product never
/// takes). If the seam is a seam, the OS holds the bubble; if it is shaped around the decoder, this
/// is where that shows.</para>
///
/// <para><b>And the ORDER the video-surface packet never took.</b> Its coexistence run measured a card that already
/// held the foreground while a video surface came up. This module does the opposite: it puts a
/// picture up, takes it down, and only THEN asks for the keyboard. That is a different question
/// about the same two capabilities, so it is asked here rather than assumed from there.</para>
///
/// <para><b><c>Overlay/**</c> and <c>Input/**</c> were not edited.</b> They are CONSUMED, and both
/// are measured through their own packets' instruments — <see cref="OverlayWindowProbe"/> and
/// <see cref="InputWindowProbe"/>, unmodified.</para>
/// </summary>
internal static class BubbleCountObservations
{
    /// <summary>The clip's picture size — the same 320x240 the video-surface fixture uses, decoded by the
    /// operating system's own media stack out of a file this suite synthesises in managed code.</summary>
    internal const int ClipWidth = 320;

    internal const int ClipHeight = 240;

    /// <summary>Deliberately not the clip's aspect, for the video-surface fixture's reason: a 400x240 surface over a 4:3
    /// picture pillarboxes, so the bar the read-back's control point lives in is a real bar.</summary>
    internal const int SurfaceWidth = 400;

    internal const int SurfaceHeight = 240;

    private static readonly Lazy<PaintedRun> LazyPainted = new(RunPainted, isThreadSafe: true);

    /// <summary>The one run. Cached: it decodes a real file and puts real windows on the real
    /// desktop.</summary>
    internal static PaintedRun Painted => LazyPainted.Value;

    /// <summary>Where this packet's fixture media lives — its own folder, never the video-surface fixture's.</summary>
    internal static string MediaFolder =>
        Path.Combine(Path.GetTempPath(), "ccp-sp112-bubblecount", $"pid{Environment.ProcessId}");

    /// <summary>The overlay's state at one moment, read entirely through the overlay's own
    /// instrument.</summary>
    internal readonly record struct OverlayReading(
        bool PointPassesThrough,
        bool AboveEveryOrdinaryWindow,
        int Alpha,
        bool TransparentStyleHeld,
        bool IsForeground);

    /// <summary>
    /// One real game's worth of measurement.
    /// </summary>
    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation is compared
    /// against.</param>
    /// <param name="ClipOpened">The OS's own media stack opened the fixture.</param>
    /// <param name="BubblesPainted">How many bubbles the run had drawn into the picture that was
    /// handed over.</param>
    /// <param name="PaintedPixelsInFrame">How many of the sampled points INSIDE the bubble's own
    /// disc differ, in the handed-over frame, from the flat colour the decoder produced. The
    /// painter's own positive control: zero here means nothing was painted and every reading below
    /// would be vacuous.</param>
    /// <param name="ShowState">What the capability said about the composed picture.</param>
    /// <param name="FrameHeld">The capability's own differential: the bar is held AND every sampled
    /// picture point matches the frame handed over.</param>
    /// <param name="PictureSampled">How many picture points the capability read back.</param>
    /// <param name="PictureMatched">How many of them matched.</param>
    /// <param name="BubblePointsSampled">How many points inside the bubble's disc were read back out
    /// of the OS's own rendering of the window.</param>
    /// <param name="BubblePointsMatchingComposed">How many of those the OS's rendering carries
    /// exactly as the port composed them. <b>This is the packet's own new evidence:</b> the
    /// operating system is holding pixels this process PAINTED, not pixels its decoder
    /// produced.</param>
    /// <param name="BubblePointsUnlikeTheDecodersColour">How many of those the OS's rendering shows
    /// as something OTHER than the flat colour the decoder produced. <b>Without this the match count
    /// above would be satisfied by background:</b> a mapping that resolved to a pixel outside the
    /// disc would compare background against background and pass.</param>
    /// <param name="RenderedPixels">How many pixels that rendering returned at all — the read's own
    /// negative control.</param>
    /// <param name="CardTookTheInputAfterTheVideo">The question card, put up AFTER the surface came
    /// down, really took the foreground and the system keyboard focus.</param>
    /// <param name="CardPromptState">What the input capability said about it.</param>
    /// <param name="OverlayPresented">The overlay claimed Available before anything else existed.</param>
    /// <param name="OverlayBefore">The overlay before the clip.</param>
    /// <param name="OverlayDuringClip">The overlay while the composed picture was up.</param>
    /// <param name="OverlayDuringCard">The overlay while the question held the keyboard.</param>
    /// <param name="OverlayAfter">The overlay after everything came down.</param>
    /// <param name="OverlayCatchesItsOwnPointWhenMadeOpaque">The differential: with
    /// <c>WS_EX_TRANSPARENT</c> cleared the same point routes TO the overlay, so "the point went
    /// elsewhere" cannot be satisfied by an overlay that stopped existing.</param>
    /// <param name="OverlayStillEarnsAvailable">The overlay capability's own oracle, re-asked at the
    /// end.</param>
    /// <param name="OverlayRePresentState">That state, for failure messages.</param>
    internal sealed record PaintedRun(
        bool MachineHasInteractiveDesktop,
        bool ClipOpened,
        int BubblesPainted,
        int PaintedPixelsInFrame,
        CapabilityState ShowState,
        bool FrameHeld,
        int PictureSampled,
        int PictureMatched,
        int BubblePointsSampled,
        int BubblePointsMatchingComposed,
        int BubblePointsUnlikeTheDecodersColour,
        int RenderedPixels,
        bool CardTookTheInputAfterTheVideo,
        CapabilityState? CardPromptState,
        bool OverlayPresented,
        OverlayReading OverlayBefore,
        OverlayReading OverlayDuringClip,
        OverlayReading OverlayDuringCard,
        OverlayReading OverlayAfter,
        bool OverlayCatchesItsOwnPointWhenMadeOpaque,
        bool OverlayStillEarnsAvailable,
        CapabilityState OverlayRePresentState);

    private static PaintedRun RunPainted()
    {
        var machine = InputWindowProbe.MachineHasInteractiveDesktop;
        if (!machine)
        {
            return new PaintedRun(
                false, false, 0, 0, new CapabilityState.Unavailable(new CapabilityReason("(no desktop)", "none")),
                false, 0, 0, 0, 0, 0, 0, false, null, false, default, default, default, default, false, false,
                new CapabilityState.Unavailable(new CapabilityReason("(no desktop)", "none")));
        }

        var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;

        // DISJOINT rectangles: the overlay's hit-test point must never be occluded by the things
        // under test, or "the point went past the overlay" would be measuring one of them.
        const int overlayWidth = 200;
        const int overlayHeight = 150;
        var overlayBounds = new OverlayBounds(
            Math.Max(0, (screenWidth / 2) - overlayWidth - 460),
            Math.Max(0, (screenHeight / 2) - overlayHeight),
            overlayWidth,
            overlayHeight);
        var (overlayX, overlayY) = overlayBounds.Centre;

        using var overlay = new Win32OverlayPresence();
        var presented = overlay.Present(new OverlaySurfaceRequest(overlayBounds, 0.6, ClickThrough: true));
        var overlayWindow = overlay.NativeHandles.Window;

        OverlayReading ReadOverlay() => new(
            PointPassesThrough: OverlayWindowProbe.HitTest(overlayX, overlayY) != overlayWindow,
            AboveEveryOrdinaryWindow: OverlayWindowProbe.ReadZOrder(overlayWindow).AboveEveryOrdinaryWindow,
            Alpha: OverlayWindowProbe.LayeredAlphaOf(overlayWindow),
            TransparentStyleHeld: (OverlayWindowProbe.ExStyleOf(overlayWindow) & 0x00000020) != 0,
            IsForeground: OverlayWindowProbe.IsForeground(overlayWindow));

        var overlayBefore = ReadOverlay();

        // ---- the clip, decoded by the OPERATING SYSTEM out of a file this suite synthesised ----
        Directory.CreateDirectory(MediaFolder);
        var path = Path.Combine(MediaFolder, "bubbles.avi");
        TestAvi.Write(
            path,
            ClipWidth,
            ClipHeight,
            [TestAvi.Solid(ClipWidth, ClipHeight, 0x20, 0x30, 0x40)]);

        var source = VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows);
        var opened = source.Open(path, out var clip) is CapabilityState.Available && clip is not null;
        var frame = clip?.ReadFrame();

        // ---- the module's own painter, on the real decoded picture ----
        // A scripted roll of 0.5 puts the bubble at the middle of the picture and gives it the
        // middle of upstream's size range, so the sample points below are derivable rather than
        // hunted for.
        var run = new BubbleCountRun(BubbleCountDifficulty.Medium, new FixedRandom(0.5));
        if (clip is not null)
        {
            run.Opening(clip.Info);
        }

        var painted = 0;
        var beforePaint = frame is null ? 0u : frame.ColourAt(ClipWidth / 2, ClipHeight / 2);
        if (frame is not null)
        {
            run.Paint(frame, BubbleCountArithmetic.SpawnLeadIn + BubbleCountRun.GrowDuration);
        }

        // The painter's own positive control, taken on the FRAME rather than on the screen: how many
        // sampled points inside the bubble's disc are no longer the decoder's flat colour.
        var bubblePoints = BubbleSamplePoints(run);
        if (frame is not null)
        {
            foreach (var (x, y) in bubblePoints)
            {
                if (frame.ColourAt(x, y) != beforePaint)
                {
                    painted++;
                }
            }
        }

        var videoBounds = new VideoBounds(
            Math.Max(0, (screenWidth / 2) - 200),
            Math.Max(0, (screenHeight / 2) - 320),
            SurfaceWidth,
            SurfaceHeight);
        var video = new Win32VideoPresence(source);
        video.Present(new VideoSurfaceRequest(videoBounds, 0x000000));
        var show = frame is null
            ? new CapabilityState.Unavailable(new CapabilityReason("(no frame)", "nothing decoded"))
            : video.Show(frame);
        var observation = video.LastObservation;
        var overlayDuringClip = ReadOverlay();

        // The INDEPENDENT read: the OS's own rendering of the window, through a call the product
        // never makes. The composed surface is letterboxed, so the frame's own points map into the
        // picture band before they are compared.
        var box = VideoLetterbox.Fit(SurfaceWidth, SurfaceHeight, ClipWidth, ClipHeight);
        var rendered = FlashPixelProbe.RenderWindow(observation.Window, SurfaceWidth, SurfaceHeight);
        var renderedPixels = rendered.Length;
        var bubbleMatched = 0;
        var bubbleUnlikeDecoder = 0;
        const uint decoderColour = (0x20u << 16) | (0x30u << 8) | 0x40u;
        if (renderedPixels > 0 && frame is not null)
        {
            foreach (var (x, y) in bubblePoints)
            {
                // The picture is SCALED into the box, so a picture point maps to a surface point
                // through the composer's own INVERSE map — the same nearest-neighbour arithmetic
                // VideoLetterbox.Compose uses (:118, :126). Mapping forwards would land on a surface
                // pixel whose source was a NEIGHBOURING picture pixel, and inside a gradient bubble
                // those differ: the comparison would fail on arithmetic rather than on evidence.
                var surfaceX = box.X + Math.Clamp((x * box.Width / ClipWidth), 0, box.Width - 1);
                var surfaceY = box.Y + Math.Clamp((y * box.Height / ClipHeight), 0, box.Height - 1);
                var index = (surfaceY * SurfaceWidth) + surfaceX;
                if (surfaceX < 0 || surfaceY < 0 || surfaceX >= SurfaceWidth || surfaceY >= SurfaceHeight
                    || index >= renderedPixels)
                {
                    continue;
                }

                var sourceX = Math.Min(ClipWidth - 1, (surfaceX - box.X) * ClipWidth / box.Width);
                var sourceY = Math.Min(ClipHeight - 1, (surfaceY - box.Y) * ClipHeight / box.Height);
                if (rendered[index] == frame.ColourAt(sourceX, sourceY))
                {
                    bubbleMatched++;
                }

                if (rendered[index] != decoderColour)
                {
                    bubbleUnlikeDecoder++;
                }
            }
        }

        video.Withdraw();
        video.Dispose();
        clip?.Dispose();

        // ---- and only NOW the question: the order the video-surface run never took ----
        var cardBounds = new InputBounds(
            Math.Max(0, (screenWidth / 2) + 140),
            Math.Max(0, (screenHeight / 2) + 120),
            360,
            180);
        using var card = new Win32InputPresence();
        var prompt = card.Prompt(new InputPromptRequest(
            cardBounds,
            new InputPromptContent(
                BubbleCountAnswer.Question, "Attempts remaining: 3", string.Empty, BubbleCountEffect.GiveUpHint),
            _ => { }));
        var cardWindow = card.NativeHandles.Window;

        // Sourced from the PROBE, never from the presence under test: a capability that lied would
        // otherwise turn this whole run into a test of nothing happening.
        var cardTook = InputWindowProbe.WindowIsVisible(cardWindow)
            && InputWindowProbe.Foreground() == cardWindow
            && InputWindowProbe.SystemKeyboardFocus() == cardWindow;
        var overlayDuringCard = ReadOverlay();

        card.Dismiss();

        overlay.Reassert();
        var overlayAfter = ReadOverlay();

        // The differential: with WS_EX_TRANSPARENT cleared the same point must route TO the overlay.
        var opaque = overlay.Present(new OverlaySurfaceRequest(overlayBounds, 0.6, ClickThrough: false));
        var catchesItsOwn = OverlayWindowProbe.HitTest(overlayX, overlayY) == overlayWindow;
        var rePresent = overlay.Present(new OverlaySurfaceRequest(overlayBounds, 0.6, ClickThrough: true));

        return new PaintedRun(
            MachineHasInteractiveDesktop: true,
            ClipOpened: opened,
            BubblesPainted: run.BubblesShown,
            PaintedPixelsInFrame: painted,
            ShowState: show,
            FrameHeld: observation.FrameHeld,
            PictureSampled: observation.PictureSampled,
            PictureMatched: observation.PictureMatched,
            BubblePointsSampled: bubblePoints.Count,
            BubblePointsMatchingComposed: bubbleMatched,
            BubblePointsUnlikeTheDecodersColour: bubbleUnlikeDecoder,
            RenderedPixels: renderedPixels,
            CardTookTheInputAfterTheVideo: cardTook,
            CardPromptState: prompt,
            OverlayPresented: presented is CapabilityState.Available && opaque is CapabilityState.Available,
            OverlayBefore: overlayBefore,
            OverlayDuringClip: overlayDuringClip,
            OverlayDuringCard: overlayDuringCard,
            OverlayAfter: overlayAfter,
            OverlayCatchesItsOwnPointWhenMadeOpaque: catchesItsOwn,
            OverlayStillEarnsAvailable: rePresent is CapabilityState.Available,
            OverlayRePresentState: rePresent);
    }

    /// <summary>
    /// Points well inside the bubble's own disc, in the PICTURE's coordinates. Derived from the run's
    /// own geometry rather than guessed, so a change to the placement arithmetic moves these with it
    /// instead of quietly sampling background.
    /// </summary>
    private static List<(int X, int Y)> BubbleSamplePoints(BubbleCountRun run)
    {
        var points = new List<(int X, int Y)>();
        if (run.Bubbles.Count == 0)
        {
            return points;
        }

        var bubble = run.Bubbles[0];
        var centreX = (int)(bubble.CentreX * ClipWidth);
        var centreY = (int)(bubble.CentreY * ClipHeight);
        var radius = bubble.DiameterFraction * ClipWidth / 2.0;

        // Inside the rim and inside the disc: the rim is white at the edge and the fill is a
        // gradient, and both are "not the decoder's colour", which is what the count asks.
        foreach (var (dx, dy) in new[] { (0, 0), (-1, 0), (1, 0), (0, -1), (0, 1) })
        {
            var scale = radius * 0.4;
            points.Add((
                Math.Clamp(centreX + (int)(dx * scale), 0, ClipWidth - 1),
                Math.Clamp(centreY + (int)(dy * scale), 0, ClipHeight - 1)));
        }

        return points;
    }

    /// <summary>A <see cref="Random"/> whose every draw is the same number, so the bubble's position
    /// and size are derivable in the fact rather than hunted for.</summary>
    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;

        public override int Next(int maxValue) => (int)(value * maxValue);
    }
}
