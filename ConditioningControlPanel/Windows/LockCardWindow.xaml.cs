using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Speech;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Lock Card window - user must type a phrase multiple times to dismiss
    /// Supports multi-monitor with synced input
    /// </summary>
    public partial class LockCardWindow : Window
    {
        // Per-session config (mutable: a pooled window is reconfigured on every reuse — see Configure).
        private string _phrase = "";
        private int _requiredRepeats;
        private bool _strictMode;
        private bool _voiceMode;   // solve by speaking instead of typing (may fall back mid-session)
        private int _completedRepeats = 0;
        private bool _isCompleted = false;
        private DispatcherTimer? _closeTimer;
        private bool _voiceListening = false;
        private System.Threading.CancellationTokenSource? _voiceCts; // cancels the in-flight recognize on close/panic/privacy
        private bool _evictedAutonomy = false; // we stood the "Hey Bambi" wake/PTT mic down and must restore it

        // Multi-monitor support
        private bool _isPrimary;
        private System.Windows.Forms.Screen? _screen;   // remembered so re-show can reposition on reuse
        private static List<LockCardWindow> _allWindows = new();   // the visible set (drives IsAnyOpen / sync)
        private static string _sharedInput = "";

        // Keep-alive pool. A lock card is a WS_EX_LAYERED/AllowsTransparency window; a FRESH Window.Show()
        // runs a synchronous MediaContext.CompleteRender on first realization, which — under a render thread
        // already backed up by many animating overlays — never returns and wedges the whole UI (the #494
        // freeze; same mechanism dump-confirmed for flashes 2026-07-05). So instead of new/Close per card we
        // realize once, then Hide()/Show() and reconfigure. Hidden instances live here between cards.
        private static readonly Stack<LockCardWindow> _pool = new();
        private const int POOL_MAX = 6;   // enough for a very wide multi-monitor rig; surplus is closed
        private static DispatcherTimer? _deferTimer;   // holds a show while a display change settles
        private static int _deferAttempts = 0;
        private const int DEFER_MAX_ATTEMPTS = 6;      // ~5.4s ceiling — a required card always appears

        private const int WM_DPICHANGED = 0x02E0;
        
        // Achievement tracking
        private static DateTime _startTime;
        private static int _totalErrors = 0;
        private static int _totalCharsTyped = 0;

        // Test mode — no XP or achievements
        private static bool _isTest = false;

        // Win32 focus-stealing support
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_HIDEWINDOW = 0x0080;
        private const uint SWP_ASYNCWINDOWPOS = 0x4000;
        private const uint LWA_ALPHA = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern void DisableProcessWindowsGhosting();

        private IntPtr _hwnd;

        // ── Dead-man's switch ───────────────────────────────────────────────────
        // Every escape hatch this card has (Esc, panic/ForceCloseAll, the completion close timer) runs
        // on the UI dispatcher. If the UI thread wedges while a card is up, the card stays behind as a
        // frozen fullscreen HWND_TOPMOST window that even Task Manager opens BEHIND, and the user's only
        // visible way out is a reboot. A background thread heartbeats the dispatcher while any card is
        // visible; on a sustained wedge it force-drops the cover at the Win32 level (which needs nothing
        // from the hung thread), and if the app still hasn't recovered long after that it terminates the
        // process - a dead app frees the screen, a wedged one holds it hostage.
        private const int WATCHDOG_WEDGE_MS = 10_000;     // unanswered ping ⇒ UI thread considered wedged
        private const int WATCHDOG_FAILFAST_MS = 40_000;  // unanswered ping ⇒ kill the process
        private static readonly object _watchdogLock = new();
        private static System.Threading.Thread? _watchdogThread;
        private static IntPtr[] _watchdogHwnds = Array.Empty<IntPtr>();
        private static volatile int _watchdogOpenCount;
        private static volatile bool _watchdogPongPending;
        private static volatile bool _watchdogDropped;
        // Hwnds hit by EmergencyDrop. SLWA permanently breaks WPF's layered rendering on them, so any
        // window in here must be Closed and never handed back out of the keep-alive pool (a wedged
        // dismissal can pool a dropped window before the Send-priority recovery cleanup runs).
        private static readonly HashSet<IntPtr> _droppedHwnds = new();
        private static bool _ghostingDisabled;

        // Verified on Win11 26200: with ghosting active, one click at a hung fullscreen card makes DWM
        // replace it on screen with a topmost "Ghost" hwnd the emergency drop can't reach - the cover
        // then survives SLWA/SetWindowPos on the real hwnd. Per-process, irreversible, so flip it once
        // before the first card can ever hang.
        private static void DisableGhostingOnce()
        {
            if (_ghostingDisabled) return;
            _ghostingDisabled = true;
            try { DisableProcessWindowsGhosting(); }
            catch (Exception ex) { App.Logger?.Debug("DisableProcessWindowsGhosting failed: {E}", ex.Message); }
        }



        /// <summary>
        /// Check if any lock card window is currently open
        /// </summary>
        // A card is "open" if any window is visible OR a show is deferred waiting on a display change to
        // settle (so a poller like BubbleCountResultWindow doesn't conclude the card closed before it opened).
        public static bool IsAnyOpen() => _allWindows.Count > 0 || _deferTimer != null;

        /// <summary>
        /// Create a lock card window for a specific screen
        /// </summary>
        /// <param name="phrase">The phrase to type</param>
        /// <param name="repeats">Number of times to type it</param>
        /// <param name="strictMode">If true, ESC is disabled</param>
        /// <param name="screen">The screen to show on (null for primary)</param>
        /// <param name="isPrimary">If true, this window handles input</param>
        // One-time shell construction. All per-session state is applied in Configure(), so a single
        // realized instance can be reused for any later card without a fresh Window.Show() on the hot path.
        public LockCardWindow()
        {
            InitializeComponent();

            // When focus is lost, immediately reclaim it using Win32 to prevent keystrokes from leaking
            // into other apps (e.g. Discord). Wired once and unconditionally: it reads the CURRENT
            // session's _isPrimary/_isCompleted/_voiceMode, so it keeps working across reuse (and a
            // hidden pooled window never raises Deactivated, with the IsVisible check as a backstop).
            Deactivated += (s, e) =>
            {
                if (!_isPrimary || _isCompleted) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_isCompleted || !IsVisible) return;
                    if (_hwnd != IntPtr.Zero)
                    {
                        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        SetForegroundWindow(_hwnd);
                    }
                    Activate();
                    FocusInput();
                }), DispatcherPriority.Input);
            };

            // Re-assert full-screen physical-pixel coverage whenever this window is activated. Clicking a
            // card on a second (different-DPI) monitor as it pops was leaving it under-covering the screen
            // (#539): we swallow WM_DPICHANGED to avoid the #494 render-thread deadlock, which also denies
            // WPF the DPI update, so the DIP-computed bounds render at the wrong scale. Re-covering in
            // device pixels here restores full coverage regardless of WPF's (possibly stale) DPI.
            Activated += (s, e) =>
            {
                if (IsVisible) ApplyPhysicalBounds();
            };
        }

        /// <summary>
        /// Apply per-session configuration to this (possibly reused) window: tear down any prior card's
        /// state, set the phrase/mode/colors, and position it on the target screen. Safe to call repeatedly
        /// on a pooled instance — this is what lets us avoid realizing a fresh layered window per card.
        /// </summary>
        private void Configure(string phrase, int repeats, bool strictMode,
            System.Windows.Forms.Screen? screen, bool isPrimary, bool voiceMode)
        {
            // ── reset transient state (a pooled window still carries the previous card's) ──
            _closeTimer?.Stop();
            _closeTimer = null;
            _voiceMode = false;      // force any prior voice loop to fully tear down before we reconfigure
            StopVoiceSolve();
            _completedRepeats = 0;
            _isCompleted = false;
            _sharedInput = "";

            _phrase = phrase;
            _requiredRepeats = repeats;
            _strictMode = strictMode;
            _isPrimary = isPrimary;
            _screen = screen;
            // Voice mode degrades gracefully to typing if the offline engine isn't usable, so the
            // user can never be trapped behind a mic that won't cooperate.
            _voiceMode = voiceMode && App.Speech?.IsAvailable == true && App.Settings.Current.MicConsentGiven;
            if (voiceMode && !_voiceMode)
                App.Logger?.Information("LockCardWindow: voice mode requested but unavailable — falling back to typing");

            // Set the phrase text
            TxtPhrase.Text = phrase;

            // Clear any pulse/shake transform and reset input + panels to the fresh (unsolved) look —
            // a reused window may have been left mid-completion or mid-encouragement.
            CardBorder.RenderTransform = null;
            TxtInput.IsEnabled = true;
            TxtInput.Clear();
            CompletionPanel.Visibility = Visibility.Collapsed;
            TxtHint.Visibility = Visibility.Visible;
            TxtHint.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            TxtVoiceHeard.Text = "I heard: …";

            // Swap the input affordance for the voice panel when solving by voice.
            if (_voiceMode)
            {
                InputBorder.Visibility = Visibility.Collapsed;
                VoicePanel.Visibility = Visibility.Visible;
                TxtTitle.Text = "SAY IT TO UNLOCK";
                VoiceStateBrush.Color = VoicePink;
                if (VoiceLevelFill.RenderTransform is ScaleTransform st) st.ScaleX = 0;
            }
            else
            {
                InputBorder.Visibility = Visibility.Visible;
                VoicePanel.Visibility = Visibility.Collapsed;
                TxtTitle.Text = Loc.Get("label_type_to_unlock_2");
            }

            // Update progress display
            UpdateProgress();

            // Handle strict mode
            TxtStrict.Text = _strictMode ? Loc.Get("label_strict") : "";
            // Esc always works now (even in strict mode) so always show the hint.
            TxtEscHint.Text = Loc.Get("label_press_esc_to_close");

            // Position on screen
            if (screen != null)
            {
                WindowState = WindowState.Normal;
                PositionOnScreen(screen);
            }
            else
            {
                // Default to primary screen, maximized
                WindowState = WindowState.Maximized;
            }

            // Primary owns the keyboard; the other monitors are read-only mirrors (see ApplyInputAffordance).
            ApplyInputAffordance();

            // Apply custom colors from settings
            ApplyColors();
        }

        /// <summary>
        /// Apply the input affordance for this window's CURRENT _isPrimary / _voiceMode: exactly one card
        /// (the primary) owns the keyboard/mic, every other monitor is a read-only mirror that echoes what
        /// the primary types. Single owner of that state so Configure and the #618 late promotion below can
        /// never disagree about whether a given card can be typed into.
        /// </summary>
        private void ApplyInputAffordance()
        {
            if (!_isPrimary)
            {
                TxtInput.IsReadOnly = true;
                TxtInput.Focusable = false;
                TxtHint.Text = Loc.Get("label_input_synced_from_primary_monitor");
                if (_voiceMode) TxtVoiceState.Text = "🎤 Speak on the main monitor";
            }
            else
            {
                TxtInput.IsReadOnly = false;
                TxtInput.Focusable = true;
                if (_voiceMode)
                {
                    TxtVoiceState.Text = "🎤 Listening…";
                    TxtHint.Text = "Say the phrase out loud, clearly.";
                }
                else TxtHint.Text = Loc.Get("label_type_the_phrase_exactly_as_shown_above");
            }
        }

        /// <summary>
        /// Give this card the caret. In voice mode there is no visible textbox, so the Window itself takes
        /// focus (keeps Esc and the key handler live); in typing mode the input does.
        /// </summary>
        private void FocusInput()
        {
            if (_voiceMode) Focus(); else TxtInput.Focus();
        }

        /// <summary>
        /// #618: turn this already-shown mirror into the input owner. Only used when screen enumeration
        /// claimed that NO monitor is Primary — see the promotion block in ShowOnAllMonitors. Restores the
        /// input affordance Configure took away, then grabs foreground/focus exactly like OnShown's primary
        /// path (and starts the voice-solve loop OnShown skipped for a mirror).
        /// Show-path only: it assumes no input has been accepted by this card yet.
        /// </summary>
        private void PromoteToPrimary()
        {
            _isPrimary = true;
            ApplyInputAffordance();

            if (_hwnd != IntPtr.Zero)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetForegroundWindow(_hwnd);
            }
            Activate();
            FocusInput();

            // StartVoiceSolve is self-guarding (_voiceListening / !_voiceMode), so this is safe.
            if (_voiceMode) StartVoiceSolve();
        }

        private void PositionOnScreen(System.Windows.Forms.Screen screen)
        {
            // Use the TARGET monitor's DPI (derived from its bounds), NOT GetDpi(this): a reused/pooled
            // window may currently sit on a different-DPI monitor, and we now swallow WM_DPICHANGED so
            // WPF won't auto-correct the size after the move. Getting the scale right here keeps full-
            // screen coverage on mixed-DPI rigs — critical for a lock that must not leave the desktop
            // reachable around the card.
            var scale = BubbleCountWindow.GetDpiForScreen(screen);
            if (scale <= 0) scale = 1.0;

            // Position window to cover the entire screen
            Left = screen.Bounds.Left / scale;
            Top = screen.Bounds.Top / scale;
            Width = screen.Bounds.Width / scale;
            Height = screen.Bounds.Height / scale;

            // Lock in exact coverage at the OS level (see ApplyPhysicalBounds). The WPF DIP sizing above
            // is enough to realize the window on the right monitor; this guarantees pixel-exact cover even
            // when WPF's per-monitor DPI is stale (we swallow WM_DPICHANGED for the #494 deadlock).
            ApplyPhysicalBounds();
        }

        /// <summary>
        /// Force the window to cover its target monitor exactly, in DEVICE PIXELS, via SetWindowPos.
        /// WPF's DIP-based sizing can under-cover a second monitor on a mixed-DPI rig because we swallow
        /// WM_DPICHANGED (to dodge the #494 render-thread deadlock), leaving WPF at a stale scale. The
        /// dark overlay Grid stretches to fill the client area, so a pixel-exact window rect = full-screen
        /// cover regardless of what DPI WPF thinks it is rendering at (#539).
        /// </summary>
        private void ApplyPhysicalBounds()
        {
            if (_hwnd == IntPtr.Zero || _screen == null) return;
            var b = _screen.Bounds;
            try { SetWindowPos(_hwnd, HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, SWP_SHOWWINDOW); }
            catch (Exception ex) { App.Logger?.Debug("LockCardWindow.ApplyPhysicalBounds: {E}", ex.Message); }
        }

        private void ApplyColors()
        {
            try
            {
                var settings = App.Settings.Current;
                
                // Background
                var bgColor = ParseColor(settings.LockCardBackgroundColor, Color.FromRgb(26, 26, 46));
                CardBackground.Color = bgColor;
                
                // Make the outer background semi-transparent version of card bg
                var outerBg = Color.FromArgb(230, bgColor.R, bgColor.G, bgColor.B);
                BackgroundBrush.Color = outerBg;
                
                // Phrase text color
                var textColor = ParseColor(settings.LockCardTextColor, (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"));
                PhraseBrush.Color = textColor;
                AccentBrush.Color = textColor;
                
                // Input field
                var inputBgColor = ParseColor(settings.LockCardInputBackgroundColor, Color.FromRgb(37, 37, 66));
                InputBackground.Color = inputBgColor;
                
                var inputTextColor = ParseColor(settings.LockCardInputTextColor, Colors.White);
                InputTextBrush.Color = inputTextColor;
                
                // Accent color
                var accentColor = ParseColor(settings.LockCardAccentColor, (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"));
                InputBorderBrush.Color = accentColor;
                ProgressBar.Background = new SolidColorBrush(accentColor);
                
                // Card glow effect
                if (CardBorder.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
                {
                    glow.Color = accentColor;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to apply lock card colors: {Error}", ex.Message);
            }
        }

        private Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return fallback;
                if (!hex.StartsWith("#")) hex = "#" + hex;
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return fallback;
            }
        }

        // Realization setup. OnSourceInitialized fires ONCE, synchronously during the first Show() and
        // before Show() returns, so _hwnd is valid by the time OnShown() runs (even on the first card).
        // Hide()/Show() reuse does NOT re-raise it, so this is strictly one-time.
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;

            // Swallow WM_DPICHANGED: this window's geometry is computed manually per-monitor
            // (PositionOnScreen uses the target monitor's DPI), so WPF's automatic DPI rescale is
            // unwanted — and its OnDpiChanged -> OnResize -> synchronous MediaContext.CompleteRender
            // deadlocks the UI thread on a backed-up render thread (the same #494 mechanism). Fired only
            // on cross-DPI-monitor moves, so dropping it never affects the initial render.
            if (_hwnd != IntPtr.Zero)
                HwndSource.FromHwnd(_hwnd)?.AddHook(HandleDpiChanged);
        }

        // XAML still wires Loaded="Window_Loaded"; keep a no-op so the binding resolves. All realization
        // setup is in OnSourceInitialized; everything per-show is in OnShown().
        private void Window_Loaded(object sender, RoutedEventArgs e) { }

        // WndProc hook: drop WM_DPICHANGED so WPF never runs its auto DPI-rescale (which deadlocks under
        // a backed-up render thread). Mirrors FlashService.SwallowDpiChanged. Instance (not static) so we
        // can re-cover the monitor in device pixels afterward: swallowing alone left WPF at a stale DPI,
        // which under-covered a second monitor on a mixed-DPI rig (#539). Re-assert async so no work runs
        // synchronously inside the message hook.
        private IntPtr HandleDpiChanged(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DPICHANGED)
            {
                handled = true;   // consume it; WPF's HwndTarget never sees the deadlock-prone resize
                if (IsVisible)
                    Dispatcher.BeginInvoke(new Action(ApplyPhysicalBounds), DispatcherPriority.Background);
            }
            return IntPtr.Zero;
        }

        // Runs on EVERY show (fresh or reused) — the foreground grab, focus, log, and voice-solve start
        // that used to live in Window_Loaded. Called from ShowOnAllMonitors after Show().
        private void OnShown()
        {
            // Every card window (primary AND each mirror) must cover its whole monitor — the shrink in
            // #539 was on the SECONDARY screen, so this can't sit behind the primary-only guard below.
            ApplyPhysicalBounds();

            if (!_isPrimary) return;

            if (_hwnd != IntPtr.Zero)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetForegroundWindow(_hwnd);
            }
            Activate();
            FocusInput();

            App.Logger?.Information("Lock Card shown - Phrase: {Phrase}, Repeats: {Repeats}, Strict: {Strict}, Voice: {Voice}, Monitors: {Count}",
                _phrase, _requiredRepeats, _strictMode, _voiceMode, _allWindows.Count);

            // Begin the spoken-solve listen loop on the primary monitor.
            if (_voiceMode) StartVoiceSolve();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Esc always closes the lock card, even in strict mode. Strict mode used to
            // block Esc but that left the panic key (often "1") as the only way out —
            // and "1" can collide with mantra characters, so the user was effectively
            // trapped. Esc is a dedicated exit that won't ever be part of a mantra.
            if (e.Key == Key.Escape && !_isCompleted)
            {
                App.Logger?.Information("Lock Card closed via ESC (strict={Strict})", _strictMode);
                CloseAllWindows();
            }
            
            // Prevent Alt+F4 in strict mode
            if (_strictMode && e.Key == Key.System && e.SystemKey == Key.F4)
            {
                e.Handled = true;
            }
            
            // Prevent Ctrl+C, Ctrl+V, Ctrl+X (no cheating!)
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.C || e.Key == Key.V || e.Key == Key.X || e.Key == Key.A)
                {
                    e.Handled = true;
                }
            }
        }

        private void TxtInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isCompleted || !_isPrimary) return;
            
            var input = TxtInput.Text;
            _sharedInput = input;
            
            // Track characters typed for achievement
            _totalCharsTyped++;
            
            // Check for errors (input doesn't match phrase prefix)
            if (input.Length > 0)
            {
                var expectedPrefix = _phrase.Substring(0, Math.Min(input.Length, _phrase.Length));
                if (!string.Equals(input, expectedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    _totalErrors++;
                }
            }
            
            // Sync to all other windows
            SyncInputToAllWindows(input);
            
            // Check if the input matches the phrase (case-insensitive)
            if (string.Equals(input.Trim(), _phrase, StringComparison.OrdinalIgnoreCase))
            {
                RegisterSuccessfulRepeat();
            }
        }

        /// <summary>
        /// Shared completion step for one correct repeat — used by both the typed and the spoken
        /// solve paths. Always call on the UI thread.
        /// </summary>
        private void RegisterSuccessfulRepeat()
        {
            if (_isCompleted) return;

            _completedRepeats++;
            UpdateProgressOnAllWindows();

            // Clear input for next repeat (no-op/harmless in voice mode)
            TxtInput.Clear();
            _sharedInput = "";
            SyncInputToAllWindows("");

            // Pulse animation on all windows
            PulseAllWindows();

            // Check if completed all repeats
            if (_completedRepeats >= _requiredRepeats)
            {
                CompleteAllWindows();
            }
            else
            {
                // Show encouragement on all windows
                var hint = GetEncouragement();
                SetHintOnAllWindows(hint);
            }
        }

        private void SyncInputToAllWindows(string input)
        {
            foreach (var window in _allWindows)
            {
                if (window != this && !window._isCompleted)
                {
                    window.TxtInput.Text = input;
                }
            }
        }

        private void UpdateProgressOnAllWindows()
        {
            foreach (var window in _allWindows)
            {
                window._completedRepeats = _completedRepeats;
                window.UpdateProgress();
            }
        }

        private void PulseAllWindows()
        {
            foreach (var window in _allWindows)
            {
                window.PulseCard();
            }
        }

        private void SetHintOnAllWindows(string hint)
        {
            foreach (var window in _allWindows)
            {
                window.TxtHint.Text = hint;
                window.TxtHint.Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100));
            }
        }

        private void CompleteAllWindows()
        {
            // Calculate completion time
            var completionTime = (DateTime.Now - _startTime).TotalSeconds;
            
            // Award XP (only once, skip for test lock cards)
            if (!_isTest)
            {
                try
                {
                    var xpAmount = (50 * _requiredRepeats) + 200;
                    if (_strictMode) xpAmount = (int)(xpAmount * 1.5);
                    App.Progression?.AddXP(xpAmount, XPSource.LockCard);
                }
                catch { }

                // Track achievement
                App.Achievements?.TrackLockCardCompletion(completionTime, _totalCharsTyped, _totalErrors, _requiredRepeats);
            }

            App.Logger?.Information("Lock Card completed - {Repeats} repeats in {Time:F1}s with {Errors} errors{Test}",
                _requiredRepeats, completionTime, _totalErrors, _isTest ? " (TEST)" : "");

            if (!_isTest)
                App.LockCard?.NotifyCompleted(_phrase, _totalErrors, _requiredRepeats);

            foreach (var window in _allWindows)
            {
                window._isCompleted = true;
                window.TxtInput.IsEnabled = false;
                window.TxtHint.Visibility = Visibility.Collapsed;
                window.CompletionPanel.Visibility = Visibility.Visible;
            }
            
            // Auto-close after delay
            _closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer?.Stop();
                CloseAllWindows();
            };
            _closeTimer.Start();
        }

        private void UpdateProgress()
        {
            TxtProgress.Text = Loc.GetF("lockcard_progress", _completedRepeats, _requiredRepeats);

            // Update progress bar width based on actual container width
            var progressPercent = (double)_completedRepeats / _requiredRepeats;
            var maxWidth = ProgressBarContainer.ActualWidth > 0 ? ProgressBarContainer.ActualWidth : 200;
            ProgressBar.Width = maxWidth * progressPercent;
        }

        private void PulseCard()
        {
            var animation = new DoubleAnimation
            {
                From = 1.0,
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(150),
                AutoReverse = true
            };
            
            var transform = new ScaleTransform(1, 1);
            CardBorder.RenderTransform = transform;
            CardBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private string GetEncouragement()
        {
            var remaining = _requiredRepeats - _completedRepeats;
            var messages = new[]
            {
                Loc.GetF("lockcard_encourage_1", remaining),
                Loc.GetF("lockcard_encourage_2", remaining),
                Loc.GetF("lockcard_encourage_3", remaining),
                Loc.GetF("lockcard_encourage_4", remaining),
                Loc.GetF("lockcard_encourage_5", remaining)
            };
            
            return messages[_completedRepeats % messages.Length];
        }

        // ── Voice solve (speak the phrase) ─────────────────────────────────────

        private static readonly Color VoicePink = Color.FromRgb(0xFF, 0x69, 0xB4);
        private static readonly Color VoiceGreen = Color.FromRgb(0x00, 0xE6, 0x76);
        private static readonly Color VoiceAmber = Color.FromRgb(0xF0, 0xB4, 0x29);

        private void StartVoiceSolve()
        {
            if (_voiceListening || !_voiceMode) return;
            _voiceListening = true;

            // The mic is single-owner. If the always-on "Hey Bambi" wake/PTT loop is armed it holds the
            // mic forever, so every RecognizePhraseAsync here comes back Unavailable and the card could
            // never be solved by voice. Stand it down for the life of the card; we re-arm it per settings
            // on teardown (mirrors SpeakPromptSession).
            try
            {
                if (App.Autonomy?.UserDrivenVoiceArmed == true)
                {
                    App.Autonomy.StopVoiceInput();
                    _evictedAutonomy = true;
                    App.Logger?.Information("LockCardWindow: claimed mic from Autonomy wake/PTT for voice solve");
                }
            }
            catch (Exception ex) { App.Logger?.Debug("LockCardWindow: evict Autonomy failed: {E}", ex.Message); }

            _voiceCts = new System.Threading.CancellationTokenSource();
            if (App.Speech != null)
            {
                App.Speech.LevelChanged += OnVoiceLevel;
                App.Speech.PartialTranscript += OnVoicePartial;
            }
            _ = RunVoiceSolveLoopAsync(_voiceCts.Token);
        }

        private void StopVoiceSolve()
        {
            if (!_voiceListening) return;
            _voiceListening = false;
            if (App.Speech != null)
            {
                App.Speech.LevelChanged -= OnVoiceLevel;
                App.Speech.PartialTranscript -= OnVoicePartial;
            }
            // Cut any in-flight recognize so the mic closes immediately on close / panic / privacy pill,
            // instead of staying hot until the 10s listen window expires.
            try { _voiceCts?.Cancel(); } catch { }
            try { if (App.Speech?.IsListening == true) App.Speech.StopListening(); } catch { }
            try { _voiceCts?.Dispose(); } catch { }
            _voiceCts = null;
            RestoreAutonomyVoice();
        }

        // Hand the mic back to the "Hey Bambi" wake/PTT loop if we stood it down. Idempotent.
        private void RestoreAutonomyVoice()
        {
            if (!_evictedAutonomy) return;
            _evictedAutonomy = false;
            try { App.Autonomy?.RefreshVoiceInputModes(); }
            catch (Exception ex) { App.Logger?.Debug("LockCardWindow: restore Autonomy failed: {E}", ex.Message); }
        }

        private async Task RunVoiceSolveLoopAsync(System.Threading.CancellationToken ct)
        {
            int consecutiveUnavailable = 0;
            try
            {
                // If we just stood Autonomy down, give its capture session a beat to release the mic
                // before our first listen (mirrors AutonomyService.RequestVoiceCommand).
                if (_evictedAutonomy)
                {
                    for (int i = 0; i < 24 && App.Speech?.IsListening == true && !ct.IsCancellationRequested; i++)
                        await Task.Delay(25, ct);
                }

                while (!_isCompleted && _voiceMode && !ct.IsCancellationRequested)
                {
                    if (App.Speech?.IsAvailable != true)
                    {
                        // Engine/mic vanished mid-session — degrade to typing so we never trap.
                        if (++consecutiveUnavailable > 6) { FallBackToTextMidSession(); break; }
                        await Task.Delay(500, ct);
                        continue;
                    }

                    PhraseResult res;
                    try
                    {
                        res = await App.Speech.RecognizePhraseAsync(
                            _phrase, new RecognizeOptions { Timeout = TimeSpan.FromSeconds(10) }, ct);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { res = PhraseResult.NotAvailable; }

                    if (_isCompleted || !_voiceMode || ct.IsCancellationRequested) break;

                    if (res.Unavailable)
                    {
                        // Mic held by another session (e.g. a wake loop we couldn't evict). If we can
                        // never get it, don't trap the user on a card unsolvable by voice — degrade to
                        // typing after a few tries (the Unavailable path must count toward the fallback).
                        if (++consecutiveUnavailable > 6) { FallBackToTextMidSession(); break; }
                        await Task.Delay(350, ct);
                        continue;
                    }
                    consecutiveUnavailable = 0;
                    SetVoiceLevel(0);

                    if (res.Matched)
                    {
                        SetVoiceState("✓ Yes~", VoiceGreen);
                        RegisterSuccessfulRepeat();
                        if (_isCompleted) break;
                        await Task.Delay(700, ct);
                        SetVoiceState("🎤 Listening…", VoicePink);
                    }
                    else if (!res.LoudEnough && res.Score >= 0.45)
                    {
                        SetVoiceHeard(res.Transcript);
                        SetVoiceState("🔊 Louder…", VoiceAmber);
                        await Task.Delay(800, ct);
                        SetVoiceState("🎤 Listening…", VoicePink);
                    }
                    else if (res.TimedOut && string.IsNullOrWhiteSpace(res.Transcript))
                    {
                        // Pure silence — keep listening without nagging.
                    }
                    else
                    {
                        SetVoiceHeard(res.Transcript);
                        SetVoiceState("✗ Again, slower…", VoiceAmber);
                        await Task.Delay(800, ct);
                        SetVoiceState("🎤 Listening…", VoicePink);
                    }
                }
            }
            catch (OperationCanceledException) { /* cancelled on close / panic / privacy pill */ }
            catch (Exception ex) { App.Logger?.Warning("LockCardWindow: voice solve loop failed: {Error}", ex.Message); }
            finally { StopVoiceSolve(); }
        }

        private void OnVoiceLevel(object? sender, double level) =>
            Dispatcher.BeginInvoke(new Action(() => SetVoiceLevel(level)));

        private void OnVoicePartial(object? sender, string text) =>
            Dispatcher.BeginInvoke(new Action(() => SetVoiceHeard(text)));

        private void SetVoiceLevel(double level)
        {
            if (VoiceLevelFill.RenderTransform is ScaleTransform st)
                st.ScaleX = Math.Min(1.0, Math.Max(0.0, level / 0.2)); // RMS ~0..0.2 -> full bar
        }

        private void SetVoiceHeard(string text) =>
            TxtVoiceHeard.Text = string.IsNullOrWhiteSpace(text) ? "I heard: …" : $"I heard: {text}";

        private void SetVoiceState(string text, Color color)
        {
            TxtVoiceState.Text = text;
            VoiceStateBrush.Color = color;
        }

        /// <summary>Drop back to typed solve if speech dies mid-card, so the user is never stuck.</summary>
        private void FallBackToTextMidSession()
        {
            _voiceMode = false;
            StopVoiceSolve();
            VoicePanel.Visibility = Visibility.Collapsed;
            InputBorder.Visibility = Visibility.Visible;
            TxtTitle.Text = Loc.Get("label_type_to_unlock_2");
            // Re-derive the hint/read-only state from _isPrimary: DisableVoiceForAll runs this on the
            // mirrors too, and they must keep saying "synced from primary" and must not grab focus (#618).
            ApplyInputAffordance();
            if (_isPrimary) FocusInput();
            App.Logger?.Information("LockCardWindow: fell back to typed solve (speech unavailable mid-card)");
        }

        private void CloseAllWindows()
        {
            DismissAll();
        }

        /// <summary>
        /// Stop voice solving on every open lock card (the mic privacy pill): drop each voice-mode
        /// card to typed solve so the microphone closes but the lock still has to be solved. The
        /// card is never force-closed here — that would let the user escape the lock.
        /// </summary>
        public static void DisableVoiceForAll()
        {
            foreach (var window in new List<LockCardWindow>(_allWindows))
            {
                try
                {
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (window._voiceMode) window.FallBackToTextMidSession();
                    }));
                }
                catch { }
            }
        }

        /// <summary>
        /// Dismiss all lock card windows (panic button, engine stop, remote stop, and the normal
        /// post-completion auto-close). Windows are hidden and returned to the keep-alive pool rather
        /// than closed, so the next card reuses a pre-realized layered window instead of realizing a
        /// fresh one on the hot path — the fix for the #494 render-thread-deadlock freeze.
        /// </summary>
        public static void ForceCloseAll() => DismissAll();

        private static void DismissAll()
        {
            // Cancel any pending deferred show so a card can't pop up after "stop everything".
            _deferTimer?.Stop();
            _deferTimer = null;
            _deferAttempts = 0;

            // Copy to avoid modification during iteration.
            var windows = new List<LockCardWindow>(_allWindows);
            _allWindows.Clear();

            foreach (var window in windows)
            {
                try { window.DismissToPool(); } catch { }
            }

            UpdateWatchdogSnapshot();   // visible set is now empty - stand the dead-man's switch down

            // Notify InteractionQueue that lock card is complete (triggers queued items).
            // Guarded on the slot actually being ours: engine stop calls this as blanket
            // cleanup, and an unconditional Complete(LockCard) here cleared whatever WAS
            // current (e.g. an in-flight Video), letting the session summary race the video
            // teardown (#462). The current-interaction check also dedups against OnClosing's
            // last-window release (whichever runs first wins; the other sees a foreign slot).
            if (windows.Count > 0 &&
                App.InteractionQueue?.CurrentInteraction == Services.InteractionQueueService.InteractionType.LockCard)
            {
                App.InteractionQueue.Complete(Services.InteractionQueueService.InteractionType.LockCard);
            }
        }

        // Hide this window and return it to the keep-alive pool for reuse. Full teardown of voice + timers
        // so no zombie loop survives; the window is deliberately NOT closed (closing would force a fresh
        // layered-window realization — and its CompleteRender — on the next card's hot path).
        private void DismissToPool()
        {
            _isCompleted = true;   // allow dismissal even in strict mode; also unblocks the voice loop guard
            _closeTimer?.Stop();
            _closeTimer = null;
            _voiceMode = false;    // drop voice so RunVoiceSolveLoopAsync's `while (!_isCompleted && _voiceMode)` exits
            StopVoiceSolve();
            try { Hide(); } catch { }
            ReturnToPool();
        }

        // True if this window was hit by EmergencyDrop — its layered rendering is permanently broken
        // (see EmergencyDrop) and it must be destroyed, never pooled or rented.
        private bool IsDroppedShell()
        {
            if (_hwnd == IntPtr.Zero) return false;
            lock (_watchdogLock) return _droppedHwnds.Contains(_hwnd);
        }

        private void ReturnToPool()
        {
            if (IsDroppedShell())
            {
                // SLWA-poisoned by an emergency drop: WPF can never render this window again. Pooling
                // it would hand a later card an invisible shell that still steals keyboard focus.
                App.Logger?.Warning("LockCardWindow: refusing to pool an emergency-dropped window - closing it");
                try { _isCompleted = true; Close(); } catch { }
                return;
            }
            if (_pool.Count < POOL_MAX)
            {
                if (!_pool.Contains(this)) _pool.Push(this);
            }
            else
            {
                // Over the cap (e.g. a wide multi-monitor rig shrank): close the surplus so hidden windows
                // don't leak. This Close() is on the dismissal path, never a card show, so it's off the
                // deadlock-prone hot path.
                try { _isCompleted = true; Close(); } catch { }
            }
        }

        // ── Dead-man's switch implementation ───────────────────────────────────

        // Refresh the watchdog's thread-safe view of the visible set. UI thread only (it reads
        // _allWindows); call whenever the visible set changes.
        private static void UpdateWatchdogSnapshot()
        {
            lock (_watchdogLock)
            {
                var hwnds = new List<IntPtr>(_allWindows.Count);
                foreach (var w in _allWindows)
                    if (w._hwnd != IntPtr.Zero) hwnds.Add(w._hwnd);
                _watchdogHwnds = hwnds.ToArray();
                _watchdogOpenCount = hwnds.Count;
                if (_watchdogOpenCount > 0 && (_watchdogThread == null || !_watchdogThread.IsAlive))
                {
                    _watchdogThread = new System.Threading.Thread(WatchdogLoop)
                    {
                        IsBackground = true,
                        Name = "LockCardDeadManSwitch"
                    };
                    _watchdogThread.Start();
                }
            }
        }

        private static void WatchdogLoop()
        {
            long pingSentAt = 0;
            while (true)
            {
                System.Threading.Thread.Sleep(1000);

                if (_watchdogOpenCount == 0)
                {
                    // No card up: idle. Reset so a stale ping from a previous card can't count as a wedge.
                    _watchdogPongPending = false;
                    _watchdogDropped = false;
                    continue;
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                if (!_watchdogPongPending)
                {
                    // Previous ping was answered ⇒ the UI thread is (or is back) alive.
                    _watchdogDropped = false;
                    _watchdogPongPending = true;
                    pingSentAt = Environment.TickCount64;
                    try
                    {
                        dispatcher.BeginInvoke(new Action(() => _watchdogPongPending = false),
                            DispatcherPriority.Input);
                    }
                    catch { _watchdogPongPending = false; }
                    continue;
                }

                var stalled = Environment.TickCount64 - pingSentAt;
                if (stalled < WATCHDOG_WEDGE_MS) continue;

                if (!_watchdogDropped)
                {
                    _watchdogDropped = true;
                    IntPtr[] hwnds;
                    lock (_watchdogLock)
                    {
                        hwnds = _watchdogHwnds;
                        // Mark them poisoned BEFORE dropping: if the wedged dispatcher item itself
                        // pools these windows when it resumes, ReturnToPool must already know to
                        // Close them instead (the Send-priority cleanup can't preempt that item).
                        foreach (var h in hwnds) _droppedHwnds.Add(h);
                    }
                    App.Logger?.Warning(
                        "LockCardWindow: UI thread unresponsive for {Ms}ms with {N} lock card(s) covering the screen - force-dropping the topmost cover",
                        stalled, hwnds.Length);
                    // Sacrificial thread: if any user32 call unexpectedly blocks on the hung owner
                    // thread, the watchdog itself must stay alive to reach the fail-fast rung.
                    new System.Threading.Thread(() => EmergencyDrop(hwnds)) { IsBackground = true }.Start();
                    // Queued at Send so it runs the moment the dispatcher recovers, before the pong:
                    // the dropped windows are unusable (see EmergencyDrop) and must be torn down.
                    try { dispatcher.BeginInvoke(new Action(EmergencyRecoveryCleanup), DispatcherPriority.Send); }
                    catch { }
                }
                else if (stalled >= WATCHDOG_FAILFAST_MS && _watchdogOpenCount > 0)
                {
                    App.Logger?.Fatal(
                        "LockCardWindow: UI thread still wedged {Ms}ms after the emergency drop - terminating so the screen is freed",
                        stalled);
                    System.Threading.Thread.Sleep(500);   // let the file sink write the line above
                    Environment.FailFast(
                        "LockCardWindow dead-man's switch: UI thread wedged with a fullscreen topmost lock card up");
                }
            }
        }

        // Off-UI-thread removal of the fullscreen cover. Two rungs, neither needs the hung thread to run:
        // the SWP_ASYNCWINDOWPOS de-topmost+hide is POSTED to the owner thread (applies the instant it
        // recovers), while SetLayeredWindowAttributes takes effect immediately at the compositor level -
        // alpha 0 makes the frozen cover invisible AND click-through (layered windows don't hit-test
        // transparent pixels), so Task Manager is visible and usable again. Side effect: SLWA switches
        // the window out of UpdateLayeredWindow mode, so WPF can never render it again - recovery must
        // Close() these windows, never pool them.
        private static void EmergencyDrop(IntPtr[] hwnds)
        {
            foreach (var h in hwnds)
            {
                try
                {
                    SetWindowPos(h, HWND_NOTOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_HIDEWINDOW | SWP_ASYNCWINDOWPOS);
                    SetLayeredWindowAttributes(h, 0, 0, LWA_ALPHA);
                }
                catch { }
            }
            App.Logger?.Information("LockCardWindow: emergency drop applied to {N} window(s)", hwnds.Length);
        }

        // Runs on the dispatcher once it recovers from a wedge that triggered an emergency drop.
        private static void EmergencyRecoveryCleanup()
        {
            // This runs ON the dispatcher, so the UI thread is provably alive again: acknowledge the
            // outstanding ping and clear the drop flag NOW. Without this, a recovery landing near the
            // 40s deadline (with a wedge-parked card request re-arming the count before the lower-
            // priority pong drains) could FailFast a healthy app off the stale wedge clock.
            _watchdogPongPending = false;
            _watchdogDropped = false;
            try
            {
                _deferTimer?.Stop();
                _deferTimer = null;
                _deferAttempts = 0;

                var windows = new List<LockCardWindow>(_allWindows);
                _allWindows.Clear();
                App.Logger?.Warning(
                    "LockCardWindow: UI thread recovered after emergency drop - closing {N} dropped card(s) (not poolable: SLWA broke their layered rendering)",
                    windows.Count);
                foreach (var w in windows)
                {
                    try
                    {
                        w._isCompleted = true;   // let OnClosing through even in strict mode
                        w._voiceMode = false;
                        w.StopVoiceSolve();
                        w._closeTimer?.Stop();
                        w._closeTimer = null;
                        w.Close();
                    }
                    catch { }
                }

                // A wedged dismissal may have pooled dropped windows before this cleanup could run
                // (a dispatcher item can't be preempted): sweep the pool for poisoned shells too.
                if (_pool.Count > 0)
                {
                    var kept = new List<LockCardWindow>(_pool.Count);
                    while (_pool.Count > 0)
                    {
                        var w = _pool.Pop();
                        if (w == null) continue;
                        if (w.IsDroppedShell())
                        {
                            App.Logger?.Warning("LockCardWindow: closing an emergency-dropped shell found in the pool");
                            try { w._isCompleted = true; w.Close(); } catch { }
                        }
                        else kept.Add(w);
                    }
                    for (int i = kept.Count - 1; i >= 0; i--) _pool.Push(kept[i]);
                }

                if (windows.Count > 0 &&
                    App.InteractionQueue?.CurrentInteraction == Services.InteractionQueueService.InteractionType.LockCard)
                {
                    App.InteractionQueue.Complete(Services.InteractionQueueService.InteractionType.LockCard);
                }
            }
            finally { UpdateWatchdogSnapshot(); }
        }

        // Take a window from the keep-alive pool, or realize a new one on a pool miss (the first card of a
        // session, or more monitors than we've pooled). Only the miss path pays the one-time realization.
        private static LockCardWindow RentWindow()
        {
            while (_pool.Count > 0)
            {
                var w = _pool.Pop();
                if (w == null) continue;
                // A dropped shell that slipped into the pool (wedged dismissal racing the recovery
                // cleanup) is unrenderable — destroy it and keep looking.
                if (w.IsDroppedShell())
                {
                    App.Logger?.Warning("LockCardWindow: rented an emergency-dropped shell - closing it and renting another");
                    try { w._isCompleted = true; w.Close(); } catch { }
                    continue;
                }
                return w;
            }
            return new LockCardWindow();
        }

        // Drop a window from the pool (it's being genuinely closed). Stack has no Remove, so rebuild.
        private static void RemoveFromPool(LockCardWindow target)
        {
            if (_pool.Count == 0 || !_pool.Contains(target)) return;
            var kept = new List<LockCardWindow>(_pool.Count);
            while (_pool.Count > 0)
            {
                var w = _pool.Pop();
                if (w != target) kept.Add(w);
            }
            // Restore original top-of-stack order.
            for (int i = kept.Count - 1; i >= 0; i--) _pool.Push(kept[i]);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // In strict mode, only allow closing if completed
            if (_strictMode && !_isCompleted)
            {
                e.Cancel = true;
                ShakeCard();
                return;
            }
            
            _closeTimer?.Stop();
            // Stop the voice-solve loop for good: StopVoiceSolve() only clears _voiceListening, but the
            // loop's condition is `while (!_isCompleted && _voiceMode)`. Closing an unsolved card (legal
            // in non-strict mode) would otherwise leave a zombie loop re-grabbing the mic every ~10s and
            // writing UI state to a dead window. Mirror FallBackToTextMidSession and drop _voiceMode.
            _voiceMode = false;
            StopVoiceSolve();
            _allWindows.Remove(this);
            UpdateWatchdogSnapshot();
            // Un-mark a dropped shell as it is destroyed: the OS may recycle this hwnd value for a
            // future window, which must not inherit the "poisoned" verdict.
            if (_hwnd != IntPtr.Zero)
                lock (_watchdogLock) { _droppedHwnds.Remove(_hwnd); }
            // If this window is being genuinely closed (app shutdown, Alt+F4), make sure it can't be
            // handed back out of the keep-alive pool as a dead shell.
            RemoveFromPool(this);
            // Non-strict cards can be closed without completing (Alt+F4, titlebar) — if this
            // was the last one, release the interaction slot or videos/lock cards stay blocked
            // for the 5-minute stuck timer. Guarded on CurrentInteraction so a close arriving
            // after something else became current can't clear the wrong slot (#462).
            if (_allWindows.Count == 0 &&
                App.InteractionQueue?.CurrentInteraction == Services.InteractionQueueService.InteractionType.LockCard)
            {
                App.InteractionQueue.Complete(Services.InteractionQueueService.InteractionType.LockCard);
            }
            base.OnClosing(e);
        }

        private void ShakeCard()
        {
            var animation = new DoubleAnimation
            {
                From = -10,
                To = 10,
                Duration = TimeSpan.FromMilliseconds(50),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };
            
            animation.Completed += (s, e) =>
            {
                CardBorder.RenderTransform = null;
            };
            
            var transform = new TranslateTransform();
            CardBorder.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        /// <summary>
        /// Create lock card windows for all monitors
        /// </summary>
        public static void ShowOnAllMonitors(string phrase, int repeats, bool strictMode, bool isTest = false, bool voiceMode = false)
        {
            // A display topology / DPI change is mid-flight: realizing or juggling layered windows during
            // that volatile ~900ms window is exactly what backs up the render thread and wedges the UI.
            // Defer briefly rather than drop — a lock card is a required interaction, not a transient
            // effect — and bound the retries so the card always appears even if changes keep firing.
            if (DisplayChangeCoordinator.SpawnsSuppressed && _deferAttempts < DEFER_MAX_ATTEMPTS)
            {
                _deferAttempts++;
                _deferTimer?.Stop();
                _deferTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                _deferTimer.Tick += (s, e) =>
                {
                    _deferTimer?.Stop();
                    _deferTimer = null;
                    ShowOnAllMonitors(phrase, repeats, strictMode, isTest, voiceMode);
                };
                _deferTimer.Start();
                App.Logger?.Debug("LockCardWindow: display change in progress — deferring card (attempt {N})", _deferAttempts);
                return;
            }
            _deferTimer?.Stop();
            _deferTimer = null;
            _deferAttempts = 0;

            // Defensive: hide + pool any still-visible cards from a prior show before starting a new one.
            // The interaction queue normally serializes lock cards, but a stray overlap must never leak a
            // visible window (it would sit on screen forever, un-dismissable). Does NOT release the queue
            // slot — we're reusing it for the new card.
            if (_allWindows.Count > 0)
            {
                foreach (var w in new List<LockCardWindow>(_allWindows))
                {
                    try { w.DismissToPool(); } catch { }
                }
            }
            _allWindows.Clear();
            _sharedInput = "";
            // Refresh the switch NOW: the early returns below (no screens) must not leave it armed
            // on the stale snapshot of the just-pooled (hidden) windows — a later unrelated UI stall
            // would "drop" pooled shells and could even FailFast with nothing on screen.
            UpdateWatchdogSnapshot();

            // Reset achievement tracking
            _startTime = DateTime.Now;
            _totalErrors = 0;
            _totalCharsTyped = 0;
            _isTest = isTest;

            // Hung fullscreen-topmost covers must never be replaced by a DWM ghost: the ghost copies
            // the cover's rect and topmost band but is a separate hwnd the emergency drop can't touch,
            // so with ghosting on, one click at a frozen card keeps the screen covered until FailFast.
            DisableGhostingOnce();

            var screens = App.GetAllScreensCached();
            if (screens.Length == 0)
            {
                App.Logger?.Warning("LockCardWindow: No screens available");
                return;
            }

            LockCardWindow? primaryWindow = null;

            foreach (var screen in screens)
            {
                // Exactly ONE card may own the keyboard (input syncs primary -> mirrors). A stale screen
                // cache can report Primary on more than one entry after a topology change; taking only the
                // first keeps the single-writer invariant instead of double-counting repeats (#618).
                var isPrimary = screen.Primary && primaryWindow == null;
                // Reuse a pre-realized shell from the pool (or realize one on a miss). Reusing means the
                // Show() below re-shows an existing layered window instead of realizing a fresh one —
                // no synchronous CompleteRender on the hot path, so no render-thread deadlock (#494).
                var window = RentWindow();
                window.Configure(phrase, repeats, strictMode, screen, isPrimary, voiceMode);
                _allWindows.Add(window);

                if (isPrimary)
                {
                    primaryWindow = window;
                }

                // Arm the dead-man's switch INSIDE the loop, around each Show: the most-documented
                // wedge site is the show sequence itself (#494's synchronous CompleteRender on a fresh
                // realization), and by iteration N the earlier monitors' cards are already fullscreen
                // topmost. Pre-Show covers pooled reuse (hwnd already valid); post-Show covers a fresh
                // window whose hwnd materialized inside Show(). Windows without an hwnd yet are skipped
                // by the snapshot, so the pre-Show call is safe on a pool miss.
                UpdateWatchdogSnapshot();
                window.Show();
                window.OnShown();   // per-show foreground/focus/voice (Loaded no longer re-fires on reuse)
                UpdateWatchdogSnapshot();
            }

            // #618: screen enumeration can hand us a topology in which NO screen reports Primary - a stale
            // GetAllScreensCached() entry after a monitor hot-plug / RDP reconnect / display reconfiguration
            // (the same enumeration hazard that already makes that helper defensive). Every card then comes
            // up as a read-only mirror, primaryWindow stays null so nothing is even Activate()d, and the
            // user is left staring at a fullscreen topmost cover that refuses keystrokes - panic key or
            // reboot. A lock card must ALWAYS be solvable, so promote the first card we showed. Logged as a
            // warning on purpose: if this fires, enumeration lied and we want it in the next activity log.
            if (primaryWindow == null && _allWindows.Count > 0)
            {
                App.Logger?.Warning(
                    "LockCardWindow: no screen reported Primary ({Count} enumerated) - promoting the first card to input owner so the lock stays solvable (#618)",
                    screens.Length);
                primaryWindow = _allWindows[0];
                try { primaryWindow.PromoteToPrimary(); }
                catch (Exception ex) { App.Logger?.Warning("LockCardWindow: primary promotion failed: {E}", ex.Message); }
            }

            // Focus primary window
            primaryWindow?.Activate();
            primaryWindow?.FocusInput();
        }
    }
}
