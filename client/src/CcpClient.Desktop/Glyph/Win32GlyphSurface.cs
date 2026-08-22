using System.Runtime.InteropServices;
using System.Text;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Glyph;

/// <summary>
/// The handle an out-of-band prober needs to ask the OPERATING SYSTEM about this surface without
/// asking the surface itself — the same reason <c>OverlayNativeHandles</c> and
/// <c>TrayNativeHandles</c> are exposed. A capability that can only be interrogated through its own
/// claim is the capability nobody can verify.
/// </summary>
/// <param name="Window">The top-level window the surface is, or zero before one exists.</param>
public readonly record struct GlyphNativeHandles(nint Window);

/// <summary>
/// The Windows per-pixel-alpha backend: a real layered, click-through, always-on-top top-level
/// window composited with <c>UpdateLayeredWindow</c>.
///
/// <para><b>The rule this class obeys, and how it differs from the overlay's.</b> Nothing here
/// returns <see cref="CapabilityState.Available"/> until the operating system has been asked back.
/// The overlay's decisive question is <c>GetLayeredWindowAttributes</c>, and that question is
/// unavailable here: a window composited per pixel answers it FALSE by design, exactly as a window
/// that was never composited at all answers it. So the decisive question is a different one —
/// <b><c>PrintWindow(PW_RENDERFULLCONTENT)</c>: give me the surface back</b> — and it was chosen
/// because it was MEASURED to separate the two states, on this machine, before this file was
/// written:</para>
///
/// <list type="bullet">
/// <item>a layered window that never received a composite: <b>0 non-zero pixels of 14400</b>;</item>
/// <item>the same window after one composite: the frame's fully-opaque samples EXACTLY, 0 mismatches
/// over 10 colours, on the first call after the show and on every call after that.</item>
/// </list>
///
/// <para><b>The overlay's hazard, and why it cannot happen from here.</b> The recorded hazard is that
/// toggling <c>WS_EX_LAYERED</c> and then calling <c>UpdateLayeredWindow</c> succeeds and destroys
/// the overlay's alpha read-back forever. Four things stop it. This class only ever touches a
/// window it created itself; the layered style is set in the <c>CreateWindowExW</c> call and is
/// never removed, so the two lines are never adjacent anywhere;
/// <c>SetLayeredWindowAttributes</c> is not declared in this capability at all
/// (<see cref="Win32GlyphInterop"/>); and the operating system itself refuses the conversion in
/// that direction anyway — measured, ULW on a window that has had uniform attributes set returns
/// FALSE with last-error 87 and the uniform attributes survive intact.</para>
///
/// <para><b>Thread affinity.</b> The window belongs to the thread that first called
/// <see cref="Present"/>. Disposing from another thread records
/// <see cref="TeardownDiagnostic"/> rather than pretending.</para>
/// </summary>
public sealed class Win32GlyphSurface : IGlyphSurface
{
    /// <summary>
    /// Hard ceiling on the re-raise/hit-test loop. Contention for the topmost band is real and
    /// measured — on this machine the window that wins a click-through surface's own point is the
    /// shipping WPF product, topmost and re-raising on a cadence
    /// (<c>Services/Flash/FlashService.cs:206-243</c>). Bounded iteration is how that is absorbed
    /// without a wall-clock wait, which this repository's suite forbids outright.
    /// </summary>
    public const int MaxRaiseAttempts = 32;

    /// <summary>
    /// How many fully-transparent points the content read-back checks alongside the frame's ink.
    /// They are a weaker fact than the ink (a ghost passes them trivially, since everything reads
    /// zero) and they are still worth asking, because a surface that composited the WRONG frame —
    /// an opaque plate where the glyphs should have holes — fails them and passes nothing else.
    /// </summary>
    public const int TransparentSampleTarget = 32;

    private readonly string _windowClassName = "CcpClientGlyphSurface." + Guid.NewGuid().ToString("N");
    private readonly Win32GlyphInterop.WndProc _windowProc;

    private nint _window;
    private nint _moduleHandle;
    private ushort _classAtom;
    private int _ownerThreadId;
    private bool _presenting;
    private bool _disposed;
    private GlyphSurfaceRequest? _current;

    // The composited frame lives in a top-down 32bpp DIB section selected into a memory DC, which
    // is what UpdateLayeredWindow reads from. There is no WM_PAINT path and no retained repaint:
    // a layered window's content is held by the compositor, not by the window's device context,
    // which is exactly why GetDC on this window reads back black (measured) and why nothing here
    // ever asks it.
    private nint _frameDc;
    private nint _frameBitmap;
    private nint _frameBits;
    private nint _readbackDc;
    private nint _readbackBitmap;
    private nint _readbackBits;
    private int _frameWidth;
    private int _frameHeight;

