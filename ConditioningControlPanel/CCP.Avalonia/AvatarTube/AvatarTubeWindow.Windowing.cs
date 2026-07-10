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
    public partial class AvatarTubeWindow
    {
        private IntPtr _tubeHandle;
        private IntPtr _parentHandle;
        private bool _reassertingAboveParent;
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
                // Position ~(-32000,-32000); showing then makes UpdatePosition() anchor off-screen,
                // hit its out-of-range guard, silently skip the move, and leave the tube parked in
                // the wrong spot with nothing to re-trigger it ("reappears in the wrong place").
                // Keeping the flag set retries on the next tick until the parent settles.
                bool parentReady = _parentWindow?.IsVisible == true
                                   && _parentWindow.WindowState != WindowState.Minimized;
                if (parentReady && _wasAttachedBeforeFullscreen && _settings?.Current?.AvatarEnabled == true)
                {
                    _hiddenForFullscreen = false;
                    Show();
                    // Defer the anchor one dispatcher pass so the just-shown window has a settled
                    // size/scale before UpdatePosition() reads RenderScaling (running synchronously
                    // here can sample a pre-layout transient). Re-derive scale too in case the
                    // monitor/DPI changed while we were hidden. Design-size anchoring is untouched.
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_isAttached)
                        {
                            CalculateScaleFactor();
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

        private void CalculateScaleFactor()
        {
            try
            {
                var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
                if (screen != null)
                {
                    double maxHeightScale = (screen.WorkingArea.Height * 0.85) / DesignHeight;
                    double maxWidthScale = (screen.WorkingArea.Width * 0.3) / DesignWidth;
                    _scaleFactor = Math.Max(0.4, Math.Min(1.0, Math.Min(maxHeightScale, maxWidthScale)));
                }
                else
                {
                    _scaleFactor = 0.7;
                }
            }
            catch
            {
                _scaleFactor = 0.7;
            }

            if (ContentViewbox != null)
            {
                ContentViewbox.Width = DesignWidth * _scaleFactor;
                ContentViewbox.Height = DesignHeight * _scaleFactor;
            }
        }

        private void ApplyScale()
        {
            if (ContentViewbox != null)
            {
                ContentViewbox.Width = DesignWidth * _scaleFactor * _currentScale;
                ContentViewbox.Height = DesignHeight * _scaleFactor * _currentScale;
            }
            ClampAvatarPosition();
        }

        private void ClampAvatarPosition()
        {
            if (_screenProvider == null) return;
            var screens = _screenProvider.GetAllScreens();
            var screen = screens.FirstOrDefault(s => s.Bounds.X <= Position.X && Position.X < s.Bounds.Right
                                                  && s.Bounds.Y <= Position.Y && Position.Y < s.Bounds.Bottom)
                        ?? _screenProvider.GetPrimaryScreen();
            if (screen == null) return;

            int w = (int)Math.Max(1, Width);
            int h = (int)Math.Max(1, Height);
            int x = Math.Clamp(Position.X, (int)screen.WorkingArea.X, (int)(screen.WorkingArea.Right - w));
            int y = Math.Clamp(Position.Y, (int)screen.WorkingArea.Y, (int)(screen.WorkingArea.Bottom - h));
            Position = new PixelPoint(x, y);
        }

        public void UpdatePosition()
        {
            if (!_isAttached || _parentWindow == null) return;
            if (_parentWindow.ClientSize.Height <= 0 || _parentWindow.ClientSize.Width <= 0) return;
            if (_parentWindow.Position.X == 0 && _parentWindow.Position.Y == 0 && _parentWindow.ClientSize.Height < 100) return;

            // Anchor on the DETERMINISTIC design size (the same values ContentViewbox is
            // sized with in CalculateScaleFactor) instead of the live ClientSize: the window
            // opens with SizeToContent=WidthAndHeight and only freezes to Manual on first
            // render, so ClientSize is timing-dependent - late-loading tube art or an open
            // speech bubble inflating it made this centering math park the tube too HIGH,
            // where it stayed ("avatar drifts up"). Design-based anchoring keeps the tube
            // put no matter what transient content does to the window size.
            double tubeWidth = DesignWidth * _scaleFactor;
            double tubeHeight = DesignHeight * _scaleFactor;
            double scaledOffset = BaseOffsetFromParent * _scaleFactor;

            // Window.Position is PHYSICAL pixels while ClientSize and the design constants
            // are LOGICAL units - convert via the parent's scaling or the pair drifts apart
            // on monitors above 100% DPI (attached tube shares the parent's monitor).
            double s = _parentWindow.RenderScaling;

            double newLeft = _parentWindow.Position.X - (tubeWidth + scaledOffset) * s;
            double newTop = _parentWindow.Position.Y + ((_parentWindow.ClientSize.Height - tubeHeight) / 2 + VerticalOffset * _scaleFactor) * s;
            if (newTop < -500 || newTop > 5000 || newLeft < -2000 || newLeft > 5000) return;

            Position = new PixelPoint((int)newLeft, (int)newTop);
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

        private void Attach()
        {
            _isAttached = true;
            Topmost = false;

            // Switch back to original tube image and attached layout.
            SetTubeStyle(false);
            ApplyTubeLayoutOffsets();

            Show();
            UpdatePosition();
            BringAttachedPairToFront(true);
        }

        private void Detach()
        {
            _isAttached = false;
            Topmost = true;

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
                    var helper = new WindowInteropHelper(this);
                    var parentHelper = new WindowInteropHelper(_parentWindow);
                    SetWindowPos(helper.Handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    SetWindowPos(parentHelper.Handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
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
                var helper = new WindowInteropHelper(this);
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                int newExStyle = isToolWindow
                    ? exStyle | WS_EX_TOOLWINDOW
                    : exStyle & ~WS_EX_TOOLWINDOW;
                SetWindowLong(helper.Handle, GWL_EXSTYLE, newExStyle);
                SetWindowPos(helper.Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
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

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            return IntPtr.Zero;
        }

        private IntPtr ParentWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            return IntPtr.Zero;
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
                if (_settings?.Current?.AvatarEnabled == true && PlatformImpl != null)
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
