using System.Runtime.InteropServices;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Glyph;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Pointer;
using CcpClient.Desktop.Video;

namespace CcpClient.Tests;

/// <summary>
/// <b>Brings the port's native surfaces up on the real desktop, tears the application down through
/// the product's own single teardown entry point, and then asks the OPERATING SYSTEM whether
/// anything of ours survived.</b>
///
/// <para><b>The invariant, and why it had no instrument.</b> <c>overlay-clickthrough</c>'s safety
/// list carries two lines nothing measured: <i>"No failure leaves an invisible input-blocking or
/// permanently topmost surface"</i> and <i>"Teardown and display/window transitions restore normal
/// desktop input"</i>. Two of the port's native surfaces are deliberately NOT click-through and
/// they are the dangerous ones — <c>Pointer/Win32PointerSurface.cs:850-852</c> is
/// <c>WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW</c> with <c>WS_EX_TRANSPARENT</c> deliberately absent
/// because a poppable bubble must RECEIVE clicks, and <c>Input/Win32InputPresence.cs:1097-1099</c>
/// is <c>WS_EX_TOOLWINDOW</c> alone, taking the foreground AND the keyboard. Strand either and the
/// user's desktop eats their clicks, or their keyboard, with no visible window to close.</para>
///
/// <para><b>Why this reads the OS and not the styles.</b> A source guard over the extended styles
/// is the wrong tool twice over. <c>overlay-clickthrough/SKILL.md:48</c> forbids assuming those
/// styles are the approved design, and <c>Overlay/Win32OverlayPresence.cs:504-511</c> records a
/// MEASURED run where every style write returned success and the ex-style still read back wrong
/// because another process had stripped topmost. Styles are a claim; the z-order and the hit test
/// are the answer. So every reading below comes from <see cref="Os"/>, which only interrogates the
/// window manager — it creates nothing, raises nothing and clicks nothing.</para>
///
/// <para><b>The run carries its own negative control.</b> A teardown fact that passes because
/// nothing was ever shown is the vacuous case, so the run first proves all five surfaces really
/// reached the desktop, and then tears down TWICE: once with the pointer surface deliberately
/// withheld from the participant — a leak, the exact failure the invariant forbids, which must show
/// a survivor — and once with it restored, which is where the invariant is asserted. Same role, and
/// the same reason, as
/// <c>PointerCoexistenceTests.ALLFOURSurfacesReallyReachedTheDesktop_OrEveryReadingBelowIsATestOfNothingHappening</c>
/// and the overlay's own opaque differential.</para>
///
/// <para><b>The sixth surface is named, not covered.</b> <c>Tray/Win32TrayPresence</c>'s owner
/// window is created never-visible and never joins the z-order, so it can win no hit test and is
/// neither input-blocking nor topmost — it is outside the invariant's wording, and the window walk
/// below (which counts only <c>IsWindowVisible</c> windows) would never see it either way.</para>
/// </summary>
internal static class SurfaceTeardownObservations
{
    private const uint Fill = 0x00201020;
    private const uint Ink = 0x00E0C0FF;
    private const int PointerSide = 160;
    private const int OverlayWidth = 200;
    private const int OverlayHeight = 150;

