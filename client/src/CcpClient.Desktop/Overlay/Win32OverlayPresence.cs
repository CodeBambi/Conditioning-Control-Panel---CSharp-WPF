using System.Runtime.InteropServices;
using System.Text;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Overlay;

/// <summary>
/// The handle a test (or a diagnostic surface) needs to ask the OPERATING SYSTEM about this
/// presence's surface without asking the presence itself. Exposed on purpose, for the same reason
/// <see cref="Tray.TrayNativeHandles"/> is: a capability that can only be interrogated through its
/// own claim is exactly the capability nobody can verify — and an overlay is the worst case of
/// that, because every one of its failure modes is invisible from inside the process.
/// </summary>
/// <param name="Window">The top-level window the surface is, or zero before one exists.</param>
public readonly record struct OverlayNativeHandles(nint Window);

/// <summary>
/// The Windows overlay backend: a real layered, click-through, always-on-top top-level window.
///
/// <para><b>The rule this class obeys.</b> Nothing here returns
/// <see cref="CapabilityState.Available"/> until the operating system has been asked back and has
/// confirmed, one at a time: the window exists and is visible; the OS holds exactly the rectangle
/// that was requested; the OS holds a non-zero <c>LWA_ALPHA</c> for it, so the compositor has
/// something to draw; the extended-style read-back carries every bit that was written; the OS's
/// own top-level z-order puts it above every ordinary window; the window manager's hit test routes
/// a point inside it the way the request asked, proven in BOTH polarities in the same breath; and
/// the surface did not take the foreground. Any "no" is a typed
/// <see cref="CapabilityState.Unavailable"/> carrying the failing check and the Win32 last-error.
/// There are exactly FOUR places in this file that construct <c>Available</c> - Present,
/// SetClickThrough, Paint and Withdraw - and every one of them sits downstream of an OS round trip
/// that was asked and answered (the content check added the fourth, and its round trip is reading the surface's
/// content back out of the OS).</para>
///
/// <para><b>Why the hit test is asked twice.</b> "The point does not route to this window" is
/// satisfied just as well by a window that was never created, or one that is buried. So the
/// confirmation first makes the surface momentarily opaque to input and requires the point to
/// route TO it — establishing that this point is really this surface's — and only then restores
/// click-through and requires the point to route AWAY. The pair is the fact; either half alone is
/// the flag assertion this packet exists to refuse. The flip is two style writes and two hit-test
/// queries with no wait between them, and it never touches the alpha, so nothing changes on
/// screen.</para>
///
/// <para><b>Why topmost is re-asserted in a loop.</b> Topmost is contested, and the shipping
/// product proves it: <c>Services/Flash/FlashService.cs:206-243</c> re-raises every live flash
/// window on a cadence precisely because other layers bury an already-showing flash. Measured on
/// this machine while this capability was being written, the window that won the hit test under a
/// click-through surface was the shipping WPF app itself, topmost and re-raising. So each hit-test
/// query is preceded by WPF's own <c>SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)</c>
/// (<c>:3867</c>), bounded by <see cref="MaxRaiseAttempts"/> iterations — a count, never a
/// wall-clock wait.</para>
///
/// <para><b>Content.</b> <see cref="Paint"/> blits a <see cref="OverlayFrame"/> into the
/// window's own device context and then reads the surface BACK from the OS to confirm it holds
/// those pixels. GDI, not <c>UpdateLayeredWindow</c>: ULW is mutually exclusive with
/// <c>SetLayeredWindowAttributes</c>, so taking it would remove the alpha read-back that
/// <see cref="OverlayReasonCodes.OverlayNotComposited"/> — the check that catches the exact defect
/// the first attempt shipped — depends on. The frame is retained in a DIB section, re-blitted on
/// <c>WM_PAINT</c> while the surface is up and handed back at <see cref="Withdraw"/>. <b>None of
/// this is a claim that a human saw anything</b>; that is a headed capture.</para>
///
/// <para><b>Thread affinity.</b> The window belongs to the thread that first called
/// <see cref="Present"/>. Call <see cref="Dispose"/> from that same thread; disposing from another
/// records <see cref="TeardownDiagnostic"/> rather than pretending.</para>
/// </summary>
public sealed class Win32OverlayPresence : IOverlayPresence
{
    /// <summary>
    /// Hard ceiling on the re-raise/hit-test loop. Contention for the topmost band is real (see
    /// the class remarks) and bounded iteration is how it is absorbed without a wall-clock wait,
    /// which this repository's test suite forbids outright.
    /// </summary>
    public const int MaxRaiseAttempts = 32;

    /// <summary>
    /// How many points of a painted frame the content read-back compares when it compares the
    /// WHOLE surface. A bounded, spread sample rather than the whole buffer: a full memcmp of a
    /// full-screen frame on the UI thread would be a rendering cost paid to learn something a
    /// spread sample already answers. The four corners and the centre are always included, and
    /// <see cref="ContentBands"/> decides what a steady-state frame re-reads instead.
    ///
    /// <para><b>The exact limit of what this can catch, measured (divergence D64).</b> It
    /// catches the class that matters and the class that actually happens: the blit reported
    /// success and the surface holds something else, or nothing. It CANNOT catch an error that the
    /// read-back shares — <see cref="EnsureFrameSurfaces"/> builds the frame section and the
    /// read-back section from ONE <c>BITMAPINFO</c>, so a wrong row order, a wrong stride or a
    /// wrong origin cancels out and compares equal. Proven, not assumed: flipping <c>biHeight</c>
    /// to a bottom-up section makes the flash appear upside down on the screen and this
    /// confirmation still says <c>Available</c>. Anything in that family needs an instrument that
    /// does not share this header — the suite reads the same surface back through
    /// <c>PrintWindow</c> and through a composited desktop capture, and those are what fail.</para>
    /// </summary>
    public const int ContentSampleTarget = 1024;

    private readonly string _windowClassName = "CcpClientOverlaySurface." + Guid.NewGuid().ToString("N");
    private readonly Win32OverlayInterop.WndProc _windowProc;

    private nint _window;
    private nint _moduleHandle;
    private ushort _classAtom;
    private int _ownerThreadId;
    private bool _presenting;
    private bool _disposed;
    private OverlaySurfaceRequest? _current;

    // The retained frame: a top-down 32bpp DIB section selected into a memory DC. It is the
    // window's content, kept so WM_PAINT can be serviced from it — an overlay the OS invalidates
    // (a move, a resolution change, a compositor restart) must not go blank while the effect still
    // believes it is showing something. For exactly that long: Withdraw hands the pair back.
    private nint _frameDc;
    private nint _frameBitmap;
    private nint _frameBits;
    private nint _readbackDc;
    private nint _readbackBitmap;
    private nint _readbackBits;
    private int _frameWidth;
    private int _frameHeight;

