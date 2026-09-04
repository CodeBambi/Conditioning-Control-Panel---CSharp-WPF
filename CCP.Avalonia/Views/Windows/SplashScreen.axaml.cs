using System;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Splash screen shown during application startup while services initialize.
    ///
    /// PORTED from ConditioningControlPanel/Windows/SplashScreen.xaml.cs. Deviations:
    ///  - THE OWN-THREAD TRICK IS GONE. The WPF original ran on a dedicated STA thread with its
    ///    own dispatcher so it kept animating while App.OnStartup blocked the main UI thread.
    ///    Avalonia has exactly one UI thread per process and no per-window dispatcher, so
    ///    <see cref="ShowOnOwnThread"/> now just shows the window on the UI thread. The fix for
    ///    the problem it solved is to make startup async rather than to fork a UI thread; that
    ///    belongs to whoever ports App.OnStartup, not here.
    ///  - Every public member stays thread-safe the same way, marshalling with
    ///    <c>Dispatcher.UIThread.Post</c> (never a blocking Invoke) instead of the window's own
    ///    dispatcher.
    ///  - <c>DragMove()</c> -> <c>BeginMoveDrag</c>, which needs the PointerPressed args.
    ///  - <c>BeginAnimation(OpacityProperty, ...)</c> -> an <see cref="Animation"/> run with
    ///    <c>RunAsync</c>; Avalonia has no per-property BeginAnimation.
    ///  - The shimmer sweep moved into the XAML as a Style.Animations block.
    ///  - The named <c>ScaleTransform</c> became a field: Avalonia generates fields only for
    ///    named Controls, so the transform is built here and assigned to ProgressFill.
    ///
    /// <para>NO CALLER ON THIS HEAD, and the call site is not missing - it is out of reach. WPF
    /// owns the splash entirely from <c>ConditioningControlPanel/App.xaml.cs</c>: the field
    /// <c>_splash</c> (line 83), <c>_splash = SplashScreen.ShowOnOwnThread()</c> in OnStartup
    /// (line 1539), the <c>SetProgress</c> calls threaded through service initialisation, and
    /// <c>FadeOutAndClose</c> when the main window appears. The Avalonia twin of that file is
    /// <c>CCP.Avalonia/App.axaml.cs</c>, and the splash belongs in
    /// <c>OnFrameworkInitializationCompleted</c> immediately before
    /// <c>desktop.MainWindow = new MainShellWindow()</c> - not on MainShellWindow, which by
    /// definition exists only after the startup this window is meant to cover. That file is
    /// off-limits to this layer, so the call is named here rather than put somewhere it would be
    /// a defect. There is also nothing yet for <c>SetProgress</c> to report: this head's startup
    /// is one settings service and five seam assignments, all synchronous.</para>
    /// </summary>
    public partial class SplashScreen : Window
    {
        private readonly TextBlock _txtStatus, _txtHint;
        private readonly ScaleTransform _progressScale = new ScaleTransform(0, 1);

        private double _targetProgress;
        private double _displayedProgress;
        private DispatcherTimer? _creepTimer;
        private DispatcherTimer? _reassureTimer;
        private int _reassureStage;
        private bool _closing;

        public SplashScreen()
        {
            AvaloniaXamlLoader.Load(this);

            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            _txtHint = this.FindControl<TextBlock>("TxtHint")!;
            this.FindControl<Border>("ProgressFill")!.RenderTransform = _progressScale;

            // WPF's `TxtVersion.Text = $"v{UpdateService.AppVersion}"`. UpdateService stays in the
            // WPF head (it is the installer's release constants), but the version it reported is
            // the CoreReleaseContent seam, which App.axaml.cs seeds from this assembly's own
            // version - so the splash reports the build that is actually running. Unseeded the
            // seam answers "0.0.0", which is honest rather than a stale literal.
            this.FindControl<TextBlock>("TxtVersion")!.Text = $"v{CoreReleaseContent.AppVersion}";

            // ponytail: placeholder progress. App.OnStartup is the only caller of SetProgress and
            // it is still in the WPF head, so nothing drives the bar in this head yet. Seeding a
            // mid-load value keeps the gradient visible in the render proof; delete this whole
            // four-line block when startup calls SetProgress.
            _targetProgress = 0.62;
            _displayedProgress = 0.62;
            _progressScale.ScaleX = 0.62;
            _txtStatus.Text = "Loading services...";

            // Let impatient clicks do something harmless: drag the splash around.
            PointerPressed += (_, e) => { try { BeginMoveDrag(e); } catch { } };

            // Progress creep: advance the displayed bar toward the reported target, and
            // when the target itself is stalled (a long init step), keep inching a few
            // percent past it so the bar never looks frozen mid-load.
            _creepTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Render, (_, _) => CreepTick());
            _creepTimer.Start();

            // Reassurance text: on a long load, tell the user explicitly that the app
            // is fine and will open on its own.
            _reassureTimer = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Normal, (_, _) => ReassureTick());
            _reassureTimer.Start();

            Closed += (_, _) =>
            {
                _closing = true;
                try { _creepTimer?.Stop(); } catch { }
                try { _reassureTimer?.Stop(); } catch { }
            };
        }

        /// <summary>
        /// Create and show the splash. Returns null if it could not be created (startup then
        /// simply proceeds without one — every caller uses ?. access).
        ///
        /// The name is kept so the WPF call sites port across unchanged, but there is no longer
        /// an own thread: see the class remarks.
        /// </summary>
        public static SplashScreen? ShowOnOwnThread()
        {
            try
            {
                var splash = new SplashScreen();
                splash.Show();
                return splash;
            }
            catch
            {
                // A cosmetic window must never take the app down.
                return null;
            }
        }

        /// <summary>
        /// Update the progress bar target and status text. Safe to call from any thread.
        /// </summary>
        /// <param name="progress">Progress value from 0.0 to 1.0</param>
        /// <param name="status">Status message to display</param>
        public void SetProgress(double progress, string status)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => SetProgress(progress, status));
                return;
            }
            if (_closing) return;

            _txtStatus.Text = status;
            _targetProgress = Math.Min(1.0, Math.Max(0.0, progress));
        }

        /// <summary>
        /// Close the splash screen with a fade-out animation. Safe to call from any thread.
        /// The optional callback fires after the window has closed.
        /// </summary>
        public void FadeOutAndClose(Action? afterClosed = null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => FadeOutAndClose(afterClosed));
                return;
            }
            if (_closing) { afterClosed?.Invoke(); return; }
            _closing = true;

            // Drop Topmost first so windows/dialogs appearing behind the fade
            // (What's New, Age Verification) aren't hidden under the splash.
            try { Topmost = false; } catch { }

            if (afterClosed != null)
                Closed += (_, _) => { try { afterClosed(); } catch { } };

            FadeThenClose();
        }

        private async void FadeThenClose()
        {
            var fadeOut = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(200),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 1.0) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 0.0) } },
                },
            };

            try { await fadeOut.RunAsync(this); } catch { }
            try { Close(); } catch { }
        }

        /// <summary>
        /// Close the splash immediately, no animation. Safe to call from any thread.
        /// Used by early-exit paths (second instance) and error handlers.
        /// </summary>
        public void CloseImmediate()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(CloseImmediate);
                return;
            }
            _closing = true;
            try { Close(); } catch { }
        }

        private void CreepTick()
        {
            if (_closing) return;

            if (_displayedProgress < _targetProgress)
            {
                // Catch up to a newly-reported target quickly but smoothly.
                _displayedProgress = Math.Min(_targetProgress,
                    _displayedProgress + Math.Max(0.008, (_targetProgress - _displayedProgress) * 0.22));
            }
            else if (_targetProgress < 1.0)
            {
                // Target is stalled (a slow init step): idle-creep up to 5% past it,
                // capped at 99%, so the bar visibly keeps moving.
                double cap = Math.Min(_targetProgress + 0.05, 0.99);
                if (_displayedProgress < cap)
                    _displayedProgress = Math.Min(cap, _displayedProgress + 0.0015);
            }

            _progressScale.ScaleX = _displayedProgress;
        }

        private void ReassureTick()
        {
            if (_closing) return;
            _reassureStage++;
            switch (_reassureStage)
            {
                case 1:
                    _txtHint.Text = "Still loading... the app is fine and will open on its own.";
                    break;
                case 2:
                    _txtHint.Text = "Almost there. Large libraries can take a minute to warm up.";
                    _reassureTimer?.Stop();
                    break;
            }
        }
    }
}
