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
        nint Foreground)
    {
        internal bool EveryInjectionAccepted => ClickAccepted && DragAccepted && WheelAccepted && KeyAccepted;

        internal string Counts =>
            $"{Label}: downs={Downs} dragMoves={DragMoves} moves={Moves} wheel={Wheel} keys={KeyDowns} "
            + $"keeperKeys={KeeperKeyDowns} activations={Activations} "
            + $"routedTo={PointerWindowProbe.DescribeWindow(Routed)} "
            + $"foreground={PointerWindowProbe.DescribeWindow(Foreground)} "
            + $"accepted(click/drag/wheel/key)={ClickAccepted}/{DragAccepted}/{WheelAccepted}/{KeyAccepted}";
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
            centreX, centreY);

        // ---- the overlay goes up over it, asking to be passed through ----
        using var overlay = new Win32OverlayPresence();
        var presentState = overlay.Present(new OverlaySurfaceRequest(bounds, Opacity, ClickThrough: true));
        var overlayWindow = overlay.NativeHandles.Window;

        // ---- leg 1: all four channels reach the window underneath THROUGH the overlay ----
        var (passRouted, overlayIndex, underneathIndex) =
            SettleWithOverlayOnTop(overlay, overlayWindow, underneathWindow, centreX, centreY, underneathWindow);
        var passThrough = DriveFourChannels(
            "click-through ON", underneath, keeper, passRouted == underneathWindow, passRouted, centreX, centreY);

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
            centreX, centreY);

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
            centreX, centreY);

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
    /// </summary>
    private static ChannelPass DriveFourChannels(
        string label,
        PointerWindowProbe.ScratchTarget? underneath,
        PointerWindowProbe.ParkedScratchTarget keeper,
        bool pointIsOurs,
        nint routed,
        int centreX,
        int centreY)
    {
        var downsBefore = underneath?.Downs ?? 0;
        var dragBefore = underneath?.DragMoves ?? 0;
        var wheelBefore = underneath?.WheelNotches ?? 0;
        var keysBefore = underneath?.KeyDowns ?? 0;

        var click = pointIsOurs && PointerWindowProbe.InjectClickAt(centreX, centreY);
        PointerWindowProbe.PumpUntil(() => (underneath?.Downs ?? 0) > downsBefore);

        var drag = pointIsOurs && underneath is not null
            && PointerWindowProbe.InjectDragAt(
                underneath, centreX, centreY, DragDeltaX, DragDeltaY, DragSteps);
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
            Foreground: PointerWindowProbe.Foreground());
    }

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
