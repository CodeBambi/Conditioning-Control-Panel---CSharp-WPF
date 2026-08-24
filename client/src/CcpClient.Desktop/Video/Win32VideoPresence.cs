using System.Runtime.InteropServices;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Video;

/// <summary>
/// The Windows video surface: one layered, non-activating, topmost window this presence owns, and
/// the operating system asked back after every frame.
///
/// <para><b>Nothing here reports success because a call returned.</b> Every
/// <see cref="CapabilityState.Available"/> is downstream of a read-back: the window's existence,
/// visibility, held rectangle and z-order position come from the OS; the occlusion answer is the
/// window manager's own hit test; and the picture is compared, pixel for pixel at ten points,
/// against the buffer that was handed over — over a bar that must come back exactly the colour the
/// painter filled it with.</para>
///
/// <para><b>The frame path is ONE blit.</b> The bars and the scaled picture are composed into a
/// single client-sized buffer (<see cref="VideoLetterbox.Compose"/>) and blitted 1:1, so a frame is
/// never half-painted and the expected read-back value is EXACT rather than whatever GDI's stretch
/// blender produced.</para>
/// </summary>
public sealed class Win32VideoPresence : IVideoPresence
{
    /// <summary>
    /// How many times the surface re-asserts the topmost band before reporting itself occluded.
    ///
    /// <para><b>The band is CONTESTED and this is not a retry-until-green.</b> The shipping WPF
    /// product re-asserts <c>HWND_TOPMOST</c> on a roughly one-second cadence for its own flash
    /// windows (<c>Services/Flash/FlashService.cs:206-243</c>, <c>:3867</c>) precisely because any
    /// other application can take the top of the band away, and the port's own overlay carries the
    /// same <c>Reassert</c> for the same reason. Measured while this capability was being written:
    /// a surface placed topmost lost its own centre point to the shipping product's window within
    /// milliseconds (the packet plan.md §0, Q4). A capability that gave up on the first loss would be
    /// unusable on the desktop it is meant to run on.</para>
    ///
    /// <para><b>Re-asserting cannot manufacture the answer.</b> It can only make this surface MORE
    /// likely to win its own point, so a refusal after the last attempt is stronger evidence than a
    /// refusal after the first — and a surface that is genuinely covered by a foreign topmost window
    /// that keeps re-asserting loses every attempt and is reported as occluded, which is the truth.
    /// The same reasoning, in the same words, as <c>OverlayWindowProbe.HitTestExpecting</c>.</para>
    /// </summary>
    public const int MaxBandAttempts = 4;

    private readonly string _windowClassName = $"CcpVideoSurface_{Environment.ProcessId}_{Guid.NewGuid():N}";
    private readonly Win32VideoInterop.WndProc _windowProc;
    private readonly IVideoClipSource _clips;

    private nint _moduleHandle;
    private ushort _classAtom;
    private nint _window;
    private bool _disposed;

    private VideoBounds _bounds;
    private uint _letterbox;
    private PictureBox _box;
    private byte[]? _composed;
    private uint _lastSurfaceSample;
    private uint _previousSurfaceSample;
    private uint _lastFrameSample;
    private bool _frameDiffersFromPrevious;
    private bool _anyFramePresented;