    private static readonly Lazy<TeardownRun> LazyTeardown =
        new(RunTeardown, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The one run. It puts five real always-on-top windows on the user's screen and takes
    /// the foreground, so it happens once per suite execution and is cached.</summary>
    internal static TeardownRun Teardown => LazyTeardown.Value;

    internal static string Describe(CapabilityState? state) => state switch
    {
        null => "(nothing was asked)",
        CapabilityState.Available available => $"Available({available.Detail})",
        CapabilityState.Degraded degraded => $"Degraded({degraded.Reason.Code}: {degraded.Reason.Detail})",
        CapabilityState.Unavailable unavailable =>
            $"Unavailable({unavailable.Reason.Code}: {unavailable.Reason.Detail})",
        _ => state.ToString() ?? "null",
    };

    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation is compared
    /// against.</param>
    /// <param name="BaselineOurVisibleWindows">How many visible top-level windows this PROCESS
    /// already owned before the run placed anything. It is the subtrahend for every count below, so
    /// a survivor from some earlier fixture cannot be scored against this one — and it is asserted
    /// to be zero in its own right, because a suite that reaches this point already holding a
    /// visible window has itself stranded one.</param>
    /// <param name="OverlayPresentState">What the overlay's own <c>Present</c> returned.</param>
    /// <param name="GlyphPresentState">And the glyph surface's.</param>
    /// <param name="VideoShowState">And the video surface's <c>Show</c>.</param>
    /// <param name="PointerOpenState">And the pointer surface's <c>Open</c>.</param>
    /// <param name="OverlayVisibleBefore">The OS reports the overlay's own handle visible.</param>
    /// <param name="GlyphVisibleBefore">The glyph surface's.</param>
    /// <param name="PointerVisibleBefore">The pointer target's.</param>
    /// <param name="CardVisibleBefore">The card's.</param>
    /// <param name="PointerOwnsItsPointBefore"><b>The dangerous surface, proved dangerous:</b> the
    /// window manager routes the pointer target's own centre TO the pointer target. It is not
    /// click-through by design, so before teardown it really is eating that point.</param>
    /// <param name="CardHeldForegroundAndKeyboardBefore"><b>The other dangerous surface:</b> the
    /// card really holds the two things the OS lends to exactly one window at a time.</param>
    /// <param name="OurVisibleWindowsBefore">Every visible top-level window this process owns with
    /// all five up. Above the baseline it must contain the four named handles plus at least one
    /// more — the video surface, which exposes no handle accessor and is therefore found by
    /// difference rather than by asking it.</param>
    /// <param name="UnnamedNewWindowsBefore">That "at least one more", counted.</param>
    /// <param name="TopmostOfOursBefore">How many of our new visible windows the OS reports holding
    /// <c>WS_EX_TOPMOST</c>. Non-zero is the "permanently topmost" hazard being armed.</param>
    /// <param name="LeakShutdownCompletedSynchronously">The leaking host's <c>ShutdownAsync</c>
    /// finished on the calling thread. A native window belongs to the thread that made it, so a
    /// teardown that resumed elsewhere would fail <c>DestroyWindow</c> for the HARNESS's reason and
    /// this run would report a product defect that is not one.</param>
    /// <param name="LeakStopThread">The managed thread the leaking participant's <c>StopAsync</c>
    /// actually ran on.</param>
    /// <param name="CreatingThread">The managed thread that created every window.</param>
    /// <param name="OurVisibleWindowsAfterLeak">What the OS still shows after a teardown that
    /// deliberately forgot one surface.</param>
    /// <param name="LeakedPointerStillAWindow"><b>The negative control.</b> The withheld surface's
    /// window is still a window.</param>
    /// <param name="LeakedPointerStillVisible">And still visible.</param>
    /// <param name="LeakedPointerStillTopmost">And still in the topmost band.</param>
    /// <param name="LeakedPointerStillEatsItsPoint">And the window manager still routes its centre
    /// to it — with nothing of ours left on screen to explain it. <b>This is the stranded
    /// input-blocking surface the invariant forbids, constructed on purpose so the instrument is
    /// proved able to say NO.</b></param>
    /// <param name="RestoreShutdownCompletedSynchronously">The restoring host's, same reason.</param>
    /// <param name="RestoreStopThread">And its stop thread.</param>
    /// <param name="OurVisibleWindowsAfterRestore"><b>The invariant.</b> Every visible top-level
    /// window this process owns once the leak is repaired and the application is fully down.</param>
    /// <param name="OverlayHandleGone">The OS no longer knows the overlay's handle.</param>
    /// <param name="GlyphHandleGone">Nor the glyph surface's.</param>
    /// <param name="PointerHandleGone">Nor the pointer target's.</param>
    /// <param name="CardHandleGone">Nor the card's.</param>
    /// <param name="PointerPointOwnerAfter">Who owns the pointer target's old centre now.</param>
    /// <param name="PointerPointRoutesAwayFromUs"><b>The other half of "restores normal desktop
    /// input":</b> that point's owner belongs to another process. A handle-existence check alone
    /// would pass on a window that was destroyed and instantly re-created; this asks the routing
    /// question the user's mouse asks.</param>
    /// <param name="CardPointRoutesAwayFromUs">Same question at the card's old centre.</param>
    /// <param name="OverlayPointRoutesAwayFromUs">And at the overlay's.</param>
    /// <param name="GlyphPointRoutesAwayFromUs">And at the glyph surface's.</param>
    /// <param name="VideoPointRoutesAwayFromUs">And at the video surface's — the one whose handle
    /// this run never learned, so the routing question is the ONLY question that can be asked
    /// about it.</param>
    /// <param name="ForegroundOwnerAfter">Which process holds the foreground now.</param>
    /// <param name="ForegroundReturnedToTheDesktop">It is not this one. The card took the keyboard;
    /// teardown had to give it back.</param>
    /// <param name="TeardownDiagnostics">Anything any surface could not clean up, joined. Empty is
    /// the expectation; a non-empty string is the surface's own words about its own failure and is
    /// worth more in a failure message than any assertion this file could write.</param>
    internal sealed record TeardownRun(
        bool MachineHasInteractiveDesktop,
        int BaselineOurVisibleWindows,
        CapabilityState OverlayPresentState,
        CapabilityState GlyphPresentState,
        CapabilityState VideoShowState,
        CapabilityState PointerOpenState,
        bool OverlayVisibleBefore,
        bool GlyphVisibleBefore,
        bool PointerVisibleBefore,
        bool CardVisibleBefore,
        bool PointerOwnsItsPointBefore,
        bool CardHeldForegroundAndKeyboardBefore,
        int OurVisibleWindowsBefore,
        int UnnamedNewWindowsBefore,
        int TopmostOfOursBefore,
        bool LeakShutdownCompletedSynchronously,
        int LeakStopThread,
        int CreatingThread,
        int OurVisibleWindowsAfterLeak,
        bool LeakedPointerStillAWindow,
        bool LeakedPointerStillVisible,
        bool LeakedPointerStillTopmost,
        bool LeakedPointerStillEatsItsPoint,
        bool RestoreShutdownCompletedSynchronously,
        int RestoreStopThread,
        int OurVisibleWindowsAfterRestore,
        string SurvivorsAfterRestore,
        bool OverlayHandleGone,
        bool GlyphHandleGone,
        bool PointerHandleGone,
        bool CardHandleGone,
        string PointerPointOwnerAfter,
        bool PointerPointRoutesAwayFromUs,
        bool CardPointRoutesAwayFromUs,
        bool OverlayPointRoutesAwayFromUs,
        bool GlyphPointRoutesAwayFromUs,
        bool VideoPointRoutesAwayFromUs,
        string ForegroundOwnerAfter,
        bool ForegroundReturnedToTheDesktop,
        string TeardownDiagnostics);

    private static TeardownRun RunTeardown()
    {
        var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
        var creatingThread = Environment.CurrentManagedThreadId;

        // What this PROCESS already had on the desktop. Everything below is measured against it, so
        // an earlier fixture's survivor is never scored against this run.
        var baseline = Os.OurVisibleWindows();

        // ---- five surfaces, five disjoint rectangles ----
        // Disjoint on purpose: each surface's own hit-test point must never be occluded by another,
        // or a reading about one would be a reading about its neighbour. The rectangles are the ones
        // the landed coexistence runs already use (PointerSurfaceObservations.RunCoexistence,
        // GlyphSurfaceObservations.RunCoexistence), so this run is a strict extension of an
        // arrangement that is already known to fit on one screen.
        var overlayBounds = new OverlayBounds(
            Math.Max(0, (screenWidth / 2) - OverlayWidth - 460),
            Math.Max(0, (screenHeight / 2) - OverlayHeight),
            OverlayWidth,
            OverlayHeight);
        var (overlayX, overlayY) = overlayBounds.Centre;

        var overlay = new Win32OverlayPresence();
        var overlayPresent = overlay.Present(new OverlaySurfaceRequest(overlayBounds, 0.6, ClickThrough: true));
        var overlayWindow = overlay.NativeHandles.Window;

        var glyphBounds = new GlyphBounds(
            Math.Max(0, (screenWidth / 2) - 660),
            Math.Max(0, (screenHeight / 2) + 60),
            GlyphSurfaceObservations.SurfaceSide,
            GlyphSurfaceObservations.SurfaceSide);
        var (glyphX, glyphY) = glyphBounds.Centre;

        var glyph = new Win32GlyphSurface();
        var glyphPresent = glyph.Present(
            new GlyphSurfaceRequest(glyphBounds, 1.0, ClickThrough: true),
            GlyphSurfaceObservations.Quadrants(GlyphSurfaceObservations.SurfaceSide));
        var glyphWindow = glyph.NativeHandles.Window;

        var videoBounds = new VideoBounds(
            Math.Max(0, (screenWidth / 2) - 200),
            Math.Max(0, (screenHeight / 2) - 340),
            VideoSurfaceObservations.SurfaceWidth,
            VideoSurfaceObservations.SurfaceHeight);
        var (videoX, videoY) = videoBounds.Centre;

        var clipPath = VideoSurfaceObservations.WriteFixtureClip("surface-teardown.avi");
        var clipSource = VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows);
        clipSource.Open(clipPath, out var clip);
        var videoFrame = clip?.ReadFrame();
        var video = new Win32VideoPresence(clipSource);
        video.Present(new VideoSurfaceRequest(videoBounds, VideoSurfaceObservations.Letterbox));
        var videoShow = videoFrame is null
            ? new CapabilityState.Unavailable(new CapabilityReason("(no frame)", "nothing decoded"))
            : video.Show(videoFrame);

        // The first dangerous surface: NOT click-through, by design (Pointer/Win32PointerSurface.cs:850-852).
        var pointerBounds = new PointerBounds(
            Math.Max(0, (screenWidth / 2) + 260),
            Math.Max(0, (screenHeight / 2) - 320),
            PointerSide,
            PointerSide);
        var (pointerX, pointerY) = pointerBounds.Centre;

        var pointer = new Win32PointerSurface();
        var pointerOpen = pointer.Open(new PointerTargetRequest(pointerBounds, Fill, Ink), out var target);
        var pointerWindow = pointer.NativeHandlesFor(target).Window;

        // The second: it takes the foreground AND the keyboard (Input/Win32InputPresence.cs:1097-1099).
        var cardBounds = new InputBounds(
            Math.Max(0, (screenWidth / 2) + 200),
            Math.Max(0, (screenHeight / 2) + 160),
            360,
            180);
        var (cardX, cardY) = cardBounds.Centre;

        var card = new Win32InputPresence();
        card.Prompt(new InputPromptRequest(
            cardBounds,
            new InputPromptContent("say this", "1 of 1", string.Empty, "Press Esc to close"),
            _ => { }));
        var cardWindow = card.NativeHandles.Window;

        // ---- the positive control: all five really reached the desktop ----
        // Read WHILE they are up, and read through the OS rather than through the capabilities:
        // a capability that lied about its own state would otherwise turn the whole run into a test
        // of nothing happening.
        var named = new[] { overlayWindow, glyphWindow, pointerWindow, cardWindow };
        var newBefore = Os.OurVisibleWindows().Except(baseline).ToArray();

        // Every one of these is a question about a LIVE window, so it is asked here and not folded
        // into the record at the bottom: the run destroys all five further down, and asking the OS
        // afterwards would get a truthful "no" about a window that no longer exists. Measured, not
        // reasoned — the topmost count was written that way first and came back 0 out of 5.
        var topmostOfOursBefore = newBefore.Count(Os.IsTopmost);
        var overlayVisibleBefore = Os.IsVisibleWindow(overlayWindow);
        var glyphVisibleBefore = Os.IsVisibleWindow(glyphWindow);
        var pointerVisibleBefore = Os.IsVisibleWindow(pointerWindow);
        var cardVisibleBefore = Os.IsVisibleWindow(cardWindow);

        // Not "the hit test happens to name it": the pointer target is raised into the top of the
        // topmost band first, exactly as PointerWindowProbe does everywhere else, so the reading is
        // about this surface's input policy and not about who else is contesting the band.
        var pointerOwnsItsPointBefore =
            PointerWindowProbe.HitTestAfterRaising(pointerWindow, pointerX, pointerY) == pointerWindow;
        var cardHeldTheInputBefore = InputWindowProbe.Foreground() == cardWindow
            && InputWindowProbe.SystemKeyboardFocus() == cardWindow;

        // ---- PHASE B: the leak ----
        // The application tears down through its ONE guarded entry point (Lifecycle/ApplicationHost.cs,
        // ShutdownAsync), over a participant that disposes the surfaces in the order
        // Session/SessionParticipant.cs:884-940 disposes them — drawing surfaces, then video, then
        // pointer, then input. Here the participant is handed FOUR of the five: the pointer surface
        // is deliberately withheld, which is the leak. Nothing about the product is modified to
        // produce it; the surface simply never reaches an owner, which is exactly how a real one
        // would be stranded.
        var log = new ListLog();
        var leaking = new SurfaceParticipant("surfaces (pointer withheld)", overlay, glyph, video, input: card);
        var leakShutdown = new ApplicationHost(log, [leaking], new StartupTrace()).ShutdownAsync();

        var afterLeak = Os.OurVisibleWindows().Except(baseline).ToArray();
        var leakedStillAWindow = Os.IsWindowHandle(pointerWindow);
        var leakedStillVisible = Os.IsVisibleWindow(pointerWindow);
        var leakedStillTopmost = Os.IsTopmost(pointerWindow);

        // No raising here. With every other surface of ours gone the target has to win its own
        // centre unaided, which is exactly the state a user would be left in.
        var leakedStillEatsItsPoint = Os.HitTest(pointerX, pointerY) == pointerWindow;

        // ---- PHASE C: the leak repaired, and the invariant read off the window manager ----
        var repairing = new SurfaceParticipant("surfaces", pointer: pointer);
        var restoreShutdown = new ApplicationHost(log, [repairing], new StartupTrace()).ShutdownAsync();

        var afterRestore = Os.OurVisibleWindows().Except(baseline).ToArray();
        clip?.Dispose();

        var diagnostics = new[]
            {
                overlay.TeardownDiagnostic, glyph.TeardownDiagnostic,
                pointer.TeardownDiagnostic, card.TeardownDiagnostic,
            }
            .Where(d => !string.IsNullOrEmpty(d))
            .Select(d => d!)
            .ToArray();

        return new TeardownRun(
            MachineHasInteractiveDesktop: OverlayWindowProbe.MachineHasInteractiveDesktop,
            BaselineOurVisibleWindows: baseline.Length,
            OverlayPresentState: overlayPresent,
            GlyphPresentState: glyphPresent,
            VideoShowState: videoShow,
            PointerOpenState: pointerOpen,
            OverlayVisibleBefore: overlayVisibleBefore,
            GlyphVisibleBefore: glyphVisibleBefore,
            PointerVisibleBefore: pointerVisibleBefore,
            CardVisibleBefore: cardVisibleBefore,
            PointerOwnsItsPointBefore: pointerOwnsItsPointBefore,
            CardHeldForegroundAndKeyboardBefore: cardHeldTheInputBefore,
            OurVisibleWindowsBefore: newBefore.Length,
            UnnamedNewWindowsBefore: newBefore.Count(w => !named.Contains(w)),
            TopmostOfOursBefore: topmostOfOursBefore,
            LeakShutdownCompletedSynchronously: leakShutdown.IsCompleted,
            LeakStopThread: leaking.StoppedOnThread,
            CreatingThread: creatingThread,
            OurVisibleWindowsAfterLeak: afterLeak.Length,
            LeakedPointerStillAWindow: leakedStillAWindow,
            LeakedPointerStillVisible: leakedStillVisible,
            LeakedPointerStillTopmost: leakedStillTopmost,
            LeakedPointerStillEatsItsPoint: leakedStillEatsItsPoint,
            RestoreShutdownCompletedSynchronously: restoreShutdown.IsCompleted,
            RestoreStopThread: repairing.StoppedOnThread,
            OurVisibleWindowsAfterRestore: afterRestore.Length,
            SurvivorsAfterRestore: afterRestore.Length == 0
                ? "(none)"
                : string.Join("; ", afterRestore.Select(Os.Describe)),
            OverlayHandleGone: !Os.IsWindowHandle(overlayWindow),
            GlyphHandleGone: !Os.IsWindowHandle(glyphWindow),
            PointerHandleGone: !Os.IsWindowHandle(pointerWindow),
            CardHandleGone: !Os.IsWindowHandle(cardWindow),
            PointerPointOwnerAfter: Os.Describe(Os.HitTest(pointerX, pointerY)),
            PointerPointRoutesAwayFromUs: Os.RoutesAwayFromUs(pointerX, pointerY),
            CardPointRoutesAwayFromUs: Os.RoutesAwayFromUs(cardX, cardY),
            OverlayPointRoutesAwayFromUs: Os.RoutesAwayFromUs(overlayX, overlayY),
            GlyphPointRoutesAwayFromUs: Os.RoutesAwayFromUs(glyphX, glyphY),
            VideoPointRoutesAwayFromUs: Os.RoutesAwayFromUs(videoX, videoY),
            ForegroundOwnerAfter: Os.Describe(Os.Foreground()),
            ForegroundReturnedToTheDesktop: !Os.IsOurs(Os.Foreground()),
            TeardownDiagnostics: diagnostics.Length == 0 ? "(none)" : string.Join("; ", diagnostics));
    }