    public Win32OverlayPresence()
    {
        // Rooted for the lifetime of this instance: the window manager calls this pointer back
        // from native code, and a collected delegate is an access violation, not an exception.
        _windowProc = WindowProc;
    }

    public bool IsPresenting => _presenting;

    /// <summary>Zero until a window exists. What an out-of-band prober needs.</summary>
    public OverlayNativeHandles NativeHandles => new(_window);

    /// <summary>
    /// How many topmost re-assertions the last input-routing confirmation needed. One means the
    /// surface won the hit test on the first ask; more means another window was contesting the
    /// topmost band. Read by diagnostics; never an input to a claim.
    /// </summary>
    public int LastRaiseAttempts { get; private set; }

    /// <summary>
    /// Set only when teardown could not complete (wrong thread, or <c>DestroyWindow</c> refused).
    /// Null after a clean <see cref="Dispose"/>. Dispose must not throw, and it must not pretend
    /// either — this is where the difference is recorded.
    /// </summary>
    public string? TeardownDiagnostic { get; private set; }

    public CapabilityState Present(OverlaySurfaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_disposed)
        {
            return Unavailable(OverlayReasonCodes.OverlayPresenceDisposed,
                "this overlay presence was disposed; its window is gone and it will never present another");
        }

        // Backend SELECTION by platform is permitted; capability AVAILABILITY by platform is not
        // (runtime-capability-contract §2 rule 2). This branch only refuses. Available below is
        // earned by the exercised OS round-trips, never by this check.
        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(OverlayReasonCodes.OverlayMechanismAbsent,
                "Win32OverlayPresence drives layered top-level windows through USER32, which is a Windows "
                + $"mechanism; this process is on {RuntimeInformation.OSDescription} and nothing was attempted");
        }

        var window = EnsureWindow(request.Bounds, out var creationFailure);
        if (window == 0)
        {
            return Unavailable(OverlayReasonCodes.OverlayWindowCreationFailed, creationFailure);
        }

        // Alpha BEFORE the first show. A layered window that reaches the screen before the OS has
        // an alpha for it is the first attempt's exact defect
        // (CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45), and WPF's own reason for
        // configuring a flash window fully before Show() is the same family of hazard
        // (FlashService.cs:3576-3583).
        if (!Win32OverlayInterop.SetLayeredWindowAttributes(window, 0, request.Alpha, Win32OverlayInterop.LwaAlpha))
        {
            var error = Marshal.GetLastWin32Error();
            return Unavailable(OverlayReasonCodes.OverlayNotComposited,
                $"SetLayeredWindowAttributes(LWA_ALPHA, {request.Alpha}) returned FALSE for window 0x{window:X} "
                + $"(last-error {error}); the OS holds no alpha for this layered window, so the compositor would "
                + "draw nothing and the surface would be present, on top and invisible");
        }

        ApplyClickThroughStyle(window, request.ClickThrough);

        var bounds = request.Bounds;
        if (!Win32OverlayInterop.SetWindowPos(
                window, Win32OverlayInterop.HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                Win32OverlayInterop.SwpNoactivate | Win32OverlayInterop.SwpShowwindow))
        {
            var error = Marshal.GetLastWin32Error();
            return Unavailable(OverlayReasonCodes.OverlayGeometryRefused,
                $"SetWindowPos(HWND_TOPMOST, {bounds}, SWP_NOACTIVATE|SWP_SHOWWINDOW) returned FALSE for window "
                + $"0x{window:X} (last-error {error}); nothing is on screen");
        }

        SetCurrent(request);

        var refusal = Confirm(request);
        if (refusal is not null)
        {
            _presenting = false;
            return refusal;
        }

        _presenting = true;
        return new CapabilityState.Available(
            $"window 0x{window:X} is on screen at {bounds}: the OS reports it visible with that exact rectangle, "
            + $"holds LWA_ALPHA {request.Alpha} for it (so the compositor has something to draw), places it above "
            + $"every ordinary window in its own top-level z-order, routes a click at its centre "
            + $"{(request.ClickThrough ? "PAST it to whatever is underneath (and TO it when momentarily made opaque, "
                + "which is what makes the pass-through non-vacuous)" : "TO it")} after "
            + $"{LastRaiseAttempts} topmost assertion(s), and did not take the foreground. Confirms WINDOW STATE "
            + "only — nothing is drawn on this surface by THIS call (content is Paint's, and it is confirmed the "
            + "same way, by asking the OS for it back), and that a human sees it is a headed claim");
    }

    public CapabilityState SetClickThrough(bool clickThrough)
    {
        if (_disposed)
        {
            return Unavailable(OverlayReasonCodes.OverlayPresenceDisposed,
                "this overlay presence was disposed; there is no window whose input routing could change");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(OverlayReasonCodes.OverlayMechanismAbsent,
                "Win32OverlayPresence drives WS_EX_TRANSPARENT through USER32, which is a Windows mechanism; "
                + "nothing was ever presented");
        }

        if (!_presenting || _current is null)
        {
            return Unavailable(OverlayReasonCodes.OverlayNothingPresented,
                "no surface is presented by this presence, so there is no input routing to change and nothing "
                + "succeeded");
        }

        var request = new OverlaySurfaceRequest(_current.Bounds, _current.Opacity, clickThrough);
        ApplyClickThroughStyle(_window, clickThrough);
        SetCurrent(request);

        var refusal = ConfirmInputRouting(request);
        if (refusal is not null)
        {
            return refusal;
        }

        return new CapabilityState.Available(
            $"window 0x{_window:X} now routes a click at its centre "
            + $"{(clickThrough ? "PAST it" : "TO it")}, confirmed by asking the window manager's hit test after "
            + $"{LastRaiseAttempts} topmost assertion(s) — not by the style write returning");
    }

    public CapabilityState Paint(OverlayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (_disposed)
        {
            return Unavailable(OverlayReasonCodes.OverlayPresenceDisposed,
                "this overlay presence was disposed; there is no surface to paint on");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(OverlayReasonCodes.OverlayMechanismAbsent,
                "Win32OverlayPresence paints through GDI into a USER32 window, which is a Windows mechanism; "
                + "nothing was ever presented and nothing was drawn");
        }

        if (!_presenting || _current is null)
        {
            return Unavailable(OverlayReasonCodes.OverlayNothingPresented,
                "no surface is presented by this presence, so there is nothing on screen to paint and nothing "
                + "succeeded. Present first: painting a window the OS has not confirmed is on screen is exactly "
                + "the claim this capability refuses to make");
        }

        var bounds = _current.Bounds;
        if (frame.Width != bounds.Width || frame.Height != bounds.Height)
        {
            return Unavailable(OverlayReasonCodes.OverlayFrameSizeMismatch,
                $"the frame is {frame.Width}x{frame.Height} and the presented surface is {bounds.Width}x"
                + $"{bounds.Height}; this capability scales nothing, so a mismatched frame is refused rather than "
                + "stretched onto the user's screen");
        }

        if (!EnsureFrameSurfaces(frame.Width, frame.Height, out var surfaceFailure))
        {
            return Unavailable(OverlayReasonCodes.OverlayPaintRefused, surfaceFailure);
        }

        Marshal.Copy(frame.Pixels, 0, _frameBits, frame.Pixels.Length);

        var windowDc = Win32OverlayInterop.GetDC(_window);
        if (windowDc == 0)
        {
            return Unavailable(OverlayReasonCodes.OverlayPaintRefused,
                $"GetDC(0x{_window:X}) returned NULL (last-error {Marshal.GetLastWin32Error()}); the OS gave no "
                + "device context for the surface, so nothing was drawn");
        }

        var blitted = Win32OverlayInterop.BitBlt(
            windowDc, 0, 0, frame.Width, frame.Height, _frameDc, 0, 0, Win32OverlayInterop.Srccopy);
        var blitError = Marshal.GetLastWin32Error();
        Win32OverlayInterop.ReleaseDC(_window, windowDc);

        if (!blitted)
        {
            return Unavailable(OverlayReasonCodes.OverlayPaintRefused,
                $"BitBlt({frame.Width}x{frame.Height}, SRCCOPY) into window 0x{_window:X} returned FALSE "
                + $"(last-error {blitError}); nothing was drawn");
        }

        var refusal = ConfirmContent(frame);
        if (refusal is not null)
        {
            return refusal;
        }

        return new CapabilityState.Available(
            $"window 0x{_window:X} holds a {frame.Width}x{frame.Height} frame: the pixels were blitted into the "
            + $"surface and then read BACK out of it from the OS — {LastContentProof} — and every point read is "
            + "the colour the frame carries. "
            + $"The surface composites at LWA_ALPHA {_current.Alpha}, so this is content the compositor has "
            + "something to draw. That a human SEES it is a headed claim and is not made here");
    }

    /// <inheritdoc/>
    public void Reassert()
    {
        if (_disposed || !_presenting || !OperatingSystem.IsWindows())
        {
            return;
        }

        Reraise();
    }

    public CapabilityState Withdraw()
    {
        if (_disposed)
        {
            return Unavailable(OverlayReasonCodes.OverlayPresenceDisposed,
                "this overlay presence was disposed; its window was already destroyed by teardown");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(OverlayReasonCodes.OverlayMechanismAbsent,
                "Win32OverlayPresence drives top-level windows through USER32, which is a Windows mechanism; "
                + "nothing was ever presented");
        }

        if (!_presenting || _current is null)
        {
            return Unavailable(OverlayReasonCodes.OverlayNothingPresented,
                "no surface is presented by this presence, so there is nothing to withdraw and nothing succeeded");
        }

        var bounds = _current.Bounds;
        Win32OverlayInterop.ShowWindow(_window, Win32OverlayInterop.SwHide);

        // Symmetric to Present: confirm the ABSENCE, twice, and from the OS both times.
        if (Win32OverlayInterop.IsWindowVisible(_window))
        {
            return Unavailable(OverlayReasonCodes.OverlayWithdrawRefused,
                $"ShowWindow(SW_HIDE) ran but the OS still reports window 0x{_window:X} visible; the surface is "
                + "still on screen and withdrawal is not claimed");
        }

        var (x, y) = bounds.Centre;
        var winner = Win32OverlayInterop.WindowFromPoint(new Win32OverlayInterop.Point { X = x, Y = y });
        if (winner == _window)
        {
            return Unavailable(OverlayReasonCodes.OverlayWithdrawRefused,
                $"the OS reports window 0x{_window:X} hidden, but its hit test still routes the point {x},{y} to "
                + "it; the surface is still taking input and withdrawal is not claimed");
        }

        _presenting = false;
        // A pooled presence outlives the flash that used it, so the pixels go back HERE.
        ReleaseFrameSurfaces();
        return new CapabilityState.Available(WithdrawnDetail(x, y));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _presenting = false;
        _current = null;

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_ownerThreadId != 0 && _ownerThreadId != Environment.CurrentManagedThreadId)
        {
            TeardownDiagnostic =
                $"Dispose ran on managed thread {Environment.CurrentManagedThreadId} but window 0x{_window:X} "
                + $"belongs to thread {_ownerThreadId}; DestroyWindow only works on the owning thread, so the "
                + "window may outlive this presence";
        }

        if (_window != 0)
        {
            if (!Win32OverlayInterop.DestroyWindow(_window))
            {
                TeardownDiagnostic ??=
                    $"DestroyWindow(0x{_window:X}) returned FALSE (last-error {Marshal.GetLastWin32Error()}); an "
                    + "invisible top-level window may survive for the life of the process";
            }

            _window = 0;
        }

        if (_classAtom != 0)
        {
            Win32OverlayInterop.UnregisterClassW(_windowClassName, _moduleHandle);
            _classAtom = 0;
        }

        // The frame surfaces are GDI objects, and GDI objects are a process-wide, quota-limited
        // resource: a flash that leaked one DIB section per image would exhaust the quota during a
        // long session and start failing to draw with no error anyone could read.
        ReleaseFrameSurfaces();

        GC.KeepAlive(_windowProc);
    }

    // ---------- the OS round trips ----------

    /// <summary>
    /// Every confirmation, in order, cheapest first. Returns null when all of them held, and the
    /// typed refusal for the first one that did not. This is the only path to
    /// <c>Available</c> in <see cref="Present"/>.
    /// </summary>
    private CapabilityState? Confirm(OverlaySurfaceRequest request)
    {
        var window = _window;

        if (!Win32OverlayInterop.IsWindow(window))
        {
            return Unavailable(OverlayReasonCodes.OverlayNotVisible,
                $"the OS does not recognise 0x{window:X} as a window at all after placement");
        }

        if (!Win32OverlayInterop.IsWindowVisible(window))
        {
            return Unavailable(OverlayReasonCodes.OverlayNotVisible,
                $"SetWindowPos(SWP_SHOWWINDOW) returned success but the OS does not report window 0x{window:X} "
                + "visible; nothing is on screen");
        }

        if (!Win32OverlayInterop.GetWindowRect(window, out var rect))
        {
            return Unavailable(OverlayReasonCodes.OverlayGeometryRefused,
                $"GetWindowRect(0x{window:X}) returned FALSE (last-error {Marshal.GetLastWin32Error()}); the OS "
                + "will not say where the surface is, so no placement is claimed");
        }

        var held = new OverlayBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (held != request.Bounds)
        {
            return Unavailable(OverlayReasonCodes.OverlayGeometryRefused,
                $"the OS holds {held} for window 0x{window:X} but {request.Bounds} was asked for; the surface is "
                + "not where the caller placed it");
        }

        // THE GHOST CHECK. A layered window whose attributes were never set reports
        // IsWindowVisible = TRUE while GetLayeredWindowAttributes returns FALSE — measured on this
        // machine, and the exact state the first attempt shipped
        // (CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45).
        if (!Win32OverlayInterop.GetLayeredWindowAttributes(window, out _, out var alpha, out var flags))
        {
            return Unavailable(OverlayReasonCodes.OverlayNotComposited,
                $"the OS holds NO layered attributes for window 0x{window:X} (GetLayeredWindowAttributes returned "
                + $"FALSE, last-error {Marshal.GetLastWin32Error()}); it is a window that exists, reports visible, "
                + "and composites nothing");
        }

        if ((flags & Win32OverlayInterop.LwaAlpha) == 0 || alpha == 0)
        {
            return Unavailable(OverlayReasonCodes.OverlayNotComposited,
                $"the OS holds alpha {alpha} with flags 0x{flags:X} for window 0x{window:X}; without a non-zero "
                + "LWA_ALPHA the compositor draws nothing and the surface is invisible");
        }

        if (alpha != request.Alpha)
        {
            return Unavailable(OverlayReasonCodes.OverlayNotComposited,
                $"the OS holds alpha {alpha} for window 0x{window:X} but {request.Alpha} was asked for "
                + $"(opacity {request.Opacity:0.###}); the surface would not be drawn at the requested strength");
        }

        // WS_EX_TOPMOST IS NOT A WRITE, IT IS A CLAIM THE OS KEEPS RE-ADJUDICATING, and this
        // read-back used to treat it as a write. SetWindowPos(HWND_TOPMOST) returns TRUE and the
        // bit is genuinely set, then ANOTHER process asserting topmost can leave this window
        // without it before the very next instruction reads it. Measured, not theorised: the whole
        // four-surface coexistence run reds in a full suite pass with exStyle 0x80800A0 — every
        // requested bit but 0x8 — while the identical run PASSES in isolation, the difference
        // being the twenty-odd real-desktop classes ahead of it creating and destroying topmost
        // windows of their own. It reproduces off-suite too, whenever any other topmost window
        // (a screen recorder, a chat overlay, a game bar) wins the same adjudication.
        //
        // The re-assertion mechanism already existed for exactly this — Raise() is WPF's
        // ForceTopmost (FlashService.cs:3865-3868) — but it lived only in the hit-test loop, which
        // this refusal returned before ever reaching. So the FACT is unchanged and still absolute:
        // every required bit must be present in the OS's own read-back. What changed is that the
        // topmost claim is now re-asserted the bounded way this class already knows it must be,
        // instead of being read once and abandoned.
        var exStyle = (uint)Win32OverlayInterop.GetWindowLongPtrW(window, Win32OverlayInterop.GwlExstyle);
        var required = Win32OverlayInterop.WsExLayered | Win32OverlayInterop.WsExToolwindow
            | Win32OverlayInterop.WsExNoactivate | Win32OverlayInterop.WsExTopmost;
        var styleAttempts = 1;
        while ((exStyle & required) != required
            && (exStyle & required & ~Win32OverlayInterop.WsExTopmost) == (required & ~Win32OverlayInterop.WsExTopmost)
            && styleAttempts < MaxRaiseAttempts)
        {
            // Only the topmost bit is re-assertable this way; a window missing LAYERED, TOOLWINDOW
            // or NOACTIVATE is genuinely not the window that was asked for, and loops out at once.
            Raise();
            styleAttempts++;
            exStyle = (uint)Win32OverlayInterop.GetWindowLongPtrW(window, Win32OverlayInterop.GwlExstyle);
        }

        if ((exStyle & required) != required)
        {
            return Unavailable(OverlayReasonCodes.OverlayStyleRefused,
                $"the extended-style read-back for window 0x{window:X} is 0x{exStyle:X}, missing 0x{required & ~exStyle:X} "
                + "of WS_EX_LAYERED|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TOPMOST after "
                + $"{styleAttempts} topmost assertion(s) (last SetWindowPos(HWND_TOPMOST) "
                + $"{(LastRaiseSucceeded ? "SUCCEEDED" : $"FAILED, last-error {LastRaiseError}")}, owner 0x"
                + $"{Win32OverlayInterop.GetWindow(window, Win32OverlayInterop.GwOwner):X}, desktop foreground 0x"
                + $"{Win32OverlayInterop.GetForegroundWindow():X}); the window is not the "
                + "window that was asked for, whatever the write calls returned");
        }

        var zOrder = ReadZOrder(window);
        if (!zOrder.AboveEveryOrdinaryWindow)
        {
            return Unavailable(OverlayReasonCodes.OverlayNotOnTop,
                $"the OS's own top-level z-order puts window 0x{window:X} at position {zOrder.Index} of "
                + $"{zOrder.VisibleCount} visible windows, at or below the first ordinary (non-topmost) window at "
                + $"position {zOrder.FirstOrdinaryIndex}; WS_EX_TOPMOST is set and the ordering does not follow it, "
                + "so the surface is buried");
        }

        var routing = ConfirmInputRouting(request);
        if (routing is not null)
        {
            return routing;
        }

        // Not "the foreground window is unchanged" — the user may legitimately switch apps while
        // this runs. The property that matters is that the SURFACE did not become the foreground.
        var foreground = Win32OverlayInterop.GetForegroundWindow();
        if (foreground == window)
        {
            return Unavailable(OverlayReasonCodes.OverlayStoleFocus,
                $"window 0x{window:X} is the foreground window after being shown; WS_EX_NOACTIVATE and "
                + "SWP_NOACTIVATE did not hold and the surface interrupted whatever the user was doing");
        }

        return null;
    }

    /// <summary>
    /// The input-routing fact, in both polarities, from the window manager's own hit test.
    ///
    /// <para>Leg one makes the surface opaque to input and requires the point to route TO it. That
    /// is what establishes the point is really this surface's, and it is why leg two is not
    /// vacuous — "the point does not route to us" is also true of a window that does not exist.
    /// Leg two restores the requested polarity and requires the answer the request asked for.</para>
    /// </summary>
    private CapabilityState? ConfirmInputRouting(OverlaySurfaceRequest request)
    {
        var (x, y) = request.Bounds.Centre;
        var point = new Win32OverlayInterop.Point { X = x, Y = y };
        var window = _window;

        ApplyClickThroughStyle(window, clickThrough: false);
        var opaqueWinner = HitTest(point, expectSurface: true, out var opaqueAttempts);
        LastRaiseAttempts = opaqueAttempts;

        if (opaqueWinner != window)
        {
            // Restore what the caller asked for before refusing: a refusal must not leave the
            // surface in a state nobody requested.
            ApplyClickThroughStyle(window, request.ClickThrough);
            return Unavailable(OverlayReasonCodes.OverlayInputNotReceived,
                $"with click-through cleared and HWND_TOPMOST re-asserted {opaqueAttempts} time(s), the window "
                + $"manager still routes the point {x},{y} to {Describe(opaqueWinner)} instead of the surface "
                + $"0x{window:X}. Something is above the surface at that point, so its input behaviour cannot be "
                + "distinguished from being buried and nothing is claimed");
        }

        ApplyClickThroughStyle(window, request.ClickThrough);
        var finalWinner = HitTest(point, expectSurface: !request.ClickThrough, out var finalAttempts);
        LastRaiseAttempts = opaqueAttempts + finalAttempts;

        if (request.ClickThrough && finalWinner == window)
        {
            return Unavailable(OverlayReasonCodes.OverlayInputNotPassingThrough,
                $"WS_EX_TRANSPARENT is set on window 0x{window:X} and the window manager STILL routes the point "
                + $"{x},{y} to it. Clicks are being swallowed: the desktop underneath is broken while the overlay "
                + "looks implemented");
        }

        if (!request.ClickThrough && finalWinner != window)
        {
            return Unavailable(OverlayReasonCodes.OverlayInputNotReceived,
                $"the surface was asked to catch its own clicks, and after {finalAttempts} topmost assertion(s) the "
                + $"window manager routes the point {x},{y} to {Describe(finalWinner)} instead of 0x{window:X}");
        }

        return null;
    }

    /// <summary>
    /// One hit-test question, preceded by WPF's own topmost re-assertion
    /// (<c>FlashService.cs:3865-3868</c>) and repeated up to
    /// <see cref="MaxRaiseAttempts"/> times while the answer is not the expected one. A bounded
    /// iteration count, never a wall-clock wait.
    /// </summary>
    private nint HitTest(Win32OverlayInterop.Point point, bool expectSurface, out int attempts)
    {
        var winner = (nint)0;
        for (attempts = 1; attempts <= MaxRaiseAttempts; attempts++)
        {
            Raise();
            winner = Win32OverlayInterop.WindowFromPoint(point);
            var matched = expectSurface ? winner == _window : winner != _window;
            if (matched)
            {
                return winner;
            }
        }

        attempts = MaxRaiseAttempts;
        return winner;
    }

    /// <summary>
    /// The content fact: read the surface's own pixels back OUT of the OS and require them to be
    /// the frame's — the WHOLE surface on the first paint after anything about the window changed,
    /// and one <see cref="ContentBands"/>th of it on each unchanged frame after that.
    ///
    /// <para><b>Why the read-back is from the window and not from the buffer.</b> The blit's return
    /// value says a GDI call succeeded. Reading the WINDOW's device context back into an
    /// independent DIB section says the pixels are in the surface the compositor draws from — a
    /// different surface from the one that was written, reached by a different call. Measured
    /// before this was written: immediately after the blit, with no wait of any kind, the window's
    /// DC returns exactly the painted colour (the packet record §1).</para>
    /// </summary>
    private CapabilityState? ConfirmContent(OverlayFrame frame)
    {
        // A frame that differs from the one before it ONLY in content, on a surface whose size,
        // style, position and topmost band have not moved, is not the event this confirmation
        // exists to catch, and re-reading a whole 2880x1800 surface for it costs 4.6 ms of the UI
        // thread per frame (measured; ContentBands carries the run).
        var full = !_contentConfirmedFully;
        var height = full ? frame.Height : BandHeight(frame.Height);
        var top = full ? 0 : Math.Min(frame.Height - 1, _bandCursor * height);
        height = Math.Min(height, frame.Height - top);

        var windowDc = Win32OverlayInterop.GetDC(_window);
        if (windowDc == 0)
        {
            return Unavailable(OverlayReasonCodes.OverlayContentNotHeld,
                $"GetDC(0x{_window:X}) returned NULL when asking the surface for its content back "
                + $"(last-error {Marshal.GetLastWin32Error()}); the paint cannot be confirmed and is not claimed");
        }

        // The read-back lands at the coordinates it came FROM: one offset arithmetic, both cases.
        var copied = Win32OverlayInterop.BitBlt(
            _readbackDc, 0, top, frame.Width, height, windowDc, 0, top, Win32OverlayInterop.Srccopy);
        var error = Marshal.GetLastWin32Error();
        Win32OverlayInterop.ReleaseDC(_window, windowDc);

        if (!copied)
        {
            return Unavailable(OverlayReasonCodes.OverlayContentNotHeld,
                $"reading window 0x{_window:X}'s own content back returned FALSE (last-error {error}); the blit "
                + "reported success and the surface cannot be asked what it holds, so nothing is claimed");
        }

        var mismatch = CompareRegion(frame, top, height);
        if (mismatch is not null)
        {
            // Left NOT confirmed: the next paint re-reads the whole surface rather than the next
            // band, because a surface that failed once has nothing a band sweep may assume.
            return mismatch;
        }

        RecordContentProof(full, top, height);
        return null;
    }

    private static (int X, int Y)[] Corners(OverlayFrame frame) =>
    [
        (0, 0),
        (frame.Width - 1, 0),
        (0, frame.Height - 1),
        (frame.Width - 1, frame.Height - 1),
        (frame.Width / 2, frame.Height / 2),
    ];

    /// <summary>The stride that spreads about <see cref="ContentSampleTarget"/> samples over the
    /// whole frame, never below 1.</summary>
    private static int SampleStep(int width, int height) =>
        Math.Max(1, (int)Math.Sqrt((double)width * height / ContentSampleTarget));

    private bool SamplesMatch(OverlayFrame frame, int x, int y, out uint held)
    {
        var offset = (y * frame.Stride) + (x * OverlayFrame.BytesPerPixel);
        held = (uint)((Marshal.ReadByte(_readbackBits, offset) << 16)
            | (Marshal.ReadByte(_readbackBits, offset + 1) << 8)
            | Marshal.ReadByte(_readbackBits, offset + 2));
        return held == frame.ColourAt(x, y);
    }

    private CapabilityState ContentMismatch(OverlayFrame frame, int x, int y, uint held) =>
        Unavailable(OverlayReasonCodes.OverlayContentNotHeld,
            $"the blit into window 0x{_window:X} returned TRUE, and the OS returns 0x{held:X6} at {x},{y} where "
            + $"the frame carries 0x{frame.ColourAt(x, y):X6}. The surface does not hold what was drawn into it, "
            + "so 'it was painted' is not claimed — a draw call that returned is not a picture on a screen");

    /// <summary>
    /// The retained frame and read-back sections, (re)created when the size changes. Both are
    /// TOP-DOWN (negative <c>biHeight</c>) so a frame's first row is the surface's first row and
    /// nothing between a decoder and the screen has to flip anything.
    /// </summary>
    private bool EnsureFrameSurfaces(int width, int height, out string failure)
    {
        failure = string.Empty;
        if (_frameDc != 0 && _frameWidth == width && _frameHeight == height)
        {
            return true;
        }

        ReleaseFrameSurfaces();

        var screenDc = Win32OverlayInterop.GetDC(0);
        if (screenDc == 0)
        {
            failure = $"GetDC(NULL) returned NULL (last-error {Marshal.GetLastWin32Error()}); no device context "
                + "to build a frame surface from, so nothing was drawn";
            return false;
        }

        try
        {
            var info = new Win32OverlayInterop.BitmapInfo
            {
                bmiHeader = new Win32OverlayInterop.BitmapInfoHeader
                {
                    biSize = (uint)Marshal.SizeOf<Win32OverlayInterop.BitmapInfoHeader>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = Win32OverlayInterop.BiRgb,
                },
            };

            _frameBitmap = Win32OverlayInterop.CreateDIBSection(
                screenDc, ref info, Win32OverlayInterop.DibRgbColors, out _frameBits, 0, 0);
            _readbackBitmap = Win32OverlayInterop.CreateDIBSection(
                screenDc, ref info, Win32OverlayInterop.DibRgbColors, out _readbackBits, 0, 0);

            if (_frameBitmap == 0 || _readbackBitmap == 0 || _frameBits == 0 || _readbackBits == 0)
            {
                failure = $"CreateDIBSection({width}x{height}, 32bpp top-down) returned NULL "
                    + $"(last-error {Marshal.GetLastWin32Error()}); no frame surface, so nothing was drawn";
                ReleaseFrameSurfaces();
                return false;
            }

            _frameDc = Win32OverlayInterop.CreateCompatibleDC(screenDc);
            _readbackDc = Win32OverlayInterop.CreateCompatibleDC(screenDc);
            if (_frameDc == 0 || _readbackDc == 0)
            {
                failure = $"CreateCompatibleDC returned NULL (last-error {Marshal.GetLastWin32Error()}); no memory "
                    + "device context for the frame, so nothing was drawn";
                ReleaseFrameSurfaces();
                return false;
            }

            Win32OverlayInterop.SelectObject(_frameDc, _frameBitmap);
            Win32OverlayInterop.SelectObject(_readbackDc, _readbackBitmap);
            _frameWidth = width;
            _frameHeight = height;
            return true;
        }
        finally
        {
            Win32OverlayInterop.ReleaseDC(0, screenDc);
        }
    }

    private void ReleaseFrameSurfaces()
    {
        if (_frameDc != 0)
        {
            Win32OverlayInterop.DeleteDC(_frameDc);
            _frameDc = 0;
        }

        if (_readbackDc != 0)
        {
            Win32OverlayInterop.DeleteDC(_readbackDc);
            _readbackDc = 0;
        }

        if (_frameBitmap != 0)
        {
            Win32OverlayInterop.DeleteObject(_frameBitmap);
            _frameBitmap = 0;
        }

        if (_readbackBitmap != 0)
        {
            Win32OverlayInterop.DeleteObject(_readbackBitmap);
            _readbackBitmap = 0;
        }

        _frameBits = 0;
        _readbackBits = 0;
        _frameWidth = 0;
        _frameHeight = 0;
    }

    /// <summary>
    /// The surface's window procedure. It exists for exactly one message: <c>WM_PAINT</c> is
    /// serviced from the retained frame, so an overlay the OS invalidates does not go blank
    /// underneath an effect that still believes it is showing something. Everything else goes to
    /// <c>DefWindowProc</c>, and nothing here allocates or throws — this runs on a native
    /// callback, where an exception is a process kill rather than a fault.
    /// </summary>
    private nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == Win32OverlayInterop.WmPaint && _frameDc != 0 && _frameWidth > 0)
        {
            var dc = Win32OverlayInterop.BeginPaint(window, out var paint);
            if (dc != 0)
            {
                Win32OverlayInterop.BitBlt(
                    dc, 0, 0, _frameWidth, _frameHeight, _frameDc, 0, 0, Win32OverlayInterop.Srccopy);
                Win32OverlayInterop.EndPaint(window, ref paint);
                return 0;
            }
        }

        return Win32OverlayInterop.DefWindowProcW(window, message, wParam, lParam);
    }

    /// <summary>WPF's <c>ForceTopmost</c>, verbatim in effect (<c>FlashService.cs:3865-3868</c>).
    /// The OS's answer is RECORDED rather than discarded: "the bit is still missing" and "the call
    /// that would set it is being refused" are different diagnoses with different fixes, and a void
    /// Raise cannot tell them apart.</summary>
    private void Raise()
    {
        LastRaiseSucceeded = Win32OverlayInterop.SetWindowPos(
            _window, Win32OverlayInterop.HwndTopmost, 0, 0, 0, 0,
            Win32OverlayInterop.SwpNomove | Win32OverlayInterop.SwpNosize | Win32OverlayInterop.SwpNoactivate);
        LastRaiseError = LastRaiseSucceeded ? 0 : Marshal.GetLastWin32Error();
    }

    /// <summary>Whether the most recent topmost assertion was accepted by the OS.</summary>
    public bool LastRaiseSucceeded { get; private set; }

    /// <summary>The last-error from the most recent REFUSED topmost assertion, or 0.</summary>
    public int LastRaiseError { get; private set; }

    /// <summary>
    /// WPF's <c>ApplyClickability</c> (<c>FlashService.cs:3660-3673</c>): the flag is written to the
    /// LIVE hwnd every time rather than at creation, because the same window flips polarity across
    /// spawns. <c>WS_EX_LAYERED</c> and <c>WS_EX_NOACTIVATE</c> are re-asserted alongside it, as
    /// WPF does at <c>:3666</c> — the first attempt's disable path dropped only
    /// <c>WS_EX_TRANSPARENT</c> and left the rest asymmetric.
    /// </summary>
    private static void ApplyClickThroughStyle(nint window, bool clickThrough)
    {
        var style = (uint)Win32OverlayInterop.GetWindowLongPtrW(window, Win32OverlayInterop.GwlExstyle)
            | Win32OverlayInterop.WsExLayered
            | Win32OverlayInterop.WsExToolwindow
            | Win32OverlayInterop.WsExNoactivate;

        if (clickThrough)
        {
            style |= Win32OverlayInterop.WsExTransparent;
        }
        else
        {
            style &= ~Win32OverlayInterop.WsExTransparent;
        }

        Win32OverlayInterop.SetWindowLongPtrW(window, Win32OverlayInterop.GwlExstyle, (nint)style);
    }

    /// <summary>
    /// Where the OS itself puts this window among the visible top-level windows, and where the
    /// first ordinary (non-topmost) one is. The ORDERING is the fact; <c>WS_EX_TOPMOST</c> is a
    /// flag and a flag is what the first attempt trusted.
    /// </summary>
    private static ZOrderPosition ReadZOrder(nint window)
    {
        var index = -1;
        var firstOrdinary = -1;
        var visible = 0;

        for (var candidate = Win32OverlayInterop.GetTopWindow(0);
             candidate != 0;
             candidate = Win32OverlayInterop.GetWindow(candidate, Win32OverlayInterop.GwHwndnext))
        {
            if (!Win32OverlayInterop.IsWindowVisible(candidate))
            {
                continue;
            }

            if (candidate == window)
            {
                index = visible;
            }
            else if (firstOrdinary < 0)
            {
                var exStyle = (uint)Win32OverlayInterop.GetWindowLongPtrW(candidate, Win32OverlayInterop.GwlExstyle);
                if ((exStyle & Win32OverlayInterop.WsExTopmost) == 0)
                {
                    firstOrdinary = visible;
                }
            }

            visible++;
        }

        return new ZOrderPosition(index, firstOrdinary, visible);
    }

    private readonly record struct ZOrderPosition(int Index, int FirstOrdinaryIndex, int VisibleCount)
    {
        internal bool AboveEveryOrdinaryWindow =>
            Index >= 0 && (FirstOrdinaryIndex < 0 || Index < FirstOrdinaryIndex);
    }

    private nint EnsureWindow(OverlayBounds bounds, out string failure)
    {
        failure = string.Empty;
        if (_window != 0)
        {
            return _window;
        }

        _moduleHandle = Win32OverlayInterop.GetModuleHandleW(null);
        var windowClass = new Win32OverlayInterop.WndClassExW
        {
            cbSize = (uint)Marshal.SizeOf<Win32OverlayInterop.WndClassExW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = _moduleHandle,
            lpszClassName = _windowClassName,
        };

        _classAtom = Win32OverlayInterop.RegisterClassExW(ref windowClass);
        if (_classAtom == 0)
        {
            failure = $"RegisterClassEx(\"{_windowClassName}\") returned 0 "
                + $"(last-error {Marshal.GetLastWin32Error()}); no overlay window could be created";
            return 0;
        }

        // Created at the requested rectangle and NOT visible: the surface reaches the screen only
        // at the SetWindowPos in Present, by which time the OS already holds its alpha. WPF
        // configures a flash window completely before its first Show() for the neighbouring reason
        // (FlashService.cs:3576-3583, :3618-3624).
        _window = Win32OverlayInterop.CreateWindowExW(
            Win32OverlayInterop.WsExLayered | Win32OverlayInterop.WsExTransparent
                | Win32OverlayInterop.WsExToolwindow | Win32OverlayInterop.WsExNoactivate,
            _windowClassName,
            "CCP overlay surface",
            Win32OverlayInterop.WsPopup,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            0, 0, _moduleHandle, 0);

        if (_window == 0)
        {
            failure = $"CreateWindowEx for class \"{_windowClassName}\" at {bounds} returned NULL "
                + $"(last-error {Marshal.GetLastWin32Error()})";
            Win32OverlayInterop.UnregisterClassW(_windowClassName, _moduleHandle);
            _classAtom = 0;
            return 0;
        }

        _ownerThreadId = Environment.CurrentManagedThreadId;
        return _window;
    }

    private static string Describe(nint window)
    {
        if (window == 0)
        {
            return "no window at all";
        }

        var buffer = new StringBuilder(256);
        var copied = Win32OverlayInterop.GetClassNameW(window, buffer, buffer.Capacity);
        return copied > 0 ? $"0x{window:X} (class \"{buffer}\")" : $"0x{window:X}";
    }

    private static CapabilityState.Unavailable Unavailable(string code, string detail) =>
        new(new CapabilityReason(code, detail));

    /// <summary>
    /// What a confirmed withdrawal says, including the half that used to be untrue by omission.
    ///
    /// <para><b>Why <see cref="Withdraw"/> releases the frame surfaces at all.</b> A presence is
    /// POOLED and is never removed from its set (<c>Effects/OverlaySurfaceSet.cs:238-258</c>), so
    /// "freed at <see cref="Dispose"/>" meant "held until the session ends". Measured before that
    /// call existed (<c>tests/CcpClient.Tests/OverlayFrameSurfaceRetentionTests.cs</c>): one flash
    /// pool of ten presences kept 40 GDI objects and 129 MB of private commit after the operating
    /// system had confirmed every one of its surfaces off screen — at the image-scale dial's
    /// ceiling, where a flash frame is the whole monitor. The same run at the dial's default kept
    /// 20 MB, which is the same defect and the reason the number scales with a user's slider.</para>
    ///
    /// <para><b>What the pooling still buys, unchanged.</b> The window and its registered class:
    /// <see cref="Withdraw"/> hides rather than destroys, and that is what the pool was for
    /// (<c>Effects/OverlaySurfaceSet.cs:230-237</c> names it). It was never for the pixels —
    /// <see cref="EnsureFrameSurfaces"/> already rebuilds the pair whenever the frame size changes,
    /// and a flash frame is sized per SOURCE image, so a recycled slot rebuilds it on almost every
    /// show in any case. Measured on the same run: the present-and-paint phase for ten
    /// monitor-sized surfaces did not move (87-123 ms before, 98-99 ms after), because the paint
    /// already copies the whole frame twice (the figures skip each run's JIT-warming first pass);
    /// the withdraw phase went from 5-7 ms to 16-17 ms for all ten. What remains after a
    /// withdrawal is exactly the state a presence that has never painted is in, which every
    /// session's first flash already runs through.</para>
    /// </summary>
    private string WithdrawnDetail(int x, int y) =>
        $"window 0x{_window:X} is off screen: the OS reports it not visible and its hit test no longer routes "
        + $"the point {x},{y} to it. The window itself is kept for the next Present; the frame surfaces it was "
        + "holding are not — those went back to the OS here rather than at Dispose, because a pooled presence "
        + "outlives the flash that used it";

    // ---------- the steady-state content check ----------
    //
    // EVERYTHING BELOW THIS LINE IS APPENDED, and that is deliberate rather than tidy: forty-one
    // citations in twenty-two files point INTO this file by line number, one of them from
    // client/docs/task-board.md, which a lane may not edit. So the change that added this section
    // is line-neutral above it — every edit above replaced the same number of lines it removed —
    // and no citation moved.

    /// <summary>
    /// How many horizontal bands the surface is re-proved in when nothing about the WINDOW has
    /// changed. Every band is read back and compared in turn, so a surface that stops holding its
    /// frame is caught in the band it happens in — immediately for any content that differs there,
    /// and within this many frames for anything at all.
    ///
    /// <para><b>Why this exists, measured on the running product at maximum settings rather than
    /// argued.</b> The full-surface read-back is 4.6 ms of the UI thread per frame at 2880x1800
    /// (per-stage probe, one spiral surface: copy 0.9 ms, blit 2.2 ms, confirm 4.6 ms). It was
    /// UNCONDITIONAL on every frame of every moving surface, and a moving surface repaints tens of
    /// times a second. <b>It is NOT the dominant cost</b> — the same probe puts 130 ms of the same
    /// frame in one GDI+ resample — and the honest size of this change is stated where the numbers
    /// are, not inflated here.</para>
    ///
    /// <para><b>What the guarantee still is.</b> <see cref="IOverlayPresence.Paint"/> promises the
    /// operating system was asked for the surface's content BACK. It still is, on every single
    /// frame; what changed is how much of it, and only for a frame that differs from the one
    /// before it in NOTHING BUT CONTENT. The whole surface is re-read on the first paint after a
    /// <see cref="Present"/>, after a <see cref="SetClickThrough"/>, after any resize, after a
    /// <see cref="Reassert"/> re-asserts the band, and after any failed comparison — every event
    /// that could have changed the surface out from under a frame.</para>
    /// </summary>
    public const int ContentBands = 16;

    /// <summary>
    /// True once the WHOLE surface has been read back and matched for the frame it currently
    /// holds, and false again the moment anything about the window changes. It is the only input
    /// to the full-versus-band decision, and every writer of it is one line: <see cref="SetCurrent"/>
    /// (present and click-through), <see cref="Reraise"/> (the band), and the failure arm of
    /// <see cref="ConfirmContent"/>.
    /// </summary>
    private bool _contentConfirmedFully;

    /// <summary>Which band the next steady-state frame reads back. Sweeps, so an unchanged surface
    /// is re-proved end to end within <see cref="ContentBands"/> frames.</summary>
    private int _bandCursor;

    /// <summary>
    /// What the last confirmed paint actually read back out of the operating system, in words, for
    /// the <see cref="CapabilityState.Available"/> detail. A capability that says "the OS was asked
    /// for the content back" and reads a sixteenth of the surface has to SAY a sixteenth: the
    /// detail string is the only place a reader learns which of the two was done.
    /// </summary>
    public string LastContentProof { get; private set; } = "nothing has been painted on this surface yet";

    /// <summary>
    /// The rows in one band, never zero for a one-row surface. <b>Public because the sweep's
    /// COVERAGE is the fact worth checking</b> and checking it needs no window: a band height that
    /// rounds down leaves the last rows of the surface in no band at all, which on a 1800-row
    /// monitor is eight rows the capability would never read back again.
    /// </summary>
    public static int BandHeight(int frameHeight) =>
        Math.Max(1, (frameHeight + ContentBands - 1) / ContentBands);

    /// <summary>
    /// The one place the current request is recorded — and therefore the one place the "the whole
    /// surface is proved" latch is dropped. Present and SetClickThrough both land here, which is
    /// why a geometry, alpha or style change can never be followed by a band-only confirmation.
    /// </summary>
    private void SetCurrent(OverlaySurfaceRequest request)
    {
        _current = request;
        _contentConfirmedFully = false;
    }

    /// <summary>
    /// <see cref="Raise"/> for <see cref="Reassert"/>: the same single <c>SetWindowPos</c>, plus
    /// the admission that the band was contested. A re-assertion is the OS being asked to move
    /// this window in the z-order, so the next paint re-reads the whole surface rather than one
    /// band of it — which is also what gives the steady state a bounded full re-proof cadence, on
    /// the caller's own topmost cadence (<c>Effects/OverlaySurfaceSet.cs:466-473</c>), with no
    /// timer of this class's own.
    /// </summary>
    private void Reraise()
    {
        _contentConfirmedFully = false;
        Raise();
    }

    /// <summary>
    /// Compare the read-back against the frame over <paramref name="height"/> rows starting at
    /// <paramref name="top"/>: the spread grid of <see cref="SampleStep"/> plus every corner that
    /// falls inside the region. Called with the whole surface it is exactly the comparison this
    /// class has always made; called with one band it is that comparison restricted to the band.
    /// </summary>
    private CapabilityState? CompareRegion(OverlayFrame frame, int top, int height)
    {
        var step = SampleStep(frame.Width, frame.Height);
        for (var y = top; y < top + height; y += step)
        {
            for (var x = 0; x < frame.Width; x += step)
            {
                if (!SamplesMatch(frame, x, y, out var held))
                {
                    return ContentMismatch(frame, x, y, held);
                }
            }
        }

        foreach (var (x, y) in Corners(frame))
        {
            if (y < top || y >= top + height)
            {
                continue;
            }

            if (!SamplesMatch(frame, x, y, out var held))
            {
                return ContentMismatch(frame, x, y, held);
            }
        }

        return null;
    }

    /// <summary>
    /// Record what was proved and move the sweep on. Nothing here decides anything: the latch is
    /// set only on the path where the WHOLE surface matched, so a band comparison can never be the
    /// thing that lets the next band comparison happen.
    /// </summary>
    private void RecordContentProof(bool full, int top, int height)
    {
        if (full)
        {
            _contentConfirmedFully = true;
            _bandCursor = 0;
            LastContentProof = $"the WHOLE {_frameWidth}x{_frameHeight} surface, at about {ContentSampleTarget} "
                + "spread sample points including all four corners and the centre";
            return;
        }

        var band = _bandCursor;
        _bandCursor = (_bandCursor + 1) % ContentBands;
        LastContentProof = $"rows {top}-{top + height - 1} of {_frameHeight}, band {band + 1} of {ContentBands} "
            + "of a sweep that re-reads every row of an unchanged surface within that many frames, the whole "
            + "surface having been read back and matched when this one was last presented, re-styled or re-raised";
    }
}
