using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Serilog;

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
    ///    ponytail: needs App.MainWindowRef, wired when the shell moves to Core. Dropping it also
    ///    means nothing here can outlive the window during --render-all.
    ///  - <c>PopQuizQuestion</c> is copied to the bottom of this file: it lives in the WPF head's
    ///    PopQuizService and this project may not reference that, same as TextEditorDialog's
    ///    TextItem.
    ///  - <c>MouseLeftButtonDown</c>/<c>MouseEnter</c>/<c>MouseLeave</c>/<c>KeyDown</c> are wired
    ///    in the constructor as PointerPressed / PointerEntered / PointerExited / KeyDown.
    ///  - <c>Application.Current.Windows</c> becomes the desktop lifetime's window list; that
    ///    lifetime is null under a headless render, which the existing try/catch already covers.
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
            new PopQuizQuestion("How does obedience feel?",
                new[] { "Natural", "Peaceful", "Exciting", "Like coming home" },
                new[] { "That's right — it's always been natural.", "Peace comes from letting go.", "The thrill never fades.", "Welcome home." }),
            isTest: true)
        {
        }

        public PopQuizWindow(PopQuizQuestion question, bool isTest = false)
        {
            IsOpen = true;

            // ponytail: needs AvatarWindow.IsMuted / SetMuteAvatar from
            // ConditioningControlPanel/Windows/AvatarWindow.xaml.cs (head-side). WPF muted the
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
            Opened += (_, _) => Focus();

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
                // ponytail: needs ProgressionService.AddXP(25, XPSource.Other) from
                // ConditioningControlPanel/Services/ProgressionService.cs — still head-side, and
                // there is no CoreProgression seam yet.
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
            // Mark answered BEFORE completing: OnClosed re-Completes when !_answered as a
            // safety net, and the ESC path (still unanswered) would otherwise double-Complete —
            // the second call hits the mismatch branch and clears whatever interaction the
            // first Complete just dequeued (same #462 class as the lock-card fix).
            _answered = true;
            // ponytail: needs InteractionQueueService.Complete(InteractionType.PopQuiz) from
            // ConditioningControlPanel/Services/InteractionQueueService.cs — still head-side.
            Close();
        }

        /// <summary>
        /// The WPF body verbatim against the seams: <c>App.Settings.Current</c> is
        /// <see cref="CoreSettings.Current"/>, <c>App.Audio</c> is <see cref="CoreAudio"/> and
        /// <c>App.Logger</c> is Serilog's static <c>Log</c>. Silent on this head for the two
        /// reasons that are both the WPF no-op branch: the chimes are Content in the WPF head and
        /// are not laid down beside CCP.Avalonia, so the probe misses; and nothing seeds
        /// <c>CoreAudio.PlayOneShotProvider</c> yet, so the seam fires its finished callback and
        /// returns.
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
                    try { window.Close(); } catch { }
                }
            }
            catch { }
        }

        private static System.Collections.Generic.IEnumerable<PopQuizWindow> DesktopWindows() =>
            (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.Windows.OfType<PopQuizWindow>()
            ?? Enumerable.Empty<PopQuizWindow>();

        protected override void OnClosed(EventArgs e)
        {
            IsOpen = false;

            // ponytail: restoring the avatar mute state needs AvatarWindow.IsMuted /
            // SetMuteAvatar from ConditioningControlPanel/Windows/AvatarWindow.xaml.cs, and the
            // safety net `if (!_answered) InteractionQueue.Complete(...)` needs
            // ConditioningControlPanel/Services/InteractionQueueService.cs. Both head-side. _answered is deliberately NOT set here — that flag is what tells the
            // safety net an unanswered quiz still owes a Complete.

            base.OnClosed(e);
        }
    }

    /// <summary>
    /// One quiz question and its four answer/affirmation pairs.
    /// Copied from ConditioningControlPanel/Services/Quiz/PopQuizService.cs: the type lives in the
    /// WPF head, not in CCP.Core, and neither may be touched by this port.
    /// </summary>
    public class PopQuizQuestion
    {
        public string QuestionText { get; }
        public string[] Answers { get; }
        public string[] Affirmations { get; }

        public PopQuizQuestion(string questionText, string[] answers, string[] affirmations)
        {
            QuestionText = questionText;
            Answers = answers;
            Affirmations = affirmations;
        }
    }
}