    /// <summary>
    /// The surface-disposal half of <c>Session/SessionParticipant.cs:884-940</c>, in that method's
    /// order and with its reasons.
    ///
    /// <para><b>What it deliberately does NOT reproduce.</b> That method's first act is
    /// <c>Engine.Stop()</c>, which disarms every module and is what takes a live surface off the
    /// screen before anything is disposed; reproducing it would require the whole composition root,
    /// and it is not what this fact is about. What this fact is about is the state AFTER the last
    /// disposal, and every path through that method — stop, window close, panic — ends in the same
    /// <c>Dispose</c> calls in the same order.</para>
    ///
    /// <para><b>And why the disposals are inline rather than posted.</b> <c>DisposeSurface</c> posts
    /// through the UI dispatch boundary when it is bound and disposes inline when it is not
    /// (<c>SessionParticipant.cs:1012-1021</c>), and both branches exist for ONE reason: a native
    /// window belongs to the thread that created it, so the disposal has to reach that thread. In
    /// the product that thread is the Avalonia UI thread and the post is how it is reached; here the
    /// creating thread is the fact's own, so the inline branch reaches it directly. The run records
    /// <c>StoppedOnThread</c> and the fact asserts it equals the creating thread, so a harness that
    /// tore down from the wrong thread reds as itself instead of impersonating a product defect.</para>
    /// </summary>
    private sealed class SurfaceParticipant(
        string name,
        Win32OverlayPresence? overlay = null,
        Win32GlyphSurface? glyph = null,
        Win32VideoPresence? video = null,
        Win32PointerSurface? pointer = null,
        Win32InputPresence? input = null) : IBackgroundParticipant
    {
        public string Name { get; } = name;

        public bool Running { get; private set; }

        /// <summary>The managed thread the disposals actually ran on.</summary>
        public int StoppedOnThread { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Running = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StoppedOnThread = Environment.CurrentManagedThreadId;
            Running = false;

            overlay?.Dispose();
            glyph?.Dispose();
            video?.Dispose();
            pointer?.Dispose();
            input?.Dispose();

            return Task.CompletedTask;
        }
    }

