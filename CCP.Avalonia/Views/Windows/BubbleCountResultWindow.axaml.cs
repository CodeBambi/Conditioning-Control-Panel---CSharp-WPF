using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Result window for bubble counting - enter the number, 3 attempts, then mercy card.
    /// Multi-monitor support.
    ///
    /// PORTED from ConditioningControlPanel/Windows/BubbleCountResultWindow.xaml.cs. Deviations:
    ///
    ///  - <b>Win32 is gone.</b> The only P/Invoke here was
    ///    <c>SetWindowLong(GWL_EXSTYLE, WS_EX_TOOLWINDOW)</c> in <c>SourceInitialized</c>, to keep
    ///    the window out of Alt+Tab. That is <c>ShowInTaskbar="False"</c> in the .axaml, so nothing
    ///    is lost and no <c>user32</c> reference reaches this net8.0 head.
    ///  - <b>Screens.</b> <c>System.Windows.Forms.Screen</c> + <c>BubbleCountWindow.GetDpiForScreen</c>
    ///    become <see cref="Screen"/> from <c>Window.Screens</c>. The DPI division drops out with
    ///    them: WPF's Left/Top are DIPs, Avalonia's <c>Position</c> is physical pixels, which is
    ///    what <c>Screen.Bounds</c> is already in.
    ///  - <b>Width=400/Height=300 in the ctor are dropped.</b> They were dead in WPF too - the XAML
    ///    already carries <c>WindowState="Maximized"</c> and Loaded re-asserts it, so the 400x300
    ///    box was never on screen.
    ///  - <b>The monitor set and the mercy card are real.</b> <c>DualMonitorEnabled</c> is in Core
    ///    and the screens come from <c>ScreenList.Enumerate</c> (the same helper the pink-filter
    ///    control uses); the mercy phrase comes from <c>CoreMods.GetPhrases("BubbleCountMercy")</c>
    ///    and drives <c>LockCardWindow.ShowOnAllMonitors</c> plus the 500 ms
    ///    <c>IsAnyOpen()</c> poll, exactly as WPF. Those two LockCardWindow statics are no-ops on
    ///    this head - that note lives in LockCardWindow, not here.
    ///  - <b>The XP and achievement writes are real</b>, through <see cref="CoreProgression"/>;
    ///    they are silent no-ops on a head with no progression service, which is this one today.
    ///    Only the duration scaling is still stubbed (see the ponytail comment in CheckAnswer):
    ///    the award is the flat WPF base of 250. Everything that only touches the view - the 3-attempt loop, the
    ///    too-high/too-low hints, the cross-window input mirroring, the inactivity watchdog - is
    ///    ported verbatim.
    ///  - <c>PreviewTextInput</c> -> a tunnelling <c>TextInputEvent</c> handler; <c>Visibility</c>
    ///    -> <c>IsVisible</c>; <c>SourceInitialized</c>/<c>Loaded</c> -> <c>Opened</c>/<c>Loaded</c>.
    ///  - The feedback and attempt strings stay the hardcoded English of the WPF original: there is
    ///    no loc key with a count placeholder for either, so inventing one would be worse than
    ///    keeping the two heads identical.
    /// </summary>
    public partial class BubbleCountResultWindow : Window
    {
        private readonly int _correctAnswer;
        private readonly bool _strictMode;
        private readonly Action<bool> _onComplete;
        private readonly Screen? _screen;
        private readonly bool _isPrimary;

        private int _attemptsRemaining = 3;
        private bool _isCompleted = false;

        // #633: hard inactivity watchdog (primary only). If an idle user is stranded on the
        // fullscreen/topmost result window (strict mode has no Esc), auto-complete after
        // this timeout. Reset on every keystroke/text change so an active typist is never cut off.
        private DispatcherTimer? _watchdogTimer;
        private static readonly TimeSpan WatchdogTimeout = TimeSpan.FromSeconds(120);

        // Multi-monitor support
        private static List<BubbleCountResultWindow> _allWindows = new();
        private static string _sharedInput = "";

        private readonly TextBox _txtAnswer;
        private readonly TextBlock _txtAttempts, _txtFeedback, _txtStrict, _txtEscHint;
        private readonly Button _btnSubmit;

        /// <summary>Render/design constructor: sample data so --render-view can draw the window.</summary>
        internal BubbleCountResultWindow() : this(42, false, _ => { }) { }

        public BubbleCountResultWindow(int correctAnswer, bool strictMode, Action<bool> onComplete,
            Screen? screen = null, bool isPrimary = true)
        {
            AvaloniaXamlLoader.Load(this);

            _txtAnswer = this.FindControl<TextBox>("TxtAnswer")!;
            _txtAttempts = this.FindControl<TextBlock>("TxtAttempts")!;
            _txtFeedback = this.FindControl<TextBlock>("TxtFeedback")!;
            _txtStrict = this.FindControl<TextBlock>("TxtStrict")!;
            _txtEscHint = this.FindControl<TextBlock>("TxtEscHint")!;
            _btnSubmit = this.FindControl<Button>("BtnSubmit")!;

            _correctAnswer = correctAnswer;
            _strictMode = strictMode;
            _onComplete = onComplete;
            _screen = screen;
            _isPrimary = isPrimary;

            // Setup UI
            UpdateAttemptsDisplay();

            if (_strictMode)
            {
                _txtStrict.IsVisible = true;
                _txtEscHint.IsVisible = false;
            }

            // Non-primary windows are read-only
            if (!_isPrimary)
            {
                _txtAnswer.IsReadOnly = true;
                _txtAnswer.Focusable = false;
                _btnSubmit.IsEnabled = false;
            }

            // Register window
            _allWindows.Add(this);

            // Position on screen. WPF divided by the per-screen DPI because Left/Top are DIPs;
            // Avalonia's Position is in the same physical pixels Screen.Bounds reports, so the
            // conversion - and BubbleCountWindow.GetDpiForScreen with it - disappears. Screens is
            // only populated once the window has a platform impl, hence Opened rather than the ctor.
            Opened += (_, _) =>
            {
                var target = _screen ?? Screens?.Primary;
                if (target is not null)
                    Position = new PixelPoint(target.Bounds.X + 100, target.Bounds.Y + 100);
            };

            // Focus input on primary
            Loaded += (_, _) =>
            {
                WindowState = WindowState.Maximized;
                if (_isPrimary)
                {
                    _txtAnswer.Focus();
                    StartWatchdog();
                }
            };

            // Key handlers
            _btnSubmit.Click += (_, _) => BtnSubmit_Click();
            KeyDown += OnKeyDown;
            _txtAnswer.KeyDown += OnInputKeyDown;
            _txtAnswer.TextChanged += OnTextChanged;

            // Only allow numbers. WPF's PreviewTextInput is a tunnelling TextInput here; handling
            // it in the bubble phase would be too late, the character is already in the box.
            _txtAnswer.AddHandler(TextInputEvent, (object? _, TextInputEventArgs e) =>
            {
                e.Handled = string.IsNullOrEmpty(e.Text) || !char.IsDigit(e.Text, 0);
            }, RoutingStrategies.Tunnel);
        }

        /// <summary>
        /// Show result window on all monitors.
        ///
        /// <para>Avalonia has no screen list without a TopLevel, so the primary window is built
        /// first and its <c>Screens</c> is what the set is drawn from - the reverse of WPF's
        /// secondaries-first order, which nothing depends on. An empty enumeration (headless, or a
        /// window with no platform impl yet) is the single-screen path rather than WPF's
        /// <c>onComplete(false)</c>: on this head empty means "no topology reported", not "no
        /// display".</para>
        /// </summary>
        public static void ShowOnAllMonitors(int correctAnswer, bool strictMode, Action<bool> onComplete)
        {
            _allWindows.Clear();
            _sharedInput = "";

            var primaryWindow = new BubbleCountResultWindow(correctAnswer, strictMode, onComplete, null, true);

            if (CoreSettings.Current.DualMonitorEnabled)
            {
                var all = Features.ScreenList.Enumerate(primaryWindow);
                var primary = all.FirstOrDefault(s => s.IsPrimary) ?? all.FirstOrDefault();
                foreach (var screen in all.Where(s => s != primary))
                {
                    var window = new BubbleCountResultWindow(correctAnswer, strictMode, onComplete, screen, false);
                    window.Show();
                }
            }

            primaryWindow.Show();
            primaryWindow.Activate();   // WPF SetForegroundWindow equivalent
        }

        private void OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (!_isPrimary) return;

            // Active typist: keep the inactivity watchdog from firing.
            ResetWatchdog();

            _sharedInput = _txtAnswer.Text ?? "";

            // Sync to all windows
            foreach (var window in _allWindows.Where(w => w != this))
            {
                window._txtAnswer.Text = _sharedInput;
            }
        }

        private void OnInputKeyDown(object? sender, KeyEventArgs e)
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

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_strictMode && !_isCompleted)
            {
                CompleteAll(false);
            }
        }

        private void BtnSubmit_Click()
        {
            if (_isPrimary) CheckAnswer();
        }

        private void CheckAnswer()
        {
            if (_isCompleted) return;

            if (!int.TryParse((_txtAnswer.Text ?? "").Trim(), out int answer))
            {
                ShowFeedbackOnAll("Please enter a number!", Colors.Orange);
                _txtAnswer.Clear();
                _txtAnswer.Focus();
                return;
            }

            if (answer == _correctAnswer)
            {
                // Correct! XP scaled by video duration.
                // ponytail: the scaling still needs BubbleCountService.ScaleXpByDuration(250)
                // (ConditioningControlPanel/Services/BubbleCountService.cs), which is head-side and
                // has no Core seam, so the flat WPF base of 250 is awarded until it moves.
                var xp = 250;
                CoreProgression.AddXP(xp, "BubbleCount");
                ShowFeedbackOnAll($"🎉 CORRECT! +{xp} XP 🎉", Color.FromRgb(50, 205, 50));
                DisableInputOnAll();
                StopWatchdog(); // terminal success: no more input expected

                // Track achievement - correct answer
                CoreProgression.TrackBubbleCountResult(true);

                // Delay then complete
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) =>
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
                CoreProgression.TrackBubbleCountResult(false);

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
                    _txtAnswer.Clear();
                    _txtAnswer.Focus();
                }
            }
        }

        private void ShowFeedbackOnAll(string message, Color color)
        {
            foreach (var window in _allWindows)
            {
                window._txtFeedback.Text = message;
                window._txtFeedback.Foreground = new SolidColorBrush(color);
                window._txtFeedback.IsVisible = true;
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
                window._btnSubmit.IsEnabled = false;
                window._txtAnswer.IsEnabled = false;
            }
        }

        /// <summary>
        /// ponytail: the count has no loc key with a placeholder, so this stays the WPF original's
        /// hardcoded English. It also writes over the {loc:Str label_attempts_remaining_3} binding
        /// the .axaml puts on TxtAttempts, which Avalonia keeps alive under the local value - a
        /// language change mid-game would snap the label back to "3". Add an
        /// `label_attempts_remaining` format key and use Loc.GetF to fix both at once.
        /// </summary>
        private void UpdateAttemptsDisplay()
        {
            _txtAttempts.Text = $"Attempts remaining: {_attemptsRemaining}";

            // Color based on attempts
            if (_attemptsRemaining == 1)
            {
                _txtAttempts.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
            }
            else if (_attemptsRemaining == 2)
            {
                _txtAttempts.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0));
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
            var mercyPhrases = CoreMods.GetPhrases("BubbleCountMercy") ?? new[] { "GOOD GIRLS PAY ATTENTION" };

            var phrase = mercyPhrases[Random.Shared.Next(mercyPhrases.Length)];

            // Show mercy lock card (no answer in phrase!)
            LockCardWindow.ShowOnAllMonitors(
                phrase,
                2, // Type twice
                _strictMode);

            // After lock card closes, complete
            // Note: LockCardWindow handles its own close, we just complete after a delay
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
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
            _watchdogTimer = new DispatcherTimer { Interval = WatchdogTimeout };
            _watchdogTimer.Tick += (_, _) =>
            {
                _watchdogTimer?.Stop();
                if (_isCompleted) return;
                Serilog.Log.Warning("BubbleCountResultWindow: Inactivity watchdog fired after {Seconds}s - auto-completing to prevent lockout",
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
    }
}
