using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Result window for bubble counting - enter the number, 3 attempts, then mercy card
    /// Multi-monitor support
    /// </summary>
    public partial class BubbleCountResultWindow : Window
    {
        private readonly int _correctAnswer;
        private readonly bool _strictMode;
        private readonly Action<bool> _onComplete;
        private readonly System.Windows.Forms.Screen _screen;
        private readonly bool _isPrimary;
        
        private int _attemptsRemaining = 3;
        private bool _isCompleted = false;

        // #633: hard inactivity watchdog (primary only). If an idle user is stranded on the
        // fullscreen/topmost result window (strict mode has no Esc), auto-complete after
        // this timeout. Reset on every keystroke/text change so an active typist is never cut off.
        private System.Windows.Threading.DispatcherTimer? _watchdogTimer;
        private static readonly TimeSpan WatchdogTimeout = TimeSpan.FromSeconds(120);

        // Multi-monitor support
        private static List<BubbleCountResultWindow> _allWindows = new();
        private static string _sharedInput = "";

        public BubbleCountResultWindow(int correctAnswer, bool strictMode, Action<bool> onComplete,
            System.Windows.Forms.Screen? screen = null, bool isPrimary = true)
        {
            InitializeComponent();
            
            _correctAnswer = correctAnswer;
            _strictMode = strictMode;
            _onComplete = onComplete;
            _screen = screen ?? System.Windows.Forms.Screen.PrimaryScreen!;
            _isPrimary = isPrimary;
            
            // Position on screen — convert physical pixels to WPF DIPs using per-screen DPI
            var dpiScale = BubbleCountWindow.GetDpiForScreen(_screen);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = (_screen.Bounds.X + 100) / dpiScale;
            Top = (_screen.Bounds.Y + 100) / dpiScale;
            Width = 400;
            Height = 300;
            
            // Setup UI
            UpdateAttemptsDisplay();
            
            if (_strictMode)
            {
                TxtStrict.Visibility = Visibility.Visible;
                TxtEscHint.Visibility = Visibility.Collapsed;
            }
            
            // Non-primary windows are read-only
            if (!_isPrimary)
            {
                TxtAnswer.IsReadOnly = true;
                TxtAnswer.Focusable = false;
                BtnSubmit.IsEnabled = false;
            }
            
            // Register window
            _allWindows.Add(this);
            
            // Hide from Alt+Tab
            SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            };
            
            // Focus input on primary
            Loaded += (s, e) =>
            {
                WindowState = WindowState.Maximized;
                if (_isPrimary)
                {
                    TxtAnswer.Focus();
                    StartWatchdog();
                }
            };
            
            // Key handlers
            KeyDown += OnKeyDown;
            TxtAnswer.KeyDown += OnInputKeyDown;
            TxtAnswer.TextChanged += OnTextChanged;
            
            // Only allow numbers
            TxtAnswer.PreviewTextInput += (s, e) =>
            {
                e.Handled = !char.IsDigit(e.Text, 0);
            };
        }

        /// <summary>
        /// Show result window on all monitors
        /// </summary>
        public static void ShowOnAllMonitors(int correctAnswer, bool strictMode, Action<bool> onComplete)
        {
            _allWindows.Clear();
            _sharedInput = "";
            
            var settings = App.Settings.Current;
            var allScreens = App.GetAllScreensCached();
            var screens = settings.DualMonitorEnabled
                ? allScreens
                : new[] { allScreens.FirstOrDefault(s => s.Primary) ?? allScreens.FirstOrDefault() ?? System.Windows.Forms.Screen.PrimaryScreen! };

            if (screens.Length == 0)
            {
                App.Logger?.Warning("BubbleCountResultWindow: No screens available");
                onComplete?.Invoke(false);
                return;
            }

            var primary = screens.FirstOrDefault(s => s.Primary) ?? screens[0];
            
            // Create secondary windows first
            foreach (var screen in screens.Where(s => s != primary))
            {
                var window = new BubbleCountResultWindow(correctAnswer, strictMode, onComplete, screen, false);
                window.Show();
            }
            
            // Create primary window
            var primaryWindow = new BubbleCountResultWindow(correctAnswer, strictMode, onComplete, primary, true);
            primaryWindow.Show();
            primaryWindow.Activate();
        }

        private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_isPrimary) return;

            // Active typist: keep the inactivity watchdog from firing.
            ResetWatchdog();

            _sharedInput = TxtAnswer.Text;
            
            // Sync to all windows
            foreach (var window in _allWindows.Where(w => w != this))
            {
                window.TxtAnswer.Text = _sharedInput;
            }
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            // Any keystroke counts as activity (covers keys that don't change the text,
            // e.g. Enter/backspace on an empty field).
            if (_isPrimary) ResetWatchdog();

            if (e.Key == Key.Enter && _isPrimary)
            {
                CheckAnswer();
                e.Handled = true;
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_strictMode && !_isCompleted)
            {
                CompleteAll(false);
            }
        }

        private void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            if (_isPrimary) CheckAnswer();
        }

        private void CheckAnswer()
        {
            if (_isCompleted) return;
            
            if (!int.TryParse(TxtAnswer.Text.Trim(), out int answer))
            {
                ShowFeedbackOnAll("Please enter a number!", Colors.Orange);
                TxtAnswer.Clear();
                TxtAnswer.Focus();
                return;
            }
            
            if (answer == _correctAnswer)
            {
                // Correct! XP scaled by video duration
                var xp = Services.BubbleCountService.ScaleXpByDuration(250);
                App.Progression?.AddXP(xp, XPSource.BubbleCount);
                ShowFeedbackOnAll($"🎉 CORRECT! +{xp} XP 🎉", Color.FromRgb(50, 205, 50));
                DisableInputOnAll();
                StopWatchdog(); // terminal success: no more input expected
                
                // Track achievement - correct answer
                App.Achievements?.TrackBubbleCountResult(true);
                
                // Delay then complete
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    CompleteAll(true);
                };
                timer.Start();
            }
            else
            {
                // Wrong answer
                _attemptsRemaining--;
                UpdateAttemptsOnAll();
                
                // Track achievement - wrong answer (breaks streak)
                App.Achievements?.TrackBubbleCountResult(false);
                
                if (_attemptsRemaining <= 0)
                {
                    if (_strictMode)
                    {
                        // Strict mode: signal failure - service handles retry/mercy
                        CompleteAll(false);
                    }
                    else
                    {
                        // Non-strict: show mercy lock card
                        ShowMercyCard();
                    }
                }
                else
                {
                    // Give hint
                    string hint = answer < _correctAnswer ? "Too low! Try higher." : "Too high! Try lower.";
                    ShowFeedbackOnAll($"❌ {hint}", Color.FromRgb(255, 107, 107));
                    TxtAnswer.Clear();
                    TxtAnswer.Focus();
                }
            }
        }

        private void ShowFeedbackOnAll(string message, Color color)
        {
            foreach (var window in _allWindows)
            {
                window.TxtFeedback.Text = message;
                window.TxtFeedback.Foreground = new SolidColorBrush(color);
                window.TxtFeedback.Visibility = Visibility.Visible;
            }
        }

        private void UpdateAttemptsOnAll()
        {
            foreach (var window in _allWindows)
            {
                window._attemptsRemaining = _attemptsRemaining;
                window.UpdateAttemptsDisplay();
            }
        }

        private void DisableInputOnAll()
        {
            foreach (var window in _allWindows)
            {
                window.BtnSubmit.IsEnabled = false;
                window.TxtAnswer.IsEnabled = false;
            }
        }

        private void UpdateAttemptsDisplay()
        {
            TxtAttempts.Text = $"Attempts remaining: {_attemptsRemaining}";
            
            // Color based on attempts
            if (_attemptsRemaining == 1)
            {
                TxtAttempts.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
            }
            else if (_attemptsRemaining == 2)
            {
                TxtAttempts.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0));
            }
        }

        private void ShowMercyCard()
        {
            _isCompleted = true;
            StopWatchdog();

            // Hide all result windows
            foreach (var window in _allWindows)
            {
                window._isCompleted = true;
                window.StopWatchdog();
                window.Hide();
            }
            
            // Mod-aware mercy phrases (no answer included!)
            var mercyPhrases = App.Mods?.GetPhrases("BubbleCountMercy") ?? new[] { "GOOD GIRLS PAY ATTENTION" };

            var phrase = mercyPhrases[Random.Shared.Next(mercyPhrases.Length)];
            
            // Show mercy lock card (no answer in phrase!)
            LockCardWindow.ShowOnAllMonitors(
                phrase,
                2, // Type twice
                _strictMode);
            
            // After lock card closes, complete
            // Note: LockCardWindow handles its own close, we just complete after a delay
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                // Panic/engine stop force-closed everything while we were polling: bail without
                // firing the completion callback — in strict mode OnGameComplete(false) retries
                // the game, resurrecting a fullscreen bubble count seconds after "stop everything".
                // (Normal mercy flow keeps the hidden result windows in _allWindows until
                // CompleteAll, so an empty list can only mean ForceCloseAll ran.)
                if (_allWindows.Count == 0) return;
                // Check if lock card is still open
                if (LockCardWindow.IsAnyOpen())
                {
                    timer.Start(); // Keep checking
                }
                else
                {
                    CompleteAll(false);
                }
            };
            timer.Start();
        }

        /// <summary>
        /// Force close all result windows (used by panic button)
        /// </summary>
        public static void ForceCloseAll()
        {
            foreach (var window in _allWindows.ToArray())
            {
                window._isCompleted = true;
                try { window.Close(); } catch { }
            }
            _allWindows.Clear();
        }

        private void CompleteAll(bool success)
        {
            StopWatchdog();
            foreach (var window in _allWindows.ToArray())
            {
                window._isCompleted = true;
                window.StopWatchdog();
                try { window.Close(); } catch { }
            }
            _allWindows.Clear();

            _onComplete?.Invoke(success);
        }

        protected override void OnClosed(EventArgs e)
        {
            StopWatchdog();
            _allWindows.Remove(this);

            if (!_isCompleted && _isPrimary)
            {
                _onComplete?.Invoke(false);
            }
            base.OnClosed(e);
        }

        #region Inactivity watchdog (#633)

        /// <summary>
        /// Start the primary window's inactivity watchdog. Fires <see cref="WatchdogTimeout"/>
        /// after the last activity, auto-completing so an idle user is never stranded on the
        /// fullscreen/topmost result window (strict mode offers no Esc).
        /// </summary>
        private void StartWatchdog()
        {
            if (!_isPrimary) return;
            _watchdogTimer?.Stop();
            _watchdogTimer = new System.Windows.Threading.DispatcherTimer { Interval = WatchdogTimeout };
            _watchdogTimer.Tick += (s, e) =>
            {
                _watchdogTimer?.Stop();
                if (_isCompleted) return;
                App.Logger?.Warning("BubbleCountResultWindow: Inactivity watchdog fired after {Seconds}s - auto-completing to prevent lockout",
                    WatchdogTimeout.TotalSeconds);
                CompleteAll(false);
            };
            _watchdogTimer.Start();
        }

        /// <summary>Reset the inactivity countdown on user activity (keystroke / text change).</summary>
        private void ResetWatchdog()
        {
            if (_watchdogTimer == null) return;
            _watchdogTimer.Stop();
            _watchdogTimer.Start();
        }

        private void StopWatchdog()
        {
            _watchdogTimer?.Stop();
            _watchdogTimer = null;
        }

        #endregion

        #region Win32
        
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        #endregion
    }
}
