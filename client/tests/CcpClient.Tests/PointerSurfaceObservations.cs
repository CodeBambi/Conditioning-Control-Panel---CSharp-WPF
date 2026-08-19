using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Pointer;
using CcpClient.Desktop.Video;

namespace CcpClient.Tests;

/// <summary>
/// Runs the real pointer surface on the real desktop and records, side by side, <b>what the surface
/// claimed</b> and <b>what the operating system independently reports</b> (through
/// <see cref="PointerWindowProbe"/>, whose P/Invokes are a second copy of the product's).
///
/// <para>Keeping both in one record is what lets every fact assert at statement depth 0 — no
/// conditional, no early return, nothing that can silence an assertion. Same shape and same lineage
/// as <see cref="OverlayObservations"/> (SP-099), <see cref="InputCaptureObservations"/> (SP-110)
/// and <see cref="VideoSurfaceObservations"/> (SP-111).</para>
///
/// <para><b>Each run happens ONCE per suite execution and is cached.</b> These runs put real
/// always-on-top windows on the user's screen and SYNTHESISE MOUSE CLICKS at them, and there is no
/// reason to do that more often than the facts need.</para>
///
/// <para><b>Every injected click is aimed at a window this run owns, and the negative legs are
/// aimed at a probe-owned catcher rather than at the bare desktop.</b> That is not politeness: a
/// click injected at a point where nothing of ours is listening lands on whatever the user really
/// has there. So a click is injected only when the OS has ALREADY answered that the point routes to
/// one of our windows, and the "it did not arrive" legs prove the click went to the probe's own
/// scratch target instead of proving it vanished.</para>
/// </summary>
internal static class PointerSurfaceObservations
{
    /// <summary>The target's side. Comfortably above the capability's own 60 px minimum and above the
    /// widest one-step displacement the race legs apply.</summary>
    internal const int TargetSide = 160;

    private const uint Fill = 0x00201020;
    private const uint Ink = 0x00E0C0FF;

