using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using IOPath = System.IO.Path;
using ConditioningControlPanel.Services;
using NAudio.Wave;

namespace ConditioningControlPanel
{
    public partial class PopQuizWindow : Window
    {
        public static bool IsOpen { get; private set; }

        private readonly PopQuizQuestion _question;
        private readonly bool _isTest;
        private readonly bool _wasAvatarMuted;
        private bool _answered;
        private static readonly Random _random = new();
        private readonly DispatcherTimer _keepOnTopTimer;
        private IntPtr _hwnd;

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public PopQuizWindow(PopQuizQuestion question, bool isTest = false)
        {
            IsOpen = true;

            // Mute avatar while quiz is open — prevents her z-order work from covering us
            var avatar = App.AvatarWindow;
            _wasAvatarMuted = avatar?.IsMuted ?? true;
            if (avatar != null && !_wasAvatarMuted)
                avatar.SetMuteAvatar(true);

            InitializeComponent();
            _question = question;
            _isTest = isTest;

            // Shuffle answer order
            var indices = new[] { 0, 1, 2, 3 };
            for (int i = 3; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            // Timer: re-assert topmost AND check if main window closed
            _keepOnTopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _keepOnTopTimer.Tick += (s, e) =>
            {
                if (_answered || !IsVisible)
                {
                    _keepOnTopTimer.Stop();
                    return;
                }

                // Self-close if main window is gone (closed or minimized to tray)
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow == null || !mainWindow.IsVisible)
                {
                    CleanupAndClose();
                    return;
                }

                // Re-assert topmost + force to front
                if (_hwnd != IntPtr.Zero)
                {
                    SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                }
            };
            Loaded += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                _keepOnTopTimer.Start();
            };

            // When something steals focus from us, grab it right back
            Deactivated += (s, e) =>
            {
                if (_answered) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_answered || !IsVisible) return;
                    if (_hwnd != IntPtr.Zero)
                    {
                        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        SetForegroundWindow(_hwnd);
                    }
                    Activate();
                }), DispatcherPriority.Input);
            };

            TxtQuestion.Text = question.QuestionText;
            TxtAnswerA.Text = question.Answers[indices[0]];
            TxtAnswerB.Text = question.Answers[indices[1]];
            TxtAnswerC.Text = question.Answers[indices[2]];
            TxtAnswerD.Text = question.Answers[indices[3]];

            // Store the mapped indices so we can look up the correct affirmation
            AnswerA.Tag = indices[0];
            AnswerB.Tag = indices[1];
            AnswerC.Tag = indices[2];
            AnswerD.Tag = indices[3];
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_answered)
            {
                CleanupAndClose();
            }
        }

        private async void Answer_Click(object sender, MouseButtonEventArgs e)
        {
            if (_answered) return;
            _answered = true;

            var border = sender as FrameworkElement;
            if (border?.Tag == null) return;

            var answerIndex = (int)border.Tag;

            // Highlight selected answer pink
            if (border is System.Windows.Controls.Border b)
            {
                b.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4));
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x69, 0xB4));
            }

            // Play chime
            PlayChime();

            // Award XP
            if (!_isTest)
            {
                try
                {
                    App.Progression?.AddXP(25, Services.XPSource.Other);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("PopQuiz XP award failed: {Error}", ex.Message);
                }
            }

            // Show affirmation
            await Task.Delay(300);
            TxtAffirmation.Text = _question.Affirmations[answerIndex];
            QuestionPanel.Visibility = Visibility.Collapsed;
            AffirmationPanel.Visibility = Visibility.Visible;

            // Auto-dismiss after 1.5s
            await Task.Delay(1500);
            CleanupAndClose();
        }

        private void CleanupAndClose()
        {
            _keepOnTopTimer.Stop();
            App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.PopQuiz);
            Close();
        }

        private static void PlayChime()
        {
            try
            {
                var soundsPath = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds");
                var files = new[] { "chime1.mp3", "chime2.mp3", "chime3.mp3" };
                var file = files[_random.Next(files.Length)];
                var path = IOPath.Combine(soundsPath, file);
                if (!System.IO.File.Exists(path)) return;

                var master = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                var volume = (float)Math.Pow(master * 0.5f, 1.5);

                Task.Run(() =>
                {
                    WaveOutEvent? output = null;
                    AudioFileReader? reader = null;
                    try
                    {
                        reader = new AudioFileReader(path) { Volume = volume };
                        output = new WaveOutEvent();
                        output.Init(reader);
                        output.Play();
                        while (output.PlaybackState == PlaybackState.Playing)
                            System.Threading.Thread.Sleep(50);
                    }
                    catch { }
                    finally
                    {
                        reader?.Dispose();
                        try { output?.Stop(); } catch { }
                        output?.Dispose();
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PopQuiz chime failed: {Error}", ex.Message);
            }
        }

        // Hover effects
        private void Answer_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_answered && sender is System.Windows.Controls.Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4));
            }
        }

        private void Answer_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_answered && sender is System.Windows.Controls.Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            }
        }

        /// <summary>
        /// Force close all pop quiz windows (used by panic button)
        /// </summary>
        public static void ForceCloseAll()
        {
            try
            {
                foreach (var window in Application.Current.Windows.OfType<PopQuizWindow>().ToList())
                {
                    try { window.Close(); } catch { }
                }
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            IsOpen = false;
            _keepOnTopTimer.Stop();

            // Restore avatar mute state
            if (!_wasAvatarMuted)
            {
                try { App.AvatarWindow?.SetMuteAvatar(false); }
                catch { }
            }

            // Ensure queue is cleared even if close happens unexpectedly
            if (!_answered)
            {
                App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.PopQuiz);
            }
            base.OnClosed(e);
        }
    }
}
