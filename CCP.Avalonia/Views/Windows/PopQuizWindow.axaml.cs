using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Serilog;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The pop quiz: one question, four shuffled answers, every one of them "correct", and an
    /// affirmation plus 25 XP whichever is picked.
    ///
    /// PORTED from ConditioningControlPanel/Windows/PopQuizWindow.xaml.cs. Deviations:
    ///  - The two Win32 calls are gone. <c>SetWindowPos(HWND_TOPMOST)</c> is <c>Topmost="True"</c>
    ///    in the markup — Avalonia maps it to <c>_NET_WM_STATE_ABOVE</c>, which is the correct X11
    ///    mechanism (see <c>Platform/X11Overlay</c>'s header) — and <c>SetForegroundWindow</c> is
    ///    <see cref="Window.Activate"/> in the Deactivated handler.
    ///  - The 200ms <c>_keepOnTopTimer</c> is dropped entirely. Both halves of it are gone: the
    ///    topmost re-assert is covered by the property above, and the "self-close once the main
    ///    window is genuinely gone" watchdog reads <c>App.MainWindowRef</c>.
    ///    App.MainWindowRef is NOT the blocker - it resolves here as the desktop lifetime's
    ///    <c>MainWindow</c>, the same lookup EmiMutePromptWindow.Ask uses. The watchdog stays
    ///    DROPPED on purpose: a 200ms static timer is the only thing in this file that can outlive
    ///    the window, and under --render-all (no lifetime, no shell) it would tick forever against
    ///    a null MainWindow and close every rendered quiz. Restore it with the shell, not before.
    ///  - <c>PopQuizQuestion</c> and the question bank come from Core's <c>PopQuizScheduler</c>.
    ///  - <c>MouseLeftButtonDown</c>/<c>MouseEnter</c>/<c>MouseLeave</c>/<c>KeyDown</c> are wired
    ///    in the constructor as PointerPressed / PointerEntered / PointerExited / KeyDown.
    ///  - <c>Application.Current.Windows</c> becomes the static <c>_shown</c> list.
    /// </summary>
    public partial class PopQuizWindow : Window
    {
        public static bool IsOpen { get; private set; }

        private readonly PopQuizQuestion _question;
        private readonly bool _isTest;
        private bool _answered;
        private static readonly Random _random = new();

        private readonly TextBlock _txtQuestion, _txtAffirmation;
        private readonly StackPanel _questionPanel, _affirmationPanel;
        private readonly Border[] _answers;

        /// <summary>Render/design constructor: the first question of the WPF pool, whose strings are
        /// hardcoded English there too, so the sample is faithful rather than invented.</summary>
        internal PopQuizWindow() : this(
            PopQuizScheduler.QuestionPool[0],
            isTest: true)
        {
        }

        public PopQuizWindow(PopQuizQuestion question, bool isTest = false)
        {
            IsOpen = true;

            // ponytail: needs App.AvatarWindow.IsMuted / SetMuteAvatar - the type is
            // AvatarTubeWindow, ConditioningControlPanel/AvatarTube/AvatarTubeWindow.*.cs (there is
            // no AvatarWindow.xaml.cs; App.xaml.cs:952 is where the name comes from). WPF muted the
            // avatar for the whole quiz so her z-order work could not cover this window, and
            // restored it in OnClosed.

            AvaloniaXamlLoader.Load(this);
            _question = question;
            _isTest = isTest;

            _txtQuestion = this.FindControl<TextBlock>("TxtQuestion")!;
            _txtAffirmation = this.FindControl<TextBlock>("TxtAffirmation")!;
            _questionPanel = this.FindControl<StackPanel>("QuestionPanel")!;
            _affirmationPanel = this.FindControl<StackPanel>("AffirmationPanel")!;
            _answers = new[] { "AnswerA", "AnswerB", "AnswerC", "AnswerD" }
                .Select(n => this.FindControl<Border>(n)!).ToArray();

            // Shuffle answer order
            var indices = new[] { 0, 1, 2, 3 };
            for (int i = 3; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            // When something steals focus from us, grab it right back. WPF did this with
            // SetWindowPos(HWND_TOPMOST) + SetForegroundWindow; Topmost is declarative now and
            // Activate() is the rest. The !IsVisible guard matters under --render-all, which
            // closes each window before showing the next.
            Deactivated += (_, _) =>
            {
                if (_answered) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_answered || !IsVisible) return;
                    Activate();
                }, DispatcherPriority.Input);
            };

            KeyDown += Window_KeyDown;
            // No focusable child, and Avalonia only routes KeyDown to the focused element, so
            // without this ESC does nothing on a real desktop.
            Opened += (_, _) => { _shown.Add(this); Focus(); };

            var texts = new[] { "TxtAnswerA", "TxtAnswerB", "TxtAnswerC", "TxtAnswerD" };
            for (int slot = 0; slot < 4; slot++)
            {
                var border = _answers[slot];
                this.FindControl<TextBlock>(texts[slot])!.Text = question.Answers[indices[slot]];

                // Store the mapped indices so we can look up the correct affirmation
                border.Tag = indices[slot];

                border.PointerPressed += Answer_Click;
                border.PointerEntered += Answer_MouseEnter;
                border.PointerExited += Answer_MouseLeave;
            }

            _txtQuestion.Text = question.QuestionText;

            // "Turn these off" link: mouse (WPF MouseLeftButtonUp) or Enter/Space when focused (P17).
            var turnOff = this.FindControl<TextBlock>("TxtTurnOff")!;
            turnOff.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                e.Handled = true;
                TurnOff_Click();
            };
            turnOff.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Space)) return;
                e.Handled = true;
                TurnOff_Click();
            };
        }

        /// <summary>Raised after the card's link switched pop quizzes off, so the Graded Intake
        /// switch can follow (WPF reached into MainWindowRef.GradedIntakeTab directly).</summary>
        public static event Action? TurnedOff;

        /// <summary>
        /// WPF PopQuizWindow.TurnOffPopQuestions (7.1.5): switches the Graded Intake page's own
        /// setting off. PURE on the settings object; returns whether anything changed.
        /// </summary>
        internal static bool TurnOffPopQuestions(ConditioningControlPanel.Models.AppSettings settings)
        {
            if (!settings.PopQuizEnabled) return false;
            settings.PopQuizEnabled = false;
            return true;
        }

        // WPF PopQuizWindow.TurnOff_Click: closes like Esc - never an answer, no XP, no penalty.
        private void TurnOff_Click()
        {
            if (_answered) return;
            try
            {
                if (TurnOffPopQuestions(CoreSettings.Current))
                {
                    CoreSettings.Save();
                    TurnedOff?.Invoke();
                    Log.Information("PopQuiz: turned off from the card");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PopQuiz: turning off from the card failed");
            }
            CleanupAndClose();
            CoreEngine.PopQuiz?.Stop();
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_answered)
            {
                CleanupAndClose();
            }
        }

        private async void Answer_Click(object? sender, PointerPressedEventArgs e)
        {
            if (_answered) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _answered = true;

            if (sender is not Border b || b.Tag is not int answerIndex) return;

            // Highlight selected answer pink
            b.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4));
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x69, 0xB4));

            // Play chime
            PlayChime();

            // Award XP
            if (!_isTest)
            {
                CoreProgression.AddXP(25, "Other");
            }

            // Show affirmation
            await Task.Delay(300);
            _txtAffirmation.Text = _question.Affirmations[answerIndex];
            _questionPanel.IsVisible = false;
            _affirmationPanel.IsVisible = true;

            // Auto-dismiss after 1.5s
            await Task.Delay(1500);
            CleanupAndClose();
        }

        private void CleanupAndClose()
        {
            _closeReason = _answered ? "answered, auto-dismiss" : "ESC";
            // Mark answered BEFORE completing: OnClosed re-Completes when !_answered as a
            // safety net, and the ESC path (still unanswered) would otherwise double-Complete —
            // the second call hits the mismatch branch and clears whatever interaction the
            // first Complete just dequeued (same #462 class as the lock-card fix).
            _answered = true;
            // No InteractionQueue.Complete: PopQuizHost holds no queue slot on this head.
            Close();
        }

        /// <summary>
        /// The WPF body verbatim against the seams: <c>App.Settings.Current</c> is
        /// <see cref="CoreSettings.Current"/>, <c>App.Audio</c> is <see cref="CoreAudio"/> and
        /// <c>App.Logger</c> is Serilog's static <c>Log</c>. The chimes ship at Resources/sounds
        /// on both heads (SharedSoundsPackagingTests).
        /// </summary>
        private static void PlayChime()
        {
            try
            {
                var soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds");
                var files = new[] { "chime1.mp3", "chime2.mp3", "chime3.mp3" };
                var file = files[_random.Next(files.Length)];
                var path = Path.Combine(soundsPath, file);
                if (!File.Exists(path)) return;

                var master = CoreSettings.Current.MasterVolume / 100f;
                var volume = (float)Math.Pow(master * 0.5f, 1.5);

                CoreAudio.PlayOneShot(path, volume, "popquiz-chime");
            }
            catch (Exception ex)
            {
                Log.Debug("PopQuiz chime failed: {Error}", ex.Message);
            }
        }

        // Hover effects
        private void Answer_MouseEnter(object? sender, PointerEventArgs e)
        {
            if (!_answered && sender is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4));
            }
        }

        private void Answer_MouseLeave(object? sender, PointerEventArgs e)
        {
            if (!_answered && sender is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            }
        }

        /// <summary>
        /// Whether a pop quiz is actually on screen right now. Gates on the visible set rather than the
        /// <see cref="IsOpen"/> flag: that flag is raised in the constructor and only lowered in
        /// OnClosed, so a quiz that failed between construction and Show() would leave it stuck true
        /// and block every later interaction.
        /// </summary>
        public static bool IsAnyOpen()
        {
            try
            {
                return DesktopWindows().Any(w => w.IsVisible);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Force close all pop quiz windows (used by panic button)
        /// </summary>
        public static void ForceCloseAll()
        {
            try
            {
                foreach (var window in DesktopWindows().ToList())
                {
                    window._closeReason = "ForceCloseAll (engine stop / panic)";
                    try { window.Close(); } catch { }
                }
            }
            catch { }
        }

        /// <summary>Shown quizzes. Own list rather than the desktop lifetime's, which is null under a
        /// headless host (same shape as LockCardWindow._allWindows).</summary>
        private static readonly System.Collections.Generic.List<PopQuizWindow> _shown = new();

        private static System.Collections.Generic.IEnumerable<PopQuizWindow> DesktopWindows() => _shown;

        /// <summary>The last shown quiz left the screen: a lock card held behind it replays (#763).</summary>
        public static event Action? AllClosed;

        private string _closeReason = "window closed externally";

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (_closeReason == "window closed externally") _closeReason = $"close request ({e.CloseReason}, programmatic: {e.IsProgrammatic})";
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            IsOpen = false;
            _shown.Remove(this);
            Log.Information("PopQuizWindow closed (answered: {Answered}, reason: {Reason})", _answered, _closeReason);

            // ponytail: restoring the avatar mute state needs App.AvatarWindow (AvatarTubeWindow,
            // ConditioningControlPanel/AvatarTube/), head-side. The WPF
            // queue safety net has nothing to release here (PopQuizHost holds no slot).

            base.OnClosed(e);
            if (_shown.Count == 0) AllClosed?.Invoke();
        }
    }
}
