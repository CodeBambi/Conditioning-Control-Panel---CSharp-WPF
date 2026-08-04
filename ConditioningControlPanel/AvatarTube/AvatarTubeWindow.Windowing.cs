using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Moderation;
using XamlAnimatedGif;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    public partial class AvatarTubeWindow : Window
    {
        private IntPtr _tubeHandle;
        private IntPtr _parentHandle;
        private bool _chaosRunActive;       // A Chaos run owns the screen (see SetChaosRunActive)
        private bool _reattachAfterChaos;   // we auto-detached for the run and should re-attach when it ends
        private bool _hiddenForChaos;       // Story-mode run hid the companion; restore only if we hid it

        // ============================================================
        // POSITIONING & SCALING - ADJUST THESE VALUES AS NEEDED
        // ============================================================

        // Design reference size (what the XAML is designed for)
        private const double DesignWidth = 780;
        private const double DesignHeight = 1020;

        // Gap between tube window and main window (negative = overlap)
        // This will be scaled based on actual window size
        private const double BaseOffsetFromParent = -350;

        // Vertical offset from center (positive = lower, negative = higher)
        private const double VerticalOffset = 20;

        // Floating animation settings
        private const double FloatDistance = 4;
        private const double FloatDuration = 2.0;

        // Current scale factor
        private double _scaleFactor = 1.0;

        // Current avatar scale (for Ctrl+scroll/arrow key/menu resizing when detached)
        private double _currentScale = 1.0;
        private const double MinScale = 0.5;   // 50% - can shrink twice from 100%
        private const double MaxScale = 1.5;   // 150% - can grow twice from 100%
        private const double ScaleStep = 0.25; // 25% per step
        // Set while restoring saved placement (#669) so ApplyScale/drag don't re-persist mid-restore.
        private bool _restoringPlacement = false;

        // Fullscreen detection
        private DispatcherTimer? _fullscreenCheckTimer;
        private bool _hiddenForFullscreen = false;
        private bool _wasAttachedBeforeFullscreen = false;
        // [AVATAR-BLINK DIAG] last window that tripped the fullscreen detector (class/proc/pid/rect).
        private string _diagLastFullscreenWindow = "(none)";

        // Win32 API
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        // Used by ForceForegroundWindow to bypass Windows' focus-stealing prevention
        // on this tool window (WS_EX_TOOLWINDOW). Without this, Activate() is silently
        // ignored when the user clicks the avatar from another foreground app.
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const int GWL_EXSTYLE = -20;
        private const int GWL_STYLE = -16;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_CAPTION = 0x00C00000;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        private HwndSource? _hwndSource;

        // Native (Win32) ownership: while attached the tube is OWNED by the main window, so the
        // window manager itself keeps the tube directly above main and moves/raises the two as a
        // group. This replaces the old hand-rolled z-order machinery (parent WndProc raise hook,
        // 300ms refresh timer, forced HWND_TOP reasserts) which could race other windows and lift
        // the tube above unrelated surfaces (e.g. the Graded Intake host) while main stayed buried.
        private const int GWL_HWNDPARENT = -8;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint GW_HWNDPREV = 3;   // the window directly ABOVE hWnd in the z-order
        private const uint GW_HWNDNEXT = 2;   // the window directly BELOW hWnd in the z-order

        /// <summary>
        /// Start monitoring for fullscreen applications
        /// </summary>
        private void StartFullscreenDetection()
        {
            _fullscreenCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Check every 500ms
            };
            _fullscreenCheckTimer.Tick += FullscreenCheckTimer_Tick;
            _fullscreenCheckTimer.Start();
        }

        // While a fullscreen game host (DTRH / Graded Intake / Loom / Bureau) owns the screen, an
        // ATTACHED tube rides at main's z-level inside main's owner group; ANY self-show/raise
        // leapfrogs the host sibling and floats the tube over the game (its native-owned upper-left
        // position). Every attached self-raise path below consults this and stands down until the
        // host closes (mirrors the chat-focus guard in .ChatInput.cs). Detached tubes are
        // intentionally topmost widgets and stay exempt.
        private bool SuppressSelfRaiseForGameHost => !IsDetached && ChaosWebViewHost.AnyHostActive;

        /// <summary>
        /// Drop the ATTACHED tube to the bottom of MainWindow's owner group — directly above main,
        /// and therefore BELOW every game-host window glued over it (DTRH / Graded Intake / Loom /
        /// Bureau).
        ///
        /// This is the ACTIVE half of <see cref="SuppressSelfRaiseForGameHost"/>, which is purely
        /// passive: it stops the tube RAISING itself, but it cannot undo a raise that already
        /// happened, and plenty of them are not self-raises at all — the window manager re-showing
        /// the owned tube when main comes back from the intake's duck, a click that activates the
        /// tube, or a stale WS_EX_TOPMOST left over from an <c>App.ForceWindowToFront</c>-style
        /// Topmost pulse propagating through main to its owned windows. A tube that is already
        /// above the host stays there forever, which is
        /// exactly the "tube floats over Graded Intake" report.
        ///
        /// Passing main's OWN handle as hWndInsertAfter asks for "directly behind main", which the
        /// window manager clamps to "directly above main" because an owned window may never sit
        /// below its owner. That is the same trick
        /// <see cref="ChaosWebViewHost"/>.ApplyNativeOwner uses to slot a host above the pair, run
        /// in the opposite direction — so the tube can never end up buried under main. Ordering
        /// against a non-topmost window also clears WS_EX_TOPMOST, which is a repair rather than a
        /// loss: an attached tube is never meant to be topmost (<see cref="Attach"/> sets
        /// Topmost=false) and <see cref="Detach"/> puts the band back itself.
        ///
        /// Nothing has to be restored when the host closes: this only reorders inside the band the
        /// tube already belongs to, and main's next raise carries its owned tube back up with it.
        ///
        /// Callable from ANY thread — it self-marshals to the avatar dispatcher. Doing the
        /// SetWindowPos on the tube's OWN thread matters: cross-thread SetWindowPos sends
        /// WM_WINDOWPOSCHANGING synchronously and would let a busy avatar thread block main (the
        /// app's historic mixed-DPI / render-deadlock cluster).
        /// </summary>
        internal void SinkToMainZOrder()
        {
            if (!Dispatcher.CheckAccess()) { RunOnAvatar(SinkToMainZOrder); return; }
            try
            {
                // A DETACHED tube is a deliberate free-floating topmost widget — never sink it.
                if (!_isAttached) return;
                var tube = _tubeHandle;
                var main = _parentHandle;
                if (tube == IntPtr.Zero || main == IntPtr.Zero) return;
                // Hidden (ducked with main, or hidden for a fullscreen app) can't float over
                // anything, and reordering it would only churn frames 2x/second for nothing.
                if (!IsWindowVisible(tube)) return;
                // An ATTACHED tube is never meant to carry WS_EX_TOPMOST (Attach sets Topmost=false;
                // only Detach puts the band back). Finding it set means a Topmost pulse on main
                // propagated into its owner group and never propagated back out, which parks the
                // tube above the entire non-topmost band - the host included - no matter where it
                // sits relative to main. Always repair that, never trust the cheap check below for it.
                bool strayTopmost = (GetWindowLong(tube, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
                // Otherwise: already sitting directly above main? Then nothing is between us and
                // main, so nothing we could be floating over — leave the z-order (and this layered
                // window's WM_WINDOWPOSCHANGING path) completely alone. This is what keeps the 500ms
                // tick idle in the steady state instead of poking the tube twice a second.
                if (!strayTopmost && GetWindow(main, GW_HWNDPREV) == tube) return;
                SetWindowPos(tube, main, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { /* window may be tearing down */ }
        }

        private void FullscreenCheckTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // A game host owns the screen: the attached tube is already tucked away with the
                // minimized main window, and its own hide/restore dance here is the confirmed cause
                // of the tube popping over the game. Do nothing until the host closes — except push
                // the tube back down to main's level, which is the only tick that runs while a host
                // is up and so is the safety net for every raise nobody told us about (the window
                // manager's own owner-group re-show, a click on the tube, a Topmost pulse).
                if (SuppressSelfRaiseForGameHost) { SinkToMainZOrder(); return; }

                bool isOtherAppFullscreen = IsOtherAppFullscreen();

                // When DETACHED, avatar should stay visible as a widget overlay
                // Only hide for fullscreen when ATTACHED
                if (_isAttached)
                {
                    if (isOtherAppFullscreen && !_hiddenForFullscreen)
                    {
                        // Another app went fullscreen - hide the avatar (attached mode only)
                        _hiddenForFullscreen = true;
                        _wasAttachedBeforeFullscreen = _isAttached;
                        Hide();
                        // [AVATAR-BLINK DIAG] Info-level so we can catch the "random disappear":
                        // _diagLastFullscreenWindow holds the offending window's class/pid/rect.
                        App.Logger?.Information("[AVATAR-BLINK] hidden — fullscreen app detected (attached). Offender: {Win}", _diagLastFullscreenWindow);
                    }
                    else if (!isOtherAppFullscreen && _hiddenForFullscreen)
                    {
                        // Fullscreen app closed - restore the avatar.
                        // IMPORTANT: only clear the flag once we actually Show(). If the parent
                        // is momentarily minimized/hidden during the fullscreen-exit transition
                        // (common when leaving an exclusive-fullscreen game), clearing the flag
                        // here without showing would leave the avatar stuck hidden forever - the
                        // hide-branch can't re-fire (no fullscreen) and this branch can't re-fire
                        // (flag cleared). Keeping the flag set lets us retry on the next tick.
                        if (ParentVisibleNotMinimized && App.Settings?.Current?.AvatarEnabled == true)
                        {
                            _hiddenForFullscreen = false;
                            Show();
                            if (_wasAttachedBeforeFullscreen && _isAttached)
                            {
                                UpdatePosition();
                            }
                            App.Logger?.Information("[AVATAR-BLINK] restored — fullscreen app closed");
                        }
                    }
                }
                else
                {
                    // DETACHED mode - periodically reassert topmost to stay visible as widget
                    // This handles cases where other topmost windows or focus changes demote us
                    if (_hiddenForFullscreen && App.Settings?.Current?.AvatarEnabled == true)
                    {
                        _hiddenForFullscreen = false;
                        Show();
                    }
                    ReassertTopmost();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error checking fullscreen state");
            }
        }

        /// <summary>
        /// Check if another application (not our app) is running in EXCLUSIVE fullscreen mode.
        /// This is conservative - only hides for true DirectX/OpenGL exclusive fullscreen,
        /// NOT for borderless windowed games or browser video fullscreen.
        /// </summary>
        private bool IsOtherAppFullscreen()
        {
            try
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero) return false;

                // Check if it's our own window
                if (foregroundWindow == _tubeHandle || foregroundWindow == _parentHandle)
                    return false;

                // Get the process ID of the foreground window
                GetWindowThreadProcessId(foregroundWindow, out uint foregroundPid);
                uint ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                if (foregroundPid == ourPid)
                    return false;

                // Get window class name to exclude known safe applications
                var className = new System.Text.StringBuilder(256);
                GetClassName(foregroundWindow, className, className.Capacity);
                string windowClass = className.ToString();

                // Exclude browsers and common media applications - these use "fake" fullscreen
                // that covers the screen but isn't exclusive DirectX/OpenGL fullscreen
                string[] safeClasses = {
                    "Chrome_WidgetWin",      // Chrome, Edge (Chromium), Brave, etc.
                    "MozillaWindowClass",    // Firefox
                    "ApplicationFrameWindow", // UWP apps (Netflix, Disney+, etc.)
                    "Windows.UI.Core",       // Modern Windows apps
                    "CabinetWClass",         // Windows Explorer
                    "Shell_TrayWnd",         // Taskbar
                    "Progman",               // Desktop
                    "WorkerW",               // Desktop worker
                    "XLMAIN",                // Excel
                    "OpusApp",               // Word
                    "PPTFrameClass",         // PowerPoint
                    "VLC",                   // VLC media player
                    "mpv",                   // mpv player
                    "MediaPlayerClassicW",   // MPC
                };

                foreach (var safeClass in safeClasses)
                {
                    if (windowClass.StartsWith(safeClass, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // Get the window style
                int style = GetWindowLong(foregroundWindow, GWL_STYLE);
                int exStyle = GetWindowLong(foregroundWindow, GWL_EXSTYLE);

                bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;
                bool isPopup = (style & WS_POPUP) == WS_POPUP;
                bool isTopmost = (exStyle & WS_EX_TOPMOST) == WS_EX_TOPMOST;

                // If the window has a caption (title bar), it's definitely not exclusive fullscreen
                if (hasCaption)
                    return false;

                // For exclusive fullscreen, we require BOTH:
                // 1. Window is popup style (no borders) AND
                // 2. Window is topmost (exclusive fullscreen apps set this)
                // This excludes borderless windowed games which usually aren't topmost
                if (!isPopup || !isTopmost)
                    return false;

                // Get the window rect
                if (!GetWindowRect(foregroundWindow, out RECT windowRect))
                    return false;

                // Get FULL screen bounds (not working area - must cover taskbar too)
                var screen = System.Windows.Forms.Screen.FromHandle(foregroundWindow);
                var screenBounds = screen.Bounds;

                // For true fullscreen, window must cover the ENTIRE screen including taskbar
                int tolerance = 5;
                bool coversFullScreen =
                    windowRect.Left <= screenBounds.Left + tolerance &&
                    windowRect.Top <= screenBounds.Top + tolerance &&
                    windowRect.Right >= screenBounds.Right - tolerance &&
                    windowRect.Bottom >= screenBounds.Bottom - tolerance;

                if (coversFullScreen)
                {
                    GetWindowThreadProcessId(foregroundWindow, out uint offPid);
                    string procName = "?";
                    try { procName = System.Diagnostics.Process.GetProcessById((int)offPid).ProcessName; } catch { }
                    _diagLastFullscreenWindow = $"class={windowClass} proc={procName}(pid {offPid}) rect=[{windowRect.Left},{windowRect.Top},{windowRect.Right},{windowRect.Bottom}]";
                    App.Logger?.Debug("Exclusive fullscreen detected: {Win}", _diagLastFullscreenWindow);
                }

                return coversFullScreen;
            }
            catch
            {
                return false;
            }
        }

        private const int WM_DPICHANGED = 0x02E0;
        private DispatcherTimer? _dpiQuiesceTimer;
        private bool _dpiFloatWasRunning;

        /// <summary>
        /// Window procedure hook. Only handles WM_DPICHANGED: when a drag crosses onto a
        /// monitor with a different scale factor, PerMonitorV2 WPF resizes this layered
        /// window, and that resize runs a SYNCHRONOUS CompleteRender that deadlocks when
        /// the shared AllowsTransparency render thread is busy (the documented hang in
        /// OnFirstContentRendered — #477).
        ///
        /// The blocking present can only complete if NOTHING is writing to this surface, and
        /// TWO independent drivers do: the 60fps float/breath transform, and the Circe emote
        /// crossfade (~1Hz idle rotation plus its opacity clocks). The original mitigation
        /// quiesced only the former — emote mode landed on this surface afterwards and kept
        /// compositing straight through the transition, which is how an attached tube dragged
        /// between mixed-DPI monitors could still wedge (Hang 1002, no crash.log, last log
        /// line an [EMOTE] xfade). Quiesce both for the transition, then resume.
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DPICHANGED)
            {
                try
                {
                    // NB: the quiesce must NOT be conditional on the float timer running — the emote
                    // crossfade deadlocks this surface just as happily with the float transform idle.
                    if (_floatTimer?.IsEnabled == true)
                    {
                        _floatTimer.Stop();
                        _dpiFloatWasRunning = true;   // sticky across rapid re-crossings; cleared on resume
                    }
                    QuiesceEmotesForDpi();

                    if (_dpiQuiesceTimer == null)
                    {
                        _dpiQuiesceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                        _dpiQuiesceTimer.Tick += (s, e) =>
                        {
                            _dpiQuiesceTimer?.Stop();
                            try { if (_dpiFloatWasRunning) { _dpiFloatWasRunning = false; _floatTimer?.Start(); } }
                            catch { /* window tearing down */ }
                            try { ResumeEmotesAfterDpi(); } catch { /* window tearing down */ }
                        };
                    }
                    _dpiQuiesceTimer.Stop(); // restart the debounce on rapid re-crossings
                    _dpiQuiesceTimer.Start();
                }
                catch { /* never let a hook throw */ }
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Set or clear the tube's NATIVE (Win32) owner. When attached the tube is owned by the
        /// main window: the window manager keeps an owned window directly above its owner and
        /// raises/lowers the two as a group, so the pair can never separate — no polling, no
        /// manual raises, and other windows (ours or other apps') can freely cover BOTH.
        /// NOTE: this is raw GWL_HWNDPARENT ownership, NOT WPF's Window.Owner — the managed
        /// property adds minimize/visibility coupling that caused black-window artifacts, and
        /// it cannot be set across threads in AvatarOwnThread mode anyway.
        /// </summary>
        private void ApplyNativeOwner(bool owned)
        {
            if (_tubeHandle == IntPtr.Zero) return;
            var owner = owned ? _parentHandle : IntPtr.Zero;
            if (owned && owner == IntPtr.Zero) { CaptureParentGeom(); return; } // parent HWND not seeded yet; next call has it
            SetWindowLongPtr(_tubeHandle, GWL_HWNDPARENT, owner);
            // Owner changes are cached — flush the frame so the z-order link takes effect now.
            SetWindowPos(_tubeHandle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        private void CalculateScaleFactor()
        {
            try
            {
                // Get DPI scaling
                var source = PresentationSource.FromVisual(this);
                double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                // Get primary screen working area
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return;

                double screenHeight = screen.WorkingArea.Height / dpiScale;
                double screenWidth = screen.WorkingArea.Width / dpiScale;

                // Calculate max scale that fits on screen (leave some margin)
                double maxHeightScale = (screenHeight * 0.85) / DesignHeight;
                double maxWidthScale = (screenWidth * 0.3) / DesignWidth; // Tube shouldn't be more than 30% of screen width

                _scaleFactor = Math.Min(maxHeightScale, maxWidthScale);
                _scaleFactor = Math.Max(0.4, Math.Min(1.0, _scaleFactor)); // Clamp between 40% and 100%

                // Apply scale to viewbox
                ContentViewbox.Width = DesignWidth * _scaleFactor;
                ContentViewbox.Height = DesignHeight * _scaleFactor;

                App.Logger?.Information("AvatarTube scale factor: {Scale:F2} (Screen: {W}x{H}, DPI: {DPI:F2})",
                    _scaleFactor, screenWidth, screenHeight, dpiScale);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to calculate scale factor: {Error}", ex.Message);
                _scaleFactor = 0.7; // Safe default for smaller screens
                ContentViewbox.Width = DesignWidth * _scaleFactor;
                ContentViewbox.Height = DesignHeight * _scaleFactor;
            }
        }

        /// <summary>
        /// Ensure the window is visible when detached - acts as a persistent widget
        /// </summary>
        private void EnsureVisibleWhenDetached()
        {
            if (!_isAttached)
            {
                Show();
                // Reassert topmost so avatar stays visible as a widget overlay
                ReassertTopmost();
            }
        }

        /// <summary>
        /// Toggle the WS_EX_TOOLWINDOW style (controls Alt+Tab visibility)
        /// </summary>
        private void SetToolWindowStyle(bool isToolWindow)
        {
            if (_tubeHandle == IntPtr.Zero) return;

            int exStyle = GetWindowLong(_tubeHandle, GWL_EXSTYLE);
            if (isToolWindow)
            {
                SetWindowLong(_tubeHandle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            }
            else
            {
                SetWindowLong(_tubeHandle, GWL_EXSTYLE, exStyle & ~WS_EX_TOOLWINDOW);
            }
            // SetWindowLong frame data is cached — flush with SWP_FRAMECHANGED
            // so the new style takes effect without requiring a window move.
            SetWindowPos(_tubeHandle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        private DispatcherTimer? _floatTimer;
        private double _floatPhase = 0;

        private void StartFloatingAnimation()
        {
            // Stop any existing animation first
            StopFloatingAnimation();

            // Use a timer-based approach instead of WPF animations for maximum reliability
            // This won't interfere with other animations on the element
            _floatPhase = 0;
            _floatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60fps
            };
            _floatTimer.Tick += (s, e) =>
            {
                // Sine wave oscillation (the existing gentle vertical bob)
                _floatPhase += 0.05; // Speed of oscillation
                var y = Math.Sin(_floatPhase) * FloatDistance;
                AvatarTranslate.Y = y;

                // Portrait mode adds breathing (subtle scale), wobble (subtle rotation), and a drifting
                // pink mist. Written every tick to BOTH portrait layers so the incoming crossfade image
                // isn't snapped back to neutral mid-pulse. Crossfade lives on Opacity (orthogonal to these
                // transform DPs), so nothing fights the 60fps writes. Legacy mods skip this block entirely.
                if (_portraitMode)
                {
                    _breathPhase += 0.013;
                    _wobblePhase += 0.017;
                    _mistPhase += 0.009;

                    double scale = 1.0 + Math.Sin(_breathPhase) * BreathAmplitude;
                    double angle = Math.Sin(_wobblePhase) * WobbleAmplitudeDeg;
                    double xJit = 0.0;

                    // While a clip is actually playing: add a faster vibration + a tiny horizontal jitter,
                    // so she visibly "talks". Stops the instant the audio ends (_isSpeakingAudio flips false).
                    bool speaking = _isSpeakingAudio;
                    if (speaking)
                    {
                        _speakPhase += 0.45;       // fast carrier
                        _speakEnvPhase += 0.035;   // slow envelope (~3s) → intermittent bursts
                        // env is ~0 most of the time and briefly swells toward 1 → occasional, barely-there shimmer.
                        double env = Math.Pow(Math.Max(0.0, Math.Sin(_speakEnvPhase)), 3);
                        double vib = Math.Sin(_speakPhase) * env;
                        angle += vib * SpeakWobbleDeg;
                        xJit = vib * SpeakShakePx;
                    }

                    AvatarScale.ScaleX = AvatarScale.ScaleY = scale;
                    AvatarRotate.Angle = angle;
                    AvatarTranslate.X = xJit;
                    AvatarScaleB.ScaleX = AvatarScaleB.ScaleY = scale;
                    AvatarRotateB.Angle = angle;
                    AvatarTranslateB.X = xJit;
                    AvatarTranslateB.Y = y; // layer B bobs in lockstep with layer A

                    if (MistOverlay.Visibility == Visibility.Visible)
                    {
                        // Pink mist drifts over the avatar; thicker + livelier while she's speaking.
                        double mistBase = speaking ? 0.26 : 0.15;
                        double mistAmp = speaking ? 0.10 : 0.06;
                        double driftSpeed = speaking ? 1.0 : 0.7;
                        MistOverlay.Opacity = mistBase + (Math.Sin(_mistPhase) + 1.0) * 0.5 * mistAmp;
                        double mistScale = 1.0 + (Math.Sin(_mistPhase * driftSpeed) + 1.0) * 0.5 * (speaking ? 0.06 : 0.04);
                        MistScale.ScaleX = MistScale.ScaleY = mistScale;
                    }
                }
            };
            _floatTimer.Start();
        }

        private void StopFloatingAnimation()
        {
            _floatTimer?.Stop();
            _floatTimer = null;
            AvatarTranslate.Y = 0;
            // Reset portrait-mode transforms so a stopped avatar isn't frozen mid-breath/wobble.
            if (_portraitMode)
            {
                AvatarScale.ScaleX = AvatarScale.ScaleY = 1.0;
                AvatarRotate.Angle = 0;
                AvatarScaleB.ScaleX = AvatarScaleB.ScaleY = 1.0;
                AvatarRotateB.Angle = 0;
                AvatarTranslateB.Y = 0;
            }
        }

        public void UpdatePosition()
        {
            if (!_isAttached || _parentWindow == null) return;

            // Read the parent geometry from the cache (refreshed on the parent thread by the parent
            // events) so this is safe to run on the avatar thread. If the cache isn't seeded yet, seed
            // it and bail — the next parent event / call will have it.
            var g = _parentGeom;
            if (g == null) { CaptureParentGeom(); return; }

            // Don't update position if parent window has invalid dimensions (can happen during focus changes)
            if (g.Height <= 0 || g.Width <= 0) return;

            // Don't update if parent window is at origin with zero size (likely transitioning)
            if (g.Top == 0 && g.Left == 0 && g.Height < 100) return;

            // Get actual window dimensions (scaled)
            double actualWidth = ActualWidth > 0 ? ActualWidth : DesignWidth * _scaleFactor;
            double actualHeight = ActualHeight > 0 ? ActualHeight : DesignHeight * _scaleFactor;

            // Scale the offset based on current scale factor
            double scaledOffset = BaseOffsetFromParent * _scaleFactor;

            // Calculate new position
            double newLeft = g.Left - actualWidth - scaledOffset;
            double newTop = g.Top + (g.Height - actualHeight) / 2 + (VerticalOffset * _scaleFactor);

            // Sanity check: don't jump to extreme positions (likely invalid data)
            // This prevents the "bounce to top" issue during focus changes
            if (newTop < -500 || newTop > 5000 || newLeft < -2000 || newLeft > 5000) return;

            // Position to the LEFT of the parent window
            Left = newLeft;
            Top = newTop;
        }

        private void ParentWindow_PositionChanged(object? sender, EventArgs e)
        {
            // Skip if parent is null, window is closing, or parent is minimized.
            // Fires on the PARENT thread: read parent state + refresh the geom cache here, then run the
            // avatar-window work on the avatar thread (own-thread mode; a no-op marshal when shared).
            if (_parentWindow == null) return;
            try
            {
                if (_parentWindow.WindowState == WindowState.Minimized) return;
                CaptureParentGeom();
                RunOnAvatar(UpdatePosition);
            }
            catch { /* Window may be closing */ }
        }

        private void ParentWindow_StateChanged(object? sender, EventArgs e)
        {
            if (_parentWindow == null) return;
            try
            {
                CaptureParentGeom();
                var state = _parentWindow.WindowState;        // parent-thread reads
                bool parentVisible = _parentWindow.IsVisible;
                RunOnAvatar(() =>
                {
                    switch (state)
                    {
                        case WindowState.Minimized:
                            if (_isAttached)
                            {
                                PauseAvatarGif();
                                App.Logger?.Information("[AVATAR-BLINK] hidden — parent window minimized");
                                Hide();
                            }
                            else
                            {
                                // #744: a detached tube is a standalone widget - it stays on screen
                                // when the main window minimizes, so it has to stay animated too.
                                // PauseAvatarGif used to run above this branch while the only resume
                                // sat on the Normal/Maximized case, leaving the tube visible and
                                // frozen on one frame. Worse in emote mode, where the pause also
                                // stops the watchdog and the rotation is driven by clip-completion
                                // callbacks, so nothing could ever restart it.
                                ResumeAvatarGif();
                                // When detached, force visibility and topmost
                                EnsureVisibleWhenDetached();
                            }
                            break;
                        case WindowState.Normal:
                        case WindowState.Maximized:
                            ResumeAvatarGif();
                            if (parentVisible && App.Settings?.Current?.AvatarEnabled == true)
                            {
                                if (!SuppressSelfRaiseForGameHost)
                                {
                                    Show();
                                    if (_isAttached) UpdatePosition();
                                    // When detached, WPF Topmost property handles it
                                }
                                else
                                {
                                    // Main just came back from the intake's duck. The window manager
                                    // re-shows the windows IT hid with the owner, behind WPF's back
                                    // and in an order nothing guarantees — so the tube can surface
                                    // above the host here even though we never asked it to.
                                    SinkToMainZOrder();
                                }
                            }
                            break;
                    }
                });
            }
            catch { /* Window may be closing */ }
        }

        private void ParentWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_parentWindow == null) return;
            try
            {
                CaptureParentGeom();
                bool nowVisible = (bool)e.NewValue;
                var state = _parentWindow.WindowState;   // parent-thread read
                RunOnAvatar(() =>
                {
                    if (nowVisible && state != WindowState.Minimized
                        && App.Settings?.Current?.AvatarEnabled == true)
                    {
                        ResumeAvatarGif();
                        if (!SuppressSelfRaiseForGameHost)
                        {
                            Show();
                            if (_isAttached) UpdatePosition();
                        }
                        else SinkToMainZOrder();   // main became visible again under a live host
                        // When detached, WPF Topmost property handles it
                    }
                    else
                    {
                        if (_isAttached)
                        {
                            PauseAvatarGif();
                            App.Logger?.Information("[AVATAR-BLINK] hidden — parent IsVisible went false (state={State})", state);
                            Hide();
                        }
                        else
                        {
                            // #744: same as the minimize case - a detached tube stays on screen, so
                            // it must keep animating. This is the minimize-to-tray path.
                            ResumeAvatarGif();
                            // When detached, force visibility and topmost
                            EnsureVisibleWhenDetached();
                        }
                    }
                });
            }
            catch { /* Window may be closing */ }
        }

        private void ParentWindow_Activated(object? sender, EventArgs e)
        {
            if (_parentWindow == null) return;

            // Don't do any z-order work when pop quiz is open, or while a game host owns the
            // screen (raising the attached tube would float it over DTRH/Intake).
            if ((PopQuizWindow.IsOpen || QuizWindow.IsOpen)) return;
            // Activating main raises its whole owned GROUP, and Win32 defines no order between two
            // windows that share an owner — so the tube can come up alongside (or over) the host
            // without any raise of our own. Push it back to main's level instead of just standing
            // down. SinkToMainZOrder self-marshals; this handler runs on the PARENT thread.
            if (SuppressSelfRaiseForGameHost) { SinkToMainZOrder(); return; }

            try
            {
                CaptureParentGeom();
                bool ok = _parentWindow.WindowState != WindowState.Minimized && _parentWindow.IsVisible
                    && App.Settings?.Current?.AvatarEnabled == true;
                if (!ok) return;
                RunOnAvatar(() =>
                {
                    Show();
                    UpdatePosition();
                    // Z-order: native ownership keeps the tube above main automatically.
                });
            }
            catch { /* Window may be closing */ }
        }

        private void ParentWindow_Closed(object? sender, EventArgs e)
        {
            if (_isAttached)
            {
                // Attached mode: close the tube with the main window (Close must run on the avatar thread)
                RunOnAvatar(() => { try { Close(); } catch { /* Already closing */ } });
            }
            else
            {
                // Detached mode: keep floating independently
                App.Logger?.Information("Main window closed while detached - tube continues floating");
                // Wrap in try-catch in case app is shutting down
                try
                {
                    if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                    {
                        Giggle("Main window closed! Right-click to dismiss~");
                    }
                }
                catch { /* App shutting down */ }
            }
        }

        // ============================================================
        // PUBLIC METHODS
        // ============================================================

        public void ShowTube()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ShowTube)); return; }
            try
            {
                // Never raise an attached tube while a game host owns the screen (it would float
                // over DTRH/Intake); the game-end restore path re-shows it once the host is gone.
                // Take the opportunity to push it back down: whatever asked for this show (a bark,
                // a tray restore, a session event) usually follows something that reordered us.
                if (SuppressSelfRaiseForGameHost) { SinkToMainZOrder(); return; }

                // Manual/explicit show (checkbox toggle, tray "Wake Bambi Up", session events)
                // is a deliberate user/system request to make the avatar visible, so clear the
                // fullscreen-hidden flag. Otherwise IsAvatarVisibleOnScreen and the fullscreen
                // timer would still think we're hidden and could fight this show.
                _hiddenForFullscreen = false;

                // When attached, only show if our process owns the foreground window
                if (_isAttached && _parentWindow != null)
                {
                    var foreground = GetForegroundWindow();
                    if (foreground != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(foreground, out uint foregroundPid);
                        uint ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                        if (foregroundPid != ourPid)
                            return; // Don't show tube if our app isn't in front
                    }
                }

                Show();

                // Only update position if parent is visible
                if (_parentWindow != null && ParentVisibleNotMinimized)
                {
                    UpdatePosition();
                }

                StartFloatingAnimation();

                // Ensure TOOLWINDOW style is applied when attached
                if (_isAttached)
                {
                    SetToolWindowStyle(true);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Error showing tube: {Error}", ex.Message);
            }
        }

        public void HideTube()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(HideTube)); return; }
            Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _poseTimer?.Stop();
                _fullscreenCheckTimer?.Stop();
                StopFloatingAnimation();

                // Stop companion timers
                _speechTimer?.Stop();
                _speechDelayTimer?.Stop();
                _idleTimer?.Stop();
                _triggerTimer?.Stop();
                _randomBubbleTimer?.Stop();

                // Stop voice line audio
                StopVoiceLineAudio();

                // Release GIF animation frames to prevent memory leak
                AnimationBehavior.SetSourceUri(ImgAvatarAnimated, null);

                // Remove the window message hook (DPI-transition quiesce only).
                _hwndSource?.RemoveHook(WndProc);
                _hwndSource = null;

                // Unsubscribe from video service events
                if (App.Video != null)
                {
                    App.Video.VideoAboutToStart -= OnVideoAboutToStart;
                    App.Video.VideoEnded -= OnVideoEnded;
                }

                // LockCardCompleted is owned by BarkService now (no self-subscription to remove).

                // Unsubscribe from game events
                if (App.BubbleCount != null)
                {
                    App.BubbleCount.GameCompleted -= OnGameCompleted;
                    App.BubbleCount.GameFailed -= OnGameFailed;
                }

                // Unsubscribe from flash events
                if (App.Flash != null)
                {
                    App.Flash.FlashAboutToDisplay -= OnFlashAboutToDisplay;
                    App.Flash.FlashClicked -= OnFlashClicked;
                    App.Flash.FlashAudioPlaying -= OnFlashAudioPlaying;
                }

                // Unsubscribe from bubble events
                if (App.Bubbles != null)
                {
                    App.Bubbles.OnBubblePopped -= OnBubblePopped;
                    App.Bubbles.OnBubbleMissed -= OnBubbleMissed;
                }

                // Unsubscribe from achievement events
                if (App.Achievements != null)
                {
                    App.Achievements.AchievementUnlocked -= OnAchievementUnlocked;
                }

                // Unsubscribe from progression events
                if (App.Progression != null)
                {
                    App.Progression.LevelUp -= OnLevelUp;
                }

                // Unsubscribe from window awareness events
                if (App.WindowAwareness != null)
                {
                    App.WindowAwareness.ActivityChanged -= OnActivityChanged;
                    App.WindowAwareness.StillOnActivity -= OnStillOnActivity;
                }

                // Unsubscribe from MindWipe events
                if (App.MindWipe != null)
                {
                    App.MindWipe.MindWipeTriggered -= OnMindWipeTriggered;
                }

                // Unsubscribe from BrainDrain events
                if (App.BrainDrain != null)
                {
                    App.BrainDrain.BrainDrainTriggered -= OnBrainDrainTriggered;
                }

                // Unsubscribe from engine stop event
                if (_parentWindow is MainWindow mainWindow)
                {
                    mainWindow.EngineStopped -= OnEngineStopped;
                }

                if (_parentWindow != null)
                {
                    _parentWindow.LocationChanged -= ParentWindow_PositionChanged;
                    _parentWindow.SizeChanged -= ParentWindow_PositionChanged;
                    _parentWindow.StateChanged -= ParentWindow_StateChanged;
                    _parentWindow.IsVisibleChanged -= ParentWindow_IsVisibleChanged;
                    _parentWindow.Activated -= ParentWindow_Activated;
                    _parentWindow.Closed -= ParentWindow_Closed;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Error during tube window cleanup: {Error}", ex.Message);
            }

            base.OnClosed(e);
        }

        /// <summary>
        /// Called by <see cref="Services.Chaos.ChaosModeService"/> around a run. A chaos run blankets the
        /// screen with TOPMOST windows (the FX vignette, payload washes, effect bubbles); an ATTACHED tube
        /// lives in the non-topmost band, so its speech bubbles get buried under that layer. Rather than
        /// fight the z-order, we simply auto-detach for the run — detached mode is a self-contained topmost
        /// widget that stays visible on top — and re-attach when the run ends. Only auto-restores if WE
        /// detached it (an already-detached avatar is left as the user set it).
        ///
        /// Story mode renders its OWN characters (backdrop + the Madam): the floating companion would
        /// clutter the immersive scene, so we hide it for the run. Free Desktop has no story characters —
        /// the companion IS the on-screen character, so keep it floating over the desktop.
        /// </summary>
        public void SetChaosRunActive(bool active)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetChaosRunActive(active))); return; }
            if (_chaosRunActive == active) return;
            _chaosRunActive = active;
            try
            {
                if (active)
                {
                    // Story mode renders its OWN characters (backdrop + the Madam): the floating
                    // companion would clutter the immersive scene, so hide it for the run. BUT Story is
                    // currently disabled (StoryModeEnabled=false) and ActiveMode DEFAULTS to Story until
                    // BeginRun resolves it — and the hub opens (and calls this) BEFORE BeginRun runs. So
                    // gate the hide on StoryModeEnabled; otherwise the hub-open call hid the companion in
                    // Free Desktop and the (already-active) run-start call no-op'd, leaving it gone.
                    if (Services.Chaos.ChaosModeService.StoryModeEnabled
                        && Services.Chaos.ChaosModeService.ActiveMode == Services.Chaos.ChaosPlayMode.Story)
                    {
                        _hiddenForChaos = Visibility == Visibility.Visible;
                        if (_hiddenForChaos) Visibility = Visibility.Hidden;
                        return;
                    }

                    // Free Desktop: detach so the companion floats above the run as a topmost widget,
                    // parked in the BOTTOM-LEFT corner so the run's HUD/sidebar controls stay clear.
                    // Re-attach afterwards only if it was attached to begin with.
                    if (_isAttached)
                    {
                        _reattachAfterChaos = true;
                        Detach(silent: true);
                        try
                        {
                            // SystemParameters.WorkArea is the primary screen's work area in DIPs (the
                            // same space as Window.Left/Top) — DPI-correct, and Free Desktop chaos runs
                            // on the primary screen anyway.
                            var wa = SystemParameters.WorkArea;
                            const double margin = 12;
                            Left = wa.Left + margin;
                            Top = wa.Bottom - Math.Max(120, ActualHeight) - margin;
                        }
                        catch { /* positioning is best-effort */ }
                        // Put the freshly-parked widget back in the topmost band. NOT above the
                        // compositor host any more (#776) — the host's fullscreen effects, the pink
                        // tint above all, now win over the companion; ReassertTopmost slots the tube
                        // directly beneath the host, which still leaves it over the run's own chrome.
                        ReassertTopmost();
                    }
                }
                else
                {
                    // Restore from a Story-run hide (only if WE hid it).
                    if (_hiddenForChaos)
                    {
                        _hiddenForChaos = false;
                        Visibility = Visibility.Visible;
                    }
                    if (_reattachAfterChaos)
                    {
                        _reattachAfterChaos = false;
                        Attach(silent: true);
                    }
                }
            }
            catch { /* window may be tearing down */ }
        }

        // ============================================================
        // AVATAR "I'LL POP THIS ONE FOR YOU" EASTER EGG
        // ============================================================
        // BubbleService drives the choreography; these three members are its hooks. Movement runs in
        // PHYSICAL pixels via SetWindowPos (the same space as Screen.WorkingArea) so it stays correct
        // across monitors and mixed DPI — WPF's Left/Top DIP space is non-linear across screens.

        private bool _eggWasAttached;
        private RECT _eggReturnRect;
        private bool _eggReturnValid;

        /// <summary>True when the companion can be sent on the bubble-pop easter egg: enabled, shown,
        /// not hidden for a fullscreen app, with a live handle, and not mid-conversation.</summary>
        public bool CanPerformBubbleEgg =>
            App.Settings?.Current?.AvatarEnabled == true
            && !_hiddenForFullscreen && !_hiddenForChaos
            && Visibility == Visibility.Visible
            && _tubeHandle != IntPtr.Zero
            && !IsCompanionBusy(0);

        /// <summary>Send the companion gliding beside a bubble (physical-px centre + pop radius). Captures
        /// restore state and auto-detaches if attached. Smooth ~0.4s lerp; cancellable.</summary>
        public async Task GlideToBubbleAsync(System.Windows.Point centerPx, double radiusPx, CancellationToken ct)
        {
            if (!Dispatcher.CheckAccess())
            { await Dispatcher.InvokeAsync(() => GlideToBubbleAsync(centerPx, radiusPx, ct)).Task.Unwrap(); return; }

            _eggWasAttached = _isAttached;
            _eggReturnValid = _tubeHandle != IntPtr.Zero && GetWindowRect(_tubeHandle, out _eggReturnRect);
            if (_isAttached) Detach(silent: true);
            ReassertTopmost();
            if (_tubeHandle == IntPtr.Zero || !GetWindowRect(_tubeHandle, out var cur)) return;

            int w = cur.Right - cur.Left, h = cur.Bottom - cur.Top;
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)centerPx.X, (int)centerPx.Y));
            var wa = screen.WorkingArea;
            const int gap = 24;
            // Sit on whichever side of the bubble has more room: left of it if the bubble hugs the
            // right half of its screen, else to its right. Never cover the bubble itself.
            bool placeLeft = centerPx.X > wa.Left + wa.Width / 2.0;
            int targetX = placeLeft
                ? (int)(centerPx.X - radiusPx - gap - w)
                : (int)(centerPx.X + radiusPx + gap);
            int targetY = (int)(centerPx.Y - h / 2.0);
            targetX = Math.Clamp(targetX, wa.Left, Math.Max(wa.Left, wa.Right - w));
            targetY = Math.Clamp(targetY, wa.Top, Math.Max(wa.Top, wa.Bottom - h));

            await GlideToAsync(cur.Left, cur.Top, targetX, targetY, ct);
        }

        /// <summary>Return the companion to where it began: re-attach if it was attached, else glide back
        /// to its captured floating position.</summary>
        public async Task ReturnFromBubbleAsync(CancellationToken ct)
        {
            if (!Dispatcher.CheckAccess())
            { await Dispatcher.InvokeAsync(() => ReturnFromBubbleAsync(ct)).Task.Unwrap(); return; }

            try
            {
                if (_eggWasAttached)
                {
                    Attach(silent: true);   // UpdatePosition snaps it back to the parent
                    return;
                }
                if (_eggReturnValid && _tubeHandle != IntPtr.Zero && GetWindowRect(_tubeHandle, out var cur))
                {
                    await GlideToAsync(cur.Left, cur.Top, _eggReturnRect.Left, _eggReturnRect.Top, CancellationToken.None);
                }
            }
            finally { _eggReturnValid = false; }
        }

        /// <summary>Lerp the window from one physical-px position to another over ~0.4s (SetWindowPos
        /// per step). Ease-in-out for a soft glide; honours cancellation between steps.</summary>
        private async Task GlideToAsync(int fromX, int fromY, int toX, int toY, CancellationToken ct)
        {
            const int steps = 14;
            for (int i = 1; i <= steps; i++)
            {
                if (_tubeHandle == IntPtr.Zero) return;
                double t = i / (double)steps;
                t = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;   // ease-in-out
                int x = (int)Math.Round(fromX + (toX - fromX) * t);
                int y = (int)Math.Round(fromY + (toY - fromY) * t);
                SetWindowPos(_tubeHandle, IntPtr.Zero, x, y, 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                await Task.Delay(28, ct);
            }
        }

        /// <summary>
        /// Reassert topmost status when detached - ensures avatar stays on top as a widget.
        ///
        /// #776: the raise is UNCONDITIONAL (never gated on having lost WS_EX_TOPMOST) but it is no
        /// longer always aimed at HWND_TOPMOST. The shared compositor host carries the pink filter and
        /// re-pins itself to the front of the topmost band every 5s (OverlayService.ReassertZOrder);
        /// this tick fired every 500ms and put the tube back on top, so the tint appeared to blink on
        /// and off the companion. The tint wins: when a visible host covers the tube's monitor we
        /// insert directly BELOW it instead — still topmost, still above every ordinary app window,
        /// permanently under the tint. Runs on the tube's OWN thread (AvatarOwnThread), which is why
        /// the host handle comes from CompositorEngine's lock-free anchor snapshot rather than from
        /// its UI-thread-only host dictionaries.
        /// </summary>
        private void ReassertTopmost()
        {
            if (_tubeHandle == IntPtr.Zero || _isAttached) return;

            // Don't fight with pop quiz for topmost z-order
            if ((PopQuizWindow.IsOpen || QuizWindow.IsOpen)) return;

            var insertAfter = HWND_TOPMOST;
            if (Services.OverlayService.ResolveZOrderAction(
                    hasVideo: false, isVideoWindow: false, aboveVideo: false, needsPin: true, force: true,
                    yieldToCompositorHost: TryGetCompositorHostAnchor(out var hostHwnd))
                == Services.OverlayService.ZOrderAction.PinBelowCompositorHost)
            {
                // Already sitting directly under the host? Then the invariant holds and we skip the
                // call entirely — this is a layered window, and poking WM_WINDOWPOSCHANGING twice a
                // second for nothing is exactly the kind of churn that perturbs the emote crossfades.
                if (GetWindow(hostHwnd, GW_HWNDNEXT) == _tubeHandle) return;
                insertAfter = hostHwnd;
            }

            // Use Win32 SetWindowPos to force the z-order slot. More reliable than WPF's Topmost
            // property across monitor/focus changes; inserting after a topmost host keeps our own
            // WS_EX_TOPMOST set, so the tube never drops out of the widget band.
            SetWindowPos(_tubeHandle, insertAfter, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// The compositor host window the detached tube must sit under, if any: the visible
        /// main-surface host covering the tube's centre point (physical px, the space the anchor
        /// snapshot is published in).
        ///
        /// Deliberately keyed on the HOST EXISTING, not on the tint being active — an empty host is
        /// fully transparent and click-through, so parking below it costs nothing, whereas testing
        /// "is the tint rendering here" would re-introduce the same flip-flop the bug is about
        /// (PinkTintLayer.ShouldRenderOnScreen can also gate the tint off this monitor entirely,
        /// which is exactly a monitor where no host effect can ever cover us anyway).
        ///
        /// A playing mandatory video is left alone: the reconciler pins the hosts BELOW it (#497),
        /// so yielding to a host there would drag the companion under an opaque fullscreen video —
        /// a behaviour change #776 never asked for. In that state the tube keeps its old raise.
        /// </summary>
        private bool TryGetCompositorHostAnchor(out IntPtr hostHwnd)
        {
            hostHwnd = IntPtr.Zero;
            try
            {
                if (App.Compositor is not { } engine) return false;
                if (App.Video?.IsPlaying == true) return false;
                if (!GetWindowRect(_tubeHandle, out var r)) return false;

                int cx = r.Left + (r.Right - r.Left) / 2;
                int cy = r.Top + (r.Bottom - r.Top) / 2;
                var handle = engine.FindHostAnchorAt(cx, cy);
                if (handle == 0) return false;

                hostHwnd = handle;
                return true;
            }
            catch { return false; }   // never let a z-order hint break the tick
        }

        /// <summary>
        /// True when the active mod overrides tube.png but not tube2.png. In that case
        /// the detached state would otherwise mix the mod's avatar with the embedded
        /// default tube2.png — leaving the avatar floating outside the mod's chamber.
        /// We treat this as "use the mod's tube.png and the attached layout" so the
        /// avatar lands inside the chamber the mod author actually drew.
        /// </summary>
        private static bool ModOverridesAttachedTubeOnly()
        {
            return Services.ModResourceResolver.HasModOverride("tube.png")
                && !Services.ModResourceResolver.HasModOverride("tube2.png");
        }

        /// <summary>
        /// Switch between tube.png and tube2.png
        /// </summary>
        public void SetTubeStyle(bool useAlternative)
        {
            try
            {
                // If the active mod only ships a tube.png override, use it in both states
                // so the chamber stays consistent with the mod's art.
                if (useAlternative && ModOverridesAttachedTubeOnly())
                    useAlternative = false;

                var tubeName = useAlternative ? "tube2.png" : "tube.png";
                ImgTubeFrame.Source = Services.ModResourceResolver.ResolveImage(tubeName);
                App.Logger?.Information("Tube style changed to: {Style}", tubeName);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to change tube style");
            }
        }

        // ============================================================
        // DETACH/ATTACH FUNCTIONALITY
        // ============================================================

        /// <summary>
        /// Gets whether the avatar tube is currently detached (floating independently)
        /// </summary>
        public bool IsDetached => !_isAttached;

        /// <summary>
        /// The tube's HWND while ATTACHED, else <see cref="IntPtr.Zero"/>. Used by sibling windows that
        /// share our native owner (the game hosts) to slot themselves ABOVE the attached pair instead of
        /// directly above main - two windows owned by the same HWND have no defined order between them,
        /// which is what let a game window and the tube interleave into a torn composite.
        /// Plain-field reads only: this is called from the MAIN thread while the tube may live on its own
        /// dispatcher thread, so it must never touch a DependencyProperty (use IsWindowVisible on the
        /// handle instead of IsVisible).
        /// </summary>
        internal IntPtr AttachedHandleOrZero => _isAttached ? _tubeHandle : IntPtr.Zero;

        /// <summary>
        /// Gets whether the avatar is currently visible on screen.
        /// Returns false if attached and main window is minimized or not visible.
        /// Returns true if detached (independent widget window).
        /// </summary>
        private bool IsAvatarVisibleOnScreen
        {
            get
            {
                // If avatar is disabled, it's never visible
                if (App.Settings?.Current?.AvatarEnabled != true)
                    return false;

                // Detached mode - avatar is always visible as independent widget
                if (!_isAttached)
                    return true;

                // Attached mode - check parent window visibility
                if (_parentWindow == null)
                    return false;

                // Hidden for fullscreen app
                if (_hiddenForFullscreen)
                    return false;

                // Check window state (cache read — safe on the avatar thread)
                return ParentVisibleNotMinimized;
            }
        }

        /// <summary>
        /// Toggles between attached and detached states
        /// </summary>
        public void ToggleDetached()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ToggleDetached)); return; }
            if (_isAttached)
            {
                Detach();
            }
            else
            {
                Attach();
            }
        }

        /// <summary>
        /// Detach the avatar tube from the main window, making it a free-floating draggable widget.
        /// <paramref name="silent"/> suppresses the "I'm free!" giggle for automatic detaches (e.g. the
        /// auto-detach when a chaos run starts), where a spoken line would be intrusive.
        /// </summary>
        public void Detach(bool silent = false)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Detach(silent))); return; }
            if (!_isAttached) return;

            _isAttached = false;

            // Switch to alternative tube image
            SetTubeStyle(true);

            // Apply tube layout offsets for detached mode
            ApplyTubeLayoutOffsets();

            // Speech bubble stays at same position in both modes (right side of tube, clearly visible)
            if (SpeechBubble.Visibility == Visibility.Visible && !string.IsNullOrEmpty(TxtSpeech.Text))
            {
                AdjustBubbleSize(TxtSpeech.Text);
            }

            // Keep hidden from taskbar and Alt+Tab
            ShowInTaskbar = false;
            SetToolWindowStyle(true);

            // Break the native ownership link — a detached widget floats free of main.
            ApplyNativeOwner(false);

            // Set topmost - use both WPF property and Win32 for reliability
            Topmost = true;
            ReassertTopmost(); // Use Win32 to ensure topmost is applied immediately

            // Show the move cursor only over the draggable avatar visuals (not the transparent
            // dead-zones — see Window_MouseLeftButtonDown, #346). Window cursor stays default.
            AvatarBorder.Cursor = Cursors.SizeAll;
            SpeechBubble.Cursor = Cursors.SizeAll;
            TitleBox.Cursor = Cursors.SizeAll;
            // Let the whole visible tube vessel be grabbed (not just the avatar art) — it's
            // IsHitTestVisible=False in XAML for attached mode; turn it on while detached.
            ImgTubeFrame.IsHitTestVisible = true;
            ImgTubeFrame.Cursor = Cursors.SizeAll;
            MouseLeftButtonDown += Window_MouseLeftButtonDown;

            // Update context menu visibility
            UpdateContextMenuForState();

            App.Logger?.Information("Avatar tube detached - now floating independently");
            if (!silent) Giggle("I'm free! Ctrl+scroll to resize!");

            // Remember the detached state so the companion comes back floating next launch (#669).
            if (!_restoringPlacement)
            {
                try { if (App.Settings?.Current != null) { App.Settings.Current.AvatarTubeDetached = true; App.Settings.Save(); } }
                catch (Exception ex) { App.Logger?.Debug("Avatar tube: persist detached flag failed ({Error})", ex.Message); }
            }
        }

        /// <summary>
        /// Attach the avatar tube back to the main window. <paramref name="silent"/> suppresses the
        /// "Back home~" giggle for automatic re-attaches (e.g. when a chaos run ends).
        /// </summary>
        public void Attach(bool silent = false)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Attach(silent))); return; }
            if (_isAttached) return;

            _isAttached = true;

            // Switch back to original tube image
            SetTubeStyle(false);

            // Apply tube layout offsets for attached mode
            ApplyTubeLayoutOffsets();

            // Restore speech bubble position when attached
            if (SpeechBubble.Visibility == Visibility.Visible && !string.IsNullOrEmpty(TxtSpeech.Text))
            {
                AdjustBubbleSize(TxtSpeech.Text);
            }

            // Hide from taskbar and Alt+Tab when attached
            ShowInTaskbar = false;

            // No longer topmost when attached
            Topmost = false;

            // Disable dragging
            Cursor = Cursors.Arrow;
            AvatarBorder.Cursor = Cursors.Arrow;
            SpeechBubble.Cursor = Cursors.Arrow;
            TitleBox.Cursor = Cursors.Arrow;
            // Restore the attached-mode tube frame (non-interactive, behind everything).
            ImgTubeFrame.IsHitTestVisible = false;
            ImgTubeFrame.Cursor = Cursors.Arrow;
            MouseLeftButtonDown -= Window_MouseLeftButtonDown;

            // Reset scale BEFORE updating position - otherwise position is calculated
            // using the old scaled dimensions from when it was detached
            _currentScale = 1.0;
            try
            {
                // Reset to base calculated size
                ContentViewbox.Width = DesignWidth * _scaleFactor;
                ContentViewbox.Height = DesignHeight * _scaleFactor;
            }
            catch { }
            UpdateLayout(); // Force layout update so ActualWidth/Height reflect new size

            // Snap back to parent window position
            UpdatePosition();

            // Re-own the tube to main: the window manager keeps the pair glued in z-order.
            ApplyNativeOwner(true);

            // Defer the TOOLWINDOW style to ensure it's applied after all window state changes
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SetToolWindowStyle(true);
            }), System.Windows.Threading.DispatcherPriority.Background);

            // Update context menu visibility
            UpdateContextMenuForState();

            App.Logger?.Information("Avatar tube attached - anchored to main window");
            if (!silent) Giggle("Back home~");

            // Persist the attached state so we don't auto-detach next launch (#669).
            if (!_restoringPlacement)
            {
                try { if (App.Settings?.Current != null) { App.Settings.Current.AvatarTubeDetached = false; App.Settings.Save(); } }
                catch (Exception ex) { App.Logger?.Debug("Avatar tube: persist attached flag failed ({Error})", ex.Message); }
            }
        }

        /// <summary>
        /// Handle Ctrl+scroll wheel to resize avatar when detached
        /// </summary>
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                // Only resize when detached and Ctrl is held
                if (_isAttached || !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                    return;

                e.Handled = true;

                // Scroll up = bigger, scroll down = smaller
                if (e.Delta > 0)
                    _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
                else
                    _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);

                ApplyScale();
                // Clamp position after resize to keep avatar visible
                Dispatcher.BeginInvoke(new Action(ClampAvatarPosition), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Mouse wheel resize error: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Handle Up/Down arrow keys to resize avatar when detached
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                // Only resize when detached
                if (_isAttached)
                    return;

                if (e.Key == Key.Up)
                {
                    e.Handled = true;
                    _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
                    ApplyScale();
                    // Clamp position after resize to keep avatar visible
                    Dispatcher.BeginInvoke(new Action(ClampAvatarPosition), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                else if (e.Key == Key.Down)
                {
                    e.Handled = true;
                    _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);
                    ApplyScale();
                    // Clamp position after resize to keep avatar visible
                    Dispatcher.BeginInvoke(new Action(ClampAvatarPosition), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Key resize error: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Apply the current scale to the avatar content
        /// </summary>
        private void ApplyScale()
        {
            try
            {
                if (ContentViewbox == null || !IsLoaded) return;

                // Use Width/Height instead of transforms - much safer with animated GIFs
                // Calculate new size based on current scale factor and user scale
                var newWidth = DesignWidth * _scaleFactor * _currentScale;
                var newHeight = DesignHeight * _scaleFactor * _currentScale;

                ContentViewbox.Width = newWidth;
                ContentViewbox.Height = newHeight;
                // Window follows via the ContentViewbox.SizeChanged handler wired in OnLoaded
                // (auto-sizing is off after first paint — see OnFirstContentRendered).

                // Persist the user's chosen scale (#669). Scale only changes via detached-mode
                // gestures (Ctrl+scroll / menu / arrow keys), so this rides those; guarded so a
                // restore doesn't re-save. Debounced Save coalesces the discrete per-step writes.
                PersistTubePlacement();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("ApplyScale error: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Updates context menu items based on attached/detached state
        /// </summary>
        private void UpdateContextMenuForState()
        {
            if (_isAttached)
            {
                // When attached: show Detach, hide Attach, Dismiss, and resize options
                MenuItemDetach.Visibility = Visibility.Visible;
                MenuItemAttach.Visibility = Visibility.Collapsed;
                MenuItemShrink.Visibility = Visibility.Collapsed;
                MenuItemGrow.Visibility = Visibility.Collapsed;
                MenuItemDismiss.Visibility = Visibility.Collapsed;
            }
            else
            {
                // When detached: hide Detach, show Attach, Dismiss, and resize options
                MenuItemDetach.Visibility = Visibility.Collapsed;
                MenuItemAttach.Visibility = Visibility.Visible;
                MenuItemShrink.Visibility = Visibility.Visible;
                MenuItemGrow.Visibility = Visibility.Visible;
                MenuItemDismiss.Visibility = Visibility.Visible;

                // Update resize button states
                UpdateResizeMenuState();
            }
        }

        /// <summary>
        /// Updates the shrink/grow menu items based on current scale
        /// </summary>
        private void UpdateResizeMenuState()
        {
            // Disable shrink at minimum, grow at maximum
            MenuItemShrink.IsEnabled = _currentScale > MinScale;
            MenuItemGrow.IsEnabled = _currentScale < MaxScale;

            // Show current scale percentage
            int scalePercent = (int)(_currentScale * 100);
            MenuItemShrink.Header = _currentScale > MinScale ? Loc.Get("menu_shrink") : Loc.Get("menu_shrink_min");
            MenuItemGrow.Header = _currentScale < MaxScale ? Loc.Get("menu_grow") : Loc.Get("menu_grow_max");

            // Gray out disabled items
            MenuItemShrink.Foreground = MenuItemShrink.IsEnabled
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.Gray);
            MenuItemGrow.Foreground = MenuItemGrow.IsEnabled
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.Gray);
        }

        // Manual drag tracking
        private bool _isDragging = false;
        private Point _dragStartPoint;
        private double _dragStartLeft;
        private double _dragStartTop;

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window when detached — but only when the click actually lands on a
            // visible part of the avatar (art, speech bubble, or name tag). The window is sized to its
            // content with large transparent margins around the corner-positioned avatar; without this
            // guard those invisible dead-zones to the top/bottom-right were draggable, which felt like
            // a phantom hitbox (#346 — BUG-7KHMJW9CH7).
            if (!_isAttached)
            {
                var hit = e.OriginalSource as DependencyObject;
                bool onAvatar = IsDescendantOf(hit, AvatarBorder)
                                || IsDescendantOf(hit, SpeechBubble)
                                || IsDescendantOf(hit, TitleBox)
                                // The visible tube vessel is draggable too (enabled only when
                                // detached — see Detach). Z-index 0, so it only catches clicks the
                                // avatar/bubble/menu didn't, and the far transparent margins past
                                // the tube image's rect stay non-draggable (#346 dead-zone guard).
                                || IsDescendantOf(hit, ImgTubeFrame);
                if (!onAvatar) return;

                // PointToScreen throws InvalidOperationException while the window's
                // presentation source is torn down/rebuilt — which happens exactly when
                // a drag crosses onto a monitor with a different DPI (#477). Unhandled
                // here it takes down the whole app (possibly from the avatar's own UI
                // thread), so treat a failed conversion as "drag didn't start".
                try
                {
                    _dragStartPoint = PointToScreen(e.GetPosition(this));
                }
                catch (InvalidOperationException ex)
                {
                    App.Logger?.Debug("Avatar drag start: PointToScreen failed ({Error})", ex.Message);
                    return;
                }
                _isDragging = true;
                _dragStartLeft = Left;
                _dragStartTop = Top;
                CaptureMouse();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging && !_isAttached)
            {
                // Same DPI-transition guard as the drag start (#477): skip this move
                // tick if the presentation source is mid-rebuild; the next tick lands.
                try
                {
                    var currentPoint = PointToScreen(e.GetPosition(this));
                    Left = _dragStartLeft + (currentPoint.X - _dragStartPoint.X);
                    Top = _dragStartTop + (currentPoint.Y - _dragStartPoint.Y);
                }
                catch (InvalidOperationException ex)
                {
                    App.Logger?.Debug("Avatar drag move: PointToScreen failed ({Error})", ex.Message);
                }
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                // Remember where the user parked the detached companion (#669). Debounced Save
                // coalesces the write, so persisting on drag-end (not per move tick) is plenty.
                PersistTubePlacement();
            }
        }

        /// <summary>
        /// Save the detached companion's current Left/Top + scale so it returns to the same spot
        /// next launch (#669). No-ops while attached (position is parent-anchored, not user-owned)
        /// and while a restore is in flight (so the restore doesn't immediately re-save itself).
        /// </summary>
        private void PersistTubePlacement()
        {
            if (_isAttached || _restoringPlacement) return;
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                s.AvatarTubeLeft = Left;
                s.AvatarTubeTop = Top;
                s.AvatarTubeScale = _currentScale;
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Avatar tube: persist placement failed ({Error})", ex.Message);
            }
        }

        /// <summary>
        /// Clamps the avatar window position to ensure it stays at least half visible on screen
        /// </summary>
        private void ClampAvatarPosition()
        {
            if (_isAttached) return;

            try
            {
                // Get DPI scale factor for proper coordinate conversion
                var source = PresentationSource.FromVisual(this);
                var dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                // Convert WPF coordinates to physical pixels for screen comparison
                var physicalLeft = Left * dpiScale;
                var physicalTop = Top * dpiScale;
                var physicalWidth = ActualWidth * dpiScale;
                var physicalHeight = ActualHeight * dpiScale;

                // Get the screen that contains most of the avatar (using physical pixel coordinates)
                var screen = System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point(
                        (int)(physicalLeft + physicalWidth / 2),
                        (int)(physicalTop + physicalHeight / 2)));

                var bounds = screen.WorkingArea;

                // Calculate how much of the avatar must remain visible in physical pixels
                var minVisibleWidth = physicalWidth / 2;

                // Calculate allowed bounds in physical pixels
                var minPhysicalLeft = bounds.Left - physicalWidth + minVisibleWidth;
                var maxPhysicalLeft = bounds.Right - minVisibleWidth;
                // Allow avatar to go way off the top - practically no limit
                var minPhysicalTop = bounds.Top - physicalHeight - 1000;
                var maxPhysicalTop = bounds.Bottom - (physicalHeight / 2);

                // Clamp position in physical pixels (only clamp left/right, not top)
                var newPhysicalLeft = Math.Max(minPhysicalLeft, Math.Min(maxPhysicalLeft, physicalLeft));
                // Don't clamp top - allow avatar to go anywhere vertically
                var newPhysicalTop = Math.Min(maxPhysicalTop, physicalTop); // Only prevent going off bottom

                // Convert back to WPF units
                var newLeft = newPhysicalLeft / dpiScale;
                var newTop = newPhysicalTop / dpiScale;

                // Only update if position changed to avoid unnecessary redraws
                if (Math.Abs(newLeft - Left) > 1 || Math.Abs(newTop - Top) > 1)
                {
                    Left = newLeft;
                    Top = newTop;
                }
            }
            catch
            {
                // Ignore errors - position clamping is best-effort
            }
        }

        /// <summary>
        /// Restore the saved companion placement on startup (#669): if it was detached at last exit,
        /// re-detach silently and reapply the saved scale + Left/Top, clamped onto a currently-connected
        /// monitor so a since-disconnected screen can't strand it off-view. No-op when it was attached
        /// (the default) so existing users see no change until they move/detach it themselves.
        /// </summary>
        public void RestoreSavedPlacement()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(RestoreSavedPlacement)); return; }

            var s = App.Settings?.Current;
            if (s == null || !s.AvatarTubeDetached) return; // attached is the default — nothing to restore

            _restoringPlacement = true;
            try
            {
                // Float it free first (silent — no "I'm free!" giggle on a cold boot).
                Detach(silent: true);

                // Reapply the saved scale before positioning so the clamp uses the real footprint.
                if (!double.IsNaN(s.AvatarTubeScale) && s.AvatarTubeScale > 0)
                {
                    _currentScale = Math.Clamp(s.AvatarTubeScale, MinScale, MaxScale);
                    ApplyScale();
                    UpdateLayout(); // so ActualWidth/Height reflect the restored scale for the clamp
                }

                // Reapply the saved position, then clamp onto a live monitor. Guard AllScreens (it can
                // be empty during certain system states) and wrap in try-catch for the mixed-DPI
                // PointToScreen/coordinate hazards this window is prone to (#477).
                if (!double.IsNaN(s.AvatarTubeLeft) && !double.IsNaN(s.AvatarTubeTop))
                {
                    try
                    {
                        if (System.Windows.Forms.Screen.AllScreens.Length > 0)
                        {
                            Left = s.AvatarTubeLeft;
                            Top = s.AvatarTubeTop;
                            ClampAvatarPosition(); // keep at least half of it on a connected screen
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Avatar tube: restore position failed ({Error})", ex.Message);
                    }
                }

                EnsureVisibleWhenDetached();
                App.Logger?.Information("Avatar tube: restored detached placement (scale {Scale:0.00})", _currentScale);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Avatar tube: RestoreSavedPlacement failed ({Error})", ex.Message);
            }
            finally
            {
                _restoringPlacement = false;
            }
        }
    }
}