    private sealed class ListLog : ILogSink
    {
        public List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    /// <summary>
    /// <b>The only instrument in this file, and it can only ask questions.</b>
    ///
    /// <para>It creates no window, raises nothing, moves nothing and synthesises no input — every
    /// entry point is a read of the window manager's own state. That matters for what the run
    /// claims: the surfaces are placed by the PRODUCT and removed by the PRODUCT, and this class is
    /// only the second opinion. It also matters mechanically — a file carrying
    /// <c>CreateWindowExW</c> or <c>GetDC(0)</c> is a real-desktop helper that
    /// <c>RealDesktopCollectionGuardTests</c> requires to be named, and this one is not that.</para>
    ///
    /// <para><b>Why per-process enumeration rather than per-handle checks.</b> Four of the five
    /// surfaces expose a native handle; <c>Win32VideoPresence</c> exposes none. A run that could
    /// only ask about handles it had been given would be blind to exactly the surface it was not
    /// given — and blind to any SECOND window a surface owns. Walking the z-order and keeping the
    /// visible windows whose owning process is this one answers "is anything of ours left" without
    /// needing anyone's cooperation.</para>
    /// </summary>
    private static class Os
    {
        private const int GwlExstyle = -20;
        private const uint GwHwndnext = 2;
        private const uint WsExTopmost = 0x00000008;

