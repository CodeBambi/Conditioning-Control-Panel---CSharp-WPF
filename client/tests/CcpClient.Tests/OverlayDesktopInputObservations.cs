using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Pointer;

namespace CcpClient.Tests;

/// <summary>
/// <b>The two <c>overlay-clickthrough</c> safety invariants that had no instrument at all, measured
/// on the real desktop with real synthesised input.</b>
///
/// <para><b>Invariant one (<c>.claude/skills/overlay-clickthrough/SKILL.md:26</c>):</b> <i>"Passive
/// regions allow normal desktop click, type, drag, and scroll."</i> Only the CLICK quarter of that
/// sentence was measured anywhere in this suite. <see cref="PassiveChannels"/> drives all four
/// channels through a click-through overlay into an application-shaped window underneath it, and
/// then drives the same four again with click-through cleared, restored, and the surface withdrawn.
/// The three other channels are new evidence; they are not corollaries of the click, because they
/// are not routed the same way — a click is HIT-TESTED and a keystroke is FOCUS-routed, so an
/// overlay could pass every click through and still hold the user's keyboard.</para>
///
/// <para><b>Invariant two (<c>SKILL.md:28</c>):</b> <i>"A handled overlay click does not
/// unintentionally activate/click the underlying application."</i> Nothing looked for that leak. It
/// is the SAME run because the two invariants are each other's differential: with click-through set
/// the click must reach the window underneath, and with it cleared the identical click at the
/// identical point must reach nothing AND must leave the foreground where it was. Both halves of
/// that sentence are asserted, because a click that is swallowed by the overlay's own window can
/// still activate a window beneath it — activation and message delivery are different OS
/// decisions.</para>
///
/// <para><b>Invariant three (<c>SKILL.md:29</c>):</b> <i>"Overlays do not ... appear as ordinary
/// task-switching windows."</i> <see cref="TaskSwitcher"/> puts three of the port's native surfaces
/// on the desktop and asks the SHELL'S OWN documented task-window rule about each, over the
/// operating system's read-backs, with an ordinary unowned non-tool window as the control that must
/// answer YES. See <see cref="TaskSwitcherRun"/> for exactly what that can and cannot say.</para>
///
/// <para><b>Upstream's decisions, which are what the port owes an outcome to.</b> Every WPF flash
/// window is created <c>ShowInTaskbar = false, ShowActivated = false</c>
/// (<c>Services/Flash/FlashService.cs:3619-3620</c>) and then given
/// <c>WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE</c> on its live hwnd
/// (<c>HideFromAltTab</c>, <c>:3819-3828</c>, wired once per window at <c>:3653</c>); the bubbles do
/// the same and rebuild the ex-style from a known base every re-show because a pooled shell carries
/// the last life's bits (<c>Services/BubbleService.cs:4881-4896</c>). Click-through polarity is
/// flipped on the LIVE hwnd per spawn (<c>ApplyClickability</c>, <c>:3662-3673</c>), and a clickable
/// flash consumes its own <c>MouseLeftButtonDown</c> in WPF (<c>:3632-3636</c>) — that consumed
/// press, over a <c>WS_EX_NOACTIVATE</c> window, is the "handled overlay click" of invariant
/// two.</para>
///
/// <para><b>WINDOWS EVIDENCE ONLY.</b> Every reading below comes from <c>user32.dll</c>: the window
/// manager's hit test, <c>SendInput</c>, <c>GetForegroundWindow</c>, <c>GetGUIThreadInfo</c> and a
/// window procedure's own message counts. Nothing here says anything whatever about X11 or Wayland,
/// where the mechanisms have no counterparts and the same invariants are entirely unmeasured.</para>
///
/// <para><b>And what a green run still does NOT prove.</b> That a human sees any of this. That the
/// rendered Alt-Tab switcher or the taskbar omits a surface (no public API exposes either list —
/// see <see cref="TaskSwitcherRun"/>). That a display-topology change restores desktop input: the
/// port subscribes to no display-change notification anywhere, this machine has one monitor, and the
/// only in-process way to change the topology is to reconfigure the interactive user's real display,
/// which this suite will not do.</para>
/// </summary>
internal static class OverlayDesktopInputObservations
{
    /// <summary>Opacity for every overlay this file presents. Well clear of the surface request's
    /// minimum, and irrelevant to input: <c>LWA_ALPHA</c> governs what the compositor draws, never
    /// what the window manager hit-tests.</summary>
    private const double Opacity = 0.5;

    /// <summary>The drag's total travel from the surface centre, and how many move events the batch
    /// is built from. Both are chosen only so the whole path stays inside the rectangle under test —
    /// a drag that leaves it is a drag over somebody else's window.</summary>
    private const int DragDeltaX = 60;

    private const int DragDeltaY = 40;

    private const int DragSteps = 8;

    /// <summary>How many points a drag path has: the press point plus one per step. The pre-flight
    /// walks all of them, and its control asserts that it walked all of them.</summary>
    internal const int DragPathPointCount = DragSteps + 1;

    /// <summary>How many wheel notches one pass sends. Never asserted against — every wheel
    /// expectation compares one pass's cumulative count with the previous pass's, so no constant
    /// both drives the input and stands as the expectation.</summary>
    private const int WheelNotchesPerPass = 1;

    // -------------------------------------------------------------------------------------------
    //  (a) + (b): the four passive channels, and the handled click that must not leak
    // -------------------------------------------------------------------------------------------

