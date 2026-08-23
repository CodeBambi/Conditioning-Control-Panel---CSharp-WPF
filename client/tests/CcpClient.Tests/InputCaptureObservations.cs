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
/// as <see cref="OverlayObservations"/> and <see cref="TrayObservations"/>.</para>
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
    /// <param name="OffScreenPromptCode">The reason code, or <c>"none"</c> when the state was not
    /// <c>Unavailable</c> — which is what this edge actually produces.</param>
    /// <param name="OffScreenWindowLeftVisible">
    /// Whether the card is still on screen afterwards. <b>TRUE, on purpose</b>, and an earlier draft
    /// of this doc said the opposite and asserted nothing — which is how a real bug got through.
    /// A card on no display passes every OS-ROUTING check (WindowFromPoint walks the window tree,
    /// not the monitors) and fails only the ink read-back, so the outcome is <c>Degraded</c>, and a
    /// <c>Degraded</c> prompt deliberately keeps its window: the operating system HAS given it the
    /// input. Taking it down is the MODULE's job, and its fact
    /// (<c>LockCardModuleTests.ACardThatIsFocusedAndBLANK_IsAlsoTakenBackDown...</c>) is only
    /// testing something real while this stays true.
    /// </param>
    /// <param name="OffScreenPresenceStillPrompting">Whether the presence still claims a card. Moves
    /// with the field above, for the same reason.</param>
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

        // TAKEN FROM THE PROBE, not from the presence. Every other leg of this fixture reads the
        // window manager through OverlayWindowProbe's own declarations; sourcing the one leg that
        // says "a card really was up" from the thing under test would let a presence that lied about
        // holding the input turn the whole coexistence run into a test of nothing happening.
        var cardWindow = presence.NativeHandles.Window;
        var cardTookTheInput = InputWindowProbe.WindowIsVisible(cardWindow)
            && InputWindowProbe.Foreground() == cardWindow
            && InputWindowProbe.SystemKeyboardFocus() == cardWindow;
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

    /// <summary>How many threads enter the concurrent leg at once. More than the two the defect
    /// needs, because the discriminating power of that leg comes from at least one loser arriving
    /// while the winner is still INSIDE its prompt: with four released from one barrier, "no loser
    /// ever overlapped" stops being a plausible reading of a green.</summary>
    private const int Racers = 4;

    private static readonly Lazy<SingleTenancyRun> LazySingleTenancy =
        new(RunSingleTenancy, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The one run that proves ONE card at a time on a shared presence.</summary>
    internal static SingleTenancyRun SingleTenancy => LazySingleTenancy.Value;

    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation is compared against.</param>
    /// <param name="Window">The first card's handle, from the presence's native-handle surface.</param>
    /// <param name="FirstCardIsLive">The first prompt ended with a card up (Available or Degraded).</param>
    /// <param name="SecondPromptCode">The reason code the second prompt refused with.</param>
    /// <param name="StillPromptingAfterSecond">The live card is still the presence's card.</param>
    /// <param name="LastPromptUntouchedBySecond">The refusal did not overwrite what a panel renders
    /// for the LIVE card (<c>Views/Pages/StudioPage.axaml.cs:1696</c>) — reference identity, so
    /// nothing about the string matters.</param>
    /// <param name="HitTestAtFirstCardsRectangle">Who the window manager routes the FIRST card's
    /// centre to, read by the probe's own P/Invoke rather than by the product's.</param>
    /// <param name="HitTestAtSecondCardsRectangle">Ditto for the rectangle the REFUSED prompt asked
    /// for. Together they say the card did not move — and <c>_bounds</c> is assigned in the same
    /// lock block as <c>_content</c> and <c>_onKeystroke</c>
    /// (<c>Input/Win32InputPresence.cs</c>), so a card that did not move is a block that did not
    /// run.</param>
    /// <param name="KeyInjected">Whether the OS accepted the synthesised <c>VK_A</c>.</param>
    /// <param name="KeyReachedTheFirstCard">Whether it arrived at the FIRST caller's callback —
    /// the defect stated the way the user meets it: that module can still be answered.</param>
    /// <param name="SecondCallbackEverFired">Whether the refused caller's callback ever ran. It was
    /// never installed, so it must not have.</param>
    /// <param name="ThirdPromptCode">A THIRD prompt while the first card is still up. The refused
    /// second must not have RELEASED the live card's claim on its way out; the failure in that
    /// direction is a card stealable by the caller after next.</param>
    /// <param name="PromptAfterDismissCode">After the card comes down, the presence takes the next
    /// one: the refusal is BUSY, not BROKEN.</param>
    /// <param name="LateRefusalCode">What a prompt at an origin no window manager will honour
    /// refused with, or <c>none</c> if it did not refuse.</param>
    /// <param name="PromptingAfterLateRefusal">Whether a card survived that prompt.</param>
    /// <param name="PromptAfterLateRefusalCode">Whether the NEXT prompt on that presence was
    /// admitted. A refusal path that kept the claim would disable prompting for the process.</param>
    /// <param name="RaceAdmitted">How many of <see cref="Racers"/> CONCURRENT prompts got PAST the
    /// guard — the number an unsynchronised re-read of <c>_prompting</c> gets wrong, because that
    /// field is not true until the confirmation read at the END of Prompt, so every thread that
    /// tests it during another's prompt sees false and proceeds.</param>
    /// <param name="RaceLiveCards">How many of them ended with a card up. Deliberately NOT the
    /// headline: several admitted callers race on the presence's own <c>_window</c> field and all
    /// but one end up refusing themselves, so a count of live cards hides the defect. The count of
    /// ADMISSIONS is what shows it.</param>
    /// <param name="RaceOutcomes">Every outcome, for the failure message.</param>
    /// <param name="FirstPromptState">The full state, for failure messages.</param>
    /// <param name="SecondPromptState">Ditto.</param>
    /// <param name="LateRefusalState">Ditto.</param>
    internal sealed record SingleTenancyRun(
        bool MachineHasInteractiveDesktop,
        nint Window,
        bool FirstCardIsLive,
        string SecondPromptCode,
        bool StillPromptingAfterSecond,
        bool LastPromptUntouchedBySecond,
        nint HitTestAtFirstCardsRectangle,
        nint HitTestAtSecondCardsRectangle,
        bool KeyInjected,
        bool KeyReachedTheFirstCard,
        bool SecondCallbackEverFired,
        string ThirdPromptCode,
        string PromptAfterDismissCode,
        string LateRefusalCode,
        bool PromptingAfterLateRefusal,
        string PromptAfterLateRefusalCode,
        int RaceAdmitted,
        int RaceLiveCards,
        string RaceOutcomes,
        CapabilityState FirstPromptState,
        CapabilityState SecondPromptState,
        CapabilityState LateRefusalState);

    /// <summary>
    /// Three legs, a presence each, and all three ask the same question: who owns the card that is
    /// up.
    ///
    /// <para>Leg 1 is the reported defect — a second <c>Prompt</c> over a live card used to
    /// overwrite the shared <c>_content</c> and <c>_onKeystroke</c>, so the first module's question
    /// could never be answered. Leg 2 is the obligation that staking a claim on entry creates: a
    /// prompt that ends with NO card must give the claim back, or the first refusal disables
    /// prompting for the life of the process. Leg 3 is the only shape that can tell an ATOMIC claim
    /// from an unsynchronised re-read of <c>_prompting</c>, and it has to be concurrent to do it:
    /// outside a <c>Prompt</c> call the two are equal by construction, and the presence offers no
    /// re-entrancy hook to observe one in flight (<c>InputPromptRequest</c> and
    /// <c>InputPromptContent</c> are sealed records with non-virtual members, <c>InputBounds</c> is
    /// a readonly struct, and <c>OnKeystroke</c> is raised only on message dispatch, which nothing
    /// inside <c>Prompt</c> performs).</para>
    /// </summary>
    private static SingleTenancyRun RunSingleTenancy()
    {
        var firstKeystrokes = new List<InputKeystroke>();
        var secondKeystrokes = new List<InputKeystroke>();

        var firstBounds = CentreBounds;

        // Somewhere the first card is NOT, on purpose: had the refused prompt reached the assignment
        // block, _bounds would be this rectangle and the card would have moved there, which the
        // probe's own hit test below would see.
        var secondBounds = new InputBounds(8, 8, 220, 150);

        var presence = new Win32InputPresence();
        var first = presence.Prompt(new InputPromptRequest(
            firstBounds,
            new InputPromptContent(Question, "1 of 1", string.Empty, "Press Esc to close"),
            firstKeystrokes.Add));
        var window = presence.NativeHandles.Window;
        var lastPromptAfterFirst = presence.LastPrompt;

        var second = presence.Prompt(new InputPromptRequest(
            secondBounds,
            new InputPromptContent("HOW MANY BUBBLES", "1 of 1", string.Empty, "Press Esc to close"),
            secondKeystrokes.Add));
        var stillPrompting = presence.IsPrompting;
        var lastPromptUntouched = ReferenceEquals(lastPromptAfterFirst, presence.LastPrompt);

        var (firstX, firstY) = firstBounds.Centre;
        var (secondX, secondY) = secondBounds.Centre;
        var hitAtFirst = InputWindowProbe.HitTest(firstX, firstY);
        var hitAtSecond = InputWindowProbe.HitTest(secondX, secondY);

        // WHOSE CALLBACK THE KEYSTROKE REACHES. Gated on the OS genuinely holding the card, exactly
        // as the lifecycle run gates its own delivery leg, so an injected key can never be typed into
        // somebody else's window.
        var injected = false;
        var reachedFirst = false;
        if (InputWindowProbe.Foreground() == window && InputWindowProbe.SystemKeyboardFocus() == window)
        {
            injected = InputWindowProbe.InjectKey(InputWindowProbe.VkA);
            reachedFirst = InputWindowProbe.PumpUntil(() =>
            {
                presence.Pump(64);
                return firstKeystrokes.Any(k => k.Kind == InputKeystrokeKind.Character && k.Character == 'a');
            });
        }

        // A THIRD prompt, still over the live card: the refused second must not have handed the FIRST
        // card's claim back on its way out.
        var third = presence.Prompt(new InputPromptRequest(
            secondBounds,
            new InputPromptContent("THIRD", "1 of 1", string.Empty, "Press Esc to close"),
            _ => { }));

        presence.Dismiss();
        var afterDismiss = presence.Prompt(new InputPromptRequest(
            firstBounds,
            new InputPromptContent("AFTER THE CARD CAME DOWN", "1 of 1", string.Empty, "Press Esc to close"),
            _ => { }));
        var secondCallbackFired = secondKeystrokes.Count > 0;
        presence.Dismiss();
        presence.Dispose();

        // LEG 2. A prompt that refuses LATE — after the claim is staked and after a window exists —
        // so the release under test is the one on a failure path rather than the one on a dismissal.
        // The shape was chosen by measurement, not by taste: the request boundary already rejects
        // zero and negative area (InputPromptRequest validates), an OFF-SCREEN card comes back
        // Degraded rather than refused because WindowFromPoint walks the window tree and not the
        // monitors, and a huge SIZE hangs the compositor. What is left is a SMALL window at an
        // impossible ORIGIN: it costs the OS nothing and cannot survive the placement read-back,
        // because the rectangle the OS holds is not the rectangle that was asked for.
        var late = new Win32InputPresence();
        var lateState = late.Prompt(new InputPromptRequest(
            new InputBounds(2_000_000_000, 2_000_000_000, 320, 200),
            new InputPromptContent("IMPOSSIBLE ORIGIN", "1 of 1", string.Empty, "Press Esc to close"),
            _ => { }));
        var promptingAfterLate = late.IsPrompting;
        var afterLate = late.Prompt(new InputPromptRequest(
            CentreBounds,
            new InputPromptContent("AFTER A REFUSAL", "1 of 1", string.Empty, "Press Esc to close"),
            _ => { }));
        late.Dismiss();
        late.Dispose();

        var raceAdmitted = RunRace(out var raceLiveCards, out var raceOutcomes);

        return new SingleTenancyRun(
            MachineHasInteractiveDesktop: InputWindowProbe.MachineHasInteractiveDesktop,
            Window: window,
            FirstCardIsLive: first is CapabilityState.Available or CapabilityState.Degraded,
            SecondPromptCode: CodeOf(second),
            StillPromptingAfterSecond: stillPrompting,
            LastPromptUntouchedBySecond: lastPromptUntouched,
            HitTestAtFirstCardsRectangle: hitAtFirst,
            HitTestAtSecondCardsRectangle: hitAtSecond,
            KeyInjected: injected,
            KeyReachedTheFirstCard: reachedFirst,
            SecondCallbackEverFired: secondCallbackFired,
            ThirdPromptCode: CodeOf(third),
            PromptAfterDismissCode: CodeOf(afterDismiss),
            LateRefusalCode: CodeOf(lateState),
            PromptingAfterLateRefusal: promptingAfterLate,
            PromptAfterLateRefusalCode: CodeOf(afterLate),
            RaceAdmitted: raceAdmitted,
            RaceLiveCards: raceLiveCards,
            RaceOutcomes: raceOutcomes,
            FirstPromptState: first,
            SecondPromptState: second,
            LateRefusalState: lateState);
    }

    /// <summary>
    /// LEG 3. <see cref="Racers"/> threads, one presence, one barrier, and no wall-clock anywhere:
    /// the rendezvous is a deterministic signal and the waiting is a bounded pump.
    ///
    /// <para><b>NEITHER RACER MAY DRIVE A WINDOW THIS THREAD OWNS</b>, and that is measured rather
    /// than stylistic. <c>SetWindowPos(SWP_SHOWWINDOW)</c> and <c>SetForegroundWindow</c> SEND
    /// messages to the thread that owns the window and block until it answers, so a warm-up prompt on
    /// THIS thread followed by racers over its window would deadlock against this thread sitting in
    /// <c>Join</c>. The presence therefore starts with no window at all and the racer that wins the
    /// claim creates and drives its own; this thread pumps while they run so nothing the OS sends it
    /// goes unanswered, and the <c>Join</c> is what waits.</para>
    /// </summary>
    private static int RunRace(out int liveCards, out string outcomeText)
    {
        var racer = new Win32InputPresence();

        // Every racer asks for the SAME rectangle on purpose: with different rectangles two admitted
        // callers would refuse EACH OTHER on the held-bounds read-back, and an unsynchronised guard
        // would pass this leg for the wrong reason.
        var raceBounds = CentreBounds;

        var outcomes = new CapabilityState[Racers];
        var finished = 0;
        using (var rendezvous = new Barrier(Racers))
        {
            var threads = new Thread[Racers];
            for (var i = 0; i < threads.Length; i++)
            {
                var slot = i;
                threads[slot] = new Thread(() =>
                {
                    rendezvous.SignalAndWait();
                    outcomes[slot] = racer.Prompt(new InputPromptRequest(
                        raceBounds,
                        new InputPromptContent($"RACER {slot}", "1 of 1", string.Empty, "Press Esc to close"),
                        _ => { }));
                    Interlocked.Increment(ref finished);
                })
                {
                    IsBackground = true,
                    Name = $"input single-tenancy racer {slot}",
                };
            }

            foreach (var thread in threads)
            {
                thread.Start();
            }

            InputWindowProbe.PumpUntil(() => Volatile.Read(ref finished) == threads.Length);

            foreach (var thread in threads)
            {
                thread.Join();
            }
        }

        // The winning racer's thread has exited, and Windows destroys the windows a thread created
        // when it ends — so this teardown is hygiene on an already-dead handle, not a claim.
        racer.Dismiss();
        racer.Dispose();

        liveCards = outcomes.Count(o => o is CapabilityState.Available or CapabilityState.Degraded);
        outcomeText = string.Join(" | ", outcomes.Select(Describe));
        return outcomes.Count(o => CodeOf(o) != InputReasonCodes.InputAlreadyPrompting);
    }

    /// <summary>The refusal code, or <c>none</c> for anything that is not a refusal.</summary>
    private static string CodeOf(CapabilityState state) =>
        state is CapabilityState.Unavailable unavailable ? unavailable.Reason.Code : "none";
}
