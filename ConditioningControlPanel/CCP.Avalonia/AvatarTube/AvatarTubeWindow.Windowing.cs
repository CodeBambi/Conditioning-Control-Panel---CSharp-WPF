using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    // NOTE (2026-07-11 core rebuild): all sizing + anchoring + parent-follow + the shared
    // z-order funnel live in TubeGeometryController (the single writer of Window.Position
    // and of the ContentViewbox/window size). This partial keeps only NON-geometry window
    // behavior: fullscreen auto-hide, the float/liveness animation, attach/detach
    // orchestration, the platform pair-raise implementation, and the chaos-run
    // park/reattach hooks.
    //
    // Platform note: CCP.Avalonia targets plain net8.0, so the pre-rebuild "#if WINDOWS"
    // blocks here NEVER compiled — the pair raise, topmost reassert, foreground gate and
    // fullscreen detection were silently inert in the Avalonia head. The rebuild uses the
    // repo's established shared-assembly pattern instead (ChaosWin32Helper.cs): P/Invoke
    // declarations always compiled, every call runtime-guarded by OperatingSystem.IsWindows().
    public partial class AvatarTubeWindow
    {
        private IntPtr _tubeHandle;
        private IntPtr _parentHandle;
        private IScreenProvider? _screenProvider;
        private string _diagLastFullscreenWindow = "(none)";

        // ===== Detached per-region hit-testing (obs #6 retest-2 fix 3b) =====
        // WPF layered windows hit-test per-pixel for free (alpha-0 pixels are click-
        // through at the OS level), so the WPF detached tube never blocked the main
        // window. Avalonia windows hit-test their whole rect (overlay-clickthrough skill
        // rule 5), so the detached tube's transparent dead-zones swallowed clicks. The
        // WM_NCHITTEST hook below restores per-pixel behavior: everything except the tube
        // art returns HTTRANSPARENT while detached. Registered via Avalonia's own
        // Win32Properties hook list - NOT SetWindowSubclass, which is banned on v12 HWNDs
        // (native 0xC0000005 race, CompositorWindow.axaml.cs:115-122).
        private Win32Properties.CustomWndProcHookCallback? _hitTestHook;
        private byte[]? _tubeArtPixels;      // BGRA8888 copy of the current tube art
        private PixelSize _tubeArtPixelSize;
        private int _tubeArtRowBytes;
        private const byte TubeArtAlphaThreshold = 16; // ~6% opacity counts as painted

        private void RegisterHitTestHook()
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                _hitTestHook = TubeWndProcHook;
                Win32Properties.AddWndProcHookCallback(this, _hitTestHook);
            }
            catch (Exception ex)
            {
                _hitTestHook = null;
                _logger?.LogDebug("AvatarTube: hit-test hook registration failed ({Error}) - detached dead-zones stay clickable", ex.Message);
            }
        }

        private void RemoveHitTestHook()
        {
            if (_hitTestHook == null) return;
            try { Win32Properties.RemoveWndProcHookCallback(this, _hitTestHook); }
            catch { /* window already torn down */ }
            _hitTestHook = null;
        }

        private IntPtr TubeWndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Attached mode keeps default hit-testing: the attached tube deliberately
            // overlaps the main window's left edge and its z-order pair-raise depends on
            // clicks landing on it. Only the detached tube goes art-only (the spec scope).
            if (msg != WM_NCHITTEST || _isAttached) return IntPtr.Zero;
            try
            {
                long lp = lParam.ToInt64();
                int screenX = unchecked((short)(lp & 0xFFFF));
                int screenY = unchecked((short)((lp >> 16) & 0xFFFF));
                var client = this.PointToClient(new PixelPoint(screenX, screenY));
                if (!IsPointOnTubeArt(client))
                {
                    // HTTRANSPARENT forwards the hit to the next window in this thread
                    // (the main window) and lets clicks fall through the dead-zones.
                    handled = true;
                    return new IntPtr(HTTRANSPARENT);
                }
            }
            catch
            {
                // Fail open: default hit-testing (clicks land on the tube). Never let an
                // exception escape into the WndProc.
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// True when the client-space point lands on interactive tube content: any real
        /// control (avatar, speech bubble, title box, chat input, resize grips), or a
        /// PAINTED pixel of the vessel art. The window's own Background=Transparent makes
        /// the whole rect framework-hit-testable, so a hit on the Window itself IS a
        /// dead-zone; the vessel image hit-tests its bounds rect, so it is refined by the
        /// cached per-pixel alpha (WPF layered-window parity).
        /// </summary>
        private bool IsPointOnTubeArt(global::Avalonia.Point client)
        {
            var hit = this.InputHitTest(client);
            if (hit is null || ReferenceEquals(hit, this)) return false;
            if (!ReferenceEquals(hit, ImgTubeFrame)) return true;

            var pixels = _tubeArtPixels;
            if (pixels == null || ImgTubeFrame == null) return true; // no cache: whole vessel rect stays clickable (conservative)
            var local = this.TranslatePoint(client, ImgTubeFrame);
            if (local is not { } imagePoint) return true;
            var bounds = ImgTubeFrame.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return true;

            // Image uses Stretch=Uniform and measures to the bitmap aspect, so its bounds
            // ARE the drawn bitmap rect; a linear map lands on the exact source pixel.
            int px = (int)(imagePoint.X / bounds.Width * _tubeArtPixelSize.Width);
            int py = (int)(imagePoint.Y / bounds.Height * _tubeArtPixelSize.Height);
            if (px < 0 || py < 0 || px >= _tubeArtPixelSize.Width || py >= _tubeArtPixelSize.Height)
                return false;
            int alphaIndex = py * _tubeArtRowBytes + px * 4 + 3;
            return alphaIndex < pixels.Length && pixels[alphaIndex] >= TubeArtAlphaThreshold;
        }

        /// <summary>
        /// Caches the tube art's BGRA pixels for the per-pixel hit-test above. Called by
        /// SetTubeStyle (the single ImgTubeFrame.Source writer), so theme switches and
        /// attach/detach art swaps refresh the cache automatically. Decodes with
        /// SkiaSharp (the repo-established pixel-access path, AvaloniaChaosCompat.cs) into
        /// a fully managed byte copy. On any failure the cache is cleared and the vessel
        /// falls back to bounds-rect hit-testing (conservative: captures clicks, never
        /// traps the desktop).
        /// </summary>
        private void CacheTubeArtPixels(string resourcePath)
        {
            _tubeArtPixels = null;
            _tubeArtPixelSize = default;
            _tubeArtRowBytes = 0;
            try
            {
                using var stream = OpenTubeArtStream(resourcePath);
                if (stream == null) return;
                using var decoded = SKBitmap.Decode(stream);
                if (decoded == null || decoded.Width <= 0 || decoded.Height <= 0) return;

                SKBitmap bgra = decoded;
                bool converted = false;
                if (decoded.ColorType != SKColorType.Bgra8888)
                {
                    var copy = decoded.Copy(SKColorType.Bgra8888);
                    if (copy == null) return;
                    bgra = copy;
                    converted = true;
                }
                try
                {
                    _tubeArtPixels = bgra.Bytes; // managed copy
                    _tubeArtRowBytes = bgra.RowBytes;
                    _tubeArtPixelSize = new PixelSize(bgra.Width, bgra.Height);
                }
                finally
                {
                    if (converted) bgra.Dispose();
                }
            }
            catch (Exception ex)
            {
                _tubeArtPixels = null;
                _tubeArtPixelSize = default;
                _tubeArtRowBytes = 0;
                _logger?.LogDebug("AvatarTube: tube art alpha cache failed ({Error}) - vessel bounds treated as art", ex.Message);
            }
        }

        /// <summary>
        /// Opens the raw stream behind a tube art resource, honoring the active mod's
        /// override exactly like AvaloniaModResourceResolver.ResolveBitmap does.
        /// </summary>
        private Stream? OpenTubeArtStream(string resourcePath)
        {
            var uri = _resourceResolver?.ResolveUri(resourcePath);
            if (string.IsNullOrEmpty(uri)) return null;

            if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var local = new Uri(uri).LocalPath;
                return File.Exists(local) ? File.OpenRead(local) : null;
            }

            var assetLoader = App.Services.GetService<IAssetLoader>();
            var avares = new Uri(uri, UriKind.Absolute);
            return assetLoader?.Exists(avares) == true ? assetLoader.Open(avares) : null;
        }

        // Win32 constants.
        private const uint WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const int GWL_EXSTYLE = -20;
        private const int GWL_STYLE = -16;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_CAPTION = 0x00C00000;

        // WPF AvatarTubeWindow.Windowing.cs:132-133 — HWND_TOP places a window at the top
        // of the NON-topmost band; HWND_TOPMOST is reserved for the detached tube only.
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        // GetForegroundWindow / GetWindowThreadProcessId are declared once in the
        // ChatInput partial (same class); the remaining imports live here.
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private void StartFullscreenDetection()
        {
            _screenProvider = App.Services.GetService<IScreenProvider>();
            _fullscreenCheckTimer?.Stop();
            _fullscreenCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _fullscreenCheckTimer.Tick += FullscreenCheckTimer_Tick;
            _fullscreenCheckTimer.Start();
        }

        private void FullscreenCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isAttached) return;
            bool fullscreen = IsOtherAppFullscreen();
            if (fullscreen && !_hiddenForFullscreen)
            {
                _hiddenForFullscreen = true;
                _wasAttachedBeforeFullscreen = _isAttached;
                Hide();
            }
            else if (!fullscreen && _hiddenForFullscreen)
            {
                // Mirror the WPF head (AvatarTube/AvatarTubeWindow.Windowing.cs): only clear the
                // flag and Show() once the parent is actually visible and NOT minimized. During
                // exclusive-fullscreen exit the parent is transiently minimized and reports
                // Position ~(-32000,-32000); showing then would anchor off a transient parking
                // spot. Keeping the flag set retries on the next tick until the parent settles.
                bool parentReady = _parentWindow?.IsVisible == true
                                   && _parentWindow.WindowState != WindowState.Minimized;
                if (parentReady && _wasAttachedBeforeFullscreen && _settings?.Current?.AvatarEnabled == true)
                {
                    _hiddenForFullscreen = false;
                    Show();
                    // Re-derive the screen-fit scale in case the monitor/DPI changed while we
                    // were hidden, then re-anchor. The controller coalesces the actual writes.
                    _geometry?.RecomputeScreenScale();
                    _geometry?.ApplySizing();
                    UpdatePosition();
                    BringAttachedPairToFront(true);
                }
            }
        }

        private bool IsOtherAppFullscreen()
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero) return false;

                // Ignore our own windows.
                if (foregroundWindow == _tubeHandle || foregroundWindow == _parentHandle)
                    return false;

                GetWindowThreadProcessId(foregroundWindow, out uint foregroundPid);
                if (foregroundPid == (uint)Process.GetCurrentProcess().Id)
                    return false;

                var className = new StringBuilder(256);
                GetClassName(foregroundWindow, className, className.Capacity);
                string windowClass = className.ToString();

                // WPF parity (Windowing.cs:259-274): browsers and common media apps use
                // "fake" fullscreen that covers the screen but is not exclusive.
                string[] safeClasses =
                {
                    "Chrome_WidgetWin",
                    "MozillaWindowClass",
                    "ApplicationFrameWindow",
                    "Windows.UI.Core",
                    "CabinetWClass",
                    "Shell_TrayWnd",
                    "Progman",
                    "WorkerW",
                    "XLMAIN",
                    "OpusApp",
                    "PPTFrameClass",
                    "VLC",
                    "mpv",
                    "MediaPlayerClassicW",
                };

                foreach (var safeClass in safeClasses)
                {
                    if (windowClass.StartsWith(safeClass, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                int style = GetWindowLong(foregroundWindow, GWL_STYLE);
                int exStyle = GetWindowLong(foregroundWindow, GWL_EXSTYLE);

                bool hasCaption = ((uint)style & WS_CAPTION) == WS_CAPTION;
                bool isPopup = ((uint)style & WS_POPUP) == WS_POPUP;
                bool isTopmost = (exStyle & WS_EX_TOPMOST) == WS_EX_TOPMOST;

                if (hasCaption)
                    return false;

                if (!isPopup || !isTopmost)
                    return false;

                if (!GetWindowRect(foregroundWindow, out RECT windowRect))
                    return false;

                if (_screenProvider == null)
                    return false;

                int centerX = (windowRect.Left + windowRect.Right) / 2;
                int centerY = (windowRect.Top + windowRect.Bottom) / 2;

                var screens = _screenProvider.GetAllScreens();
                var screen = screens.FirstOrDefault(s =>
                {
                    double left = s.Bounds.X * s.Scaling;
                    double top = s.Bounds.Y * s.Scaling;
                    double right = s.Bounds.Right * s.Scaling;
                    double bottom = s.Bounds.Bottom * s.Scaling;
                    return left <= centerX && centerX < right && top <= centerY && centerY < bottom;
                }) ?? _screenProvider.GetPrimaryScreen();

                if (screen == null)
                    return false;

                double screenLeft = screen.Bounds.X * screen.Scaling;
                double screenTop = screen.Bounds.Y * screen.Scaling;
                double screenRight = screen.Bounds.Right * screen.Scaling;
                double screenBottom = screen.Bounds.Bottom * screen.Scaling;

                const int tolerance = 5;
                bool coversFullScreen =
                    windowRect.Left <= screenLeft + tolerance &&
                    windowRect.Top <= screenTop + tolerance &&
                    windowRect.Right >= screenRight - tolerance &&
                    windowRect.Bottom >= screenBottom - tolerance;

                if (coversFullScreen)
                {
                    GetWindowThreadProcessId(foregroundWindow, out uint offPid);
                    string procName = "?";
                    try { procName = Process.GetProcessById((int)offPid).ProcessName; } catch { }
                    _diagLastFullscreenWindow = $"class={windowClass} proc={procName}(pid {offPid}) rect=[{windowRect.Left},{windowRect.Top},{windowRect.Right},{windowRect.Bottom}]";
                    _logger?.LogDebug("Exclusive fullscreen detected: {Win}", _diagLastFullscreenWindow);
                }

                return coversFullScreen;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Re-anchors the attached tube to the parent's left edge. Delegates to
        /// <see cref="TubeGeometryController"/> — the single writer of Window.Position.
        /// Idempotent, coalesced to at most one pass per frame, no-op unless attached.
        /// </summary>
        public void UpdatePosition()
        {
            _geometry?.UpdatePosition();
        }

        // ================= Float / liveness animation =================
        // WPF parity (Windowing.cs:501-518): a 16ms timer writes ONLY a +/-4px sine bob
        // to an INNER TranslateTransform — never Window position/size. This is the
        // owner-desired liveness cue ("gives the idea it's doing stuff", obs #7) and is
        // deliberately KEPT while the size-oscillation bug is dead: it is cosmetic and
        // orthogonal to window geometry.

        private void StartFloatingAnimation()
        {
            StopFloatingAnimation();
            _floatPhase = 0;
            _floatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _floatTimer.Tick += (_, _) =>
            {
                _floatPhase += 0.05; // WPF Windowing.cs:516 — oscillation speed
                double y = Math.Sin(_floatPhase) * FloatDistance;
                ApplyFloatOffset(ImgAvatar, y);
                ApplyFloatOffset(ImgAvatarAnimated, y);
            };
            _floatTimer.Start();
        }

        private static void ApplyFloatOffset(Image? img, double y)
        {
            if (img == null) return;
            if (img.RenderTransform is TranslateTransform tt)
                tt.Y = y;
            else
                img.RenderTransform = new TranslateTransform(0, y);
        }

        private void StopFloatingAnimation()
        {
            _floatTimer?.Stop();
            _floatTimer = null;
        }

        // ================= Attach / detach orchestration =================

        public void SetDetached(bool detached)
        {
            if (detached) Detach();
            else Attach();
        }

        public void Attach()
        {
            _isAttached = true;
            Topmost = false; // WPF contract: Topmost=false attached (board REBUILD SPEC)

            // WPF parity: re-attaching resets the detached user zoom to 1.0
            // (WPF Windowing.cs — Attach resets _currentScale; board spec).
            _currentScale = 1.0;
            _geometry?.SetUserScale(1.0);

            // Controller resumes scale-with-main-window sizing and re-anchors.
            _geometry?.SetAttached(true);

            // Switch back to original tube image and attached layout.
            SetTubeStyle(false);
            ApplyTubeLayoutOffsets();

            // Attach restores NoResize (owner contract 2026-07-11, obs #6 fix 3c):
            // corner grips disappear and the vessel goes back to non-interactive
            // (WPF Windowing.cs:1529-1531: ImgTubeFrame.IsHitTestVisible = false).
            CanResize = false;
            SetCornerGripsVisible(false);
            if (ImgTubeFrame != null)
            {
                ImgTubeFrame.IsHitTestVisible = false;
                ImgTubeFrame.Cursor = Cursor.Default;
            }
            ApplyDetachedCursors(false);

            Show();
            UpdatePosition();
            BringAttachedPairToFront(true);
        }

        public void Detach()
        {
            _isAttached = false;
            Topmost = true; // WPF contract: Topmost=true detached

            // Controller stops parent-ratio sizing; detached free user zoom applies
            // (owner contract: independent of the main window, no reverse coupling).
            _geometry?.SetAttached(false);

            // Switch to alternative tube image and detached layout.
            SetTubeStyle(true);
            ApplyTubeLayoutOffsets();

            // Detached = corner-drag resizable (NEW owner contract 2026-07-11, obs #6
            // fix 3c) + the visible vessel becomes a drag handle
            // (WPF Windowing.cs:1477-1485: SizeAll cursors + ImgTubeFrame hit-test ON).
            CanResize = true;
            SetCornerGripsVisible(true);
            if (ImgTubeFrame != null)
            {
                ImgTubeFrame.IsHitTestVisible = true;
                ImgTubeFrame.Cursor = new Cursor(StandardCursorType.SizeAll);
            }
            ApplyDetachedCursors(true);

            // Speech bubble stays at same position in both modes (right side of tube, clearly visible).
            if (SpeechBubble?.IsVisible == true && !string.IsNullOrEmpty(TxtSpeech?.Text))
            {
                AdjustBubbleSize(TxtSpeech.Text);
            }

            Show();
            ReassertTopmost();
            ReassertCirceEmoteVisuals();
        }

        private void SetCornerGripsVisible(bool visible)
        {
            if (GripTopLeft != null) GripTopLeft.IsVisible = visible;
            if (GripTopRight != null) GripTopRight.IsVisible = visible;
            if (GripBottomLeft != null) GripBottomLeft.IsVisible = visible;
            if (GripBottomRight != null) GripBottomRight.IsVisible = visible;
        }

        /// <summary>
        /// Move cursor over the draggable visuals while detached, defaults while attached
        /// (WPF Windowing.cs:1477-1480 / 1524-1527). AvatarBorder keeps its Hand cursor
        /// attached (it is the click/menu target, per the AXAML).
        /// </summary>
        private void ApplyDetachedCursors(bool detached)
        {
            var move = detached ? new Cursor(StandardCursorType.SizeAll) : null;
            if (AvatarBorder != null) AvatarBorder.Cursor = move ?? new Cursor(StandardCursorType.Hand);
            if (SpeechBubble != null) SpeechBubble.Cursor = move ?? Cursor.Default;
            if (TitleBox != null) TitleBox.Cursor = move ?? Cursor.Default;
        }

        /// <summary>
        /// Detached-only topmost reassertion (WPF Windowing.cs:1306-1308 uses
        /// SetWindowPos(HWND_TOPMOST) because the framework Topmost flag alone can lose
        /// to other topmost windows created later).
        /// </summary>
        private void ReassertTopmost()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    IntPtr tube = _tubeHandle != IntPtr.Zero ? _tubeHandle : (this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
                    if (tube != IntPtr.Zero)
                        SetWindowPos(tube, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                catch { }
            }
            else
            {
                var was = Topmost;
                Topmost = false;
                Dispatcher.UIThread.Post(() => Topmost = was);
            }
        }

        // ================= Z-order (strictly separated from position) =================

        private void BringToFrontTemporarily()
        {
            if (!_isAttached) return;
            BringAttachedPairToFront(true);
        }

        public void RaiseAttachedTubeAboveOwner() => BringAttachedPairToFront(true);

        /// <summary>
        /// Requests the attached pair raise through the controller's ONE throttled +
        /// coalesced funnel (board REBUILD SPEC invariant 3). The actual platform raise
        /// happens in <see cref="OnRaiseRequested"/>.
        /// </summary>
        private void BringAttachedPairToFront(bool force = false)
        {
            if (!_isAttached || _parentWindow == null || !_parentWindow.IsVisible || _parentWindow.WindowState == WindowState.Minimized)
                return;
            _geometry?.RequestRaise(force);
        }

        /// <summary>
        /// The platform pair raise, WPF BringAttachedPairToFront parity
        /// (WPF Windowing.cs:1031-1067): parent to HWND_TOP first, then the tube above it,
        /// so they stay paired without entering the TOPMOST band. Passive raises are gated
        /// on our process owning the foreground (:1051-1060) so we never steal z-order
        /// from other apps; force=true callers are deliberately foregrounding us
        /// (:1022-1029). Runs inside the controller's re-entrancy guard, so the
        /// WM_WINDOWPOSCHANGED these SetWindowPos calls generate cannot loop back.
        /// </summary>
        private void OnRaiseRequested(object? sender, bool force)
        {
            if (!_isAttached || _parentWindow == null || !_parentWindow.IsVisible || _parentWindow.WindowState == WindowState.Minimized)
                return;
            if (!OperatingSystem.IsWindows())
                return;

            try
            {
                IntPtr tube = _tubeHandle != IntPtr.Zero ? _tubeHandle : (this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
                IntPtr parent = _parentHandle != IntPtr.Zero ? _parentHandle : (_parentWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
                if (tube == IntPtr.Zero || parent == IntPtr.Zero) return;

                if (!force && !IsOurAppForeground())
                    return;

                SetWindowPos(parent, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                SetWindowPos(tube, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { }
        }

        private void SetToolWindowStyle(bool isToolWindow)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                IntPtr handle = _tubeHandle != IntPtr.Zero ? _tubeHandle : (this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
                if (handle == IntPtr.Zero) return;
                int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
                int newExStyle = isToolWindow
                    ? exStyle | WS_EX_TOOLWINDOW
                    : exStyle & ~WS_EX_TOOLWINDOW;
                SetWindowLong(handle, GWL_EXSTYLE, newExStyle);
                SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch { }
        }

        private bool IsOurAppForeground()
        {
            if (!OperatingSystem.IsWindows()) return true;
            try
            {
                var fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                GetWindowThreadProcessId(fg, out uint pid);
                return pid == (uint)Process.GetCurrentProcess().Id;
            }
            catch
            {
                return true;
            }
        }

        public void SetChaosRunActive(bool active)
        {
            _chaosRunActive = active;
            if (active)
            {
                _reattachAfterChaos = _isAttached;
                if (_isAttached) Hide();
            }
            else if (_reattachAfterChaos)
            {
                if (_settings?.Current?.AvatarEnabled == true && this.TryGetPlatformHandle() != null)
                {
                    Show();
                    UpdatePosition();
                    BringAttachedPairToFront(true);
                }
            }
        }
    }
}