    private static readonly Lazy<PassiveChannelRun> LazyPassive =
        new(RunPassiveChannels, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The passive-channel run. Cached: it puts three real windows on the user's screen,
    /// takes the foreground twice and synthesises four passes of real mouse and keyboard input.</summary>
    internal static PassiveChannelRun PassiveChannels => LazyPassive.Value;

    /// <summary>Where the passive-channel run works: the bottom-right of the primary display. Every
    /// other real-desktop rectangle in this assembly is centre-relative or bottom-LEFT
    /// (<c>VideoSurfaceObservations.RoutingBounds</c>), and a run that synthesises clicks must not
    /// share a point with one that does.</summary>
    internal static OverlayBounds UnderneathBounds
    {
        get
        {
            var (width, height) = PointerWindowProbe.PrimarySize;
            return new OverlayBounds(Math.Max(0, width - 320), Math.Max(0, height - 300), 240, 160);
        }
    }

    /// <summary>Where the foreground keeper sits: clear of <see cref="UnderneathBounds"/>, because
    /// its whole job is to hold the foreground while a click lands somewhere else.</summary>
    internal static OverlayBounds KeeperBounds
    {
        get
        {
            var (width, height) = PointerWindowProbe.PrimarySize;
            return new OverlayBounds(Math.Max(0, width - 320), Math.Max(0, height - 500), 200, 120);
        }
    }

    /// <summary>
    /// One pass of all four desktop input channels at one point, with the counters read afterwards.
    ///
    /// <para>Every count is CUMULATIVE on the window underneath, and every fact compares one pass's
    /// count with the previous pass's rather than with a literal. That is deliberate: a literal
    /// expectation would be a constant asserted against input driven by the same constant, and this
    /// port has been caught by that shape before.</para>
    /// </summary>
    /// <param name="Label">Which leg this was, for failure messages.</param>
    /// <param name="ClickAccepted">The OS accepted the click injection. False means UIPI, the secure
    /// desktop or a locked workstation refused it, and no delivery claim is made.</param>
    /// <param name="DragAccepted">Ditto for the drag batch.</param>
    /// <param name="WheelAccepted">Ditto for the wheel.</param>
    /// <param name="KeyAccepted">Ditto for the keystroke.</param>
    /// <param name="Routed">Who the window manager said owned the point when this pass began.</param>
    /// <param name="Downs"><c>WM_LBUTTONDOWN</c> delivered to the window underneath, cumulative.</param>
    /// <param name="DragMoves"><c>WM_MOUSEMOVE</c> carrying <c>MK_LBUTTON</c>, cumulative — the DRAG
    /// channel. A move without the button is not counted.</param>
    /// <param name="Moves">EVERY <c>WM_MOUSEMOVE</c>, cumulative. Never asserted; it is in the
    /// failure text so a drag that read as absent can be diagnosed as "no move arrived" rather than
    /// "moves arrived without the button", which are different problems.</param>
    /// <param name="Wheel">Wheel notches delivered, cumulative — the SCROLL channel.</param>
    /// <param name="KeyDowns"><c>WM_KEYDOWN</c> delivered, cumulative — the TYPE channel.</param>
    /// <param name="KeeperKeyDowns">The same count on the KEEPER window, so "the keystroke went
    /// somewhere else" is a reading rather than an inference from an absence.</param>
    /// <param name="Activations">How many times the OS made the window underneath active,
    /// cumulative.</param>
    /// <param name="Foreground">The foreground window when the pass ended.</param>
    /// <param name="Drag">How much of the drag PATH arrived and, when some of it did not, the
    /// window the OS gives that point to. <b>This exists because its absence cost three wrong
    /// diagnoses.</b> "The drag channel delivered nothing" is true of a broken injection, of a
    /// desktop that refuses synthetic input, and of a foreign window sitting on part of the
    /// rectangle — and only the third was ever happening.</param>
    /// <param name="Path">Who owned every point of the drag path BEFORE anything was injected. The
    /// reading above says who took a step after the fact; this one is the pre-flight that refuses
    /// the leg outright when the window holding a point belongs to another process — see
    /// <see cref="PointerWindowProbe.HoldWholeDragPath"/>.</param>
    internal sealed record ChannelPass(
        string Label,
        bool ClickAccepted,
        bool DragAccepted,
        bool WheelAccepted,
        bool KeyAccepted,
        nint Routed,
        int Downs,
        int DragMoves,
        int Moves,
        int Wheel,
        int KeyDowns,
        int KeeperKeyDowns,
        int Activations,
        nint Foreground,
        PointerWindowProbe.DragReading Drag,
        PointerWindowProbe.PathHold Path)
    {
        internal bool EveryInjectionAccepted => ClickAccepted && DragAccepted && WheelAccepted && KeyAccepted;

        internal string Counts =>
            $"{Label}: downs={Downs} dragMoves={DragMoves} moves={Moves} wheel={Wheel} keys={KeyDowns} "
            + $"keeperKeys={KeeperKeyDowns} activations={Activations} "
            + $"routedTo={PointerWindowProbe.DescribeWindow(Routed)} "
            + $"foreground={PointerWindowProbe.DescribeWindow(Foreground)} "
            + $"accepted(click/drag/wheel/key)={ClickAccepted}/{DragAccepted}/{WheelAccepted}/{KeyAccepted} "
            + Drag.Describe + " | " + Path.Describe;
    }

    /// <summary>
    /// <b>The refusal the four drag facts consult before they read anything.</b> The first leg among
    /// <paramref name="legs"/> whose path a foreign window held, or a clear answer.
    ///
    /// <para>Each fact passes exactly the legs it compares — a foreign window during the teardown
    /// leg has nothing to do with the baseline fact, and refusing more widely than the evidence
    /// requires is how a precondition turns into an escape hatch. The counts are cumulative on one
    /// window, so a leg whose drag never ran contaminates every later comparison against it and both
    /// sides of a comparison must therefore be offered here.</para>
    /// </summary>
    internal static PointerWindowProbe.PathHold ForeignHoldOnTheDragPath(params ChannelPass[] legs)
    {
        ArgumentNullException.ThrowIfNull(legs);

        foreach (var leg in legs)
        {
            if (leg.Path.Contended)
            {
                return leg.Path;
            }
        }

        return PointerWindowProbe.PathHold.Clear("no leg of this run", DragSteps + 1);
    }

    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation flips on.</param>
    /// <param name="UnderneathWindow">The application-shaped window under the overlay. It is
    /// ACTIVATABLE on purpose — a <c>WS_EX_NOACTIVATE</c> window is exactly the one the OS refuses
    /// the keyboard focus to, so the TYPE channel could not be measured against it, and "the click
    /// did not activate it" would be a property of the instrument instead of the surface.</param>
    /// <param name="UnderneathIsUp">The OS reports it visible.</param>
    /// <param name="KeeperWindow">The window that holds the foreground while the handled click
    /// lands, so that "the application underneath was not brought forward" is measurable at
    /// all.</param>
    /// <param name="KeeperIsUp">The OS reports it visible.</param>
    /// <param name="OverlayWindow">The overlay's own handle.</param>
    /// <param name="UnderneathTookForegroundFirst">The window underneath really can hold the
    /// foreground and the keyboard on this machine. Without it the TYPE channel below is a statement
    /// about a window the OS would never have typed into anyway.</param>
    /// <param name="BaselinePass">Leg 0, with NO overlay in existence: the desktop under this point
    /// takes a click, a drag, a scroll and a keystroke. <b>The anti-vacuity leg</b> — on a machine
    /// where synthesised input goes nowhere, every reading below would be trivially true and this one
    /// reds first.</param>
    /// <param name="PresentState">What the overlay's own <c>Present</c> returned with click-through
    /// requested.</param>
    /// <param name="OverlayIndexWhilePassing">The overlay's position in the OS's own top-level
    /// z-order at the moment the pass-through leg was read.</param>
    /// <param name="UnderneathIndexWhilePassing">And the window underneath's. <b>The overlay must be
    /// ABOVE it</b>, or "the input went through" is a reading of an overlay that was never in the
    /// way. Both windows are in the topmost band, so "above every ordinary window" — which is all
    /// the capability's own confirmation establishes — does not order this pair.</param>
    /// <param name="PassThroughPass">Leg 1: the same four channels, with the click-through overlay
    /// covering the point.</param>
    /// <param name="KeeperTookForeground">The keeper holds the foreground and the keyboard before the
    /// handled click. Without it, leg 2's "the foreground did not move" is vacuous.</param>
    /// <param name="KeeperOnItsOwnInputQueue">The keeper's window belongs to a DIFFERENT OS thread
    /// from the overlay's. <b>This is what makes the foreground reading a statement about
    /// <c>WS_EX_NOACTIVATE</c> rather than about one thread's active window</b> — see
    /// <c>PointerWindowProbe.ParkedScratchTarget</c> for the measurement that forced it.</param>
    /// <param name="ForegroundWithKeeper">The foreground the instant after the keeper took it.</param>
    /// <param name="ForegroundAfterFlip">And after <c>SetClickThrough(false)</c> ran, which
    /// re-asserts the topmost band and re-writes the extended style on the live hwnd. Recorded
    /// separately so a foreground that moves HERE is never reported as a click leaking.</param>
    /// <param name="ForegroundBeforeHandledClick">And after the routing settle, immediately before
    /// the click. This is the value the click is measured against.</param>
    /// <param name="HandledState">What <c>SetClickThrough(false)</c> returned.</param>
    /// <param name="HandledRouted">Who the window manager says owns the point with click-through
    /// cleared. It must be the overlay, or the handled click never landed on the overlay at
    /// all.</param>
    /// <param name="HandledClickAccepted">The OS accepted the handled click.</param>
    /// <param name="HandledKeyAccepted">And the keystroke that follows it.</param>
    /// <param name="DownsAfterHandled">The window underneath's click count afterwards. <b>Leg 2's
    /// first reading:</b> unchanged.</param>
    /// <param name="ActivationsAfterHandled">And its activation count. <b>Leg 2's second
    /// reading:</b> unchanged — a click the overlay ate must not bring the application underneath
    /// forward.</param>
    /// <param name="UnderneathKeysAfterHandled">And its keystroke count: unchanged, because the
    /// foreground never moved.</param>
    /// <param name="KeeperKeysAfterHandled">The keeper's, which DID move — the keystroke went where
    /// the user's keystrokes were already going.</param>
    /// <param name="ForegroundAfterHandled">The foreground window after the handled click. <b>Leg 2's
    /// third reading:</b> still the keeper.</param>
    /// <param name="RestoreState">What <c>SetClickThrough(true)</c> returned.</param>
    /// <param name="RestoredPass">Leg 3: the identical four channels at the identical point with the
    /// only difference being the click-through flag. <b>This is leg 2's control</b>: the same click
    /// now DOES reach the window underneath and DOES activate it, so leg 2's three absences are
    /// properties of the handled overlay rather than of the rig.</param>
    /// <param name="WithdrawState">What <c>Withdraw</c> returned.</param>
    /// <param name="OverlayVisibleAfterWithdraw">The PROBE's own read-back: the OS no longer reports
    /// the surface visible. Asked independently of the capability's own confirmation, and asked
    /// before anything is raised.</param>
    /// <param name="OverlayZIndexAfterWithdraw">And its position in the OS's own visible top-level
    /// z-order, which must be -1 (absent). Without these two the channel readings below would pass
    /// over a surface that was never taken down, because the raise that beats foreign topmost
    /// contention would have won the point back anyway.</param>
    /// <param name="WithdrawnPass">Leg 4: all four channels after teardown. <i>"Teardown ... restores
    /// normal desktop input"</i> (<c>SKILL.md:31</c>) is a four-channel sentence, and the landed
    /// teardown evidence only ever read the hit test and the click.</param>
    internal sealed record PassiveChannelRun(
        bool MachineHasInteractiveDesktop,
        nint UnderneathWindow,
        bool UnderneathIsUp,
        nint KeeperWindow,
        bool KeeperIsUp,
        nint OverlayWindow,
        bool UnderneathTookForegroundFirst,
        ChannelPass BaselinePass,
        CapabilityState PresentState,
        int OverlayIndexWhilePassing,
        int UnderneathIndexWhilePassing,
        ChannelPass PassThroughPass,
        bool KeeperTookForeground,
        bool KeeperOnItsOwnInputQueue,
        nint ForegroundWithKeeper,
        nint ForegroundAfterFlip,
        nint ForegroundBeforeHandledClick,
        CapabilityState HandledState,
        nint HandledRouted,
        bool HandledClickAccepted,
        bool HandledKeyAccepted,
        int DownsAfterHandled,
        int ActivationsAfterHandled,
        int UnderneathKeysAfterHandled,
        int KeeperKeysAfterHandled,
        nint ForegroundAfterHandled,
        CapabilityState RestoreState,
        ChannelPass RestoredPass,
        CapabilityState WithdrawState,
        bool OverlayVisibleAfterWithdraw,
        int OverlayZIndexAfterWithdraw,
        ChannelPass WithdrawnPass)
    {
        /// <summary>Every pass's counts in one line, so a failure names the whole run rather than the
        /// one number that tripped.</summary>
        internal string Trace => string.Join(" | ",
            BaselinePass.Counts,
            PassThroughPass.Counts,
            $"foreground walk: keeper={PointerWindowProbe.DescribeWindow(ForegroundWithKeeper)} "
                + $"-> afterFlip={PointerWindowProbe.DescribeWindow(ForegroundAfterFlip)} "
                + $"-> beforeClick={PointerWindowProbe.DescribeWindow(ForegroundBeforeHandledClick)}",
            $"handled: downs={DownsAfterHandled} keys={UnderneathKeysAfterHandled} "
                + $"keeperKeys={KeeperKeysAfterHandled} activations={ActivationsAfterHandled} "
                + $"routedTo={PointerWindowProbe.DescribeWindow(HandledRouted)} "
                + $"foreground={PointerWindowProbe.DescribeWindow(ForegroundAfterHandled)}",
            RestoredPass.Counts,
            $"after withdraw: overlayVisible={OverlayVisibleAfterWithdraw} overlayZ={OverlayZIndexAfterWithdraw}",
            WithdrawnPass.Counts);
    }

    /// <summary>
    /// <b>Four channels, four times, over one point.</b>
    ///
    /// <para>The rig is three windows of this process's own: an ACTIVATABLE counting window at the
    /// point (the "application underneath"), a second activatable window elsewhere (the "keeper",
    /// whose only job is to hold the foreground while a click lands on the overlay), and the
    /// product's own <see cref="Win32OverlayPresence"/> over the first.</para>
    ///
    /// <para><b>Why the overlay's own confirmation is not enough and this run exists.</b>
    /// <c>Win32OverlayPresence.ConfirmInputRouting</c> already asks the window manager whether the
    /// point routes PAST the surface — but "past" only means "not to us". It does not say the point
    /// reaches the window the user was aiming at, it asks nothing about the keyboard, the wheel or a
    /// drag, and a hit test is a routing QUESTION rather than delivered input. This run injects the
    /// real thing through the real system input stream and counts what a real window procedure
    /// receives.</para>
    ///
    /// <para><b>Every injection is gated on the OS having already said the point is OURS</b> — a
    /// click synthesised at a point some other window owns lands in whatever the user really had
    /// there.</para>
    /// </summary>
    private static PassiveChannelRun RunPassiveChannels()
    {
        var bounds = UnderneathBounds;
        var (centreX, centreY) = bounds.Centre;
        var keeperBounds = KeeperBounds;
        var restoreCursor = PointerWindowProbe.CursorPosition();

        // Activatable on purpose: see UnderneathWindow's remarks. Created BEFORE anything else, so
        // leg 0 measures the desktop as the user would have found it.
        using var underneath = PointerWindowProbe.ScratchTarget.Create(
            bounds.X, bounds.Y, bounds.Width, bounds.Height, activatable: true);
        // ON ITS OWN THREAD, and that is load-bearing rather than tidy: Windows scopes the
        // foreground to a thread's INPUT QUEUE, so a keeper sharing this thread with the overlay
        // would make "the foreground moved" a statement about one queue's active window instead of
        // about WS_EX_NOACTIVATE. See ParkedScratchTarget's remarks for the measurement that
        // established it.
        using var keeper = PointerWindowProbe.ParkedScratchTarget.Start(
            keeperBounds.X, keeperBounds.Y, keeperBounds.Width, keeperBounds.Height);

        var underneathWindow = underneath?.Window ?? 0;
        var keeperWindow = keeper.Window;

        // ---- leg 0: the desktop under this point is genuinely interactive ----
        var tookForeground = underneathWindow != 0 && InputWindowProbe.TakeForeground(underneathWindow);
        var baselineRouted = underneathWindow == 0
            ? 0
            : PointerWindowProbe.HitTestAfterRaising(underneathWindow, centreX, centreY);
        var baseline = DriveFourChannels(
            "baseline (no overlay)", underneath, keeper, baselineRouted == underneathWindow, baselineRouted,
            centreX, centreY, HoldingThePath(overlay: null, underneathWindow));

        // ---- the overlay goes up over it, asking to be passed through ----
        using var overlay = new Win32OverlayPresence();
        var presentState = overlay.Present(new OverlaySurfaceRequest(bounds, Opacity, ClickThrough: true));
        var overlayWindow = overlay.NativeHandles.Window;

        // ---- leg 1: all four channels reach the window underneath THROUGH the overlay ----
        var (passRouted, overlayIndex, underneathIndex) =
            SettleWithOverlayOnTop(overlay, overlayWindow, underneathWindow, centreX, centreY, underneathWindow);
        var passThrough = DriveFourChannels(
            "click-through ON", underneath, keeper, passRouted == underneathWindow, passRouted, centreX, centreY,
            HoldingThePath(overlay, underneathWindow));

        // ---- leg 2: the handled click. The foreground moves AWAY first, or the absence is vacuous ----
        var keeperTookForeground = keeperWindow != 0 && InputWindowProbe.TakeForeground(keeperWindow);
        var keeperOnItsOwnQueue = keeper.OwningThreadId != 0
            && keeper.OwningThreadId != PointerWindowProbe.OwningThreadOf(overlayWindow);
        var foregroundWithKeeper = PointerWindowProbe.Foreground();

        var handledState = overlay.SetClickThrough(clickThrough: false);
        var foregroundAfterFlip = PointerWindowProbe.Foreground();

        var (handledRouted, _, _) =
            SettleWithOverlayOnTop(overlay, overlayWindow, underneathWindow, centreX, centreY, overlayWindow);
        var foregroundBeforeHandledClick = PointerWindowProbe.Foreground();

        // Injected whenever one of OUR OWN windows owns the point, not only when the overlay does:
        // gating on the overlay alone would let a leak skip the injection entirely and leave "nothing
        // arrived" trivially true.
        var handledClick = (handledRouted == overlayWindow || handledRouted == underneathWindow)
            && handledRouted != 0
            && PointerWindowProbe.InjectClickAt(centreX, centreY);
        var handledKey = handledClick && PointerWindowProbe.InjectKey(PointerWindowProbe.VkF13);

        // A FIXED drain budget, not a wait for something to happen: the claim is that nothing
        // arrives at the window underneath, and a message still in flight is not an absence.
        PointerWindowProbe.Drain();

        var downsAfterHandled = underneath?.Downs ?? 0;
        var activationsAfterHandled = underneath?.Activations ?? 0;
        var underneathKeysAfterHandled = underneath?.KeyDowns ?? 0;
        var keeperKeysAfterHandled = keeper.KeyDowns;
        var foregroundAfterHandled = PointerWindowProbe.Foreground();

        // ---- leg 3: click-through restored. The SAME click now arrives AND activates ----
        var restoreState = overlay.SetClickThrough(clickThrough: true);
        var (restoredRouted, _, _) =
            SettleWithOverlayOnTop(overlay, overlayWindow, underneathWindow, centreX, centreY, underneathWindow);
        var restored = DriveFourChannels(
            "click-through restored", underneath, keeper, restoredRouted == underneathWindow, restoredRouted,
            centreX, centreY, HoldingThePath(overlay, underneathWindow));

        // ---- leg 4: teardown gives all four channels back ----
        var withdrawState = overlay.Withdraw();

        // Read through the PROBE and not through the capability, and read before anything is raised.
        // The routing question below is asked after HitTestAfterRaising has pushed the window
        // underneath to the top of the topmost band — which it must be, to beat the foreign topmost
        // contention this collection exists to absorb — and that raise would ALSO win the point over
        // an overlay that was still up. So "the surface is genuinely gone" is asked separately here,
        // which is the clause a withdrawal that left an invisible input-blocking surface reds on
        // (.claude/skills/overlay-clickthrough/SKILL.md:30).
        var overlayVisibleAfterWithdraw = PointerWindowProbe.WindowIsVisible(overlayWindow);
        var overlayZIndexAfterWithdraw = PointerWindowProbe.ReadZOrder(overlayWindow).Index;

        var withdrawnRouted = underneathWindow == 0
            ? 0
            : PointerWindowProbe.HitTestAfterRaising(underneathWindow, centreX, centreY);
        var withdrawn = DriveFourChannels(
            "overlay withdrawn", underneath, keeper, withdrawnRouted == underneathWindow, withdrawnRouted,
            centreX, centreY, HoldingThePath(overlay: null, underneathWindow));

        PointerWindowProbe.MovePointerTo(restoreCursor.X, restoreCursor.Y);

        return new PassiveChannelRun(
            MachineHasInteractiveDesktop: PointerWindowProbe.MachineHasInteractiveDesktop,
            UnderneathWindow: underneathWindow,
            UnderneathIsUp: PointerWindowProbe.WindowIsVisible(underneathWindow),
            KeeperWindow: keeperWindow,
            KeeperIsUp: PointerWindowProbe.WindowIsVisible(keeperWindow),
            OverlayWindow: overlayWindow,
            UnderneathTookForegroundFirst: tookForeground,
            BaselinePass: baseline,
            PresentState: presentState,
            OverlayIndexWhilePassing: overlayIndex,
            UnderneathIndexWhilePassing: underneathIndex,
            PassThroughPass: passThrough,
            KeeperTookForeground: keeperTookForeground,
            KeeperOnItsOwnInputQueue: keeperOnItsOwnQueue,
            ForegroundWithKeeper: foregroundWithKeeper,
            ForegroundAfterFlip: foregroundAfterFlip,
            ForegroundBeforeHandledClick: foregroundBeforeHandledClick,
            HandledState: handledState,
            HandledRouted: handledRouted,
            HandledClickAccepted: handledClick,
            HandledKeyAccepted: handledKey,
            DownsAfterHandled: downsAfterHandled,
            ActivationsAfterHandled: activationsAfterHandled,
            UnderneathKeysAfterHandled: underneathKeysAfterHandled,
            KeeperKeysAfterHandled: keeperKeysAfterHandled,
            ForegroundAfterHandled: foregroundAfterHandled,
            RestoreState: restoreState,
            RestoredPass: restored,
            WithdrawState: withdrawState,
            OverlayVisibleAfterWithdraw: overlayVisibleAfterWithdraw,
            OverlayZIndexAfterWithdraw: overlayZIndexAfterWithdraw,
            WithdrawnPass: withdrawn);
    }

    /// <summary>
    /// One pass of all four desktop input channels, each drained until it lands.
    ///
    /// <para><b>Order matters and is fixed:</b> click, drag, wheel, keystroke. The click parks the
    /// pointer at the point and (on an activatable window) is what the OS uses to decide activation;
    /// the drag then travels from there and stays inside the rectangle; the wheel returns the pointer
    /// to the centre; the keystroke follows the FOCUS rather than the pointer and is last, so it is
    /// read against whatever the click just did to the foreground.</para>
    ///
    /// <para><b>Nothing is injected unless the OS has already said the point is ours</b>
    /// (<paramref name="pointIsOurs"/>). A pass over a point some other window owns would be clicking
    /// and typing into whatever the user really had there.</para>
    ///
    /// <para>Each wait is <c>PumpUntil</c> — a bounded iteration count with a yield, never a
    /// wall-clock wait — and it is a wait for a COUNTER TO MOVE, so a pass that receives nothing
    /// falls out at the ceiling and reports the unchanged count rather than hanging.</para>
    ///
    /// <para><b>The click's point is asked about; the drag's path was not, and that was the
    /// defect.</b> <paramref name="pointIsOurs"/> is one question about ONE point, and the drag then
    /// injected eight more at points nobody had claimed — so a foreign topmost window over part of
    /// the rectangle took every move while the clicks kept landing. The drag now holds each of its
    /// own points through <paramref name="hold"/>, which is this leg's own settle rather than a bare
    /// raise: on the pass-through leg it re-asserts the OVERLAY on top afterwards, so holding the
    /// path can never put the window underneath above the surface the leg measures through.</para>
    ///
    /// <para><b>And holding the path is not a complete defence, which is why the leg is now
    /// PRE-FLIGHTED.</b> The hold re-asserts this run's own stack, and a system-owned surface can
    /// keep a point regardless: measured on this machine as
    /// <c>drag path 0/8 steps delivered — the first that did not arrive was aimed at (1453,814),
    /// which the window manager gives to 0x10420 (class "Windows.UI.Core.CoreWindow") even with
    /// this run's own stack re-asserted over it</c>. That is a property of the machine's desktop,
    /// not of this port, so the whole path is walked before anything is injected and a leg whose
    /// path a FOREIGN process holds is refused by name rather than driven into somebody else's
    /// window and reported as a broken drag channel.</para>
    /// </summary>
    private static ChannelPass DriveFourChannels(
        string label,
        PointerWindowProbe.ScratchTarget? underneath,
        PointerWindowProbe.ParkedScratchTarget keeper,
        bool pointIsOurs,
        nint routed,
        int centreX,
        int centreY,
        Func<int, int, nint> hold)
    {
        var downsBefore = underneath?.Downs ?? 0;
        var dragBefore = underneath?.DragMoves ?? 0;
        var wheelBefore = underneath?.WheelNotches ?? 0;
        var keysBefore = underneath?.KeyDowns ?? 0;

        // THE PRE-FLIGHT, AND IT RUNS BEFORE THE FIRST INJECTION OF THE LEG. It asks this leg's own
        // hold about every point the drag below will travel over, and it is the only thing in this
        // file that can tell "the drag channel is broken" apart from "somebody else's window owns
        // the path" BEFORE the drag has already been driven into the wrong window. The facts refuse
        // on it; nothing here changes what is injected or measured when it comes back clear.
        var path = PointerWindowProbe.HoldWholeDragPath(
            label, centreX, centreY, DragDeltaX, DragDeltaY, DragSteps, hold);

        var click = pointIsOurs && PointerWindowProbe.InjectClickAt(centreX, centreY);
        PointerWindowProbe.PumpUntil(() => (underneath?.Downs ?? 0) > downsBefore);

        var dragReading = pointIsOurs && underneath is not null
            ? PointerWindowProbe.InjectDragAt(
                underneath, centreX, centreY, DragDeltaX, DragDeltaY, DragSteps, hold)
            : new PointerWindowProbe.DragReading(false, DragSteps, 0, 0, 0, 0);
        var drag = dragReading.Accepted;
        PointerWindowProbe.PumpUntil(() => (underneath?.DragMoves ?? 0) > dragBefore);

        var wheel = pointIsOurs && PointerWindowProbe.InjectWheelAt(centreX, centreY, WheelNotchesPerPass);
        PointerWindowProbe.PumpUntil(() => (underneath?.WheelNotches ?? 0) > wheelBefore);

        var key = pointIsOurs && PointerWindowProbe.InjectKey(PointerWindowProbe.VkF13);
        PointerWindowProbe.PumpUntil(() => (underneath?.KeyDowns ?? 0) > keysBefore);

        return new ChannelPass(
            Label: label,
            ClickAccepted: click,
            DragAccepted: drag,
            WheelAccepted: wheel,
            KeyAccepted: key,
            Routed: routed,
            Downs: underneath?.Downs ?? 0,
            DragMoves: underneath?.DragMoves ?? 0,
            Moves: underneath?.Moves ?? 0,
            Wheel: underneath?.WheelNotches ?? 0,
            KeyDowns: underneath?.KeyDowns ?? 0,
            KeeperKeyDowns: keeper.KeyDowns,
            Activations: underneath?.Activations ?? 0,
            Foreground: PointerWindowProbe.Foreground(),
            Drag: dragReading,
            Path: path);
    }

    /// <summary>
    /// The re-assertion one leg needs over one point of the drag path, and the window manager's
    /// answer about who owns it afterwards.
    ///
    /// <para>It is the same pair of moves <see cref="SettleWithOverlayOnTop"/> makes and for the
    /// same reasons — the window underneath is raised so it beats FOREIGN topmost contention (the
    /// residue <see cref="RealDesktopCollection"/> names), and the overlay is then put back on top
    /// of it, which is what the shipping product does on a cadence
    /// (<c>Services/Flash/FlashService.cs:3865-3872</c>, <c>ForceTopmost</c>). Passing
    /// <paramref name="overlay"/> as null is the baseline and post-teardown shape, where there is
    /// no surface that has to stay above anything.</para>
    ///
    /// <para>It never grants an answer: the counters the facts read are still the operating
    /// system's, and the handle returned here is only used to NAME whoever holds a point — before
    /// the drag, by <see cref="PointerWindowProbe.HoldWholeDragPath"/>, and after it, by the drag's
    /// own reading of a step that did not arrive.</para>
    /// </summary>
    private static Func<int, int, nint> HoldingThePath(Win32OverlayPresence? overlay, nint underneathWindow) =>
        (x, y) =>
        {
            var winner = PointerWindowProbe.HitTestAfterRaising(underneathWindow, x, y);
            if (overlay is null)
            {
                return winner;
            }

            overlay.Reassert();
            return PointerWindowProbe.HitTest(x, y);
        };

    /// <summary>
    /// Put the overlay ABOVE the window underneath and ask the window manager who owns the point,
    /// repeating while the answer is not <paramref name="expected"/>.
    ///
    /// <para><b>Why both windows are raised, in that order.</b> The window underneath is raised first
    /// so it beats any FOREIGN topmost window contesting the point — the residue
    /// <see cref="RealDesktopCollection"/> names, and the same reason
    /// <c>PointerWindowProbe.HitTestAfterRaising</c> exists. The overlay is then re-asserted on top of
    /// it, which is exactly what the shipping product does on a cadence
    /// (<c>Services/Flash/FlashService.cs:3865-3872</c>, <c>ForceTopmost</c>). Neither raise grants
    /// the answer: the caller still compares the returned hwnd, and the two z-order indices come back
    /// so the fact can require the overlay to have genuinely been in the way.</para>
    ///
    /// <para>Bounded iteration, never a wall-clock wait. The loop exits only when the routing answer
    /// AND the ordering are both what the leg needs in the SAME iteration, so a run that read a good
    /// hit test off a moment when the overlay had slipped below cannot report it as a pass-through.</para>
    /// </summary>
    private static (nint Routed, int OverlayIndex, int UnderneathIndex) SettleWithOverlayOnTop(
        Win32OverlayPresence overlay,
        nint overlayWindow,
        nint underneathWindow,
        int centreX,
        int centreY,
        nint expected)
    {
        var routed = (nint)0;
        var overlayIndex = -1;
        var underneathIndex = -1;

        for (var attempt = 0; attempt < PointerWindowProbe.MaxRaiseAttempts; attempt++)
        {
            PointerWindowProbe.HitTestAfterRaising(underneathWindow, centreX, centreY);
            overlay.Reassert();

            routed = PointerWindowProbe.HitTest(centreX, centreY);
            overlayIndex = PointerWindowProbe.ReadZOrder(overlayWindow).Index;
            underneathIndex = PointerWindowProbe.ReadZOrder(underneathWindow).Index;

            if (routed == expected && overlayIndex >= 0 && underneathIndex > overlayIndex)
            {
                return (routed, overlayIndex, underneathIndex);
            }
        }

        return (routed, overlayIndex, underneathIndex);
    }

    // -------------------------------------------------------------------------------------------
    //  the drag instrument's own control: a window over the PATH and not over the press point
    // -------------------------------------------------------------------------------------------

    private static readonly Lazy<HeldPathRun> LazyHeldPath =
        new(RunHeldPath, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The held-path run. Cached: it puts two real windows on the user's screen and
    /// synthesises a drag between them.</summary>
    internal static HeldPathRun HeldPath => LazyHeldPath.Value;

    /// <summary>Where the held-path run works: the same bottom-right column as
    /// <see cref="UnderneathBounds"/> and <see cref="KeeperBounds"/>, clear of both, and clear of the
    /// upper-middle row <see cref="RunTaskSwitcher"/> uses. Nothing in this assembly may share a
    /// point with a run that synthesises clicks.</summary>
    internal static OverlayBounds ContendedBounds
    {
        get
        {
            var (width, height) = PointerWindowProbe.PrimarySize;
            return new OverlayBounds(Math.Max(0, width - 320), Math.Max(0, height - 720), 240, 160);
        }
    }

    /// <summary>
    /// <b>The drag instrument's control, and the regression guard for the defect that made four
    /// facts in this file unexplainable for a day.</b>
    ///
    /// <para>A second window of this process is put over the drag's PATH and deliberately NOT over
    /// its press point, and then a drag is run. That asymmetry is the whole thing: a BUTTON message
    /// is posted to the queue of the window under the cursor when the event is injected, while a
    /// <c>WM_MOUSEMOVE</c> is SYNTHESISED at peek time for whatever owns the cursor's point then —
    /// so a window over part of a rectangle takes every move and leaves every click, and the
    /// resulting <c>downs=2 dragMoves=0 moves=2</c> reads exactly like an injection that does not
    /// work at all. It is not: <see cref="Downs"/> is asserted to have moved, in the same run, at
    /// the same rectangle.</para>
    ///
    /// <para><b>Why a second window of OUR OWN rather than a foreign one.</b> A foreign process is
    /// what really did this, and no fact may depend on one existing — <see cref="RealDesktopCollection"/>
    /// says plainly that a foreign topmost window can never be excluded and this run does not try.
    /// A window this process owns reproduces the ONE thing under test, which is the z-order, and
    /// does it deterministically.</para>
    /// </summary>
    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation flips on.</param>
    /// <param name="Target">The window the drag is aimed at.</param>
    /// <param name="TargetIsUp">The OS reports it visible.</param>
    /// <param name="TargetTookForeground">It holds the foreground BEFORE the drag. Without that the
    /// drag's own button-down activates it, activation raises it over the contender, and the drag
    /// steps over the obstruction for a reason that has nothing to do with holding its path.</param>
    /// <param name="Contender">The window placed over the drag's path.</param>
    /// <param name="ContenderIsUp">Ditto.</param>
    /// <param name="OwnerOfPressPoint">Who the window manager gives the press point to. It must be
    /// the target, or the press below lands on nothing and the asymmetry is not constructed.</param>
    /// <param name="OwnerOfFirstStep">And who it gives the drag's first step to. <b>It must be the
    /// CONTENDER</b>, or this run is a drag with nothing in the way and proves nothing.</param>
    /// <param name="DownsBefore">The target's click count before the drag.</param>
    /// <param name="Downs">And after: the press still lands, which is the half of the asymmetry that
    /// sent three diagnoses after a broken injection.</param>
    /// <param name="DragMoves">The target's DRAG count after — the half that only arrives because
    /// the drag holds its own path.</param>
    /// <param name="Drag">The instrument's own account of the path.</param>
    internal sealed record HeldPathRun(
        bool MachineHasInteractiveDesktop,
        nint Target,
        bool TargetIsUp,
        bool TargetTookForeground,
        nint Contender,
        bool ContenderIsUp,
        nint OwnerOfPressPoint,
        nint OwnerOfFirstStep,
        int DownsBefore,
        int Downs,
        int DragMoves,
        PointerWindowProbe.DragReading Drag)
    {
        internal string Trace =>
            $"target={PointerWindowProbe.DescribeWindow(Target)} up={TargetIsUp} fg={TargetTookForeground} | "
            + $"contender={PointerWindowProbe.DescribeWindow(Contender)} up={ContenderIsUp} | "
            + $"pressPoint->{PointerWindowProbe.DescribeWindow(OwnerOfPressPoint)} "
            + $"firstStep->{PointerWindowProbe.DescribeWindow(OwnerOfFirstStep)} | "
            + $"downs {DownsBefore}->{Downs} dragMoves={DragMoves} | {Drag.Describe}";
    }

    private static HeldPathRun RunHeldPath()
    {
        var bounds = ContendedBounds;
        var (pressX, pressY) = bounds.Centre;
        var restoreCursor = PointerWindowProbe.CursorPosition();

        using var target = PointerWindowProbe.ScratchTarget.Create(
            bounds.X, bounds.Y, bounds.Width, bounds.Height, activatable: true);
        var targetWindow = target?.Window ?? 0;

        // THE FOREGROUND FIRST, and it is not tidiness — it was measured. Without it the drag's own
        // button-down ACTIVATES this window, activation brings it to the top of its band, and the
        // contender below is stepped over for free: the first draft of this run passed with the hold
        // deleted for exactly that reason. The passive-channel legs above already hold the foreground
        // here (InputWindowProbe.TakeForeground on the window underneath), so taking it is also what
        // makes this run the same shape as the thing it guards.
        var tookForeground = targetWindow != 0 && InputWindowProbe.TakeForeground(targetWindow);

        // Offset by less than one step, so it covers every point of the path and none of the press.
        using var contender = PointerWindowProbe.ScratchTarget.Create(
            pressX + 3, pressY + 2, DragDeltaX + 160, DragDeltaY + 160);
        var contenderWindow = contender?.Window ?? 0;

        // The target is raised first and the contender on top of it, which is the state the defect
        // needs and the state this run must PROVE it built before it may claim anything.
        PointerWindowProbe.HitTestAfterRaising(targetWindow, pressX, pressY);
        var firstStepX = pressX + (DragDeltaX / DragSteps);
        var firstStepY = pressY + (DragDeltaY / DragSteps);
        var ownerOfFirstStep = PointerWindowProbe.HitTestAfterRaising(contenderWindow, firstStepX, firstStepY);
        var ownerOfPressPoint = PointerWindowProbe.HitTest(pressX, pressY);

        var downsBefore = target?.Downs ?? 0;
        var reading = target is null || ownerOfPressPoint != targetWindow
            ? new PointerWindowProbe.DragReading(false, DragSteps, 0, 0, 0, 0)
            : PointerWindowProbe.InjectDragAt(
                target, pressX, pressY, DragDeltaX, DragDeltaY, DragSteps,
                HoldingThePath(overlay: null, targetWindow));

        var run = new HeldPathRun(
            MachineHasInteractiveDesktop: PointerWindowProbe.MachineHasInteractiveDesktop,
            Target: targetWindow,
            TargetIsUp: PointerWindowProbe.WindowIsVisible(targetWindow),
            TargetTookForeground: tookForeground,
            Contender: contenderWindow,
            ContenderIsUp: PointerWindowProbe.WindowIsVisible(contenderWindow),
            OwnerOfPressPoint: ownerOfPressPoint,
            OwnerOfFirstStep: ownerOfFirstStep,
            DownsBefore: downsBefore,
            Downs: target?.Downs ?? 0,
            DragMoves: target?.DragMoves ?? 0,
            Drag: reading);

        PointerWindowProbe.MovePointerTo(restoreCursor.X, restoreCursor.Y);
        return run;
    }

    // -------------------------------------------------------------------------------------------
    //  the PRE-FLIGHT's own control: a real foreign window, in a real second process
    // -------------------------------------------------------------------------------------------

    private static readonly Lazy<ForeignHoldRun> LazyForeignHold =
        new(RunForeignHold, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The foreign-hold run. Cached: it puts a window on the user's screen and starts a
    /// second process that puts another one there.</summary>
    internal static ForeignHoldRun ForeignHold => LazyForeignHold.Value;

    /// <summary>Where the pre-flight's control puts OUR window: the same bottom-right column as
    /// every other rig in this file, above <see cref="ContendedBounds"/> and clear of it.</summary>
    internal static OverlayBounds PreflightBounds
    {
        get
        {
            var (width, height) = PointerWindowProbe.PrimarySize;
            return new OverlayBounds(Math.Max(0, width - 320), Math.Max(0, height - 940), 240, 160);
        }
    }

    /// <summary>
    /// And where the CHILD PROCESS puts its topmost window: immediately to the left of
    /// <see cref="PreflightBounds"/>, sharing its edge and never overlapping it.
    ///
    /// <para><b>Disjoint on purpose, and this is the one thing about the construction worth
    /// arguing.</b> Two overlapping topmost windows are a RACE — whichever process re-asserted last
    /// owns the point — and a control that flips with a race measures nothing. Not overlapping makes
    /// the reading exact: our raise cannot take a point our window does not cover, so the window
    /// manager gives it to the child every time. What that costs is stated rather than hidden: this
    /// reproduces "a foreign window owns a point of the path", which is what the pre-flight's rule
    /// is written on, and NOT "a foreign window occludes our own window despite the re-assert",
    /// which is the shape the real <c>CoreWindow</c> recurrence had. No user-mode process can
    /// construct that second shape deterministically — a topmost window raised after ours wins, and
    /// ours raised after it wins back, which is precisely why the drag-hold fix recovers 8/8 against
    /// an interloper re-asserting in a tight loop and still loses to a system shell surface.</para>
    /// </summary>
    internal static OverlayBounds InterloperBounds
    {
        get
        {
            var (width, height) = PointerWindowProbe.PrimarySize;
            return new OverlayBounds(Math.Max(0, width - 560), Math.Max(0, height - 940), 240, 160);
        }
    }

    /// <summary>Total travel of the crossing path: far enough left to leave our own 240-wide window
    /// and enter the child's, straight along one row so every point stays inside both rectangles'
    /// vertical span.</summary>
    private const int CrossingDeltaX = -280;

    /// <summary>
    /// Which point of the crossing path is the first the child owns, derived here rather than
    /// discovered: the press point sits 120 into our own 240-wide window, each of the eight steps
    /// travels 35 further left, so steps 1 to 3 are still ours and step 4 is 20 past our left edge.
    /// <b>The fact asserts this exact index</b>, which is how "the press point was ours and the PATH
    /// was not" is a reading rather than a hope.
    /// </summary>
    internal const int FirstCrossedPoint = 4;

    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation flips on.</param>
    /// <param name="Ours">The window this process placed, which the press point belongs to.</param>
    /// <param name="OursIsUp">The OS reports it visible.</param>
    /// <param name="Interloper">The topmost window the CHILD PROCESS placed.</param>
    /// <param name="InterloperIsUp">The OS reports it visible.</param>
    /// <param name="InterloperProcess">And the pid that owns it, so "foreign" is a reading and not
    /// an assumption — the whole rule turns on this being some other process.</param>
    /// <param name="OwnerOfPressPoint">Who the window manager gives the press point to. It must be
    /// OURS, or the crossing path below would be refused for its very first point and the fact would
    /// prove nothing about the path.</param>
    /// <param name="OwnerOfInterloperCentre">And who it gives the child's own centre to. It must be
    /// the CHILD, or the interloper never reached the desktop and the refusal below would be about
    /// some third window.</param>
    /// <param name="AcrossTheInterloper">The pre-flight over a path that starts on our window and
    /// crosses onto the child's. <b>It must refuse, and name the child.</b></param>
    /// <param name="InsideOurOwnWindow">The pre-flight over the ordinary path, wholly inside our own
    /// window, on the same desktop moments later. <b>It must NOT refuse</b> — and it must have
    /// walked every point to say so.</param>
    internal sealed record ForeignHoldRun(
        bool MachineHasInteractiveDesktop,
        nint Ours,
        bool OursIsUp,
        nint Interloper,
        bool InterloperIsUp,
        int InterloperProcess,
        nint OwnerOfPressPoint,
        nint OwnerOfInterloperCentre,
        PointerWindowProbe.PathHold AcrossTheInterloper,
        PointerWindowProbe.PathHold InsideOurOwnWindow)
    {
        internal string Trace =>
            $"ours={PointerWindowProbe.DescribeWindow(Ours)} up={OursIsUp} | "
            + $"interloper={PointerWindowProbe.DescribeWindow(Interloper)} up={InterloperIsUp} "
            + $"pid={InterloperProcess} (this process is {Environment.ProcessId}) | "
            + $"pressPoint->{PointerWindowProbe.DescribeWindow(OwnerOfPressPoint)} "
            + $"interloperCentre->{PointerWindowProbe.DescribeWindow(OwnerOfInterloperCentre)} | "
            + $"across: {AcrossTheInterloper.Describe} | inside: {InsideOurOwnWindow.Describe}";
    }

    private static ForeignHoldRun RunForeignHold()
    {
        var clear = PointerWindowProbe.PathHold.Clear("no desktop to walk a path on", 0);
        if (!PointerWindowProbe.MachineHasInteractiveDesktop)
        {
            return new ForeignHoldRun(false, 0, false, 0, false, 0, 0, 0, clear, clear);
        }

        var ours = PreflightBounds;
        var (pressX, pressY) = ours.Centre;
        var (foreignX, foreignY) = InterloperBounds.Centre;

        using var target = PointerWindowProbe.ScratchTarget.Create(
            ours.X, ours.Y, ours.Width, ours.Height, activatable: true);
        var targetWindow = target?.Window ?? 0;

        using var interloper = ForeignTopmostChild.Launch(InterloperBounds);

        // The BASELINE leg's own hold, not a weaker one: raise our window, ask the window manager.
        // Anything less here would make this control easier to satisfy than the thing it controls.
        var hold = HoldingThePath(overlay: null, targetWindow);

        var ownerOfPressPoint = PointerWindowProbe.HitTestAfterRaising(targetWindow, pressX, pressY);
        var ownerOfInterloperCentre = PointerWindowProbe.HitTest(foreignX, foreignY);

        var across = PointerWindowProbe.HoldWholeDragPath(
            "a path that crosses a foreign window", pressX, pressY, CrossingDeltaX, 0, DragSteps, hold);

        // The SAME rig, the same hold, the same press point, moments later — only the travel
        // differs. A pre-flight that refused here would refuse on a desktop that is fine, which is
        // strictly worse than the intermittent it was built to replace.
        var inside = PointerWindowProbe.HoldWholeDragPath(
            "a path wholly inside our own window", pressX, pressY, DragDeltaX, DragDeltaY, DragSteps, hold);

        var run = new ForeignHoldRun(
            MachineHasInteractiveDesktop: true,
            Ours: targetWindow,
            OursIsUp: PointerWindowProbe.WindowIsVisible(targetWindow),
            Interloper: interloper.Window,
            InterloperIsUp: PointerWindowProbe.WindowIsVisible(interloper.Window),
            InterloperProcess: interloper.Pid,
            OwnerOfPressPoint: ownerOfPressPoint,
            OwnerOfInterloperCentre: ownerOfInterloperCentre,
            AcrossTheInterloper: across,
            InsideOurOwnWindow: inside);

        // No cursor is restored here because none was borrowed: this run asks the window manager
        // routing questions and injects nothing at all.
        return run;
    }

    // -------------------------------------------------------------------------------------------
    //  (c): does the shell's own task-window rule offer any surface of ours?
    // -------------------------------------------------------------------------------------------

    private static readonly Lazy<TaskSwitcherRun> LazyTaskSwitcher =
        new(RunTaskSwitcher, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The task-switcher run. Cached: it puts four real windows on the user's screen and one
    /// of them takes the foreground.</summary>
    internal static TaskSwitcherRun TaskSwitcher => LazyTaskSwitcher.Value;

    /// <summary>A window's handle beside what the shell's rule answered about it, so a failure names
    /// the clause that decided rather than only the verdict.</summary>
    internal sealed record SurfaceVerdict(
        string Name, nint Window, bool Visible, PointerWindowProbe.TaskSwitcherReading Reading)
    {
        internal string Describe =>
            $"{Name} {PointerWindowProbe.DescribeWindow(Window)}: visible={Visible}, "
            + $"taskWindow={Reading.IsOrdinaryTaskSwitchingWindow} ({Reading.Clause})";
    }

    /// <summary>
    /// <b>What this run can say, and what it cannot.</b>
    ///
    /// <para>It applies the SHELL'S DOCUMENTED task-window predicate — visible, not DWM-cloaked, and
    /// either unowned-and-not-a-tool-window or forced in with <c>WS_EX_APPWINDOW</c> — to the
    /// operating system's own read-backs about real windows this port really placed. That is a
    /// statement about window STATE under a published rule.</para>
    ///
    /// <para><b>It is NOT the rendered switcher and NOT the taskbar.</b> No public API exposes either
    /// list; the shell builds them itself and may filter further. A claim about what a human sees
    /// when they hold Alt, or about what appears on their taskbar, is a HEADED claim
    /// (<c>client/docs/verification-harness.md</c>) and nothing in this file discharges it. What this
    /// run does discharge is the one thing a source guard over <c>WS_EX_TOOLWINDOW</c> could not: the
    /// bits are read back from the OS after the window is on screen, not assumed from the write —
    /// <c>Overlay/Win32OverlayPresence.cs:504-511</c> records a measured run where every style write
    /// SUCCEEDED and the ex-style read back wrong anyway.</para>
    /// </summary>
    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation flips on.</param>
    /// <param name="BaselineWindows">How many visible top-level windows this PROCESS already owned
    /// before the run placed anything. Everything below is measured against it, so an earlier
    /// fixture's survivor is never scored against this run.</param>
    /// <param name="Control"><b>The control that must answer YES.</b> An ordinary, visible, unowned,
    /// activatable, NON-tool-window popup — the shape the rule offers. Without it the rule could
    /// answer NO to everything and the fact would prove nothing.</param>
    /// <param name="Overlay">The click-through overlay's verdict.</param>
    /// <param name="PointerTarget">The pointer target's — the surface that is deliberately NOT
    /// click-through (<c>Pointer/Win32PointerSurface.cs:850-852</c>).</param>
    /// <param name="Card">The lock card's — the one surface that deliberately TAKES the foreground
    /// and the keyboard (<c>Input/Win32InputPresence.cs:1097-1099</c>), and therefore the one most
    /// likely to be offered if its tool-window bit did not hold.</param>
    /// <param name="OverlayPresentState">What the overlay's <c>Present</c> returned.</param>
    /// <param name="PointerOpenState">What the pointer surface's <c>Open</c> returned.</param>
    /// <param name="CardPromptState">What the card's <c>Prompt</c> returned.</param>
    /// <param name="NewWindowCount">How many visible top-level windows this process gained.</param>
    /// <param name="OfferedByTheRule">Every one of those new windows the rule OFFERS. It must be
    /// exactly the control: a surface of ours in that list is the invariant broken, and a stray
    /// window of ours in it is a window in the user's switcher that no surface accounts for.</param>
    internal sealed record TaskSwitcherRun(
        bool MachineHasInteractiveDesktop,
        int BaselineWindows,
        SurfaceVerdict Control,
        SurfaceVerdict Overlay,
        SurfaceVerdict PointerTarget,
        SurfaceVerdict Card,
        CapabilityState OverlayPresentState,
        CapabilityState PointerOpenState,
        CapabilityState CardPromptState,
        int NewWindowCount,
        IReadOnlyList<SurfaceVerdict> OfferedByTheRule)
    {
        /// <summary>The offered set, described from the readings taken WHILE the windows were up. A
        /// description computed later would ask the OS about destroyed handles and report "not
        /// visible" for everything, which is the opposite of the failure being reported.</summary>
        internal string Offered => OfferedByTheRule.Count == 0
            ? "(nothing of ours)"
            : string.Join("; ", OfferedByTheRule.Select(v => v.Describe));

        internal string Trace => string.Join(" | ",
            Control.Describe, Overlay.Describe, PointerTarget.Describe, Card.Describe,
            $"offered={Offered}");
    }

    private static TaskSwitcherRun RunTaskSwitcher()
    {
        var (screenWidth, screenHeight) = PointerWindowProbe.PrimarySize;
        var baseline = PointerWindowProbe.OurVisibleTopLevelWindows();

        // Four disjoint rectangles across the upper middle of the primary display, clear of the
        // passive-channel run's bottom-right corner. Nothing is clicked here, so they need only be
        // separately visible.
        var overlayBounds = new OverlayBounds(
            Math.Max(0, (screenWidth / 2) - 500), Math.Max(0, (screenHeight / 2) - 300), 200, 150);
        var pointerBounds = new PointerBounds(
            Math.Max(0, (screenWidth / 2) - 250), Math.Max(0, (screenHeight / 2) - 300), 160, 160);
        var cardBounds = new InputBounds(
            Math.Max(0, (screenWidth / 2) + 40), Math.Max(0, (screenHeight / 2) - 300), 360, 180);
        var controlBounds = new OverlayBounds(
            Math.Max(0, (screenWidth / 2) - 500), Math.Max(0, (screenHeight / 2) - 100), 220, 140);

        using var overlay = new Win32OverlayPresence();
        var overlayPresent = overlay.Present(new OverlaySurfaceRequest(overlayBounds, Opacity, ClickThrough: true));
        var overlayWindow = overlay.NativeHandles.Window;

        using var pointer = new Win32PointerSurface();
        var pointerOpen = pointer.Open(new PointerTargetRequest(pointerBounds, 0x00201020, 0x00E0C0FF), out var target);
        var pointerWindow = pointer.NativeHandlesFor(target).Window;

        using var card = new Win32InputPresence();
        var cardPrompt = card.Prompt(new InputPromptRequest(
            cardBounds,
            new InputPromptContent("say this", "1 of 1", string.Empty, "Press Esc to close"),
            _ => { }));
        var cardWindow = card.NativeHandles.Window;

        // THE CONTROL, and it is the whole reason this run is not a predicate that always says no:
        // an ordinary unowned activatable popup with NO WS_EX_TOOLWINDOW, which the shell's rule must
        // offer. Placed last so it is the newest window and cannot be mistaken for a survivor.
        using var control = PointerWindowProbe.ScratchTarget.Create(
            controlBounds.X, controlBounds.Y, controlBounds.Width, controlBounds.Height,
            activatable: true, toolWindow: false);
        var controlWindow = control?.Window ?? 0;

        var verdicts = new[]
        {
            Verdict("the ordinary control window", controlWindow),
            Verdict("the click-through overlay", overlayWindow),
            Verdict("the pointer target", pointerWindow),
            Verdict("the lock card", cardWindow),
        };

        var newWindows = PointerWindowProbe.OurVisibleTopLevelWindows().Except(baseline).ToArray();
        var offered = newWindows
            .Select(w => Verdict("a window of ours", w))
            .Where(v => v.Reading.IsOrdinaryTaskSwitchingWindow)
            .ToArray();

        pointer.Close(target);

        return new TaskSwitcherRun(
            MachineHasInteractiveDesktop: PointerWindowProbe.MachineHasInteractiveDesktop,
            BaselineWindows: baseline.Count,
            Control: verdicts[0],
            Overlay: verdicts[1],
            PointerTarget: verdicts[2],
            Card: verdicts[3],
            OverlayPresentState: overlayPresent,
            PointerOpenState: pointerOpen,
            CardPromptState: cardPrompt,
            NewWindowCount: newWindows.Length,
            OfferedByTheRule: offered);
    }

    private static SurfaceVerdict Verdict(string name, nint window) => new(
        name,
        window,
        PointerWindowProbe.WindowIsVisible(window),
        PointerWindowProbe.ReadTaskSwitcherState(window));
}

/// <summary>
/// <b>A REAL foreign window: this same test executable, re-entered as a second process, holding one
/// top-most window over a rectangle the parent names.</b>
///
/// <para><b>Why a second process and not another window of our own.</b> The pre-flight this exists
/// to control refuses on exactly one thing — the window manager gives a point of the drag path to a
/// window owned by ANOTHER PROCESS — and a window of ours can never satisfy it. That narrowness is
/// what keeps the refusal from becoming an escape hatch, so its control has to be genuinely foreign
/// or it would be exercising a different rule from the one that ships. The parent asserts the
/// child's exact handle and its pid, so a refusal naming some third window fails rather than
/// passes.</para>
///
/// <para><b>Why a module initializer, and why it never contends for the lease.</b> A module
/// initializer runs before the entry point of its own module, so the child takes over ahead of the
/// xunit runner: it discovers no test, writes no TRX, and — decisively — never asks for
/// <see cref="RealDesktopLease"/>, which the PARENT holds for the whole of this run. A child that
/// reached the runner would block on that lease while the parent blocked on the child. Same shape,
/// same reason, as <see cref="SurfaceExitChild"/>.</para>
///
/// <para><b>Why the rectangle travels in the child's own environment block.</b> The xunit v3 runner
/// owns this executable's command line and rejects what it does not recognise, so the mode is a
/// variable — set on <see cref="ProcessStartInfo.Environment"/> and never through
/// <c>Environment.SetEnvironmentVariable</c>, which would be visible to every other fact in this
/// assembly. Carrying the RECTANGLE in it rather than recomputing it child-side is deliberate: two
/// processes agreeing on <c>GetSystemMetrics</c> is an assumption, and the parent asserts against
/// the geometry it sent.</para>
/// </summary>
internal sealed class ForeignTopmostChild : IDisposable
{
    /// <summary>Set on the child's own environment block only. Its value is the rectangle,
    /// <c>x,y,width,height</c>.</summary>
    internal const string ModeVariable = "CCP_FOREIGN_TOPMOST_CHILD";

    /// <summary>The handshake the parent parses: <c>READY pid hwnd</c>.</summary>
    internal const string Ready = "READY";

    private readonly Process _process;

    private ForeignTopmostChild(Process process) => _process = process;

    /// <summary>The child's process id, read from its own announcement.</summary>
    internal int Pid { get; private set; }

    /// <summary>The top-most window it placed.</summary>
    internal nint Window { get; private set; }

    /// <summary>
    /// Starts the child and returns once it has told us about the window the OS gave it. Every
    /// failure path disposes, so a handshake that throws cannot leave a foreign top-most window
    /// parked on the owner's desktop for the rest of the run.
    /// </summary>
    internal static ForeignTopmostChild Launch(OverlayBounds bounds)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment[ModeVariable] = string.Join(
            ',',
            bounds.X.ToString(CultureInfo.InvariantCulture),
            bounds.Y.ToString(CultureInfo.InvariantCulture),
            bounds.Width.ToString(CultureInfo.InvariantCulture),
            bounds.Height.ToString(CultureInfo.InvariantCulture));

        var process = Process.Start(start)
            ?? throw new Xunit.Sdk.XunitException(
                $"could not start a child of {Environment.ProcessPath} — this control needs a window owned by "
                + "another process, and there is no such window without one");

        var child = new ForeignTopmostChild(process); // owns the process BEFORE the handshake can throw
        try
        {
            var announcement = process.StandardOutput.ReadLineAsync();
            TestWait.UntilSync(
                () => announcement.IsCompleted,
                "the foreign-window child to announce the top-most window it placed",
                () => $"child pid {process.Id}, exited={process.HasExited}");

            if (announcement.Result is not { } line)
            {
                throw new Xunit.Sdk.XunitException(
                    "the foreign-window child closed its output without announcing anything (exit code "
                    + $"{process.ExitCode.ToString(CultureInfo.InvariantCulture)}). "
                    + $"Its standard error: {process.StandardError.ReadToEnd()}");
            }

            var fields = line.Split(' ');
            if (fields.Length != 3 || fields[0] != Ready)
            {
                throw new Xunit.Sdk.XunitException(
                    $"the foreign-window child's announcement was not the agreed handshake: '{line}'");
            }

            child.Pid = int.Parse(fields[1], CultureInfo.InvariantCulture);
            child.Window = (nint)long.Parse(fields[2], CultureInfo.InvariantCulture);
            return child;
        }
        catch
        {
            child.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Ends the child, and waits for the OPERATING SYSTEM to agree it is gone rather than for the
    /// child to say so — the window goes with the process, which is the property
    /// <see cref="SurfaceExitTests"/> measures directly. Bounded through the approved helper.
    /// </summary>
    public void Dispose()
    {
        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: false);
            }
            catch (InvalidOperationException)
            {
                // It exited between the question and the kill. Nothing left to end.
            }
        }

        TestWait.UntilSync(
            () => _process.HasExited,
            $"the foreign-window child (pid {Pid}) to leave the process table, taking its top-most window with it");
        _process.Dispose();
    }

    [ModuleInitializer]
    internal static void TakeOverWhenLaunchedAsAChild()
    {
        if (Environment.GetEnvironmentVariable(ModeVariable) is not { Length: > 0 } rectangle)
        {
            return;
        }

        Environment.Exit(Play(rectangle));
    }

    private static int Play(string rectangle)
    {
        var fields = rectangle.Split(',');
        if (fields.Length != 4)
        {
            Console.Error.WriteLine($"{ModeVariable} must be x,y,width,height; it was '{rectangle}'");
            return 2;
        }

        var x = int.Parse(fields[0], CultureInfo.InvariantCulture);
        var y = int.Parse(fields[1], CultureInfo.InvariantCulture);
        var width = int.Parse(fields[2], CultureInfo.InvariantCulture);
        var height = int.Parse(fields[3], CultureInfo.InvariantCulture);

        // EVERYTHING HERE IS ON THE CALLING THREAD, and that is not tidiness — it was measured, twice
        // now. A module initializer holds its module's initialization lock while it runs, so a
        // SECOND thread whose body touches any type of this module blocks on that lock while the
        // main thread waits for the thread: the parked variant of this window deadlocked exactly
        // that way and the parent timed out at 20 s with the child alive and silent
        // (SurfaceExitObservations.cs:486-492 records the same finding for the same reason).
        using var window = PointerWindowProbe.ScratchTarget.Create(x, y, width, height);

        // So this window's thread will not pump, and THAT is what this call is for: the shell
        // replaces a visible top-level window whose thread stops answering with a ghost of its own,
        // and the parent would then be hit-testing a handle the system substituted for this one.
        PointerWindowProbe.DisableWindowGhosting();

        Console.Out.WriteLine($"{Ready} {Environment.ProcessId} {(long)(window?.Window ?? 0)}");
        Console.Out.Flush();

        // Parked on a pipe the parent holds open — waiting for the parent to speak, or to die, and
        // never for time to pass. A parent that is killed outright closes this pipe, so the read
        // returns null and the window goes with this process rather than being left on the owner's
        // desktop.
        Console.ReadLine();
        return 0;
    }
}
