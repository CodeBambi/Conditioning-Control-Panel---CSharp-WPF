using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Overlay;

namespace CcpClient.Tests;

/// <summary>
/// Runs an overlay backend through a full lifecycle and records, side by side, <b>what the backend
/// claimed</b> and <b>what the window manager independently reports</b> (via
/// <see cref="OverlayWindowProbe"/>).
///
/// <para>Keeping both in one record is what lets the facts assert at statement depth 0 — no
/// conditional, no early return, nothing that can silence an assertion. The claim and the effect
/// are separate fields precisely so a backend that returns success without putting a window on
/// screen shows up as an inequality rather than as a green test. Same shape as
/// <see cref="TrayObservations"/> (SP-093).</para>
///
/// <para>The lifecycle runs ONCE per suite execution and is cached: it puts a real window on the
/// user's real screen for the duration of a few dozen syscalls, and there is no reason to do that
/// more often than the facts need.</para>
/// </summary>
internal static class OverlayObservations
{
    private const int SurfaceWidth = 240;
    private const int SurfaceHeight = 180;
    private const double SurfaceOpacity = 0.6;

    private static readonly Lazy<LifecycleRun> LazyLifecycle =
        new(RunLifecycle, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The one real-window run every Windows fact reads.</summary>
    internal static LifecycleRun Lifecycle => LazyLifecycle.Value;

    /// <summary>
    /// Where the surface goes: the centre of the primary display, sized from the PROBE's own
    /// reading of the screen, never from the product's display enumeration. The product's
    /// enumeration is a thing under test, not an input to the test.
    /// </summary>
    internal static OverlayBounds CentreBounds
    {
        get
        {
            var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
            return new OverlayBounds(
                (screenWidth - SurfaceWidth) / 2,
                (screenHeight - SurfaceHeight) / 2,
                SurfaceWidth,
                SurfaceHeight);
        }
    }

    internal static string Describe(CapabilityState state) => state switch
    {
        CapabilityState.Available available => $"Available({available.Detail})",
        CapabilityState.Unavailable unavailable => $"Unavailable({unavailable.Reason.Code}: {unavailable.Reason.Detail})",
        _ => state.ToString() ?? "null",
    };

    /// <summary>
    /// Present, measure everything the OS can be asked, flip input routing both ways and measure
    /// the hit test each time, withdraw, dispose. One window, one pass, every fact from the same
    /// lifecycle so they cannot disagree about which run they describe.
    /// </summary>
    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation is compared against.</param>
    /// <param name="Window">The surface's handle, straight from the backend's native-handle surface.</param>
    /// <param name="ClaimedAvailableOnPresent">What the backend said.</param>
    /// <param name="BackendReportsPresenting">What the backend says its own state is.</param>
    /// <param name="OsSeesWindow">Whether the OS knows the handle at all.</param>
    /// <param name="OsSeesWindowVisible">Whether the OS reports it visible.</param>
    /// <param name="OsHeldRect">The rectangle the OS holds.</param>
    /// <param name="RequestedRect">The rectangle that was asked for.</param>
    /// <param name="OsHeldAlpha">The layered alpha the OS holds, or -1 when it holds none.</param>
    /// <param name="RequestedAlpha">The alpha that was asked for.</param>
    /// <param name="OsHeldExStyle">The extended-style read-back.</param>
    /// <param name="ZOrder">Where the OS puts the surface among visible top-level windows.</param>
    /// <param name="SurfaceIsForeground">Whether showing it took the foreground.</param>
    /// <param name="PointPassesThroughWhilePresented">
    /// Click-through as presented: the hit test at the surface's centre answers something that is
    /// NOT the surface.
    /// </param>
    /// <param name="SurfaceTakesPointWhenClickThroughOff">
    /// The other half of the differential: the same point, the same surface, click-through off,
    /// answers the surface. Without this the field above would be satisfied by a window that is
    /// not there.
    /// </param>
    /// <param name="PointPassesThroughAgainAfterRestoring">The differential closed back the other way.</param>
    /// <param name="WinnerWhileClickThrough">Who won the point while clicks pass through (diagnostic).</param>
    /// <param name="ClaimedAvailableOnClickThroughOff">The backend's claim for the opaque flip.</param>
    /// <param name="ClaimedAvailableOnClickThroughOn">The backend's claim for the restore.</param>
    /// <param name="ClaimedAvailableOnWithdraw">The backend's claim for the withdraw.</param>
    /// <param name="OsSeesWindowVisibleAfterWithdraw">Whether the OS still reports it visible.</param>
    /// <param name="BackendReportsPresentingAfterWithdraw">Whether the backend still claims a surface.</param>
    /// <param name="SurfaceTakesPointAfterWithdraw">Whether the hit test still routes to it.</param>
    /// <param name="OsSeesWindowAfterDispose">Whether a top-level window survived teardown.</param>
    /// <param name="TeardownDiagnostic">Non-null only when teardown could not complete.</param>
    /// <param name="PresentState">The full state, for failure messages.</param>
    /// <param name="ClickThroughOffState">Ditto.</param>
    /// <param name="WithdrawState">Ditto.</param>
    internal sealed record LifecycleRun(
        bool MachineHasInteractiveDesktop,
        nint Window,
        bool ClaimedAvailableOnPresent,
        bool BackendReportsPresenting,
        bool OsSeesWindow,
        bool OsSeesWindowVisible,
        (int X, int Y, int Width, int Height) OsHeldRect,
        (int X, int Y, int Width, int Height) RequestedRect,
        int OsHeldAlpha,
        int RequestedAlpha,
        uint OsHeldExStyle,
        OverlayWindowProbe.ZOrderReading ZOrder,
        bool SurfaceIsForeground,
        bool PointPassesThroughWhilePresented,
        bool SurfaceTakesPointWhenClickThroughOff,
        bool PointPassesThroughAgainAfterRestoring,
        string WinnerWhileClickThrough,
        bool ClaimedAvailableOnClickThroughOff,
        bool ClaimedAvailableOnClickThroughOn,
        bool ClaimedAvailableOnWithdraw,
        bool OsSeesWindowVisibleAfterWithdraw,
        bool BackendReportsPresentingAfterWithdraw,
        bool SurfaceTakesPointAfterWithdraw,
        bool OsSeesWindowAfterDispose,
        string? TeardownDiagnostic,
        CapabilityState PresentState,
        CapabilityState ClickThroughOffState,
        CapabilityState WithdrawState);

    private static LifecycleRun RunLifecycle()
    {
        var bounds = CentreBounds;
        var request = new OverlaySurfaceRequest(bounds, SurfaceOpacity, ClickThrough: true);
        var (centreX, centreY) = bounds.Centre;

        var presence = new Win32OverlayPresence();
        var presentState = presence.Present(request);
        var window = presence.NativeHandles.Window;
        var backendReportsPresenting = presence.IsPresenting;

        var osSeesWindow = OverlayWindowProbe.WindowExists(window);
        var osSeesVisible = OverlayWindowProbe.WindowIsVisible(window);
        var heldRect = OverlayWindowProbe.RectOf(window);
        var heldAlpha = OverlayWindowProbe.LayeredAlphaOf(window);
        var heldExStyle = OverlayWindowProbe.ExStyleOf(window);
        var zOrder = OverlayWindowProbe.ReadZOrder(window);
        var isForeground = OverlayWindowProbe.IsForeground(window);

        // Leg 1 of the differential, measured by the probe rather than the product: with the
        // surface presented click-through, the window manager must answer something else.
        var winnerWhileThrough = OverlayWindowProbe.HitTestExpecting(
            centreX, centreY, window, expectSurface: false, out _);
        var passesThrough = window != 0 && winnerWhileThrough != window;

        // Leg 2: same point, same surface, click-through off. Must answer the surface.
        var clickThroughOffState = presence.SetClickThrough(clickThrough: false);
        var winnerWhileOpaque = OverlayWindowProbe.HitTestExpecting(
            centreX, centreY, window, expectSurface: true, out _);
        var takesPoint = window != 0 && winnerWhileOpaque == window;

        // Leg 3: restore, and the point must go away again. Closing the differential both ways is
        // what makes it a routing fact rather than a coincidence of who happened to be on top.
        var clickThroughOnState = presence.SetClickThrough(clickThrough: true);
        var winnerAfterRestore = OverlayWindowProbe.HitTestExpecting(
            centreX, centreY, window, expectSurface: false, out _);
        var passesThroughAgain = window != 0 && winnerAfterRestore != window;

        var withdrawState = presence.Withdraw();
        var visibleAfterWithdraw = OverlayWindowProbe.WindowIsVisible(window);
        var presentingAfterWithdraw = presence.IsPresenting;
        var takesPointAfterWithdraw = window != 0 && OverlayWindowProbe.HitTest(centreX, centreY) == window;

        presence.Dispose();
        var existsAfterDispose = OverlayWindowProbe.WindowExists(window);

        return new LifecycleRun(
            MachineHasInteractiveDesktop: OverlayWindowProbe.MachineHasInteractiveDesktop,
            Window: window,
            ClaimedAvailableOnPresent: presentState is CapabilityState.Available,
            BackendReportsPresenting: backendReportsPresenting,
            OsSeesWindow: osSeesWindow,
            OsSeesWindowVisible: osSeesVisible,
            OsHeldRect: heldRect,
            RequestedRect: (bounds.X, bounds.Y, bounds.Width, bounds.Height),
            OsHeldAlpha: heldAlpha,
            RequestedAlpha: request.Alpha,
            OsHeldExStyle: heldExStyle,
            ZOrder: zOrder,
            SurfaceIsForeground: isForeground,
            PointPassesThroughWhilePresented: passesThrough,
            SurfaceTakesPointWhenClickThroughOff: takesPoint,
            PointPassesThroughAgainAfterRestoring: passesThroughAgain,
            WinnerWhileClickThrough: OverlayWindowProbe.DescribeWindow(winnerWhileThrough),
            ClaimedAvailableOnClickThroughOff: clickThroughOffState is CapabilityState.Available,
            ClaimedAvailableOnClickThroughOn: clickThroughOnState is CapabilityState.Available,
            ClaimedAvailableOnWithdraw: withdrawState is CapabilityState.Available,
            OsSeesWindowVisibleAfterWithdraw: visibleAfterWithdraw,
            BackendReportsPresentingAfterWithdraw: presentingAfterWithdraw,
            SurfaceTakesPointAfterWithdraw: takesPointAfterWithdraw,
            OsSeesWindowAfterDispose: existsAfterDispose,
            TeardownDiagnostic: presence.TeardownDiagnostic,
            PresentState: presentState,
            ClickThroughOffState: clickThroughOffState,
            WithdrawState: withdrawState);
    }

    /// <summary>
    /// The refusal path, driven on this Windows box: the Linux backend is reachable from either OS,
    /// which is what makes the honest asymmetry testable rather than hidden.
    /// </summary>
    /// <param name="ClaimedAvailable">Must never be true.</param>
    /// <param name="ReportsPresenting">Must never be true.</param>
    /// <param name="Code">The reason code.</param>
    /// <param name="Detail">The reason detail, which must carry the gate.</param>
    /// <param name="WithdrawAlsoRefused">A refusal that only covers Present is half a refusal.</param>
    /// <param name="ClickThroughAlsoRefused">Ditto.</param>
    internal sealed record RefusalRun(
        bool ClaimedAvailable,
        bool ReportsPresenting,
        string Code,
        string Detail,
        bool WithdrawAlsoRefused,
        bool ClickThroughAlsoRefused);

    internal static RefusalRun RefusalFor(OverlayHostPlatform platform)
    {
        using var presence = OverlayPresenceFactory.CreateFor(platform);
        var present = presence.Present(new OverlaySurfaceRequest(new OverlayBounds(0, 0, 100, 100), 1.0, true));
        var reason = present is CapabilityState.Unavailable unavailable
            ? unavailable.Reason
            : new CapabilityReason("no-refusal", Describe(present));

        return new RefusalRun(
            ClaimedAvailable: present is CapabilityState.Available,
            ReportsPresenting: presence.IsPresenting,
            Code: reason.Code,
            Detail: reason.Detail,
            WithdrawAlsoRefused: presence.Withdraw() is CapabilityState.Unavailable,
            ClickThroughAlsoRefused: presence.SetClickThrough(true) is CapabilityState.Unavailable);
    }

    /// <summary>
    /// What a presence that has never presented anything says when asked to change or withdraw a
    /// surface. "Nothing happened" must not read as success.
    /// </summary>
    /// <param name="WithdrawCode">Reason code from Withdraw.</param>
    /// <param name="ClickThroughCode">Reason code from SetClickThrough.</param>
    /// <param name="WithdrawClaimedAvailable">Must be false.</param>
    /// <param name="ClickThroughClaimedAvailable">Must be false.</param>
    /// <param name="DisposedPresentCode">Reason code from Present after Dispose.</param>
    /// <param name="DisposedPresentClaimedAvailable">Must be false.</param>
    internal sealed record IdleRun(
        string WithdrawCode,
        string ClickThroughCode,
        bool WithdrawClaimedAvailable,
        bool ClickThroughClaimedAvailable,
        string DisposedPresentCode,
        bool DisposedPresentClaimedAvailable);

    internal static IdleRun NothingPresented()
    {
        var presence = new Win32OverlayPresence();
        var withdraw = presence.Withdraw();
        var clickThrough = presence.SetClickThrough(true);

        presence.Dispose();
        var afterDispose = presence.Present(new OverlaySurfaceRequest(CentreBounds, 0.5, true));

        return new IdleRun(
            WithdrawCode: CodeOf(withdraw),
            ClickThroughCode: CodeOf(clickThrough),
            WithdrawClaimedAvailable: withdraw is CapabilityState.Available,
            ClickThroughClaimedAvailable: clickThrough is CapabilityState.Available,
            DisposedPresentCode: CodeOf(afterDispose),
            DisposedPresentClaimedAvailable: afterDispose is CapabilityState.Available);
    }

    private static string CodeOf(CapabilityState state) =>
        state is CapabilityState.Unavailable unavailable ? unavailable.Reason.Code : "no-refusal";
}