    public Win32GlyphSurface()
    {
        // Rooted for the lifetime of this instance: the window manager calls this pointer back from
        // native code and a collected delegate is an access violation, not an exception.
        _windowProc = Win32GlyphInterop.DefWindowProcW;
    }

    /// <inheritdoc/>
    public bool IsPresenting => _presenting;

    /// <summary>Zero until a window exists. What an out-of-band prober needs.</summary>
    public GlyphNativeHandles NativeHandles => new(_window);

    /// <summary>How many topmost re-assertions the last input-routing confirmation needed. One means
    /// the surface won its point on the first ask. Read by diagnostics; never an input to a claim.</summary>
    public int LastRaiseAttempts { get; private set; }

    /// <summary>Set only when teardown could not complete. Null after a clean
    /// <see cref="Dispose"/>.</summary>
    public string? TeardownDiagnostic { get; private set; }

    /// <inheritdoc/>
    public CapabilityState Present(GlyphSurfaceRequest request, GlyphFrame frame)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(frame);

        if (_disposed)
        {
            return Unavailable(GlyphReasonCodes.GlyphSurfaceDisposed,
                "this glyph surface was disposed; its window is gone and it will never present another");
        }

        // Backend SELECTION by platform is permitted; capability AVAILABILITY by platform is not
        // (runtime-capability-contract §2 rule 2). This branch only refuses.
        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(GlyphReasonCodes.GlyphMechanismAbsent,
                "Win32GlyphSurface composites through UpdateLayeredWindow in USER32, which is a Windows "
                + $"mechanism; this process is on {RuntimeInformation.OSDescription} and nothing was attempted");
        }

        if (frame.Width != request.Bounds.Width || frame.Height != request.Bounds.Height)
        {
            return Unavailable(GlyphReasonCodes.GlyphFrameSizeMismatch,
                $"the frame is {frame.Width}x{frame.Height} and the requested surface is {request.Bounds.Width}x"
                + $"{request.Bounds.Height}; this capability scales nothing, so a mismatched frame is refused "
                + "rather than stretched onto the user's screen");
        }

        if (!frame.HasProvableInk)
        {
            return NoProvableInk(frame);
        }

        var window = EnsureWindow(request.Bounds, out var creationFailure);
        if (window == 0)
        {
            return Unavailable(GlyphReasonCodes.GlyphWindowCreationFailed, creationFailure);
        }

        // THE MODE CHECK, and it is a refusal rather than a repair. A window the OS holds uniform
        // layered attributes for is in SetLayeredWindowAttributes mode, and measured on this
        // machine UpdateLayeredWindow on such a window returns FALSE with last-error 87 forever
        // after. Saying so by name is the difference between a capability that reports the truth
        // and one that reports "87".
        if (Win32GlyphInterop.GetLayeredWindowAttributes(window, out _, out var uniformAlpha, out var uniformFlags))
        {
            return Unavailable(GlyphReasonCodes.GlyphUniformAlphaMode,
                $"the OS holds uniform layered attributes (alpha {uniformAlpha}, flags 0x{uniformFlags:X}) for "
                + $"window 0x{window:X}, so it is in SetLayeredWindowAttributes mode and UpdateLayeredWindow will "
                + "refuse it permanently; no per-pixel composite is possible on this window and none is claimed");
        }

        // The composite happens BEFORE the window is ever shown. A layered window that reaches the
        // screen before it has been composited is the first attempt's exact defect
        // (CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45) and the state the overlay
        // measured; there is no instant at which this surface is in it.
        var composited = Composite(window, request.Bounds, frame, request.ConstantAlpha, out var compositeError);
        if (!composited)
        {
            return Unavailable(GlyphReasonCodes.GlyphCompositeRefused,
                $"UpdateLayeredWindow({request.Bounds}, AC_SRC_ALPHA, constant {request.ConstantAlpha}) returned "
                + $"FALSE for window 0x{window:X} (last-error {compositeError}); nothing is composited. Last-error "
                + "87 here means the window is not layered, which is the overlay's measured hazard and cannot arise "
                + "from this class, which sets WS_EX_LAYERED at creation and never clears it");
        }

        ApplyClickThroughStyle(window, request.ClickThrough);

        if (!Win32GlyphInterop.SetWindowPos(
                window, Win32GlyphInterop.HwndTopmost, 0, 0, 0, 0,
                Win32GlyphInterop.SwpNomove | Win32GlyphInterop.SwpNosize
                    | Win32GlyphInterop.SwpNoactivate | Win32GlyphInterop.SwpShowwindow))
        {
            var error = Marshal.GetLastWin32Error();
            return Unavailable(GlyphReasonCodes.GlyphGeometryRefused,
                $"SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE|SWP_SHOWWINDOW) returned FALSE for window "
                + $"0x{window:X} (last-error {error}); nothing is on screen");
        }

        _current = request;

        var refusal = Confirm(request, frame);
        if (refusal is not null)
        {
            _presenting = false;
            return refusal;
        }

        _presenting = true;
        return new CapabilityState.Available(
            $"window 0x{window:X} is composited at {request.Bounds}: the OS reports it visible with that exact "
            + $"rectangle, returns this frame's {frame.ProvableInk.Count} fully-opaque ink sample(s) EXACTLY when "
            + "asked for the surface's content back (which a window that composites nothing cannot do — measured, "
            + "it returns zero everywhere), places it above every ordinary window in its own top-level z-order, "
            + $"routes a click at its centre {(request.ClickThrough ? "PAST it (and TO it when momentarily made "
                + "opaque, which is what makes the pass-through non-vacuous)" : "TO it")} after "
            + $"{LastRaiseAttempts} topmost assertion(s), and did not take the foreground. The surface composites "
            + $"at per-pixel alpha times a uniform {request.ConstantAlpha}/255. That a fully TRANSPARENT pixel "
            + "shows the desktop behind it cannot be proven from a window read-back at all — it flattens onto "
            + "black — and that a human SEES any of it is a headed claim; neither is made here");
    }

    /// <inheritdoc/>
    public CapabilityState Paint(GlyphFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (_disposed)
        {
            return Unavailable(GlyphReasonCodes.GlyphSurfaceDisposed,
                "this glyph surface was disposed; there is nothing to composite onto");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(GlyphReasonCodes.GlyphMechanismAbsent,
                "Win32GlyphSurface composites through UpdateLayeredWindow in USER32, which is a Windows "
                + "mechanism; nothing was ever presented and nothing was composited");
        }

        if (!_presenting || _current is null)
        {
            return Unavailable(GlyphReasonCodes.GlyphNothingPresented,
                "no surface is presented by this instance, so there is nothing on screen to composite onto and "
                + "nothing succeeded. Present first: compositing into a window the OS has not confirmed is on "
                + "screen is exactly the claim this capability refuses to make");
        }

        var bounds = _current.Bounds;
        if (frame.Width != bounds.Width || frame.Height != bounds.Height)
        {
            return Unavailable(GlyphReasonCodes.GlyphFrameSizeMismatch,
                $"the frame is {frame.Width}x{frame.Height} and the presented surface is {bounds.Width}x"
                + $"{bounds.Height}; this capability scales nothing");
        }

        if (!frame.HasProvableInk)
        {
            return NoProvableInk(frame);
        }

        if (!Composite(_window, bounds, frame, _current.ConstantAlpha, out var error))
        {
            return Unavailable(GlyphReasonCodes.GlyphCompositeRefused,
                $"UpdateLayeredWindow({bounds}, AC_SRC_ALPHA, constant {_current.ConstantAlpha}) returned FALSE "
                + $"for window 0x{_window:X} (last-error {error}); the surface still holds whatever it held before");
        }

        var refusal = ConfirmContent(frame);
        if (refusal is not null)
        {
            return refusal;
        }

        return new CapabilityState.Available(
            $"window 0x{_window:X} holds a {frame.Width}x{frame.Height} per-pixel-alpha frame: it was handed to "
            + $"the compositor and then read BACK out of the OS, and all {frame.ProvableInk.Count} of its "
            + "fully-opaque ink samples are exactly the colours the frame carries while its transparent samples "
            + "are nothing. That a human SEES it is a headed claim and is not made here");
    }

    /// <inheritdoc/>
    public CapabilityState MoveTo(GlyphBounds bounds)
    {
        if (_disposed)
        {
            return Unavailable(GlyphReasonCodes.GlyphSurfaceDisposed,
                "this glyph surface was disposed; there is no window to move");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(GlyphReasonCodes.GlyphMechanismAbsent,
                "Win32GlyphSurface moves layered windows through USER32, which is a Windows mechanism; nothing "
                + "was ever presented");
        }

        if (!_presenting || _current is null)
        {
            return Unavailable(GlyphReasonCodes.GlyphNothingPresented,
                "no surface is presented by this instance, so there is nothing to move and nothing succeeded");
        }

        if (bounds.Width != _current.Bounds.Width || bounds.Height != _current.Bounds.Height)
        {
            return Unavailable(GlyphReasonCodes.GlyphGeometryRefused,
                $"a move may not resize: the surface is {_current.Bounds.Width}x{_current.Bounds.Height} and "
                + $"{bounds.Width}x{bounds.Height} was asked for. Resizing means a new composite, because the "
                + "layered surface IS the frame — call Present");
        }

        var point = new Win32GlyphInterop.Point { X = bounds.X, Y = bounds.Y };
        if (!Win32GlyphInterop.UpdateLayeredWindowPosition(_window, 0, ref point, 0, 0, 0, 0, 0, 0))
        {
            var error = Marshal.GetLastWin32Error();
            return Unavailable(GlyphReasonCodes.GlyphGeometryRefused,
                $"UpdateLayeredWindow(destination {bounds.X},{bounds.Y}, everything else NULL) returned FALSE for "
                + $"window 0x{_window:X} (last-error {error}); the surface did not move");
        }

        if (!Win32GlyphInterop.GetWindowRect(_window, out var rect))
        {
            return Unavailable(GlyphReasonCodes.GlyphGeometryRefused,
                $"GetWindowRect(0x{_window:X}) returned FALSE after the move (last-error "
                + $"{Marshal.GetLastWin32Error()}); the OS will not say where the surface is, so no move is claimed");
        }

        var held = new GlyphBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (held != bounds)
        {
            return Unavailable(GlyphReasonCodes.GlyphGeometryRefused,
                $"the OS holds {held} for window 0x{_window:X} after a move to {bounds}; the surface is not where "
                + "the caller put it");
        }

        _current = new GlyphSurfaceRequest(bounds, _current.Opacity, _current.ClickThrough);
        return new CapabilityState.Available(
            $"window 0x{_window:X} is at {bounds}: one UpdateLayeredWindow carrying a destination point and "
            + "nothing else, confirmed by GetWindowRect. NO style was written, NO z-order was walked and NO hit "
            + "test was asked, so this claims the RECTANGLE only — not that the surface is still on top, still "
            + "passing clicks through, or still holding its content");
    }

    /// <inheritdoc/>
    public void Reassert()
    {
        if (_disposed || !_presenting || !OperatingSystem.IsWindows())
        {
            return;
        }

        Raise();
    }

    /// <inheritdoc/>
    public CapabilityState Withdraw()
    {
        if (_disposed)
        {
            return Unavailable(GlyphReasonCodes.GlyphSurfaceDisposed,
                "this glyph surface was disposed; its window was already destroyed by teardown");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(GlyphReasonCodes.GlyphMechanismAbsent,
                "Win32GlyphSurface drives top-level windows through USER32, which is a Windows mechanism; "
                + "nothing was ever presented");
        }

        if (!_presenting || _current is null)
        {
            return Unavailable(GlyphReasonCodes.GlyphNothingPresented,
                "no surface is presented by this instance, so there is nothing to withdraw and nothing succeeded");
        }

        var bounds = _current.Bounds;
        Win32GlyphInterop.ShowWindow(_window, Win32GlyphInterop.SwHide);

        if (Win32GlyphInterop.IsWindowVisible(_window))
        {
            return Unavailable(GlyphReasonCodes.GlyphWithdrawRefused,
                $"ShowWindow(SW_HIDE) ran but the OS still reports window 0x{_window:X} visible; the surface is "
                + "still on screen and withdrawal is not claimed");
        }

        var (x, y) = bounds.Centre;
        var winner = Win32GlyphInterop.WindowFromPoint(new Win32GlyphInterop.Point { X = x, Y = y });
        if (winner == _window)
        {
            return Unavailable(GlyphReasonCodes.GlyphWithdrawRefused,
                $"the OS reports window 0x{_window:X} hidden, but its hit test still routes the point {x},{y} to "
                + "it; withdrawal is not claimed");
        }

        _presenting = false;
        return new CapabilityState.Available(
            $"window 0x{_window:X} is off screen: the OS reports it not visible and its hit test no longer routes "
            + $"the point {x},{y} to it. The window and its composited surface are kept for the next Present "
            + "(measured: the content survives a hide and a re-show with no further composite)");
    }

    /// <inheritdoc/>
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
                + "window may outlive this surface";
        }

        if (_window != 0)
        {
            if (!Win32GlyphInterop.DestroyWindow(_window))
            {
                TeardownDiagnostic ??=
                    $"DestroyWindow(0x{_window:X}) returned FALSE (last-error {Marshal.GetLastWin32Error()}); an "
                    + "invisible top-level window may survive for the life of the process";
            }

            _window = 0;
        }

        if (_classAtom != 0)
        {
            Win32GlyphInterop.UnregisterClassW(_windowClassName, _moduleHandle);
            _classAtom = 0;
        }

        // GDI objects are a process-wide, quota-limited resource: a bouncing module that leaked one
        // DIB section per text change would exhaust the quota during a long session and then start
        // failing to composite with no error anyone could read.
        ReleaseFrameSurfaces();

        GC.KeepAlive(_windowProc);
    }

    // ---------- the OS round trips ----------

    /// <summary>
    /// Every confirmation, in order, cheapest first. Returns null when all of them held and the
    /// typed refusal for the first that did not. This is the only path to <c>Available</c> in
    /// <see cref="Present"/>.
    /// </summary>
    private CapabilityState? Confirm(GlyphSurfaceRequest request, GlyphFrame frame)
    {
        var window = _window;

        if (!Win32GlyphInterop.IsWindow(window))
        {
            return Unavailable(GlyphReasonCodes.GlyphNotVisible,
                $"the OS does not recognise 0x{window:X} as a window at all after placement");
        }

        if (!Win32GlyphInterop.IsWindowVisible(window))
        {
            return Unavailable(GlyphReasonCodes.GlyphNotVisible,
                $"SetWindowPos(SWP_SHOWWINDOW) returned success but the OS does not report window 0x{window:X} "
                + "visible; nothing is on screen");
        }

        if (!Win32GlyphInterop.GetWindowRect(window, out var rect))
        {
            return Unavailable(GlyphReasonCodes.GlyphGeometryRefused,
                $"GetWindowRect(0x{window:X}) returned FALSE (last-error {Marshal.GetLastWin32Error()}); the OS "
                + "will not say where the surface is, so no placement is claimed");
        }

        var held = new GlyphBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (held != request.Bounds)
        {
            return Unavailable(GlyphReasonCodes.GlyphGeometryRefused,
                $"the OS holds {held} for window 0x{window:X} but {request.Bounds} was asked for; the surface is "
                + "not where the caller placed it");
        }

        var exStyle = (uint)Win32GlyphInterop.GetWindowLongPtrW(window, Win32GlyphInterop.GwlExstyle);
        var required = Win32GlyphInterop.WsExLayered | Win32GlyphInterop.WsExToolwindow
            | Win32GlyphInterop.WsExNoactivate | Win32GlyphInterop.WsExTopmost;
        if ((exStyle & required) != required)
        {
            return Unavailable(GlyphReasonCodes.GlyphStyleRefused,
                $"the extended-style read-back for window 0x{window:X} is 0x{exStyle:X}, missing "
                + $"0x{required & ~exStyle:X} of WS_EX_LAYERED|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TOPMOST");
        }

        // THE GHOST CHECK, and it is the whole reason this capability was allowed to exist. See the
        // class remarks for the measurement that chose this call over the overlay's.
        var content = ConfirmContent(frame);
        if (content is not null)
        {
            return content;
        }

        var zOrder = ReadZOrder(window);
        if (!zOrder.AboveEveryOrdinaryWindow)
        {
            return Unavailable(GlyphReasonCodes.GlyphNotOnTop,
                $"the OS's own top-level z-order puts window 0x{window:X} at position {zOrder.Index} of "
                + $"{zOrder.VisibleCount} visible windows, at or below the first ordinary (non-topmost) window at "
                + $"position {zOrder.FirstOrdinaryIndex}; the surface is buried");
        }

        var routing = ConfirmInputRouting(request);
        if (routing is not null)
        {
            return routing;
        }

        var foreground = Win32GlyphInterop.GetForegroundWindow();
        if (foreground == window)
        {
            return Unavailable(GlyphReasonCodes.GlyphStoleFocus,
                $"window 0x{window:X} is the foreground window after being shown; WS_EX_NOACTIVATE and "
                + "SWP_NOACTIVATE did not hold and the surface interrupted whatever the user was doing");
        }

        return null;
    }

    /// <summary>
    /// <b>The content fact: read the surface back OUT of the operating system and require it to
    /// carry this frame.</b>
    ///
    /// <para>Two clauses, and they catch different things. Every fully-OPAQUE ink point must equal
    /// its own colour exactly — that is the clause a window compositing nothing fails, measured, and
    /// it is what stands in for the overlay's alpha read-back. Every fully-TRANSPARENT sample must
    /// read zero — that clause a ghost passes trivially, and it is still asked because a surface
    /// that composited an OPAQUE PLATE where the glyphs should have holes fails it and passes
    /// nothing else, and an opaque plate over the user's desktop is worse than an absent
    /// surface.</para>
    ///
    /// <para><b>The exact limit, measured rather than hedged.</b> This read-back flattens the
    /// surface onto black, so a fully transparent pixel and an opaque BLACK one are the same value
    /// here and no reading of it can tell them apart. Partial alphas are not compared at all: the
    /// value was measured stable across repeated calls, and ALSO measured once at half its expected
    /// value on a window whose device context had been read with <c>GetDC</c> + <c>BitBlt</c> first.
    /// Rather than model that, this class anchors on the two extremes and never calls
    /// <c>GetDC</c> on its own window. The transparent-versus-black distinction belongs to a
    /// composited-desktop read over a known background, which is the harness's and is
    /// machine-conditional.</para>
    /// </summary>
    private CapabilityState? ConfirmContent(GlyphFrame frame)
    {
        if (!EnsureFrameSurfaces(frame.Width, frame.Height, out var surfaceFailure))
        {
            return Unavailable(GlyphReasonCodes.GlyphNotComposited, surfaceFailure);
        }

        if (!Win32GlyphInterop.PrintWindow(_window, _readbackDc, Win32GlyphInterop.PwRenderfullcontent))
        {
            return Unavailable(GlyphReasonCodes.GlyphNotComposited,
                $"PrintWindow(0x{_window:X}, PW_RENDERFULLCONTENT) returned FALSE (last-error "
                + $"{Marshal.GetLastWin32Error()}); the OS will not hand back its copy of the surface, so no "
                + "composite is claimed");
        }

        foreach (var (x, y) in frame.ProvableInk)
        {
            var held = ReadBack(x, y);
            var expected = frame.PremultipliedColourAt(x, y);
            if (held != expected)
            {
                return Unavailable(GlyphReasonCodes.GlyphNotComposited,
                    $"UpdateLayeredWindow returned TRUE, and the OS returns 0x{held:X6} at the fully-opaque ink "
                    + $"point {x},{y} where the frame carries 0x{expected:X6}. The surface does not hold what was "
                    + "composited into it. A window that composites NOTHING returns zero at every point — "
                    + "measured, 0 non-zero of 14400 — so this reading cannot be produced by a ghost and is not "
                    + "claimed as one");
            }
        }

        var step = Math.Max(1, (int)Math.Sqrt((double)frame.Width * frame.Height / TransparentSampleTarget));
        for (var y = 0; y < frame.Height; y += step)
        {
            for (var x = 0; x < frame.Width; x += step)
            {
                if (frame.AlphaAt(x, y) != 0)
                {
                    continue;
                }

                var held = ReadBack(x, y);
                if (held != 0)
                {
                    return Unavailable(GlyphReasonCodes.GlyphNotComposited,
                        $"the frame is fully TRANSPARENT at {x},{y} and the OS returns 0x{held:X6} there. "
                        + "Something opaque is composited where the frame asked for a hole, which over the "
                        + "user's desktop is worse than no surface at all");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The input-routing fact, in both polarities, from the window manager's own hit test. Leg one
    /// makes the surface opaque to input and requires the point to route TO it, which is what
    /// establishes that the point is really this surface's; leg two restores the requested polarity
    /// and requires the answer the request asked for. Either half alone is the flag assertion this
    /// capability exists to refuse. The composite is never touched, so nothing changes on screen.
    /// </summary>
    private CapabilityState? ConfirmInputRouting(GlyphSurfaceRequest request)
    {
        var (x, y) = request.Bounds.Centre;
        var point = new Win32GlyphInterop.Point { X = x, Y = y };
        var window = _window;

        ApplyClickThroughStyle(window, clickThrough: false);
        var opaqueWinner = HitTest(point, expectSurface: true, out var opaqueAttempts);
        LastRaiseAttempts = opaqueAttempts;

        if (opaqueWinner != window)
        {
            ApplyClickThroughStyle(window, request.ClickThrough);
            return Unavailable(GlyphReasonCodes.GlyphInputNotReceived,
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
            return Unavailable(GlyphReasonCodes.GlyphInputNotPassingThrough,
                $"WS_EX_TRANSPARENT is set on window 0x{window:X} and the window manager STILL routes the point "
                + $"{x},{y} to it. Clicks are being swallowed: the desktop underneath is broken while the surface "
                + "looks implemented");
        }

        if (!request.ClickThrough && finalWinner != window)
        {
            return Unavailable(GlyphReasonCodes.GlyphInputNotReceived,
                $"the surface was asked to catch its own clicks, and after {finalAttempts} topmost assertion(s) "
                + $"the window manager routes the point {x},{y} to {Describe(finalWinner)} instead of "
                + $"0x{window:X}");
        }

        return null;
    }

    private nint HitTest(Win32GlyphInterop.Point point, bool expectSurface, out int attempts)
    {
        var winner = (nint)0;
        for (attempts = 1; attempts <= MaxRaiseAttempts; attempts++)
        {
            Raise();
            winner = Win32GlyphInterop.WindowFromPoint(point);
            var matched = expectSurface ? winner == _window : winner != _window;
            if (matched)
            {
                return winner;
            }
        }

        attempts = MaxRaiseAttempts;
        return winner;
    }

    /// <summary>Hand the frame to the compositor. One call carries the position, the size, the
    /// pixels and the blend.</summary>
    private bool Composite(nint window, GlyphBounds bounds, GlyphFrame frame, byte constantAlpha, out int error)
    {
        error = 0;
        if (!EnsureFrameSurfaces(frame.Width, frame.Height, out _))
        {
            return false;
        }

        Marshal.Copy(frame.Pixels, 0, _frameBits, frame.Pixels.Length);

        var destination = new Win32GlyphInterop.Point { X = bounds.X, Y = bounds.Y };
        var size = new Win32GlyphInterop.Size { Cx = bounds.Width, Cy = bounds.Height };
        var source = new Win32GlyphInterop.Point { X = 0, Y = 0 };
        var blend = new Win32GlyphInterop.BlendFunction
        {
            BlendOp = Win32GlyphInterop.AcSrcOver,
            BlendFlags = 0,
            SourceConstantAlpha = constantAlpha,
            AlphaFormat = Win32GlyphInterop.AcSrcAlpha,
        };

        var ok = Win32GlyphInterop.UpdateLayeredWindow(
            window, 0, ref destination, ref size, _frameDc, ref source, 0, ref blend,
            Win32GlyphInterop.UlwAlpha);
        error = Marshal.GetLastWin32Error();
        return ok;
    }

    private uint ReadBack(int x, int y)
    {
        var offset = (y * _frameWidth * GlyphFrame.BytesPerPixel) + (x * GlyphFrame.BytesPerPixel);
        return (uint)((Marshal.ReadByte(_readbackBits, offset) << 16)
            | (Marshal.ReadByte(_readbackBits, offset + 1) << 8)
            | Marshal.ReadByte(_readbackBits, offset + 2));
    }

    private CapabilityState NoProvableInk(GlyphFrame frame) =>
        Unavailable(GlyphReasonCodes.GlyphFrameCarriesNoProvableInk,
            $"the {frame.Width}x{frame.Height} frame carries no fully-opaque non-black pixel, so NOTHING about it "
            + "could be distinguished from a window that composites nothing: measured, an entirely transparent "
            + "composite reads back 0 non-zero pixels of 25600, which is byte for byte what a layered window that "
            + "was never composited reads. Rather than claim a composite that cannot be proven, the frame is "
            + "refused");

    /// <summary>
    /// The source and read-back DIB sections, (re)created when the size changes. Both are TOP-DOWN
    /// (negative <c>biHeight</c>) so a frame's first row is the surface's first row.
    ///
    /// <para><b>The limit the overlay's equivalent has and this one shares</b>: both sections come
    /// from ONE <c>BITMAPINFO</c>, so an error the two share — a wrong row order, a wrong stride —
    /// cancels out and compares equal. What does NOT cancel is the thing this check exists for: the
    /// read-back's pixels come from DWM's copy of the window, not from the buffer that was written,
    /// so a surface holding nothing reads as nothing however the header is laid out.</para>
    /// </summary>
    private bool EnsureFrameSurfaces(int width, int height, out string failure)
    {
        failure = string.Empty;
        if (_frameDc != 0 && _frameWidth == width && _frameHeight == height)
        {
            return true;
        }

        ReleaseFrameSurfaces();

        var screenDc = Win32GlyphInterop.GetDC(0);
        if (screenDc == 0)
        {
            failure = $"GetDC(NULL) returned NULL (last-error {Marshal.GetLastWin32Error()}); no device context "
                + "to build a composite surface from, so nothing was composited";
            return false;
        }

        try
        {
            var info = new Win32GlyphInterop.BitmapInfo
            {
                bmiHeader = new Win32GlyphInterop.BitmapInfoHeader
                {
                    biSize = (uint)Marshal.SizeOf<Win32GlyphInterop.BitmapInfoHeader>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = Win32GlyphInterop.BiRgb,
                },
            };

            _frameBitmap = Win32GlyphInterop.CreateDIBSection(
                screenDc, ref info, Win32GlyphInterop.DibRgbColors, out _frameBits, 0, 0);
            _readbackBitmap = Win32GlyphInterop.CreateDIBSection(
                screenDc, ref info, Win32GlyphInterop.DibRgbColors, out _readbackBits, 0, 0);

            if (_frameBitmap == 0 || _readbackBitmap == 0 || _frameBits == 0 || _readbackBits == 0)
            {
                failure = $"CreateDIBSection({width}x{height}, 32bpp top-down) returned NULL (last-error "
                    + $"{Marshal.GetLastWin32Error()}); no composite surface, so nothing was composited";
                ReleaseFrameSurfaces();
                return false;
            }

            _frameDc = Win32GlyphInterop.CreateCompatibleDC(screenDc);
            _readbackDc = Win32GlyphInterop.CreateCompatibleDC(screenDc);
            if (_frameDc == 0 || _readbackDc == 0)
            {
                failure = $"CreateCompatibleDC returned NULL (last-error {Marshal.GetLastWin32Error()}); no memory "
                    + "device context for the frame, so nothing was composited";
                ReleaseFrameSurfaces();
                return false;
            }

            Win32GlyphInterop.SelectObject(_frameDc, _frameBitmap);
            Win32GlyphInterop.SelectObject(_readbackDc, _readbackBitmap);
            _frameWidth = width;
            _frameHeight = height;
            return true;
        }
        finally
        {
            Win32GlyphInterop.ReleaseDC(0, screenDc);
        }
    }

    private void ReleaseFrameSurfaces()
    {
        if (_frameDc != 0)
        {
            Win32GlyphInterop.DeleteDC(_frameDc);
            _frameDc = 0;
        }

        if (_readbackDc != 0)
        {
            Win32GlyphInterop.DeleteDC(_readbackDc);
            _readbackDc = 0;
        }

        if (_frameBitmap != 0)
        {
            Win32GlyphInterop.DeleteObject(_frameBitmap);
            _frameBitmap = 0;
        }

        if (_readbackBitmap != 0)
        {
            Win32GlyphInterop.DeleteObject(_readbackBitmap);
            _readbackBitmap = 0;
        }

        _frameBits = 0;
        _readbackBits = 0;
        _frameWidth = 0;
        _frameHeight = 0;
    }

    /// <summary>WPF's own <c>ReassertTopmost</c> for this window
    /// (<c>BouncingTextService.cs:1049-1052</c>).</summary>
    private void Raise() => Win32GlyphInterop.SetWindowPos(
        _window, Win32GlyphInterop.HwndTopmost, 0, 0, 0, 0,
        Win32GlyphInterop.SwpNomove | Win32GlyphInterop.SwpNosize | Win32GlyphInterop.SwpNoactivate);

    /// <summary>
    /// The click-through flip. <c>WS_EX_LAYERED</c> is re-asserted in the SAME word rather than
    /// left to survive on its own, which is what makes that hazard unreachable from here: there
    /// is no instant at which this window is not layered, so there is no instant at which a
    /// composite could be mistaken for the toggle-then-ULW conversion.
    /// </summary>
    private static void ApplyClickThroughStyle(nint window, bool clickThrough)
    {
        var style = (uint)Win32GlyphInterop.GetWindowLongPtrW(window, Win32GlyphInterop.GwlExstyle)
            | Win32GlyphInterop.WsExLayered
            | Win32GlyphInterop.WsExToolwindow
            | Win32GlyphInterop.WsExNoactivate;

        if (clickThrough)
        {
            style |= Win32GlyphInterop.WsExTransparent;
        }
        else
        {
            style &= ~Win32GlyphInterop.WsExTransparent;
        }

        Win32GlyphInterop.SetWindowLongPtrW(window, Win32GlyphInterop.GwlExstyle, (nint)style);
    }

    /// <summary>Where the OS itself puts this window among the visible top-level windows. The
    /// ORDERING is the fact; <c>WS_EX_TOPMOST</c> is a flag, and a flag is what the first attempt
    /// trusted.</summary>
    private static ZOrderPosition ReadZOrder(nint window)
    {
        var index = -1;
        var firstOrdinary = -1;
        var visible = 0;

        for (var candidate = Win32GlyphInterop.GetTopWindow(0);
             candidate != 0;
             candidate = Win32GlyphInterop.GetWindow(candidate, Win32GlyphInterop.GwHwndnext))
        {
            if (!Win32GlyphInterop.IsWindowVisible(candidate))
            {
                continue;
            }

            if (candidate == window)
            {
                index = visible;
            }
            else if (firstOrdinary < 0)
            {
                var exStyle = (uint)Win32GlyphInterop.GetWindowLongPtrW(candidate, Win32GlyphInterop.GwlExstyle);
                if ((exStyle & Win32GlyphInterop.WsExTopmost) == 0)
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

    private nint EnsureWindow(GlyphBounds bounds, out string failure)
    {
        failure = string.Empty;
        if (_window != 0)
        {
            return _window;
        }

        _moduleHandle = Win32GlyphInterop.GetModuleHandleW(null);
        var windowClass = new Win32GlyphInterop.WndClassExW
        {
            cbSize = (uint)Marshal.SizeOf<Win32GlyphInterop.WndClassExW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = _moduleHandle,
            lpszClassName = _windowClassName,
        };

        _classAtom = Win32GlyphInterop.RegisterClassExW(ref windowClass);
        if (_classAtom == 0)
        {
            failure = $"RegisterClassEx(\"{_windowClassName}\") returned 0 (last-error "
                + $"{Marshal.GetLastWin32Error()}); no glyph window could be created";
            return 0;
        }

        // WS_EX_LAYERED is set HERE, in the creation call, and is never cleared for the life of the
        // window. That is the overlay's hazard closed structurally rather than by discipline.
        _window = Win32GlyphInterop.CreateWindowExW(
            Win32GlyphInterop.WsExLayered | Win32GlyphInterop.WsExTransparent
                | Win32GlyphInterop.WsExToolwindow | Win32GlyphInterop.WsExNoactivate,
            _windowClassName,
            "CCP glyph surface",
            Win32GlyphInterop.WsPopup,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            0, 0, _moduleHandle, 0);

        if (_window == 0)
        {
            failure = $"CreateWindowEx for class \"{_windowClassName}\" at {bounds} returned NULL (last-error "
                + $"{Marshal.GetLastWin32Error()})";
            Win32GlyphInterop.UnregisterClassW(_windowClassName, _moduleHandle);
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
        var copied = Win32GlyphInterop.GetClassNameW(window, buffer, buffer.Capacity);
        return copied > 0 ? $"0x{window:X} (class \"{buffer}\")" : $"0x{window:X}";
    }

    private static CapabilityState.Unavailable Unavailable(string code, string detail) =>
        new(new CapabilityReason(code, detail));
}