    private static readonly Lazy<DeliveryRun> LazyDelivery =
        new(RunDelivery, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<RaceRun> LazyRace =
        new(RunRace, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<CoexistenceRun> LazyCoexistence =
        new(RunCoexistence, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The one run that measures a click arriving while the foreground does not move.</summary>
    internal static DeliveryRun Delivery => LazyDelivery.Value;

    /// <summary>The one run that measures the moving-target race and its bound.</summary>
    internal static RaceRun Race => LazyRace.Value;

    /// <summary>The one run that proves the three landed surfaces survive this one.</summary>
    internal static CoexistenceRun Coexistence => LazyCoexistence.Value;

    internal static string Describe(CapabilityState? state) => state switch
    {
        null => "(nothing was asked)",
        CapabilityState.Available available => $"Available({available.Detail})",
        CapabilityState.Degraded degraded => $"Degraded({degraded.Reason.Code}: {degraded.Reason.Detail})",
        CapabilityState.Unavailable unavailable =>
            $"Unavailable({unavailable.Reason.Code}: {unavailable.Reason.Detail})",
        _ => state.ToString() ?? "null",
    };

    // ---------------------------------------------------------------------------------------
    //  THE CENTRAL RUN — a click that arrives while the foreground is unchanged
    // ---------------------------------------------------------------------------------------

    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation is compared
    /// against.</param>
    /// <param name="ProbeControlCreated">The probe built its own non-activating scratch target — the
    /// instrument's own control. Without this leg the whole run is a test of nothing happening.</param>
    /// <param name="ProbeControlClickInjected"><c>SendInput</c> was accepted for the control click.
    /// False means UIPI, the secure desktop or a locked workstation, which is a machine fact.</param>
    /// <param name="ProbeControlSawDown">The control target's OWN window procedure received
    /// <c>WM_LBUTTONDOWN</c>. This is the oracle: if the instrument cannot see a click arrive at a
    /// window it built itself, it cannot see one arrive at the product's.</param>
    /// <param name="ProbeControlSawUp">And <c>WM_LBUTTONUP</c>.</param>
    /// <param name="ProbeControlRefusedActivation">The control answered <c>WM_MOUSEACTIVATE</c> with
    /// <c>MA_NOACTIVATE</c> at least once.</param>
    /// <param name="ProbeControlForegroundHeld">The foreground did not move across the control's own
    /// click either — so "the foreground did not move" is a property of the MECHANISM and not an
    /// accident of the product's rectangle.</param>
    /// <param name="OpenState">What <see cref="IPointerSurface.Open"/> returned.</param>
    /// <param name="Window">The target's hwnd, straight from the surface's native-handle surface.</param>
    /// <param name="OsSeesWindow">The OS knows the handle.</param>
    /// <param name="OsSeesWindowVisible">The OS reports it visible.</param>
    /// <param name="OsHeldBounds">The rectangle the OS holds for it.</param>
    /// <param name="AskedBounds">The rectangle that was asked for.</param>
    /// <param name="OsHoldsNonActivatingStyle"><c>WS_EX_NOACTIVATE</c>, read back out of the OS.</param>
    /// <param name="OsHoldsClickThroughStyle"><c>WS_EX_TRANSPARENT</c>, which must be false.</param>
    /// <param name="OsPutsItAboveEveryOrdinaryWindow">The OS's own z-order walk.</param>
    /// <param name="HitTestWinner">What <c>WindowFromPoint</c> at the target's centre returned.</param>
    /// <param name="ForegroundBeforeOpen">The foreground before anything was placed.</param>
    /// <param name="ForegroundAfterOpen">And after. <b>The non-activation claim is these two.</b></param>
    /// <param name="ClickInjected"><c>SendInput</c> was accepted for the product click.</param>
    /// <param name="ForegroundBeforeClick">The foreground immediately before the click.</param>
    /// <param name="ForegroundAfterClick">And immediately after. <b>This pair is the fact worth
    /// having</b> when read beside <paramref name="PressesAfterClick"/>.</param>
    /// <param name="PressesBeforeClick">The surface's own delivered-message count before.</param>
    /// <param name="PressesAfterClick">And after.</param>
    /// <param name="DownsSeen">How many <c>WM_LBUTTONDOWN</c> the module-facing callback received.</param>
    /// <param name="UpsSeen">How many <c>WM_LBUTTONUP</c>.</param>
    /// <param name="PressedTarget">Which target handle the callback named — the OS chose it.</param>
    /// <param name="OpenedTarget">The handle that was opened, to compare against.</param>
    /// <param name="ActivationsRefusedBefore">The <c>MA_NOACTIVATE</c> count before the click.</param>
    /// <param name="ActivationsRefusedAfter">And after. A non-zero difference is the window procedure
    /// refusing activation WITH A CLICK IN ITS HAND, which no style read-back can show.</param>
    /// <param name="DecoyCreated">The probe put a catcher UNDER the point for the negative leg.</param>
    /// <param name="NegativeClickInjected">The negative click was accepted by the OS.</param>
    /// <param name="DecoySawDown">The decoy caught it — so the click really happened and really
    /// landed. <b>Without this the negative leg is satisfied by an injection that never occurred.</b></param>
    /// <param name="PressesAfterNegativeClick">The surface's count after the closed target's point was
    /// clicked again. It must equal <paramref name="PressesAfterClick"/>.</param>
    /// <param name="UnlistenedClickInjected">A click was injected with NO press listener attached.</param>
    /// <param name="PressesAfterUnlistenedClick">The delivered-message count after it — the OS still
    /// delivered, so the count still moved.</param>
    /// <param name="PressesDropped">And how many the surface COUNTED as dropped. A press is an event
    /// in time; replaying it later would be a lie about when it happened, so it is counted rather
    /// than queued, and a silently discarded press is the other way to lose a defect.</param>
    /// <param name="StyleWasCleared">The harness cleared <c>WS_EX_NOACTIVATE</c> on the target's own
    /// window from OUTSIDE the capability — the only way to construct that state deterministically.</param>
    /// <param name="MoveAfterStyleCleared">What the next <c>Move</c> returned once the OS no longer
    /// held the style. <b>This is the branch that proves the read-back is a read-back.</b></param>
    /// <param name="CloseState">What <see cref="IPointerSurface.Close"/> returned.</param>
    /// <param name="HitTestAfterClose">Where the OS routes the old point once the target is gone.</param>
    /// <param name="ForegroundAfterClose">The foreground after the take-down.</param>
    /// <param name="WindowExistsAfterDispose">The surface's window survived its own teardown — the
    /// window-existence check's ability to say "no".</param>
    /// <param name="TeardownDiagnostic">Anything the surface could not clean up.</param>
    internal sealed record DeliveryRun(
        bool MachineHasInteractiveDesktop,
        bool ProbeControlCreated,
        bool ProbeControlClickInjected,
        bool ProbeControlSawDown,
        bool ProbeControlSawUp,
        bool ProbeControlRefusedActivation,
        bool ProbeControlForegroundHeld,
        CapabilityState OpenState,
        nint Window,
        bool OsSeesWindow,
        bool OsSeesWindowVisible,
        (int X, int Y, int Width, int Height) OsHeldBounds,
        (int X, int Y, int Width, int Height) AskedBounds,
        bool OsHoldsNonActivatingStyle,
        bool OsHoldsClickThroughStyle,
        bool OsPutsItAboveEveryOrdinaryWindow,
        nint HitTestWinner,
        nint ForegroundBeforeOpen,
        nint ForegroundAfterOpen,
        bool ClickInjected,
        nint ForegroundBeforeClick,
        nint ForegroundAfterClick,
        int PressesBeforeClick,
        int PressesAfterClick,
        int DownsSeen,
        int UpsSeen,
        int PressedTarget,
        int OpenedTarget,
        int ActivationsRefusedBefore,
        int ActivationsRefusedAfter,
        bool DecoyCreated,
        bool NegativeClickInjected,
        bool DecoySawDown,
        int PressesAfterNegativeClick,
        bool UnlistenedClickInjected,
        int PressesAfterUnlistenedClick,
        int PressesDropped,
        bool StyleWasCleared,
        CapabilityState MoveAfterStyleCleared,
        CapabilityState CloseState,
        nint HitTestAfterClose,
        nint ForegroundAfterClose,
        bool WindowExistsAfterDispose,
        string? TeardownDiagnostic);

    private static DeliveryRun RunDelivery()
    {
        var (screenWidth, screenHeight) = PointerWindowProbe.PrimarySize;
        var restore = PointerWindowProbe.CursorPosition();

        // ---- the instrument's own control, before the product is touched at all ----
        using var control = PointerWindowProbe.ScratchTarget.Create(
            Math.Max(0, (screenWidth / 2) - 480), Math.Max(0, (screenHeight / 2) - 300), TargetSide, TargetSide);

        var controlWindow = control?.Window ?? 0;
        var controlCentre = (X: Math.Max(0, (screenWidth / 2) - 480) + (TargetSide / 2),
                             Y: Math.Max(0, (screenHeight / 2) - 300) + (TargetSide / 2));
        var controlRoutes = controlWindow != 0
            && PointerWindowProbe.HitTestAfterRaising(controlWindow, controlCentre.X, controlCentre.Y)
                == controlWindow;
        var controlForegroundBefore = PointerWindowProbe.Foreground();
        var controlInjected = controlRoutes && PointerWindowProbe.InjectClickAt(controlCentre.X, controlCentre.Y);
        PointerWindowProbe.PumpUntil(() => control is { Downs: > 0, Ups: > 0 });
        var controlForegroundAfter = PointerWindowProbe.Foreground();

        // ---- the product ----
        var askedBounds = new PointerBounds(
            Math.Max(0, (screenWidth / 2) - 240), Math.Max(0, (screenHeight / 2) - 300), TargetSide, TargetSide);
        var (centreX, centreY) = askedBounds.Centre;

        var presses = new List<PointerPress>();
        var surface = new Win32PointerSurface { OnPress = presses.Add };

        var foregroundBeforeOpen = PointerWindowProbe.Foreground();
        var openState = surface.Open(new PointerTargetRequest(askedBounds, Fill, Ink), out var target);
        var foregroundAfterOpen = PointerWindowProbe.Foreground();
        var window = surface.NativeHandlesFor(target).Window;

        // Read WHILE THE TARGET IS UP. Every one of these is a question about a live window, and
        // the run closes and disposes the surface further down: taking them at the end would be
        // asking the OS about a window that no longer exists and getting a truthful "no".
        var zOrder = PointerWindowProbe.ReadZOrder(window);
        var hitWinner = PointerWindowProbe.HitTestAfterRaising(window, centreX, centreY);
        var osSeesWindow = PointerWindowProbe.WindowExists(window);
        var osSeesVisible = PointerWindowProbe.WindowIsVisible(window);
        var osHeldBounds = PointerWindowProbe.BoundsOf(window);
        var osHoldsNonActivating = PointerWindowProbe.NonActivatingStyleHeld(window);
        var osHoldsClickThrough = PointerWindowProbe.ClickThroughStyleHeld(window);

        var pressesBefore = surface.PressesSeen;
        var refusedBefore = surface.MouseActivateRefusals;
        var foregroundBeforeClick = PointerWindowProbe.Foreground();

        // Only injected once the OS has ALREADY said this point routes to our window: a click at a
        // point that is not ours lands on whatever the user really has there.
        var injected = hitWinner == window && window != 0
            && PointerWindowProbe.HitTestAfterRaising(window, centreX, centreY) == window
            && PointerWindowProbe.InjectClickAt(centreX, centreY);
        surface.Pump(256);
        PointerWindowProbe.PumpUntil(() =>
        {
            surface.Pump(64);
            return presses.Count(p => p.Kind == PointerPressKind.Down) > 0
                && presses.Count(p => p.Kind == PointerPressKind.Up) > 0;
        });

        var foregroundAfterClick = PointerWindowProbe.Foreground();
        var pressesAfter = surface.PressesSeen;
        var refusedAfter = surface.MouseActivateRefusals;

        // ---- a press nobody is listening for is still COUNTED ----
        surface.OnPress = null;
        var unlistenedInjected = window != 0
            && PointerWindowProbe.HitTestAfterRaising(window, centreX, centreY) == window
            && PointerWindowProbe.InjectClickAt(centreX, centreY);
        surface.Pump(256);
        PointerWindowProbe.PumpUntil(() =>
        {
            surface.Pump(64);
            return surface.PressesSeen >= pressesAfter + 2;
        });

        var pressesAfterUnlistened = surface.PressesSeen;
        var pressesDropped = surface.PressesDropped;

        // ---- the style the OS holds is CLEARED from outside, and the next Move must catch it ----
        var styleCleared = PointerWindowProbe.ClearNonActivatingStyle(window);
        var moveAfterStyleCleared = surface.Move(target, askedBounds);

        // ---- the negative leg: a DECOY under the point, so the click lands somewhere we own ----
        var closeState = surface.Close(target);
        var hitAfterClose = PointerWindowProbe.HitTest(centreX, centreY);
        var foregroundAfterClose = PointerWindowProbe.Foreground();

        using var decoy = PointerWindowProbe.ScratchTarget.Create(
            askedBounds.X, askedBounds.Y, TargetSide, TargetSide);
        var decoyWindow = decoy?.Window ?? 0;
        var decoyRoutes = decoyWindow != 0
            && PointerWindowProbe.HitTestAfterRaising(decoyWindow, centreX, centreY) == decoyWindow;
        var negativeInjected = decoyRoutes && PointerWindowProbe.InjectClickAt(centreX, centreY);
        PointerWindowProbe.PumpUntil(() => decoy is { Downs: > 0 });
        surface.Pump(256);
        PointerWindowProbe.Drain();
        surface.Pump(256);
        var pressesAfterNegative = surface.PressesSeen;

        surface.Dispose();
        var existsAfterDispose = PointerWindowProbe.WindowExists(window);
        var teardown = surface.TeardownDiagnostic;

        PointerWindowProbe.MovePointerTo(restore.X, restore.Y);

        return new DeliveryRun(
            MachineHasInteractiveDesktop: PointerWindowProbe.MachineHasInteractiveDesktop,
            ProbeControlCreated: controlWindow != 0,
            ProbeControlClickInjected: controlInjected,
            ProbeControlSawDown: (control?.Downs ?? 0) > 0,
            ProbeControlSawUp: (control?.Ups ?? 0) > 0,
            ProbeControlRefusedActivation: (control?.ActivationsRefused ?? 0) > 0,
            ProbeControlForegroundHeld: controlForegroundBefore == controlForegroundAfter
                && controlForegroundAfter != controlWindow,
            OpenState: openState,
            Window: window,
            OsSeesWindow: osSeesWindow,
            OsSeesWindowVisible: osSeesVisible,
            OsHeldBounds: osHeldBounds,
            AskedBounds: (askedBounds.X, askedBounds.Y, askedBounds.Width, askedBounds.Height),
            OsHoldsNonActivatingStyle: osHoldsNonActivating,
            OsHoldsClickThroughStyle: osHoldsClickThrough,
            OsPutsItAboveEveryOrdinaryWindow: zOrder.AboveEveryOrdinaryWindow,
            HitTestWinner: hitWinner,
            ForegroundBeforeOpen: foregroundBeforeOpen,
            ForegroundAfterOpen: foregroundAfterOpen,
            ClickInjected: injected,
            ForegroundBeforeClick: foregroundBeforeClick,
            ForegroundAfterClick: foregroundAfterClick,
            PressesBeforeClick: pressesBefore,
            PressesAfterClick: pressesAfter,
            DownsSeen: presses.Count(p => p.Kind == PointerPressKind.Down),
            UpsSeen: presses.Count(p => p.Kind == PointerPressKind.Up),
            PressedTarget: presses.Count > 0 ? presses[0].Target : 0,
            OpenedTarget: target,
            ActivationsRefusedBefore: refusedBefore,
            ActivationsRefusedAfter: refusedAfter,
            DecoyCreated: decoyWindow != 0,
            NegativeClickInjected: negativeInjected,
            DecoySawDown: (decoy?.Downs ?? 0) > 0,
            PressesAfterNegativeClick: pressesAfterNegative,
            UnlistenedClickInjected: unlistenedInjected,
            PressesAfterUnlistenedClick: pressesAfterUnlistened,
            PressesDropped: pressesDropped,
            StyleWasCleared: styleCleared,
            MoveAfterStyleCleared: moveAfterStyleCleared,
            CloseState: closeState,
            HitTestAfterClose: hitAfterClose,
            ForegroundAfterClose: foregroundAfterClose,
            WindowExistsAfterDispose: existsAfterDispose,
            TeardownDiagnostic: teardown);
    }

    // ---------------------------------------------------------------------------------------
    //  THE RACE — measured, not argued
    // ---------------------------------------------------------------------------------------

    /// <param name="MachineHasInteractiveDesktop">The machine fact.</param>
    /// <param name="OpenState">The target was really placed before anything moved.</param>
    /// <param name="StepPixels">The displacement applied for the SMALL move: one worst-case animation
    /// step, ceiled, taken from the module's own constants.</param>
    /// <param name="Radius">Half the target's side, for comparison.</param>
    /// <param name="SmallMoveState">What <see cref="IPointerSurface.Move"/> returned for it.</param>
    /// <param name="SmallMoveClickInjected">The click after the small move was accepted by the OS.</param>
    /// <param name="DownsAfterSmallMove">Presses the callback received after clicking the ORIGINAL
    /// point once the target had moved one step. <b>This is the bound, exercised.</b></param>
    /// <param name="TargetAfterSmallMove">Which target the press named.</param>
    /// <param name="BigMovePixels">The displacement applied for the LARGE move: more than a full
    /// side, so the original point is outside the target.</param>
    /// <param name="BigMoveState">What <c>Move</c> returned for it.</param>
    /// <param name="DecoyCreated">A probe-owned catcher was placed at the original point.</param>
    /// <param name="BigMoveClickInjected">The click after the large move was accepted.</param>
    /// <param name="DecoySawDown">The decoy caught it — so the click really happened.</param>
    /// <param name="DownsAfterBigMove">Presses the callback received. It must not have moved:
    /// <b>without this leg the small-move fact is also true of a window that never moved.</b></param>
    /// <param name="BoundHoldsArithmetically">The module's own constants still satisfy the
    /// inequality the whole argument rests on.</param>
    internal sealed record RaceRun(
        bool MachineHasInteractiveDesktop,
        CapabilityState OpenState,
        int StepPixels,
        int Radius,
        CapabilityState SmallMoveState,
        bool SmallMoveClickInjected,
        int DownsAfterSmallMove,
        int TargetAfterSmallMove,
        int BigMovePixels,
        CapabilityState BigMoveState,
        bool DecoyCreated,
        bool BigMoveClickInjected,
        bool DecoySawDown,
        int DownsAfterBigMove,
        bool BoundHoldsArithmetically);

    private static RaceRun RunRace()
    {
        var (screenWidth, screenHeight) = PointerWindowProbe.PrimarySize;
        var restore = PointerWindowProbe.CursorPosition();

        var step = (int)Math.Ceiling(CcpClient.Desktop.Effects.BubblePopField.MaxStepDisplacement);
        var bounds = new PointerBounds(
            Math.Max(0, (screenWidth / 2) - 240), Math.Max(0, (screenHeight / 2) + 40), TargetSide, TargetSide);
        var (centreX, centreY) = bounds.Centre;

        var presses = new List<PointerPress>();
        var surface = new Win32PointerSurface { OnPress = presses.Add };
        var openState = surface.Open(new PointerTargetRequest(bounds, Fill, Ink), out var target);
        var window = surface.NativeHandlesFor(target).Window;

        // The routing answer is taken HERE, at the centre, and then the target MOVES. Everything
        // below is about whether the answer survives the move.
        var routesBeforeMove = window != 0
            && PointerWindowProbe.HitTestAfterRaising(window, centreX, centreY) == window;

        // --- leg 1: one worst-case step. The original point must still be this target's. ---
        var smallMove = surface.Move(target, bounds with { X = bounds.X + step, Y = bounds.Y - step });
        var smallInjected = routesBeforeMove
            && PointerWindowProbe.HitTestAfterRaising(window, centreX, centreY) == window
            && PointerWindowProbe.InjectClickAt(centreX, centreY);
        surface.Pump(256);
        PointerWindowProbe.PumpUntil(() =>
        {
            surface.Pump(64);
            return presses.Count(p => p.Kind == PointerPressKind.Down) > 0;
        });

        var downsAfterSmall = presses.Count(p => p.Kind == PointerPressKind.Down);
        var targetAfterSmall = presses.Count > 0 ? presses[0].Target : 0;

        // --- leg 2: further than a whole side. The original point must NOT be this target's. ---
        var big = TargetSide + 40;
        var bigMove = surface.Move(target, bounds with { X = bounds.X + big, Y = bounds.Y - big });

        using var decoy = PointerWindowProbe.ScratchTarget.Create(bounds.X, bounds.Y, TargetSide, TargetSide);
        var decoyWindow = decoy?.Window ?? 0;
        var decoyRoutes = decoyWindow != 0
            && PointerWindowProbe.HitTestAfterRaising(decoyWindow, centreX, centreY) == decoyWindow;
        var bigInjected = decoyRoutes && PointerWindowProbe.InjectClickAt(centreX, centreY);
        PointerWindowProbe.PumpUntil(() => decoy is { Downs: > 0 });
        surface.Pump(256);
        PointerWindowProbe.Drain();
        surface.Pump(256);

        var downsAfterBig = presses.Count(p => p.Kind == PointerPressKind.Down);

        surface.Close(target);
        surface.Dispose();
        PointerWindowProbe.MovePointerTo(restore.X, restore.Y);

        return new RaceRun(
            MachineHasInteractiveDesktop: PointerWindowProbe.MachineHasInteractiveDesktop,
            OpenState: openState,
            StepPixels: step,
            Radius: bounds.Radius,
            SmallMoveState: smallMove,
            SmallMoveClickInjected: smallInjected,
            DownsAfterSmallMove: downsAfterSmall,
            TargetAfterSmallMove: targetAfterSmall,
            BigMovePixels: big,
            BigMoveState: bigMove,
            DecoyCreated: decoyWindow != 0,
            BigMoveClickInjected: bigInjected,
            DecoySawDown: (decoy?.Downs ?? 0) > 0,
            DownsAfterBigMove: downsAfterBig,
            BoundHoldsArithmetically:
                CcpClient.Desktop.Effects.BubblePopField.OneStepNeverCarriesABubbleOffItsOwnCentre);
    }

    // ---------------------------------------------------------------------------------------
    //  FOUR SURFACES, ONE DESKTOP
    // ---------------------------------------------------------------------------------------

    /// <summary>The overlay's state at one moment, read entirely through
    /// <see cref="OverlayWindowProbe"/> — SP-099's own instrument, unmodified.</summary>
    internal readonly record struct OverlayReading(
        bool PointPassesThrough,
        bool AboveEveryOrdinaryWindow,
        int Alpha,
        bool TransparentStyleHeld,
        bool IsForeground);

    /// <summary>The card's state at one moment, read entirely through
    /// <see cref="InputWindowProbe"/> — SP-110's own instrument, unmodified.</summary>
    internal readonly record struct CardReading(bool Visible, bool IsForeground, bool HoldsSystemKeyboardFocus);

    /// <param name="MachineHasInteractiveDesktop">The machine fact.</param>
    /// <param name="OverlayPresented">The real overlay claimed Available before anything else existed.</param>
    /// <param name="CardTookTheInput">The real card really took the foreground and the keyboard —
    /// without this leg every reading below is a test of nothing happening.</param>
    /// <param name="VideoShowedAPicture">The real video surface really held a decoded picture.</param>
    /// <param name="OverlayBefore">The overlay before any pointer target existed.</param>
    /// <param name="OverlayDuringOpen">While a target was up.</param>
    /// <param name="OverlayDuringMove">While it was being moved.</param>
    /// <param name="OverlayAfter">After it was closed and the surface disposed.</param>
    /// <param name="CardBefore">The card before any pointer target existed.</param>
    /// <param name="CardDuringOpen"><b>The trap-2 fact:</b> a pointer target must not take the
    /// foreground or the keyboard from a card that holds both.</param>
    /// <param name="CardDuringMove">And it must not take them on a MOVE either, which is the
    /// operation <c>Input/**</c> does not have and therefore never had to survive.</param>
    /// <param name="CardAfter">After the pointer surface came down.</param>
    /// <param name="VideoHeldPictureDuring">The video surface's own read-back still confirms its
    /// picture while a pointer target is up beside it.</param>
    /// <param name="VideoShowStateDuring">That state, for failure messages.</param>
    /// <param name="PointerEarnedAvailableBesideThem">The pointer surface's own Open earned Available
    /// while all three of the others were up — so this is coexistence rather than the new surface
    /// failing politely.</param>
    /// <param name="PointerOpenState">That state, for failure messages.</param>
    /// <param name="PointerMoveState">And the move's.</param>
    /// <param name="OverlayCatchesItsOwnPointWhenMadeOpaque">The overlay's own differential, run
    /// after the pointer surface is gone.</param>
    /// <param name="OverlayStillEarnsAvailable">The overlay capability's own oracle, re-asked.</param>
    /// <param name="OverlayRePresentState">That state, for failure messages.</param>
    internal sealed record CoexistenceRun(
        bool MachineHasInteractiveDesktop,
        bool OverlayPresented,
        bool CardTookTheInput,
        bool VideoShowedAPicture,
        OverlayReading OverlayBefore,
        OverlayReading OverlayDuringOpen,
        OverlayReading OverlayDuringMove,
        OverlayReading OverlayAfter,
        CardReading CardBefore,
        CardReading CardDuringOpen,
        CardReading CardDuringMove,
        CardReading CardAfter,
        bool VideoHeldPictureDuring,
        CapabilityState VideoShowStateDuring,
        bool PointerEarnedAvailableBesideThem,
        CapabilityState PointerOpenState,
        CapabilityState PointerMoveState,
        bool OverlayCatchesItsOwnPointWhenMadeOpaque,
        bool OverlayStillEarnsAvailable,
        CapabilityState OverlayRePresentState);

    private static CoexistenceRun RunCoexistence()
    {
        var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
        const int overlayWidth = 200;
        const int overlayHeight = 150;

        // Every rectangle below is DISJOINT from every other on purpose: each surface's own
        // hit-test point must never be occluded by another, or a reading about one would be a
        // reading about its neighbour.
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

        var cardBounds = new InputBounds(
            Math.Max(0, (screenWidth / 2) + 200),
            Math.Max(0, (screenHeight / 2) + 160),
            360,
            180);
        using var card = new Win32InputPresence();
        card.Prompt(new InputPromptRequest(
            cardBounds,
            new InputPromptContent("say this", "1 of 1", string.Empty, "Press Esc to close"),
            _ => { }));
        var cardWindow = card.NativeHandles.Window;

        // Sourced from the PROBE, never from the capability under test's sibling: a capability that
        // lied would otherwise turn this whole run into a test of nothing happening.
        CardReading ReadCard() => new(
            Visible: InputWindowProbe.WindowIsVisible(cardWindow),
            IsForeground: InputWindowProbe.Foreground() == cardWindow,
            HoldsSystemKeyboardFocus: InputWindowProbe.SystemKeyboardFocus() == cardWindow);

        var cardTookTheInput = ReadCard() is { Visible: true, IsForeground: true, HoldsSystemKeyboardFocus: true };

        // A real video surface, holding a real decoded picture, for the whole run.
        var path = VideoSurfaceObservations.WriteFixtureClip("pointer-coexistence.avi");
        var source = VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows);
        source.Open(path, out var clip);
        var frame = clip?.ReadFrame();
        var videoBounds = new VideoBounds(
            Math.Max(0, (screenWidth / 2) - 200),
            Math.Max(0, (screenHeight / 2) - 340),
            VideoSurfaceObservations.SurfaceWidth,
            VideoSurfaceObservations.SurfaceHeight);
        var video = new Win32VideoPresence(source);
        video.Present(new VideoSurfaceRequest(videoBounds, VideoSurfaceObservations.Letterbox));
        var firstShow = frame is null
            ? new CapabilityState.Unavailable(new CapabilityReason("(no frame)", "nothing decoded"))
            : video.Show(frame);

        var overlayBefore = ReadOverlay();
        var cardBefore = ReadCard();

        // The subject: a real pointer target, opened, moved and closed beside all three.
        var pointerBounds = new PointerBounds(
            Math.Max(0, (screenWidth / 2) + 260),
            Math.Max(0, (screenHeight / 2) - 320),
            TargetSide,
            TargetSide);
        var pointer = new Win32PointerSurface();
        var openState = pointer.Open(new PointerTargetRequest(pointerBounds, Fill, Ink), out var target);

        var overlayDuringOpen = ReadOverlay();
        var cardDuringOpen = ReadCard();

        var moveState = pointer.Move(target, pointerBounds with { Y = pointerBounds.Y + 24 });
        var overlayDuringMove = ReadOverlay();
        var cardDuringMove = ReadCard();

        // The video surface's own oracle, re-asked while a pointer target is up beside it.
        var showDuring = frame is null
            ? new CapabilityState.Unavailable(new CapabilityReason("(no frame)", "nothing decoded"))
            : video.Show(frame);

        pointer.Close(target);
        pointer.Dispose();

        video.Withdraw();
        video.Dispose();
        clip?.Dispose();

        // The overlay's own re-assertion, exactly as a live session would (WPF re-raises its topmost
        // windows on a cadence, Services/Flash/FlashService.cs:206-243). Asking it is not helping the
        // overlay pass; it is what the product already does.
        overlay.Reassert();
        var overlayAfter = ReadOverlay();
        var cardAfter = ReadCard();

        card.Dismiss();

        // The differential, taken on the OVERLAY itself: it proves this specific surface WOULD have
        // caught the point, so "the point went elsewhere" cannot be satisfied by an overlay that
        // quietly stopped existing.
        overlay.SetClickThrough(false);
        var overlayCatchesItsOwnPoint = OverlayWindowProbe.HitTest(overlayX, overlayY) == overlayWindow;
        overlay.SetClickThrough(true);

        var rePresent = overlay.Present(new OverlaySurfaceRequest(overlayBounds, 0.6, ClickThrough: true));

        return new CoexistenceRun(
            MachineHasInteractiveDesktop: OverlayWindowProbe.MachineHasInteractiveDesktop,
            OverlayPresented: presented is CapabilityState.Available,
            CardTookTheInput: cardTookTheInput,
            VideoShowedAPicture: firstShow is CapabilityState.Available,
            OverlayBefore: overlayBefore,
            OverlayDuringOpen: overlayDuringOpen,
            OverlayDuringMove: overlayDuringMove,
            OverlayAfter: overlayAfter,
            CardBefore: cardBefore,
            CardDuringOpen: cardDuringOpen,
            CardDuringMove: cardDuringMove,
            CardAfter: cardAfter,
            VideoHeldPictureDuring: showDuring is CapabilityState.Available,
            VideoShowStateDuring: showDuring,
            PointerEarnedAvailableBesideThem: openState is CapabilityState.Available,
            PointerOpenState: openState,
            PointerMoveState: moveState,
            OverlayCatchesItsOwnPointWhenMadeOpaque: overlayCatchesItsOwnPoint,
            OverlayStillEarnsAvailable: rePresent is CapabilityState.Available,
            OverlayRePresentState: rePresent);
    }
}
