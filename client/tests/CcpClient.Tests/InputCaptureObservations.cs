using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Overlay;

namespace CcpClient.Tests;

/// <summary>
/// Runs the real input presence through a full lifecycle and records, side by side, <b>what the
/// presence claimed</b> and <b>what the operating system independently reports</b> (via
/// <see cref="InputWindowProbe"/>, whose P/Invokes are a second copy of the product's).
///
/// <para>Keeping both in one record is what lets the facts assert at statement depth 0 — no
/// conditional, no early return, nothing that can silence an assertion. Same shape and same lineage
/// as <see cref="OverlayObservations"/> (SP-099) and <see cref="TrayObservations"/> (SP-093).</para>
///
/// <para>Each run happens ONCE per suite execution and is cached: it puts a real window on the
/// user's real screen and takes the real foreground for the duration of a few dozen syscalls, and
/// there is no reason to do that more often than the facts need.</para>
/// </summary>
internal static class InputCaptureObservations
{
    private const int CardWidth = 460;
    private const int CardHeight = 280;

    private const string Question = "GOOD GIRLS OBEY";

    private static readonly Lazy<LifecycleRun> LazyLifecycle =
        new(RunLifecycle, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<CoexistenceRun> LazyCoexistence =
        new(RunCoexistence, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The one real-card run every Windows capture fact reads.</summary>
    internal static LifecycleRun Lifecycle => LazyLifecycle.Value;

    /// <summary>The one run that proves the overlay survives a card opening and closing.</summary>
    internal static CoexistenceRun Coexistence => LazyCoexistence.Value;

    /// <summary>
    /// Where the card goes: the centre of the primary display, sized from the PROBE's own reading of
    /// the screen, never from the product's enumeration — the product's enumeration is a thing under
    /// test, not an input to the test.
    /// </summary>
    internal static InputBounds CentreBounds
    {
        get
        {
            var (screenWidth, screenHeight) = InputWindowProbe.PrimarySize;
            return new InputBounds(
                (screenWidth - CardWidth) / 2,
                (screenHeight - CardHeight) / 2,
                CardWidth,
                CardHeight);
        }
    }

    internal static string Describe(CapabilityState state) => state switch
    {
        CapabilityState.Available available => $"Available({available.Detail})",
        CapabilityState.Degraded degraded => $"Degraded({degraded.Reason.Code}: {degraded.Reason.Detail})",
        CapabilityState.Unavailable unavailable => $"Unavailable({unavailable.Reason.Code}: {unavailable.Reason.Detail})",
        _ => state.ToString() ?? "null",
    };

    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation is compared against.</param>
    /// <param name="Window">The card's handle, straight from the presence's native-handle surface.</param>
    /// <param name="ClaimedAvailableOnPrompt">What the presence said.</param>
    /// <param name="PresenceReportsPrompting">What the presence says its own state is.</param>
    /// <param name="OsSeesWindow">Whether the OS knows the handle at all.</param>
    /// <param name="OsSeesWindowVisible">Whether the OS reports it visible.</param>
    /// <param name="ZOrder">Where the OS puts the card among visible top-level windows.</param>
    /// <param name="OsSaysCardIsForeground"><c>GetForegroundWindow()</c> is the card.</param>
    /// <param name="OsSaysSystemFocusIsCard"><c>GetGUIThreadInfo(0).hwndFocus</c> is the card — the
    /// system-wide read, taken by the PROBE rather than by the product.</param>
    /// <param name="HitTestWinnerWhilePrompting">Who the window manager routes the card's centre to.</param>
    /// <param name="ForegroundWinner">Who the OS calls the foreground window (diagnostic).</param>
    /// <param name="KeyInjected">Whether the OS accepted a synthesised <c>VK_F13</c>.</param>
    /// <param name="KeyReachedTheCard">Whether that keystroke reached the card's own window procedure.</param>
    /// <param name="TypedCharacterReachedTheModel">Whether an injected <c>VK_A</c> arrived at the
    /// caller's keystroke callback as the character <c>'a'</c> — the whole path, OS to product.</param>
    /// <param name="InkedPixels">Non-background pixels the OS holds in the card's question band.</param>
    /// <param name="SampledPixels">How many were sampled (the ink check's own negative control).</param>
    /// <param name="EscapeReachedTheModel">Whether an injected <c>VK_ESCAPE</c> arrived as a Cancel.</param>
    /// <param name="ClaimedAvailableOnUpdate">The presence's claim for a repaint.</param>
    /// <param name="ClaimedAvailableOnDismiss">The presence's claim for taking the card down.</param>
    /// <param name="OsSeesWindowVisibleAfterDismiss">Whether the OS still reports it visible.</param>
    /// <param name="CardStillForegroundAfterDismiss">Whether the OS still points the keyboard at it.</param>
    /// <param name="HitTestWinnerAfterDismiss">Whether the hit test still routes to it.</param>
    /// <param name="PresenceReportsPromptingAfterDismiss">Whether the presence still claims a card.</param>
    /// <param name="KeyAfterDismissReachedTheModel">Whether a keystroke injected after the dismissal
    /// still reached the caller — a callback the presence forgot to drop.</param>
    /// <param name="OsSeesWindowAfterDispose">Whether a top-level window survived teardown.</param>
    /// <param name="TeardownDiagnostic">Non-null only when teardown could not complete.</param>
    /// <param name="CallbackFaults">How many caller callbacks threw and were contained.</param>
    /// <param name="PromptState">The full state, for failure messages.</param>
    /// <param name="UpdateState">Ditto.</param>
    /// <param name="DismissState">Ditto.</param>
    internal sealed record LifecycleRun(
        bool MachineHasInteractiveDesktop,
        nint Window,
        bool ClaimedAvailableOnPrompt,
        bool PresenceReportsPrompting,
        bool OsSeesWindow,
        bool OsSeesWindowVisible,
        InputWindowProbe.ZOrderReading ZOrder,
        bool OsSaysCardIsForeground,
        bool OsSaysSystemFocusIsCard,
        nint HitTestWinnerWhilePrompting,
        string ForegroundWinner,
        bool KeyInjected,
        bool KeyReachedTheCard,
        bool TypedCharacterReachedTheModel,
        int InkedPixels,
        int SampledPixels,
        bool EscapeReachedTheModel,
        bool ClaimedAvailableOnUpdate,
        bool ClaimedAvailableOnDismiss,
        bool OsSeesWindowVisibleAfterDismiss,
        bool CardStillForegroundAfterDismiss,
        nint HitTestWinnerAfterDismiss,
        bool PresenceReportsPromptingAfterDismiss,
        bool KeyAfterDismissReachedTheModel,
        bool OsSeesWindowAfterDispose,
        string? TeardownDiagnostic,
        int CallbackFaults,
        CapabilityState PromptState,
        CapabilityState UpdateState,
        CapabilityState DismissState);

    private static LifecycleRun RunLifecycle()
    {
        var bounds = CentreBounds;
        var (centreX, centreY) = bounds.Centre;
        var keystrokes = new List<InputKeystroke>();

        var presence = new Win32InputPresence();
        var request = new InputPromptRequest(
            bounds,
            new InputPromptContent(Question, "1 of 3", string.Empty, "Press Esc to close"),
            keystrokes.Add);

        var promptState = presence.Prompt(request);
        var window = presence.NativeHandles.Window;
        var presenceReportsPrompting = presence.IsPrompting;
        var observation = presence.LastObservation;

        var osSeesWindow = InputWindowProbe.WindowExists(window);
        var osSeesVisible = InputWindowProbe.WindowIsVisible(window);
        var zOrder = InputWindowProbe.ReadZOrder(window);
        var osSaysForeground = InputWindowProbe.Foreground() == window && window != 0;
        var osSaysSystemFocus = InputWindowProbe.SystemKeyboardFocus() == window && window != 0;
        var hitWinner = InputWindowProbe.HitTest(centreX, centreY);
        var foregroundWinner = InputWindowProbe.DescribeWindow(InputWindowProbe.Foreground());

        // THE DELIVERY LEG. VK_F13 first: it produces no character, so a keystroke that escaped to
        // another window could not type anything into it.
        var keysBefore = presence.LastObservation.KeystrokesSeen;
        var injected = InputWindowProbe.InjectKey(InputWindowProbe.VkF13);
        var reachedCard = InputWindowProbe.PumpUntil(
            () => { presence.Pump(64); return presence.Observe().KeystrokesSeen > keysBefore; });

        // The CHARACTER leg, gated on the OS genuinely holding the card as focus so a leaked key
        // cannot be typed into somebody else's window.
        var characterReached = false;
        if (osSaysForeground && osSaysSystemFocus)
        {
            InputWindowProbe.InjectKey(InputWindowProbe.VkA);
            characterReached = InputWindowProbe.PumpUntil(() =>
            {
                presence.Pump(64);
                return keystrokes.Any(k => k.Kind == InputKeystrokeKind.Character && k.Character == 'a');
            });
        }

        var updateState = presence.Update(
            new InputPromptContent(Question, "2 of 3", "GOOD GIRLS", "Press Esc to close"));
        var inkObservation = presence.Observe();

        // The cancel leg: VK_ESCAPE must arrive as a Cancel keystroke, because that is the ONE key
        // whose meaning the module cannot get from a character.
        var escapeReached = false;
        if (osSaysForeground && osSaysSystemFocus)
        {
            InputWindowProbe.InjectKey(0x1B);
            escapeReached = InputWindowProbe.PumpUntil(() =>
            {
                presence.Pump(64);
                return keystrokes.Any(k => k.Kind == InputKeystrokeKind.Cancel);
            });
        }

        var dismissState = presence.Dismiss();
        var osSeesVisibleAfter = InputWindowProbe.WindowIsVisible(window);
        var stillForeground = InputWindowProbe.Foreground() == window && window != 0;
        var hitAfter = InputWindowProbe.HitTest(centreX, centreY);
        var promptingAfter = presence.IsPrompting;

        // A dismissed card must not still be feeding the caller.
        var countAfterDismiss = keystrokes.Count;
        InputWindowProbe.InjectKey(InputWindowProbe.VkF13);
        InputWindowProbe.Drain();
        presence.Pump(256);
        var keyAfterDismissReached = keystrokes.Count > countAfterDismiss;

        var callbackFaults = presence.CallbackFaults;
        presence.Dispose();
        var osSeesWindowAfterDispose = InputWindowProbe.WindowExists(window);

        return new LifecycleRun(
            MachineHasInteractiveDesktop: InputWindowProbe.MachineHasInteractiveDesktop,
            Window: window,
            ClaimedAvailableOnPrompt: promptState is CapabilityState.Available,
            PresenceReportsPrompting: presenceReportsPrompting,
            OsSeesWindow: osSeesWindow,
            OsSeesWindowVisible: osSeesVisible,
            ZOrder: zOrder,
            OsSaysCardIsForeground: osSaysForeground,
            OsSaysSystemFocusIsCard: osSaysSystemFocus,
            HitTestWinnerWhilePrompting: hitWinner,
            ForegroundWinner: foregroundWinner,
            KeyInjected: injected,
            KeyReachedTheCard: reachedCard,
            TypedCharacterReachedTheModel: characterReached,
            InkedPixels: Math.Max(observation.InkedPixels, inkObservation.InkedPixels),
            SampledPixels: Math.Max(observation.SampledPixels, inkObservation.SampledPixels),
            EscapeReachedTheModel: escapeReached,
            ClaimedAvailableOnUpdate: updateState is CapabilityState.Available,
            ClaimedAvailableOnDismiss: dismissState is CapabilityState.Available,
            OsSeesWindowVisibleAfterDismiss: osSeesVisibleAfter,
            CardStillForegroundAfterDismiss: stillForeground,
            HitTestWinnerAfterDismiss: hitAfter,
            PresenceReportsPromptingAfterDismiss: promptingAfter,
            KeyAfterDismissReachedTheModel: keyAfterDismissReached,
            OsSeesWindowAfterDispose: osSeesWindowAfterDispose,
            TeardownDiagnostic: presence.TeardownDiagnostic,
            CallbackFaults: callbackFaults,
            PromptState: promptState,
            UpdateState: updateState,
            DismissState: dismissState);
    }

    // ---------- the states the ordinary lifecycle cannot reach ----------

    /// <param name="OffScreenPromptClaimedAvailable">A card asked for at a rectangle no display
    /// covers.</param>
    /// <param name="OffScreenPromptCode">The reason code it refused with.</param>
    /// <param name="OffScreenWindowLeftVisible">
    /// Whether the refused card was left ON SCREEN. Must be false: a window that holds the user's
    /// screen, answers nothing and cannot be dismissed is strictly worse than no window.
    /// </param>
    /// <param name="OffScreenPresenceStillPrompting">Whether the presence still claims a card.</param>
    /// <param name="BlankPromptClaimedAvailable">A card whose content is entirely empty.</param>
    /// <param name="BlankPromptWasDegraded">It must be <c>Degraded</c>, not Available: the OS gave
    /// it the keyboard and there is nothing on it to read.</param>
    /// <param name="BlankPromptCode">The reason code that degradation carries.</param>
    /// <param name="BlankPromptInkedPixels">What the ink read-back counted for it.</param>
    /// <param name="BlankPromptBackgroundHeld">Whether the painted background was really there —
    /// which is what tells "nothing was drawn" from "nothing was painted at all".</param>
    /// <param name="BlankRepaintWasDegraded">A REPAINT that leaves the card blank must degrade the
    /// same way the first paint does — the ink check lives on both paths and both need the
    /// background control.</param>
    /// <param name="BlankRepaintCode">The reason code that repaint carried.</param>
    /// <param name="ControlCharacterReachedTheCaller">A <c>WM_CHAR</c> carrying a control character
    /// (Ctrl+V's 0x16) posted straight to the card must NOT arrive as typing.</param>
    /// <param name="PrintableCharacterReachedTheCaller">The same route with a printable character
    /// MUST arrive — otherwise the line above is satisfied by a window that ignores everything.</param>
    /// <param name="CharacterAfterDismissReachedTheCaller">A character posted to the window AFTER
    /// the card was dismissed must not reach the caller: the presence has to have dropped the
    /// callback, not merely hidden the window.</param>
    internal sealed record EdgeRun(
        bool OffScreenPromptClaimedAvailable,
        string OffScreenPromptCode,
        bool OffScreenWindowLeftVisible,
        bool OffScreenPresenceStillPrompting,
        bool BlankPromptClaimedAvailable,
        bool BlankPromptWasDegraded,
        string BlankPromptCode,
        int BlankPromptInkedPixels,
        bool BlankPromptBackgroundHeld,
        bool BlankRepaintWasDegraded,
        string BlankRepaintCode,
        bool ControlCharacterReachedTheCaller,
        bool PrintableCharacterReachedTheCaller,
        bool CharacterAfterDismissReachedTheCaller);

    private static readonly Lazy<EdgeRun> LazyEdges =
        new(RunEdges, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static EdgeRun Edges => LazyEdges.Value;

    /// <summary>
    /// The three states the ordinary lifecycle never reaches on a healthy machine, each constructed
    /// rather than simulated: a card the window manager cannot route to, a card with nothing written
    /// on it, and characters delivered by <c>PostMessage</c> — which is explicitly NOT OS routing and
    /// is labelled as such, but is the only way to hand this window a character it would never
    /// receive from a keyboard.
    /// </summary>
    private static EdgeRun RunEdges()
    {
        var content = new InputPromptContent(Question, "1 of 1", string.Empty, "Press Esc to close");

        // (1) A rectangle no display covers. The window exists and is topmost, and the window
        // manager routes nothing to it — which is exactly the refusal path, and the assertion is
        // that the card does not stay on the user's screen afterwards.
        var offScreen = new Win32InputPresence();
        var offScreenState = offScreen.Prompt(new InputPromptRequest(
            new InputBounds(-30000, -30000, 300, 200), content, _ => { }));
        var offScreenWindow = offScreen.NativeHandles.Window;
        var offScreenVisible = InputWindowProbe.WindowIsVisible(offScreenWindow);
        var offScreenPrompting = offScreen.IsPrompting;
        offScreen.Dispose();

        // (2) A card with nothing written on it. Every band is empty, so the painter fills the
        // background and draws no glyph — the ink differential must see the background and no ink.
        var blank = new Win32InputPresence();
        var blankState = blank.Prompt(new InputPromptRequest(
            CentreBounds,
            new InputPromptContent(string.Empty, string.Empty, string.Empty, string.Empty),
            _ => { }));
        var blankObservation = blank.LastObservation;
        blank.Dismiss();
        blank.Dispose();

        // (3) Characters by PostMessage. NOT a claim about OS routing — that is the SendInput leg in
        // the lifecycle run. This is the only way to hand the window procedure a control character,
        // which is what upstream's whole clipboard-gesture list reduces to in a window with no edit
        // control (LockCardWindow.xaml.cs:246-264).
        var delivered = new List<InputKeystroke>();
        var typed = new Win32InputPresence();
        typed.Prompt(new InputPromptRequest(CentreBounds, content, delivered.Add));
        var window = typed.NativeHandles.Window;

        // A REPAINT that leaves the card blank. Update carries its OWN copy of the ink check and its
        // own copy of the background control that stops an unpainted window reading as inked, so it
        // needs its own measurement.
        var blankRepaint = typed.Update(
            new InputPromptContent(string.Empty, string.Empty, string.Empty, string.Empty));
        typed.Update(content);

        InputWindowProbe.PostCharacter(window, ''); // Ctrl+V's WM_CHAR
        InputWindowProbe.PumpUntil(() => { typed.Pump(64); return false; });
        var controlArrived = delivered.Any(k => k.Kind == InputKeystrokeKind.Character);

        InputWindowProbe.PostCharacter(window, 'z');
        var printableArrived = InputWindowProbe.PumpUntil(() =>
        {
            typed.Pump(64);
            return delivered.Any(k => k.Kind == InputKeystrokeKind.Character && k.Character == 'z');
        });

        typed.Dismiss();
        var before = delivered.Count;
        InputWindowProbe.PostCharacter(window, 'q');
        InputWindowProbe.PumpUntil(() => { typed.Pump(64); return false; });
        var afterDismiss = delivered.Count > before;
        typed.Dispose();

        return new EdgeRun(
            OffScreenPromptClaimedAvailable: offScreenState is CapabilityState.Available,
            OffScreenPromptCode: offScreenState is CapabilityState.Unavailable u ? u.Reason.Code : "none",
            OffScreenWindowLeftVisible: offScreenVisible,
            OffScreenPresenceStillPrompting: offScreenPrompting,
            BlankPromptClaimedAvailable: blankState is CapabilityState.Available,
            BlankPromptWasDegraded: blankState is CapabilityState.Degraded,
            BlankPromptCode: blankState is CapabilityState.Degraded d ? d.Reason.Code : "none",
            BlankPromptInkedPixels: blankObservation.InkedPixels,
            BlankPromptBackgroundHeld: blankObservation.BackgroundHeld,
            BlankRepaintWasDegraded: blankRepaint is CapabilityState.Degraded,
            BlankRepaintCode: blankRepaint is CapabilityState.Degraded repaint ? repaint.Reason.Code : "none",
            ControlCharacterReachedTheCaller: controlArrived,
            PrintableCharacterReachedTheCaller: printableArrived,
            CharacterAfterDismissReachedTheCaller: afterDismiss);
    }

    /// <summary>Everything a refusal-shaped presence answers, from one construction.</summary>
    internal sealed record RefusalRun(
        bool ClaimedAvailable,
        bool ReportsPrompting,
        bool ReportsHoldingTheInput,
        bool ReportsCanReachAUser,
        bool StationWasAsked,
        bool ObservationWasAsked,
        bool UpdateAlsoRefused,
        bool DismissAlsoRefused,
        string Code,
        string Detail);

    internal static RefusalRun RefusalFor(InputHostPlatform platform)
    {
        using var presence = InputPresenceFactory.CreateFor(platform);
        var state = presence.Prompt(new InputPromptRequest(
            new InputBounds(0, 0, 100, 100),
            new InputPromptContent("q", "1 of 1", string.Empty, "hint"),
            _ => { }));

        var reason = state switch
        {
            CapabilityState.Unavailable unavailable => unavailable.Reason,
            CapabilityState.Degraded degraded => degraded.Reason,
            _ => new CapabilityReason("none", Describe(state)),
        };

        return new RefusalRun(
            ClaimedAvailable: state is CapabilityState.Available,
            ReportsPrompting: presence.IsPrompting,
            ReportsHoldingTheInput: presence.HoldsTheInput,
            ReportsCanReachAUser: presence.CanReachAUser,
            StationWasAsked: presence.ObserveStation().Asked,
            ObservationWasAsked: presence.Observe().Asked,
            UpdateAlsoRefused: presence.Update(new InputPromptContent("q", "1", "", "h")) is not CapabilityState.Available,
            DismissAlsoRefused: presence.Dismiss() is not CapabilityState.Available,
            Code: reason.Code,
            Detail: reason.Detail);
    }

    // ---------- TRAP 1: the overlay must survive a card ----------

    /// <summary>
    /// The overlay's state at one moment, read entirely through <see cref="OverlayWindowProbe"/>.
    /// </summary>
    /// <param name="PointPassesThrough">The window manager routes the overlay's own centre to
    /// something that is NOT the overlay.</param>
    /// <param name="AboveEveryOrdinaryWindow">The OS's z-order walk still puts it above every
    /// ordinary window.</param>
    /// <param name="Alpha">The layered alpha the OS still holds, or -1 for none.</param>
    /// <param name="TransparentStyleHeld"><c>WS_EX_TRANSPARENT</c> is still set.</param>
    /// <param name="IsForeground">Whether the overlay became the foreground window.</param>
    internal readonly record struct OverlayReading(
        bool PointPassesThrough,
        bool AboveEveryOrdinaryWindow,
        int Alpha,
        bool TransparentStyleHeld,
        bool IsForeground);

    /// <param name="MachineHasInteractiveDesktop">The machine fact.</param>
    /// <param name="OverlayPresented">The real overlay presence claimed Available.</param>
    /// <param name="Before">The overlay before any card existed.</param>
    /// <param name="During">The overlay while the card held the foreground.</param>
    /// <param name="After">The overlay after the card was dismissed and the presence disposed.</param>
    /// <param name="CardTookTheInput">The card really did take the foreground and the focus — without
    /// this the three readings above are a test of nothing happening.</param>
    /// <param name="OverlayCatchesItsOwnPointWhenMadeOpaque">
    /// The differential, run AFTER the card is gone: with <c>WS_EX_TRANSPARENT</c> cleared, the same
    /// point routes TO the overlay. Without it, "the point went elsewhere" is also true of a window
    /// that is not there.
    /// </param>
    /// <param name="OverlayStillEarnsAvailable">The overlay's own <c>Present</c> still returns
    /// Available after the card's whole lifecycle — the capability's own oracle, re-asked.</param>
    /// <param name="OverlayRePresentState">That state, for failure messages.</param>
    internal sealed record CoexistenceRun(
        bool MachineHasInteractiveDesktop,
        bool OverlayPresented,
        OverlayReading Before,
        OverlayReading During,
        OverlayReading After,
        bool CardTookTheInput,
        bool OverlayCatchesItsOwnPointWhenMadeOpaque,
        bool OverlayStillEarnsAvailable,
        CapabilityState OverlayRePresentState);

    private static CoexistenceRun RunCoexistence()
    {
        var (screenWidth, screenHeight) = InputWindowProbe.PrimarySize;
        const int overlayWidth = 220;
        const int overlayHeight = 160;

        // DISJOINT from the card's rectangle on purpose: the overlay's hit-test point must never be
        // occluded by the thing under test, or "the point went past the overlay" would be measuring
        // the card instead of the overlay.
        var overlayBounds = new OverlayBounds(
            Math.Max(0, ((screenWidth - overlayWidth) / 2) - 420),
            Math.Max(0, (screenHeight - overlayHeight) / 2),
            overlayWidth,
            overlayHeight);
        var (overlayX, overlayY) = overlayBounds.Centre;

        using var overlay = new Win32OverlayPresence();
        var presented = overlay.Present(new OverlaySurfaceRequest(overlayBounds, 0.6, ClickThrough: true));
        var overlayWindow = overlay.NativeHandles.Window;

        OverlayReading Read() => new(
            PointPassesThrough: OverlayWindowProbe.HitTest(overlayX, overlayY) != overlayWindow,
            AboveEveryOrdinaryWindow: OverlayWindowProbe.ReadZOrder(overlayWindow).AboveEveryOrdinaryWindow,
            Alpha: OverlayWindowProbe.LayeredAlphaOf(overlayWindow),
            TransparentStyleHeld: (OverlayWindowProbe.ExStyleOf(overlayWindow) & 0x00000020) != 0,
            IsForeground: OverlayWindowProbe.IsForeground(overlayWindow));

        var before = Read();

        var cardBounds = CentreBounds;
        var presence = new Win32InputPresence();
        presence.Prompt(new InputPromptRequest(
            cardBounds,
            new InputPromptContent(Question, "1 of 1", string.Empty, "Press Esc to close"),
            _ => { }));

        var cardTookTheInput = presence.HoldsTheInput;
        var during = Read();

        presence.Dismiss();
        presence.Dispose();

        // The overlay's own re-assertion, exactly as a live session would: WPF re-raises its topmost
        // windows on a cadence (Services/Flash/FlashService.cs:206-243) and the port's overlay owns
        // the same call. Asking it here is not helping the overlay pass — it is what the product
        // already does, and the z-order fact below is what would fail if the card had permanently
        // taken the band.
        overlay.Reassert();
        var after = Read();

        // The differential, and it is deliberately taken on the OVERLAY rather than on a scratch
        // window: it proves this specific surface WOULD have caught the point, so "the point went
        // elsewhere" cannot be satisfied by an overlay that quietly stopped existing.
        var opaqueState = overlay.SetClickThrough(false);
        var overlayCatchesItsOwnPoint = OverlayWindowProbe.HitTest(overlayX, overlayY) == overlayWindow;
        overlay.SetClickThrough(true);

        var rePresent = overlay.Present(new OverlaySurfaceRequest(overlayBounds, 0.6, ClickThrough: true));

        return new CoexistenceRun(
            MachineHasInteractiveDesktop: InputWindowProbe.MachineHasInteractiveDesktop,
            OverlayPresented: presented is CapabilityState.Available,
            Before: before,
            During: during,
            After: after,
            CardTookTheInput: cardTookTheInput,
            OverlayCatchesItsOwnPointWhenMadeOpaque: overlayCatchesItsOwnPoint && opaqueState is CapabilityState.Available,
            OverlayStillEarnsAvailable: rePresent is CapabilityState.Available,
            OverlayRePresentState: rePresent);
    }
}