        private static bool WindowsHost => OperatingSystem.IsWindows();

        internal static nint[] OurVisibleWindows()
        {
            if (!WindowsHost)
            {
                return [];
            }

            var ours = new List<nint>();
            for (var candidate = GetTopWindow(0); candidate != 0; candidate = GetWindow(candidate, GwHwndnext))
            {
                if (IsWindowVisible(candidate) && IsOurs(candidate))
                {
                    ours.Add(candidate);
                }
            }

            return [.. ours];
        }

        internal static bool IsWindowHandle(nint window) => WindowsHost && window != 0 && IsWindow(window);

        internal static bool IsVisibleWindow(nint window) =>
            WindowsHost && window != 0 && IsWindowVisible(window);

        internal static bool IsTopmost(nint window) =>
            WindowsHost && window != 0 && ((uint)GetWindowLongPtrW(window, GwlExstyle) & WsExTopmost) != 0;

        internal static nint HitTest(int x, int y) =>
            WindowsHost ? WindowFromPoint(new Point { X = x, Y = y }) : 0;

        internal static nint Foreground() => WindowsHost ? GetForegroundWindow() : 0;

        internal static bool IsOurs(nint window)
        {
            if (!WindowsHost || window == 0)
            {
                return false;
            }

            _ = GetWindowThreadProcessId(window, out var processId);
            return processId == Environment.ProcessId;
        }

