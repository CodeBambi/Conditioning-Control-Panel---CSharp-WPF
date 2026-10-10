using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// For You ghost mode on Windows (WPF 7.1.5 Services/Fyp/FypGhostOverlay.cs + the park half of
    /// FypHostService.EnterGhost): the OnTopReplica technique. The real WebView2 window is parked,
    /// SHOWN, off the virtual desktop; an empty GDI-surfaced popup on its home monitor carries a
    /// live DWM thumbnail of it.
    ///
    /// <para>The measured rules WPF wrote down still hold and are kept verbatim: nothing ever calls
    /// SetLayeredWindowAttributes on the FEED window (WebView2 goes black); the mirror's surface is
    /// RGB(1,1,1) keyed out with LWA_COLORKEY only (LWA_ALPHA would multiply the thumbnail too), and
    /// the user's translucency rides DWM_TNP_OPACITY alone; the mirror must be a GDI surface (a
    /// composited surface never matches the key), so it is a raw Win32 window, not an Avalonia one.</para>
    ///
    /// <para>The mirror is WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
    /// from creation: it never takes focus and never eats a click. If the colour key, the thumbnail
    /// or the click-through style does not hold, the session refuses and leaves NOTHING on screen.
    /// The two round buttons (gear, speaker) are their own tiny windows without WS_EX_TRANSPARENT.</para>
    ///
    /// <para>All of it lives on the UI thread (the windows are pumped by Avalonia's own message
    /// loop). user32 / gdi32 / dwmapi only; no package.</para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsFypGhost : IFypGhostPlatform
    {
        public IFypGhostSession? Enter(IntPtr sourceHwnd, double opacity, bool muted, Action onGear, Action onMute, out string? reason)
        {
            reason = null;
            if (sourceHwnd == IntPtr.Zero || !IsWindow(sourceHwnd)) { reason = "no-window"; return null; }
            var session = new Session(sourceHwnd, opacity, muted, onGear, onMute);
            try { reason = session.Start(); }
            catch (Exception ex)
            {
                Log.Debug("[FypGhost] enter threw: {E}", ex.Message);
                reason = "mirror-threw";
            }
            Log.Information("[FypGhost] enter: {Diag}", session.Diag);
            if (reason == null) return session;
            session.Dispose();   // never leave the real window parked with no mirror
            return null;
        }

        private sealed class Session : IFypGhostSession
        {
            private readonly IntPtr _source;
            private readonly Action _onGear, _onMute;
            private double _opacity;
            private bool _muted, _closed, _parked;
            private RECT _restore, _monitor;
            private IntPtr _mirror, _thumb;
            private GhostButton? _gear, _mute;

            public string Diag { get; private set; } = "(not measured)";

            public Session(IntPtr source, double opacity, bool muted, Action onGear, Action onMute)
            {
                _source = source;
                _opacity = FypGhostRules.ClampOpacity(opacity);
                _muted = muted;
                _onGear = onGear;
                _onMute = onMute;
            }

            /// <summary>Null = live. Anything else is the reason; the caller disposes.</summary>
            public string? Start()
            {
                EnsureClasses();
                int compHr = DwmIsCompositionEnabled(out bool composed);

                // The home monitor is read BEFORE the park moves the window off every monitor.
                var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfoW(MonitorFromWindow(_source, MONITOR_DEFAULTTONEAREST), ref mi)) return "no-monitor";
                _monitor = mi.rcMonitor;
                int monW = _monitor.Right - _monitor.Left, monH = _monitor.Bottom - _monitor.Top;
                if (monW <= 0 || monH <= 0) return "no-monitor";

                // Park past the right edge of every monitor, resized so the client area is the home
                // monitor (nothing to letterbox). One native call, physical px end to end. Still SHOWN:
                // DWM keeps composing it, so the thumbnail stays live.
                if (!GetWindowRect(_source, out _restore) || !GetClientRect(_source, out var cr)) return "no-window";
                int chromeW = (_restore.Right - _restore.Left) - cr.Right;
                int chromeH = (_restore.Bottom - _restore.Top) - cr.Bottom;
                int parkX = GetSystemMetrics(SM_XVIRTUALSCREEN) + GetSystemMetrics(SM_CXVIRTUALSCREEN) + 200;
                _parked = SetWindowPos(_source, IntPtr.Zero, parkX, _monitor.Top,
                    monW + Math.Max(0, chromeW), monH + Math.Max(0, chromeH), SWP_NOZORDER | SWP_NOACTIVATE);
                if (!_parked) return "park-failed";

                _mirror = CreateWindowExW(
                    WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
                    MirrorClass, WindowTitle, WS_POPUP, _monitor.Left, _monitor.Top, monW, monH,
                    IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
                if (_mirror == IntPtr.Zero) return "mirror-create-failed";

                // Keyed BEFORE it is shown (WPF showed first): the sheet never gets a frame on screen.
                bool keyOk = ApplyColorKey(out int keyErr, out uint keyBack, out int keyFlags);
                ShowWindow(_mirror, SW_SHOWNOACTIVATE);
                int regHr = RegisterThumbnail(out int srcHr, out int srcW, out int srcH);
                Diag = $"composition={composed} (0x{compHr:X8}), colorkey={keyOk} err={keyErr} "
                     + $"readback=0x{keyBack:X6}/flags=0x{keyFlags:X2}, register=0x{regHr:X8}, "
                     + $"sourceSize={srcW}x{srcH} (0x{srcHr:X8}), dest={monW}x{monH}, opacity={_opacity:0.00}";
                var reason = FypGhostRules.Diagnose(compHr == 0 && composed, keyOk, regHr, srcW, srcH);
                if (reason != null) return reason;
                if (!ClickThroughHolds()) return "clickthrough-rejected";

                // Gear top-left, speaker top-right, sized off the monitor like the page's own chrome.
                int size = Math.Max(40, (int)Math.Round(monH * 0.037));
                int pad = size / 2;
                _gear = new GhostButton(GearGlyph, _monitor.Left + pad, _monitor.Top + pad, size, () => _onGear());
                _mute = new GhostButton(_muted ? MutedGlyph : SpeakerGlyph, _monitor.Right - pad - size, _monitor.Top + pad, size, () => _onMute());
                return null;
            }

            public void SetOpacity(double opacity)
            {
                _opacity = FypGhostRules.ClampOpacity(opacity);
                UpdateThumbnail();
            }

            public void SetMuted(bool muted)
            {
                _muted = muted;
                try { _mute?.SetGlyph(muted ? MutedGlyph : SpeakerGlyph); }
                catch (Exception ex) { Log.Debug("[FypGhost] mute glyph: {E}", ex.Message); }
            }

            public bool Healthy()
            {
                try
                {
                    if (_closed) return false;
                    if (_mirror == IntPtr.Zero || !IsWindow(_mirror) || !IsWindow(_source)) return false;
                    // A parked source that got hidden or minimised stops being composed: the mirror
                    // would freeze on its last frame.
                    if (!IsWindowVisible(_source) || IsIconic(_source)) return false;
                    if (!ClickThroughHolds()) return false;
                    if (GetLayeredWindowAttributes(_mirror, out uint key, out _, out int flags)
                        && ((flags & LWA_COLORKEY) == 0 || key != COLOR_KEY)) return false;
                    // Topmost bands are per window and fall on a shell restart or a fullscreen app's
                    // return; nothing else puts them back. Mirror first, so the buttons land above it.
                    SetWindowPos(_mirror, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    _gear?.Raise();
                    _mute?.Raise();
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Debug("[FypGhost] health probe failed: {E}", ex.Message);
                    return true;   // a failed diagnostic is not a verdict
                }
            }

            public void Dispose()
            {
                if (_closed) return;
                _closed = true;
                var t = _thumb;
                _thumb = IntPtr.Zero;
                try { if (t != IntPtr.Zero) DwmUnregisterThumbnail(t); }
                catch (Exception ex) { Log.Debug("[FypGhost] unregister: {E}", ex.Message); }
                try { _gear?.Close(); _mute?.Close(); }
                catch (Exception ex) { Log.Debug("[FypGhost] buttons close: {E}", ex.Message); }
                _gear = null;
                _mute = null;
                try { if (_mirror != IntPtr.Zero) DestroyWindow(_mirror); }
                catch (Exception ex) { Log.Debug("[FypGhost] mirror close: {E}", ex.Message); }
                _mirror = IntPtr.Zero;
                try
                {
                    // The real geometry back, natively, whatever state the window is in by now.
                    if (_parked && IsWindow(_source))
                        SetWindowPos(_source, IntPtr.Zero, _restore.Left, _restore.Top,
                            _restore.Right - _restore.Left, _restore.Bottom - _restore.Top, SWP_NOZORDER | SWP_NOACTIVATE);
                }
                catch (Exception ex) { Log.Debug("[FypGhost] restore: {E}", ex.Message); }
                _parked = false;
            }

            private bool ClickThroughHolds()
            {
                long ex = GetWindowLongPtrW(_mirror, GWL_EXSTYLE).ToInt64();
                const long need = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                return (ex & need) == need;
            }

            /// <summary>Read back, never fire-and-forget: a key that did not take is the difference
            /// between a see-through ghost and an opaque pane over the whole monitor.</summary>
            private bool ApplyColorKey(out int win32Error, out uint readback, out int readbackFlags)
            {
                win32Error = 0; readback = 0; readbackFlags = 0;
                if (!SetLayeredWindowAttributes(_mirror, COLOR_KEY, 0, LWA_COLORKEY)) { win32Error = Marshal.GetLastWin32Error(); return false; }
                if (!GetLayeredWindowAttributes(_mirror, out readback, out _, out readbackFlags)) { win32Error = Marshal.GetLastWin32Error(); return false; }
                return (readbackFlags & LWA_COLORKEY) != 0 && readback == COLOR_KEY;
            }

            private int RegisterThumbnail(out int sourceSizeHr, out int sourceWidth, out int sourceHeight)
            {
                sourceSizeHr = E_FAIL; sourceWidth = 0; sourceHeight = 0;
                int hr = DwmRegisterThumbnail(_mirror, _source, out var thumb);
                if (hr == 0 && thumb == IntPtr.Zero) hr = E_FAIL;
                if (hr != 0) return hr;
                _thumb = thumb;
                sourceSizeHr = DwmQueryThumbnailSourceSize(_thumb, out var size);
                if (sourceSizeHr == 0) { sourceWidth = size.cx; sourceHeight = size.cy; }
                UpdateThumbnail();
                return 0;
            }

            private void UpdateThumbnail()
            {
                try
                {
                    if (_closed || _thumb == IntPtr.Zero) return;
                    int destW = Math.Max(1, _monitor.Right - _monitor.Left);
                    int destH = Math.Max(1, _monitor.Bottom - _monitor.Top);
                    var props = new DWM_THUMBNAIL_PROPERTIES
                    {
                        dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY | DWM_TNP_OPACITY,
                        rcDestination = new RECT { Left = 0, Top = 0, Right = destW, Bottom = destH },
                        opacity = FypGhostRules.OpacityByte(_opacity),
                        fVisible = true,
                        fSourceClientAreaOnly = true,
                    };
                    if (GetClientRect(_source, out var src) && src.Right > 0 && src.Bottom > 0)
                    {
                        var c = FypGhostRules.CoverSource(src.Right, src.Bottom, destW, destH);
                        props.dwFlags |= DWM_TNP_RECTSOURCE;
                        props.rcSource = new RECT { Left = c.Left, Top = c.Top, Right = c.Right, Bottom = c.Bottom };
                    }
                    DwmUpdateThumbnailProperties(_thumb, ref props);
                }
                catch (Exception ex) { Log.Debug("[FypGhost] thumbnail update: {E}", ex.Message); }
            }
        }

        /// <summary>One round, clickable island over the click-through mirror. NOACTIVATE: a click
        /// never steals focus. Faint until the pointer reaches it (plain alpha is safe here: no
        /// thumbnail rides this window).</summary>
        private sealed class GhostButton
        {
            private const byte IdleAlpha = 36;     // 0.14
            private const byte HoverAlpha = 235;   // 0.92
            private readonly Action _onClick;
            private readonly int _size;
            private IntPtr _hwnd;
            private string _glyph;
            private bool _hover;

            public GhostButton(string glyph, int x, int y, int size, Action onClick)
            {
                _glyph = glyph;
                _size = size;
                _onClick = onClick;
                _hwnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
                    ButtonClass, WindowTitle, WS_POPUP, x, y, size, size, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
                if (_hwnd == IntPtr.Zero) return;
                lock (Buttons) Buttons[_hwnd] = this;
                SetWindowRgn(_hwnd, CreateEllipticRgn(0, 0, size + 1, size + 1), false);   // the system owns the region now
                SetLayeredWindowAttributes(_hwnd, 0, IdleAlpha, LWA_ALPHA);
                ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            }

            public void SetGlyph(string glyph)
            {
                _glyph = glyph;
                if (_hwnd != IntPtr.Zero) InvalidateRect(_hwnd, IntPtr.Zero, true);
            }

            public void Raise()
            {
                if (_hwnd != IntPtr.Zero) SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }

            public void Close()
            {
                var h = _hwnd;
                _hwnd = IntPtr.Zero;
                if (h == IntPtr.Zero) return;
                lock (Buttons) Buttons.Remove(h);
                DestroyWindow(h);
            }

            public IntPtr? Handle(IntPtr hwnd, uint msg, IntPtr wParam)
            {
                switch (msg)
                {
                    case WM_MOUSEACTIVATE:
                        return MA_NOACTIVATE;
                    case WM_SETCURSOR:
                        SetCursor(LoadCursorW(IntPtr.Zero, IDC_HAND));
                        return 1;
                    case WM_MOUSEMOVE:
                        if (!_hover)
                        {
                            _hover = true;
                            var tme = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE, hwndTrack = hwnd };
                            TrackMouseEvent(ref tme);
                            SetLayeredWindowAttributes(hwnd, 0, HoverAlpha, LWA_ALPHA);
                            InvalidateRect(hwnd, IntPtr.Zero, true);
                        }
                        return 0;
                    case WM_MOUSELEAVE:
                        _hover = false;
                        SetLayeredWindowAttributes(hwnd, 0, IdleAlpha, LWA_ALPHA);
                        InvalidateRect(hwnd, IntPtr.Zero, true);
                        return 0;
                    case WM_LBUTTONUP:
                        // Off the window procedure: the handler may destroy this very window.
                        Dispatcher.UIThread.Post(() =>
                        {
                            try { _onClick(); }
                            catch (Exception ex) { Log.Debug("[FypGhost] button click: {E}", ex.Message); }
                        });
                        return 0;
                    case WM_PAINT:
                        Paint(hwnd);
                        return 0;
                    default:
                        return null;
                }
            }

            private void Paint(IntPtr hwnd)
            {
                var hdc = BeginPaint(hwnd, out var ps);
                if (hdc == IntPtr.Zero) return;
                IntPtr fill = IntPtr.Zero, rim = IntPtr.Zero, font = IntPtr.Zero;
                try
                {
                    fill = CreateSolidBrush(_hover ? ColorPink : ColorDisc);
                    rim = CreatePen(PS_SOLID, 2, ColorPink);
                    font = CreateFontW(-(int)Math.Round(_size * 0.42), 0, 0, 0, 400, 0, 0, 0, 1 /* DEFAULT_CHARSET */, 0, 0, 4 /* ANTIALIASED */, 0, "Segoe MDL2 Assets");
                    var oldBrush = SelectObject(hdc, fill);
                    var oldPen = SelectObject(hdc, rim);
                    var oldFont = SelectObject(hdc, font);
                    Ellipse(hdc, 1, 1, _size - 1, _size - 1);
                    SetBkMode(hdc, 1 /* TRANSPARENT */);
                    SetTextColor(hdc, 0x00FFFFFF);
                    var box = new RECT { Left = 0, Top = 0, Right = _size, Bottom = _size };
                    DrawTextW(hdc, _glyph, _glyph.Length, ref box, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
                    SelectObject(hdc, oldFont);
                    SelectObject(hdc, oldPen);
                    SelectObject(hdc, oldBrush);
                }
                finally
                {
                    if (fill != IntPtr.Zero) DeleteObject(fill);
                    if (rim != IntPtr.Zero) DeleteObject(rim);
                    if (font != IntPtr.Zero) DeleteObject(font);
                    EndPaint(hwnd, ref ps);
                }
            }
        }

        // ---- window classes ---------------------------------------------------------------------

        private const string MirrorClass = "CcpFypGhostMirror";
        private const string ButtonClass = "CcpFypGhostButton";
        private const string WindowTitle = "For You";
        private const string GearGlyph = "";      // Segoe MDL2 Assets: Settings
        private const string SpeakerGlyph = "";   // Volume
        private const string MutedGlyph = "";     // Mute

        private static readonly Dictionary<IntPtr, GhostButton> Buttons = new();
        private static readonly WndProcDelegate MirrorProcKeep = MirrorProc;   // strong refs: the class holds a raw pointer
        private static readonly WndProcDelegate ButtonProcKeep = ButtonProc;
        private static bool _classesReady;

        private static void EnsureClasses()
        {
            if (_classesReady) return;
            var instance = GetModuleHandleW(null);
            var mirror = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(MirrorProcKeep),
                hInstance = instance,
                hbrBackground = CreateSolidBrush(COLOR_KEY),   // every surface pixel is exactly RGB(1,1,1)
                lpszClassName = MirrorClass,
            };
            var button = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(ButtonProcKeep),
                hInstance = instance,
                hbrBackground = CreateSolidBrush(ColorBack),
                lpszClassName = ButtonClass,
            };
            if (RegisterClassExW(ref mirror) == 0 && Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
                throw new InvalidOperationException("mirror class refused");
            if (RegisterClassExW(ref button) == 0 && Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
                throw new InvalidOperationException("button class refused");
            _classesReady = true;
        }

        private static IntPtr MirrorProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            // Belt and braces under WS_EX_TRANSPARENT: never activated, never a hit target.
            if (msg == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
            if (msg == WM_NCHITTEST) return HTTRANSPARENT;
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        private static IntPtr ButtonProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                GhostButton? b;
                lock (Buttons) Buttons.TryGetValue(hwnd, out b);
                if (b?.Handle(hwnd, msg, wParam) is { } handled) return handled;
            }
            catch (Exception ex) { Log.Debug("[FypGhost] button proc: {E}", ex.Message); }   // never out of a window procedure
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // ---- native -------------------------------------------------------------------------------

        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint WS_POPUP = 0x80000000;
        private const int GWL_EXSTYLE = -20;
        private const int LWA_COLORKEY = 0x1;
        private const int LWA_ALPHA = 0x2;
        private const uint COLOR_KEY = 0x00010101;    // COLORREF RGB(1,1,1), never pure black: feed blacks stay opaque
        private const uint ColorBack = 0x001F1212;    // RGB(18,18,31)
        private const uint ColorDisc = 0x00361F1F;    // RGB(31,31,54)
        private const uint ColorPink = 0x00B469FF;    // RGB(255,105,180)
        private const int E_FAIL = unchecked((int)0x80004005);
        private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int SM_XVIRTUALSCREEN = 76, SM_CXVIRTUALSCREEN = 78;
        private const uint WM_PAINT = 0x000F, WM_SETCURSOR = 0x0020, WM_MOUSEACTIVATE = 0x0021, WM_NCHITTEST = 0x0084,
            WM_MOUSEMOVE = 0x0200, WM_LBUTTONUP = 0x0202, WM_MOUSELEAVE = 0x02A3;
        private const int MA_NOACTIVATE = 3;
        private const int HTTRANSPARENT = -1;
        private const uint TME_LEAVE = 0x2;
        private const int IDC_HAND = 32649;
        private const int PS_SOLID = 0;
        private const uint DT_CENTER = 0x1, DT_VCENTER = 0x4, DT_SINGLELINE = 0x20, DT_NOPREFIX = 0x800;
        private const int DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_RECTSOURCE = 0x2, DWM_TNP_OPACITY = 0x4,
            DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

        private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public uint cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TRACKMOUSEEVENT { public uint cbSize; public uint dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public int fErase;
            public RECT rcPaint;
            public int fRestore;
            public int fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEXW
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
        }

        [DllImport("dwmapi.dll", PreserveSig = true)] private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll", PreserveSig = true)] private static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll", PreserveSig = true)] private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);
        [DllImport("dwmapi.dll", PreserveSig = true)] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);
        [DllImport("dwmapi.dll", PreserveSig = true)] private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);

        [DllImport("user32.dll", SetLastError = true)] private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, uint style,
            int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, int flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out int flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
        [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
        [DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr instance, int id);
        [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
        [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(IntPtr hdc, string text, int count, ref RECT rect, uint format);

        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int style, int width, uint color);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int l, int t, int r, int b);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
            uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool Ellipse(IntPtr hdc, int l, int t, int r, int b);
        [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);
    }
}
