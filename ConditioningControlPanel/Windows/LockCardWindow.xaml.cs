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
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Lock Card window - user must type a phrase multiple times to dismiss
    /// Supports multi-monitor with synced input
    /// </summary>
    public partial class LockCardWindow : Window
    {
        private readonly string _phrase;
        private readonly int _requiredRepeats;
        private readonly bool _strictMode;
        private bool _voiceMode;   // solve by speaking instead of typing (may fall back mid-session)
        private int _completedRepeats = 0;
        private bool _isCompleted = false;
        private DispatcherTimer? _closeTimer;
        private bool _voiceListening = false;
        private System.Threading.CancellationTokenSource? _voiceCts; // cancels the in-flight recognize on close/panic/privacy
        private bool _evictedAutonomy = false; // we stood the "Hey Bambi" wake/PTT mic down and must restore it
        
        // Multi-monitor support
        private readonly bool _isPrimary;
        private static List<LockCardWindow> _allWindows = new();
        private static string _sharedInput = "";
        
        // Achievement tracking
        private static DateTime _startTime;
        private static int _totalErrors = 0;
        private static int _totalCharsTyped = 0;

        // Test mode — no XP or achievements
        private static bool _isTest = false;

        // Win32 focus-stealing support
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private IntPtr _hwnd;



        /// <summary>
        /// Check if any lock card window is currently open
        /// </summary>
        public static bool IsAnyOpen() => _allWindows.Count > 0;

        /// <summary>
        /// Create a lock card window for a specific screen
        /// </summary>
        /// <param name="phrase">The phrase to type</param>
        /// <param name="repeats">Number of times to type it</param>
        /// <param name="strictMode">If true, ESC is disabled</param>
        /// <param name="screen">The screen to show on (null for primary)</param>
        /// <param name="isPrimary">If true, this window handles input</param>
        public LockCardWindow(string phrase, int repeats, bool strictMode,
            System.Windows.Forms.Screen? screen = null, bool isPrimary = true, bool voiceMode = false)
        {
            InitializeComponent();

            _phrase = phrase;
            _requiredRepeats = repeats;
            _strictMode = strictMode;
            _isPrimary = isPrimary;
            // Voice mode degrades gracefully to typing if the offline engine isn't usable, so the
            // user can never be trapped behind a mic that won't cooperate.
            _voiceMode = voiceMode && App.Speech?.IsAvailable == true && App.Settings.Current.MicConsentGiven;
            if (voiceMode && !_voiceMode)
                App.Logger?.Information("LockCardWindow: voice mode requested but unavailable — falling back to typing");

            // Set the phrase text
            TxtPhrase.Text = phrase;

            // Swap the input affordance for the voice panel when solving by voice.
            if (_voiceMode)
            {
                InputBorder.Visibility = Visibility.Collapsed;
                VoicePanel.Visibility = Visibility.Visible;
                TxtTitle.Text = "SAY IT TO UNLOCK";
                TxtHint.Text = "Say the phrase out loud, clearly.";
                TxtVoiceState.Text = _isPrimary ? "🎤 Listening…" : "🎤 Speak on the main monitor";
            }

            // Update progress display
            UpdateProgress();
            
            // Handle strict mode
            if (_strictMode)
            {
                TxtStrict.Text = Loc.Get("label_strict");
            }
            // Esc always works now (even in strict mode) so always show the hint.
            TxtEscHint.Text = Loc.Get("label_press_esc_to_close");
            
            // Position on screen
            if (screen != null)
            {
                PositionOnScreen(screen);
            }
            else
            {
                // Default to primary screen, maximized
                WindowState = WindowState.Maximized;
            }
            
            // Non-primary windows show synced text but input is read-only
            if (!_isPrimary)
            {
                TxtInput.IsReadOnly = true;
                TxtInput.Focusable = false;
                TxtHint.Text = Loc.Get("label_input_synced_from_primary_monitor");
            }
            
            // Apply custom colors from settings
            ApplyColors();
            
            // Register this window
            _allWindows.Add(this);

            // When focus is lost, immediately reclaim it using Win32 to prevent
            // keystrokes from leaking into other apps (e.g., Discord)
            if (_isPrimary)
            {
                Deactivated += (s, e) =>
                {
                    if (_isCompleted) return;
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
                        if (_voiceMode) Focus(); else TxtInput.Focus();
                    }), DispatcherPriority.Input);
                };
            }
        }

        private void PositionOnScreen(System.Windows.Forms.Screen screen)
        {
            // Get DPI scale
            var dpiScale = VisualTreeHelper.GetDpi(this);
            var scaleX = dpiScale.DpiScaleX;
            var scaleY = dpiScale.DpiScaleY;
            
            // Position window to cover the entire screen
            Left = screen.Bounds.Left / scaleX;
            Top = screen.Bounds.Top / scaleY;
            Width = screen.Bounds.Width / scaleX;
            Height = screen.Bounds.Height / scaleY;
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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            // Force this window to foreground via Win32 on primary
            if (_isPrimary)
            {
                if (_hwnd != IntPtr.Zero)
                {
                    SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    SetForegroundWindow(_hwnd);
                }
                Activate();
                if (_voiceMode) Focus(); else TxtInput.Focus();

                App.Logger?.Information("Lock Card shown - Phrase: {Phrase}, Repeats: {Repeats}, Strict: {Strict}, Voice: {Voice}, Monitors: {Count}",
                    _phrase, _requiredRepeats, _strictMode, _voiceMode, _allWindows.Count);

                // Begin the spoken-solve listen loop on the primary monitor.
                if (_voiceMode) StartVoiceSolve();
            }
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
            TxtHint.Text = Loc.Get("label_type_the_phrase_exactly_as_shown_above");
            TxtInput.Focus();
            App.Logger?.Information("LockCardWindow: fell back to typed solve (speech unavailable mid-card)");
        }

        private void CloseAllWindows()
        {
            ForceCloseAll();
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
        /// Force close all lock card windows (used by panic button)
        /// </summary>
        public static void ForceCloseAll()
        {
            // Create a copy of the list to avoid modification during iteration
            var windowsToClose = new List<LockCardWindow>(_allWindows);
            _allWindows.Clear();

            foreach (var window in windowsToClose)
            {
                window._isCompleted = true; // Allow closing even in strict mode
                try
                {
                    window.Close();
                }
                catch { }
            }

            // Notify InteractionQueue that lock card is complete (triggers queued items).
            // Only when we actually closed something: engine stop calls this as blanket
            // cleanup, and an unconditional Complete(LockCard) here cleared whatever WAS
            // current (e.g. an in-flight Video), letting the session summary race the
            // video teardown (#462).
            if (windowsToClose.Count > 0)
                App.InteractionQueue?.Complete(Services.InteractionQueueService.InteractionType.LockCard);
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
            // Clear any existing windows
            _allWindows.Clear();
            _sharedInput = "";

            // Reset achievement tracking
            _startTime = DateTime.Now;
            _totalErrors = 0;
            _totalCharsTyped = 0;
            _isTest = isTest;
            
            var screens = App.GetAllScreensCached();
            if (screens.Length == 0)
            {
                App.Logger?.Warning("LockCardWindow: No screens available");
                return;
            }

            LockCardWindow? primaryWindow = null;

            foreach (var screen in screens)
            {
                var isPrimary = screen.Primary;
                var window = new LockCardWindow(phrase, repeats, strictMode, screen, isPrimary, voiceMode);

                if (isPrimary)
                {
                    primaryWindow = window;
                }

                window.Show();
            }

            // Focus primary window
            primaryWindow?.Activate();
            primaryWindow?.TxtInput.Focus();
        }
    }
}