        /// <summary>
        /// The routing question the user's mouse asks: the point resolves to a window, and that
        /// window is not ours. Both halves are load-bearing — an answer of 0 would mean the point
        /// resolves to nothing at all, which on a live desktop is not "the desktop got its input
        /// back", it is a reading taken off-screen.
        /// </summary>
        internal static bool RoutesAwayFromUs(int x, int y)
        {
            var owner = HitTest(x, y);
            return owner != 0 && !IsOurs(owner);
        }

        internal static string Describe(nint window)
        {
            if (!WindowsHost || window == 0)
            {
                return "(no window)";
            }

            var className = new System.Text.StringBuilder(128);
            _ = GetClassNameW(window, className, className.Capacity);
            _ = GetWindowThreadProcessId(window, out var processId);
            var rect = GetWindowRect(window, out var bounds)
                ? $"{bounds.Left},{bounds.Top} {bounds.Right - bounds.Left}x{bounds.Bottom - bounds.Top}"
                : "(no rect)";
            var mine = processId == Environment.ProcessId ? " OURS" : string.Empty;
            var topmost = IsTopmost(window) ? " TOPMOST" : string.Empty;
            return $"0x{window:X} class '{className}' pid {processId}{mine}{topmost} at {rect}";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")] private static extern nint GetTopWindow(nint window);

        [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);

        [DllImport("user32.dll")] private static extern bool IsWindow(nint window);

        [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);

        [DllImport("user32.dll")] private static extern nint GetForegroundWindow();

        [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);

        [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);

        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

        [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(nint window, System.Text.StringBuilder text, int count);
    }
}
