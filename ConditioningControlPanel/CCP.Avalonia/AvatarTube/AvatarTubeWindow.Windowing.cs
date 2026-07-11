using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CS0169 // Avalonia port: unused stub fields kept for future companion/avatar work
#pragma warning disable CS0414
#pragma warning disable CS0649

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    // NOTE (2026-07 windowing rewrite): all sizing + anchoring + parent-follow logic
    // lives in TubeAnchorController (the single writer of Window.Position and of
    // ContentViewbox.Width/Height). This partial keeps only NON-anchoring window
    // behavior: fullscreen auto-hide, float animation, attach/detach orchestration,
    // z-order helpers, and the chaos-run park/reattach hooks.
    public partial class AvatarTubeWindow
    {
        private IntPtr _tubeHandle;
        private IntPtr _parentHandle;
        private IScreenProvider? _screenProvider;
        private string _diagLastFullscreenWindow = "(none)";

        // Win32 constants (kept for reference; P/Invoke calls are Windows-only and stubbed here).
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint GW_HWNDPREV = 3;
        private const int GWL_EXSTYLE = -20;
        private const int GWL_STYLE = -16;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_CAPTION = 0x00C00000;

#if WINDOWS
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hWnd);
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
#endif

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
                    // Defer the anchor one dispatcher pass so the just-shown window has a settled
                    // size/scale before the controller reads RenderScaling (running synchronously
                    // here can sample a pre-layout transient). Re-derive the screen-fit scale too
                    // in case the monitor/DPI changed while we were hidden.
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_isAttached)
                        {
                            _anchor?.RecomputeScreenScale();
                            _anchor?.ApplySizing();
                            UpdatePosition();
                        }
                        BringAttachedPairToFront(true);
                    }, DispatcherPriority.Background);
                }
            }
        }

        private bool IsOtherAppFullscreen()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

#if WINDOWS
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
#else
            return false;
#endif
        }

        /// <summary>
        /// Re-anchors the attached tube to the parent's left edge. Delegates to
        /// <see cref="TubeAnchorController"/> - the single writer of Window.Position.
        /// </summary>
        public void UpdatePosition()
        {
            _anchor?.UpdatePosition();
        }

        private void StartFloatingAnimation()
        {
            StopFloatingAnimation();
            _floatPhase = 0;
            _floatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _floatTimer.Tick += (_, _) =>
            {
                _floatPhase += 0.05;
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

        public void SetDetached(bool detached)
        {
            if (detached) Detach();
            else Attach();
        }

        public void Attach()
        {
            _isAttached = true;
            Topmost = false;

            // Controller resumes parent-follow sizing (parent ratio, user zoom off)
            // and re-anchors immediately.
            _anchor?.SetAttached(true);

            // Switch back to original tube image and attached layout.
            SetTubeStyle(false);
            ApplyTubeLayoutOffsets();

            Show();
            UpdatePosition();
            BringAttachedPairToFront(true);
        }

        public void Detach()
        {
            _isAttached = false;
            Topmost = true;

            // Controller stops parent-ratio sizing; detached user zoom applies.
            _anchor?.SetAttached(false);

            // Switch to alternative tube image and detached layout.
            SetTubeStyle(true);
            ApplyTubeLayoutOffsets();

            // Speech bubble stays at same position in both modes (right side of tube, clearly visible).
            if (SpeechBubble?.IsVisible == true && !string.IsNullOrEmpty(TxtSpeech?.Text))
            {
                AdjustBubbleSize(TxtSpeech.Text);
            }

            Show();
            ReassertTopmost();
            ReassertCirceEmoteVisuals();
        }

        private void ReassertTopmost()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if WINDOWS
                // Windows-specific topmost reassertion would go here via platform helpers.
#endif
            }
            else
            {
                var was = Topmost;
                Topmost = false;
                Dispatcher.UIThread.Post(() => Topmost = was);
            }
        }

        private void BringToFrontTemporarily()
        {
            if (!_isAttached) return;
            BringAttachedPairToFront(true);
        }

        public void RaiseAttachedTubeAboveOwner() => BringAttachedPairToFront(true);

        private void BringAttachedPairToFront(bool force = false)
        {
            if (!_isAttached || _parentWindow == null || !_parentWindow.IsVisible || _parentWindow.WindowState == WindowState.Minimized)
                return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#if WINDOWS
                try
                {
                    IntPtr tube = _tubeHandle != IntPtr.Zero ? _tubeHandle : (this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
                    IntPtr parent = _parentHandle != IntPtr.Zero ? _parentHandle : (_parentWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
                    if (tube == IntPtr.Zero || parent == IntPtr.Zero) return;
                    SetWindowPos(tube, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    SetWindowPos(parent, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }
                catch { }
#endif
            }
        }

        private void SetToolWindowStyle(bool isToolWindow)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
#if WINDOWS
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
#endif
        }

        private bool IsOurAppForeground()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return true;
#if WINDOWS
            try
            {
                var fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            catch { }
#endif
            return true;
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

#pragma warning restore CS0169
#pragma warning restore CS0414
#pragma warning restore CS0649