    /// <param name="clips">The decoder, only so <see cref="ObserveDisplay"/> can report whether this
    /// machine can decode anything at all. The presence itself decodes nothing: keeping the surface
    /// ignorant of containers is what lets every draw fact run on a synthetic buffer with no codec
    /// anywhere near it, exactly as <c>Overlay.OverlayFrame</c> does for the overlay.</param>
    public Win32VideoPresence(IVideoClipSource? clips = null)
    {
        _windowProc = (window, message, wParam, lParam) =>
            Win32VideoInterop.DefWindowProcW(window, message, wParam, lParam);
        _clips = clips ?? VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows);
    }

    /// <inheritdoc/>
    public bool IsPresenting { get; private set; }

    /// <inheritdoc/>
    public CapabilityState? LastShow { get; private set; }

    /// <inheritdoc/>
    public VideoSurfaceObservation LastObservation { get; private set; } = VideoSurfaceObservation.NotAsked;

    /// <inheritdoc/>
    public int FramesHeld { get; private set; }

    /// <inheritdoc/>
    public int FramesAdvanced { get; private set; }

    /// <inheritdoc/>
    public bool PictureIsMoving => !_disposed && IsPresenting && Observe().PictureIsMoving;

    /// <inheritdoc/>
    public bool CanReachADisplay => !_disposed && ObserveDisplay().Confirmed;

    /// <inheritdoc/>
    public CapabilityState Present(VideoSurfaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_disposed)
        {
            return Refuse(VideoReasonCodes.VideoPresenceDisposed,
                "this video presence was disposed; its surface is gone and it will never present again");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Refuse(VideoReasonCodes.VideoMechanismAbsent,
                "the Win32 video surface is a Windows mechanism and this process is not on Windows");
        }

        var display = ObserveDisplay();
        if (!display.WindowStationVisible || display.DisplayCount < 1)
        {
            return Refuse(VideoReasonCodes.VideoNoDisplay,
                $"the operating system reports windowStationVisible={display.WindowStationVisible} and "
                + $"{display.DisplayCount} display(s), so no video surface this process creates could be put in "
                + "front of anybody; nothing was shown");
        }

        var window = EnsureWindow(request.Bounds, out var failure);
        if (window == 0)
        {
            return Refuse(VideoReasonCodes.VideoWindowCreationFailed, failure);
        }

        // A layered window with no attributes set is not drawn at all, so the alpha is written
        // BEFORE the surface reaches the screen — the same ordering the overlay uses, and the same
        // ordering WPF's own video window uses when it configures everything before Show().
        if (!Win32VideoInterop.SetLayeredWindowAttributes(window, 0, 0xFF, Win32VideoInterop.LwaAlpha))
        {
            return Refuse(VideoReasonCodes.VideoWindowCreationFailed,
                $"SetLayeredWindowAttributes(LWA_ALPHA, 255) returned FALSE for window 0x{window:X} "
                + $"(last-error {Marshal.GetLastWin32Error()}); a layered window with no alpha is not composited");
        }

        var bounds = request.Bounds;
        if (!Win32VideoInterop.SetWindowPos(
                window, Win32VideoInterop.HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                Win32VideoInterop.SwpNoactivate | Win32VideoInterop.SwpShowwindow))
        {
            return Refuse(VideoReasonCodes.VideoSurfaceNotOnScreen,
                $"SetWindowPos(HWND_TOPMOST, {bounds}, SWP_NOACTIVATE|SWP_SHOWWINDOW) returned FALSE for window "
                + $"0x{window:X} (last-error {Marshal.GetLastWin32Error()})");
        }

        _bounds = bounds;
        _letterbox = request.Letterbox;
        _box = default;
        _composed = null;
        // Both sample slots go to zero, which is a SENTINEL and not a fold of the freshly filled
        // surface. The consequence is worth naming where it lives: the FIRST Show after a Present
        // compares its read-back against 0, so its SurfaceAdvanced is true for any non-zero fold and
        // carries no evidence. Every later frame compares against the fold of the frame really
        // before it, which is where the differential does its work.
        _lastSurfaceSample = 0;
        _previousSurfaceSample = 0;
        _lastFrameSample = 0;
        _frameDiffersFromPrevious = false;
        _anyFramePresented = false;
        FillLetterbox();

        var observation = ReadHoldingTheBand();
        LastObservation = observation;
        IsPresenting = observation.WindowExists && observation.WindowVisible;

        if (!observation.WindowExists || !observation.WindowVisible
            || !observation.AboveEveryOrdinaryWindow || !HoldsRequestedBounds(observation, bounds))
        {
            return Refuse(VideoReasonCodes.VideoSurfaceNotOnScreen,
                $"the surface was placed but the operating system reports exists={observation.WindowExists}, "
                + $"visible={observation.WindowVisible}, bounds={observation.HeldBounds} (requested {bounds}), "
                + $"aboveEveryOrdinaryWindow={observation.AboveEveryOrdinaryWindow}");
        }

        if (!observation.HitTestRoutesHere)
        {
            return Refuse(VideoReasonCodes.VideoSurfaceOccluded,
                $"the surface is on screen and the window manager routes its own centre point to window "
                + $"0x{observation.HitTestWinner:X} rather than to it (0x{observation.Window:X}): another "
                + "window is covering it there, so a picture put on it would not be the picture in front of "
                + "the user");
        }

        if (!observation.LetterboxHeld)
        {
            return Refuse(VideoReasonCodes.VideoFrameNotHeld,
                $"the surface is on screen and the operating system does NOT hold the bar colour "
                + $"0x{_letterbox:X6} the painter filled it with, so nothing this presence draws can be "
                + "believed to have reached it");
        }

        return new CapabilityState.Available(
            $"the operating system holds a {bounds} video surface above every ordinary window, routes its centre "
            + "to it, and carries the bar colour it was filled with");
    }

    /// <inheritdoc/>
    public CapabilityState Show(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_disposed)
        {
            return Refuse(VideoReasonCodes.VideoPresenceDisposed,
                "this video presence was disposed; its surface is gone and it will never present again");
        }

        if (!IsPresenting || _window == 0)
        {
            var state = Refuse(VideoReasonCodes.VideoNothingPresented,
                "there is no video surface on screen to put a picture on; Present must succeed first");
            LastShow = state;
            return state;
        }

        _box = VideoLetterbox.Fit(_bounds.Width, _bounds.Height, frame.Width, frame.Height);
        if (!_box.HasArea)
        {
            var state = Refuse(VideoReasonCodes.VideoClipHasNoPicture,
                $"a {frame.Width}x{frame.Height} picture leaves no drawable area inside a {_bounds} surface");
            LastShow = state;
            return state;
        }

        _composed = VideoLetterbox.Compose(frame, _bounds.Width, _bounds.Height, _box, _letterbox);
        var sample = frame.Sample();
        _frameDiffersFromPrevious = _anyFramePresented && sample != _lastFrameSample;
        _lastFrameSample = sample;
        _anyFramePresented = true;
        _previousSurfaceSample = _lastSurfaceSample;

        Blit(_composed);

        var observation = ReadHoldingTheBand();
        LastObservation = observation;
        _lastSurfaceSample = observation.SurfaceSample;

        CapabilityState result;
        if (!observation.WindowExists || !observation.WindowVisible || !observation.AboveEveryOrdinaryWindow)
        {
            result = Refuse(VideoReasonCodes.VideoSurfaceNotOnScreen,
                $"the picture was handed over and the operating system reports exists={observation.WindowExists}, "
                + $"visible={observation.WindowVisible}, aboveEveryOrdinaryWindow="
                + $"{observation.AboveEveryOrdinaryWindow}");
        }
        else if (!observation.HitTestRoutesHere)
        {
            result = Refuse(VideoReasonCodes.VideoSurfaceOccluded,
                $"the picture is on a surface the window manager no longer routes its own centre to: the point "
                + $"answers window 0x{observation.HitTestWinner:X}, not 0x{observation.Window:X}");
        }
        else if (!observation.FrameHeld)
        {
            result = Refuse(VideoReasonCodes.VideoFrameNotHeld,
                $"the picture was handed to the operating system and its own copy of the surface does NOT carry "
                + $"it: barHeld={observation.LetterboxHeld}, {observation.PictureMatched} of "
                + $"{observation.PictureSampled} sampled picture points match. THIS is the difference between "
                + "frames decoded and frames displayed, and it is the whole reason this capability exists");
        }
        else
        {
            FramesHeld++;
            if (observation.SurfaceAdvanced)
            {
                FramesAdvanced++;
            }

            result = new CapabilityState.Available(
                $"the operating system's own copy of the surface carries this picture at "
                + $"{observation.PictureMatched} of {observation.PictureSampled} sampled points"
                + (observation.SurfaceAdvanced ? " and CHANGED from the previous frame" : string.Empty));
        }

        LastShow = result;
        return result;
    }

    /// <inheritdoc/>
    public CapabilityState Withdraw()
    {
        if (_disposed)
        {
            return Refuse(VideoReasonCodes.VideoPresenceDisposed, "this video presence was disposed");
        }

        if (_window == 0 || !IsPresenting)
        {
            return Refuse(VideoReasonCodes.VideoNothingPresented,
                "there is no video surface on screen to take down");
        }

        Win32VideoInterop.ShowWindow(_window, Win32VideoInterop.SwHide);
        var observation = Read();
        LastObservation = observation;
        IsPresenting = false;
        _composed = null;
        _box = default;

        if (observation.WindowVisible || observation.HitTestRoutesHere)
        {
            return Refuse(VideoReasonCodes.VideoSurfaceNotOnScreen,
                $"the surface was hidden and the operating system still reports visible="
                + $"{observation.WindowVisible} / hit test routing to it={observation.HitTestRoutesHere}");
        }

        return new CapabilityState.Available(
            "the operating system reports the video surface is no longer visible and no longer answers a hit "
            + "test at its own centre");
    }

    /// <inheritdoc/>
    public VideoSurfaceObservation Observe()
    {
        if (_disposed || !OperatingSystem.IsWindows() || _window == 0)
        {
            return VideoSurfaceObservation.NotAsked;
        }

        var observation = Read();
        LastObservation = observation;
        return observation;
    }

    /// <inheritdoc/>
    public VideoDisplayObservation ObserveDisplay()
    {
        if (_disposed || !OperatingSystem.IsWindows())
        {
            return VideoDisplayObservation.NotAsked;
        }

        var stationVisible = false;
        var station = Win32VideoInterop.GetProcessWindowStation();
        if (station != 0
            && Win32VideoInterop.GetUserObjectInformationW(
                station, Win32VideoInterop.UoiFlags, out var flags, Marshal.SizeOf<Win32VideoInterop.UserObjectFlags>(), out _))
        {
            stationVisible = (flags.dwFlags & Win32VideoInterop.WsfVisible) != 0;
        }

        var displays = Win32VideoInterop.GetSystemMetrics(Win32VideoInterop.SmCmonitors);
        var composed = Win32VideoInterop.DwmIsCompositionEnabled(out var enabled) == 0 && enabled;
        return new VideoDisplayObservation(true, stationVisible, displays, composed, _clips.MediaStackUsable);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsPresenting = false;
        _composed = null;

        if (_window != 0)
        {
            Win32VideoInterop.DestroyWindow(_window);
            _window = 0;
        }

        if (_classAtom != 0)
        {
            Win32VideoInterop.UnregisterClassW(_windowClassName, _moduleHandle);
            _classAtom = 0;
        }
    }

    private static CapabilityState Refuse(string code, string detail) =>
        new CapabilityState.Unavailable(new CapabilityReason(code, detail));

    private static bool HoldsRequestedBounds(VideoSurfaceObservation observation, VideoBounds requested) =>
        observation.HeldBounds == requested;

    private nint EnsureWindow(VideoBounds bounds, out string failure)
    {
        failure = string.Empty;
        if (_window != 0)
        {
            return _window;
        }

        _moduleHandle = Win32VideoInterop.GetModuleHandleW(null);
        var windowClass = new Win32VideoInterop.WndClassExW
        {
            cbSize = (uint)Marshal.SizeOf<Win32VideoInterop.WndClassExW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = _moduleHandle,
            lpszClassName = _windowClassName,
        };

        _classAtom = Win32VideoInterop.RegisterClassExW(ref windowClass);
        if (_classAtom == 0)
        {
            failure = $"RegisterClassEx(\"{_windowClassName}\") returned 0 "
                + $"(last-error {Marshal.GetLastWin32Error()}); no video surface could be created";
            return 0;
        }

        // Created at the requested rectangle and NOT visible: the surface reaches the screen only at
        // the SetWindowPos in Present, by which time the OS already holds its alpha. The window class
        // registers NO background brush on purpose — every pixel on this surface is one this presence
        // put there, which is what makes the bar read-back mean something (the M-t mutation).
        //
        // WS_EX_TRANSPARENT IS DELIBERATELY ABSENT: THIS SURFACE CATCHES ITS OWN CLICKS. That is
        // upstream's behaviour and not an omission. All three of upstream's video render paths
        // swallow the press at the window level — the LibVLC path's PreviewMouseDown with
        // e.Handled = true, whose own comment is "it swallows every one of them"
        // (Services/Video/VideoService.cs:2894-2907), the MediaElement fallback (:4162-4166) and the
        // mirror (:4226-4230) — over a hit-testable transparent rectangle placed to catch "all clicks
        // before they reach the video surface" (:2862-2874), on an opaque black topmost window
        // (:2619-2636). Upstream then goes out of its way to KEEP that message while refusing the
        // z-order raise it would otherwise cause: PreventClickRaise answers WM_MOUSEACTIVATE with
        // MA_NOACTIVATE "while KEEPING the mouse message" (:7205-7255), and DisableChildWindowInput
        // EnableWindow(FALSE)s LibVLC's native children so the press lands on the top-level window
        // rather than the renderer (:7264-7295).
        //
        // The strict lock is NOT the click policy and must not be read as one. It governs DISMISSAL:
        // it vetoes Closing (:4274) and eats the panic key and Alt+F4 (:4276-4306), while the
        // non-strict branch consumes ESC as "dismiss this video" (:4328-4345). Both branches assume
        // the window already owns the input; the dial only decides whether that ownership can be
        // escaped. A click-through video would therefore be LESS like upstream, not more.
        //
        // Two things are NOT copied, and both are narrower rather than stricter. WS_EX_NOACTIVATE is
        // set where upstream ACTIVATES its primary window (:2622, :2920), because Lock Card's whole
        // capability is the foreground and upstream took it only to serve an ESC this port has not
        // ported. And the rectangle is not upstream's fullscreen cover of every monitor — the
        // presenter places a bounded 0.55x0.42 of one display (Effects/VideoSurfacePresenter.cs:230,
        // D123) — so what is caught is the clicks inside that rectangle, never the desktop.
        //
        // THE COUPLING A LATER EDIT WOULD TRIP OVER: Present and Show read occlusion out of
        // VideoSurfaceObservation.HitTestRoutesHere, which is WindowFromPoint. Setting
        // WS_EX_TRANSPARENT here does not merely change the input policy, it makes that answer
        // permanently false and the capability refuses every placement with video-surface-occluded —
        // measured, by adding the bit to this call. Measured in the same sweep and worth writing
        // down: WS_EX_TRANSPARENT on a window that is NOT layered does not pass clicks through at
        // all, so it is the LAYERED bit above that gives this one its force.
        //
        // And the flag is not the fact
        // either way: the overlay measured a run where every style write SUCCEEDED and the ex-style
        // read back wrong anyway (Overlay/Win32OverlayPresence.cs:504-511), so the routing outcome is
        // measured against the window manager with a real injected click in VideoInputRoutingTests.
        _window = Win32VideoInterop.CreateWindowExW(
            Win32VideoInterop.WsExLayered | Win32VideoInterop.WsExToolwindow | Win32VideoInterop.WsExNoactivate,
            _windowClassName,
            "CCP video surface",
            Win32VideoInterop.WsPopup,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            0, 0, _moduleHandle, 0);

        if (_window == 0)
        {
            failure = $"CreateWindowEx for class \"{_windowClassName}\" at {bounds} returned NULL "
                + $"(last-error {Marshal.GetLastWin32Error()})";
            Win32VideoInterop.UnregisterClassW(_windowClassName, _moduleHandle);
            _classAtom = 0;
            return 0;
        }

        return _window;
    }

    /// <summary>Paint the whole client area with the bar colour. This is what
    /// <see cref="VideoSurfaceObservation.LetterboxHeld"/> reads back, and it runs at placement so a
    /// surface with no picture yet is a black rectangle rather than whatever the OS left behind.</summary>
    private void FillLetterbox()
    {
        var dc = Win32VideoInterop.GetDC(_window);
        if (dc == 0)
        {
            return;
        }

        var brush = Win32VideoInterop.CreateSolidBrush(_letterbox);
        try
        {
            var rect = new Win32VideoInterop.Rect { Left = 0, Top = 0, Right = _bounds.Width, Bottom = _bounds.Height };
            Win32VideoInterop.FillRect(dc, ref rect, brush);
        }
        finally
        {
            Win32VideoInterop.DeleteObject(brush);
            Win32VideoInterop.ReleaseDC(_window, dc);
        }
    }

    private void Blit(byte[] pixels)
    {
        var dc = Win32VideoInterop.GetDC(_window);
        if (dc == 0)
        {
            return;
        }

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var info = new Win32VideoInterop.BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<Win32VideoInterop.BitmapInfoHeader>(),
                biWidth = _bounds.Width,
                biHeight = -_bounds.Height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };

            // 1:1 — the scale already happened in managed code, so GDI never blends and the expected
            // read-back value is exactly the composed buffer.
            Win32VideoInterop.StretchDIBits(
                dc, 0, 0, _bounds.Width, _bounds.Height, 0, 0, _bounds.Width, _bounds.Height,
                handle.AddrOfPinnedObject(), ref info, Win32VideoInterop.DibRgbColors, Win32VideoInterop.Srccopy);
        }
        finally
        {
            handle.Free();
            Win32VideoInterop.ReleaseDC(_window, dc);
        }
    }

    /// <summary>
    /// Ask the OS about the surface, re-asserting the topmost band and asking again while the window
    /// manager routes the surface's own centre elsewhere. See <see cref="MaxBandAttempts"/> for why
    /// this is not a retry-until-green.
    /// </summary>
    private VideoSurfaceObservation ReadHoldingTheBand()
    {
        var observation = Read();
        for (var attempt = 1; attempt < MaxBandAttempts && !observation.HitTestRoutesHere; attempt++)
        {
            Win32VideoInterop.SetWindowPos(
                _window, Win32VideoInterop.HwndTopmost, 0, 0, 0, 0,
                Win32VideoInterop.SwpNomove | Win32VideoInterop.SwpNosize | Win32VideoInterop.SwpNoactivate);
            observation = Read();
        }

        return observation;
    }

    /// <summary>One complete round trip to the operating system about this surface.</summary>
    private VideoSurfaceObservation Read()
    {
        var window = _window;
        var exists = Win32VideoInterop.IsWindow(window);
        var visible = exists && Win32VideoInterop.IsWindowVisible(window);

        var held = default(VideoBounds);
        if (exists && Win32VideoInterop.GetWindowRect(window, out var rect))
        {
            held = new VideoBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        var zOrder = WalkZOrder(window);
        var centre = held.Width > 0 && held.Height > 0 ? held.Centre : _bounds.Centre;
        var winner = Win32VideoInterop.WindowFromPoint(new Win32VideoInterop.Point { X = centre.X, Y = centre.Y });

        var letterboxHeld = false;
        var sampled = 0;
        var matched = 0;
        var fold = 2166136261u;

        var dc = visible ? Win32VideoInterop.GetDC(window) : 0;
        if (dc != 0)
        {
            try
            {
                // The control point: a pixel inside the bar the painter fills and never draws
                // picture into. Its EQUALITY with the requested colour is what proves the fill
                // happened — without it, a surface nobody ever painted reads back "not the
                // background" just as reliably as a painted one (the M-t mutation).
                var control = Win32VideoInterop.GetPixel(dc, 1, 1);
                letterboxHeld = control == _letterbox;
                unchecked
                {
                    fold = (fold ^ control) * 16777619u;
                }

                if (_composed is { } composed && _box.HasArea)
                {
                    foreach (var (x, y) in VideoLetterbox.SamplePoints(_box))
                    {
                        var read = Win32VideoInterop.GetPixel(dc, x, y);
                        var offset = ((y * _bounds.Width) + x) * VideoFrame.BytesPerPixel;
                        var expected = (uint)((composed[offset] << 16) | (composed[offset + 1] << 8) | composed[offset + 2]);
                        sampled++;
                        if (read == expected)
                        {
                            matched++;
                        }

                        unchecked
                        {
                            fold = (fold ^ read) * 16777619u;
                        }
                    }
                }
            }
            finally
            {
                Win32VideoInterop.ReleaseDC(window, dc);
            }
        }

        return new VideoSurfaceObservation(
            Asked: true,
            Window: window,
            WindowExists: exists,
            WindowVisible: visible,
            HeldBounds: held,
            AboveEveryOrdinaryWindow: zOrder.AboveEveryOrdinaryWindow,
            HitTestWinner: winner,
            LetterboxHeld: letterboxHeld,
            PictureSampled: sampled,
            PictureMatched: matched,
            SurfaceSample: fold,
            PreviousSurfaceSample: _previousSurfaceSample,
            FrameDiffersFromPrevious: _frameDiffersFromPrevious,
            FramesPresented: FramesHeld);
    }

    /// <summary>
    /// The OS's own top-level z-order, walked. The claim is "before the first visible ORDINARY
    /// window", never "before every window": another topmost window — a card, an overlay, another
    /// application's — legitimately contests the band, and asserting otherwise would make this
    /// capability fail because a sibling capability worked.
    /// </summary>
    private static ZOrderPosition WalkZOrder(nint window)
    {
        var index = -1;
        var firstOrdinary = -1;
        var visibleCount = 0;

        var current = Win32VideoInterop.GetTopWindow(0);
        var guard = 0;
        while (current != 0 && guard++ < 10_000)
        {
            if (Win32VideoInterop.IsWindowVisible(current))
            {
                if (current == window)
                {
                    index = visibleCount;
                }
                else if (firstOrdinary < 0
                    && ((uint)Win32VideoInterop.GetWindowLongPtrW(current, Win32VideoInterop.GwlExstyle)
                        & Win32VideoInterop.WsExTopmost) == 0)
                {
                    firstOrdinary = visibleCount;
                }

                visibleCount++;
            }

            current = Win32VideoInterop.GetWindow(current, Win32VideoInterop.GwHwndnext);
        }

        return new ZOrderPosition(index, firstOrdinary);
    }

    private readonly record struct ZOrderPosition(int Index, int FirstOrdinaryIndex)
    {
        internal bool AboveEveryOrdinaryWindow =>
            Index >= 0 && (FirstOrdinaryIndex < 0 || Index < FirstOrdinaryIndex);
    }
}
