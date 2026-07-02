using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Splash screen shown during application startup while services initialize.
    ///
    /// Runs on its OWN STA thread with its own dispatcher (see ShowOnOwnThread).
    /// App.OnStartup does all service initialization synchronously on the main UI
    /// thread; a splash living on that same thread could not pump messages during
    /// that work, so its animations froze and a single click made Windows flag it
    /// "Not Responding". On a dedicated thread it stays responsive and animating
    /// no matter how long startup takes. All public members are thread-safe:
    /// they marshal to the splash dispatcher with BeginInvoke (never a blocking
    /// Invoke, so a busy splash can never stall startup either).
    /// </summary>
    public partial class SplashScreen : Window
    {
        private double _targetProgress;
        private double _displayedProgress;
        private DispatcherTimer? _creepTimer;
        private DispatcherTimer? _reassureTimer;
        private int _reassureStage;
        private bool _closing;

        public SplashScreen()
        {
            InitializeComponent();

            // Set version from UpdateService
            TxtVersion.Text = $"v{Services.UpdateService.AppVersion}";

            // Let impatient clicks do something harmless: drag the splash around.
            MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };

            // Progress creep: advance the displayed bar toward the reported target, and
            // when the target itself is stalled (a long init step), keep inching a few
            // percent past it so the bar never looks frozen mid-load.
            _creepTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _creepTimer.Tick += (_, _) => CreepTick();
            _creepTimer.Start();

            // Reassurance text: on a long load, tell the user explicitly that the app
            // is fine and will open on its own.
            _reassureTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(8)
            };
            _reassureTimer.Tick += (_, _) => ReassureTick();
            _reassureTimer.Start();

            Loaded += (_, _) => StartShimmer();
            Closed += (_, _) =>
            {
                _closing = true;
                try { _creepTimer?.Stop(); } catch { }
                try { _reassureTimer?.Stop(); } catch { }
            };
        }

        /// <summary>
        /// Create and show the splash on a dedicated STA UI thread and return once it
        /// is visible. Returns null if the splash could not be created (startup then
        /// simply proceeds without one — every caller uses ?. access).
        /// </summary>
        public static SplashScreen? ShowOnOwnThread()
        {
            SplashScreen? created = null;
            var ready = new ManualResetEventSlim(false);

            var thread = new Thread(() =>
            {
                try
                {
                    var splash = new SplashScreen();

                    // An unhandled exception on this dispatcher must never take the app
                    // down over a cosmetic window: swallow it and drop the splash.
                    splash.Dispatcher.UnhandledException += (_, args) =>
                    {
                        args.Handled = true;
                        try { splash.Close(); } catch { }
                    };

                    // End this thread's message loop when the window closes.
                    splash.Closed += (_, _) =>
                    {
                        try { splash.Dispatcher.InvokeShutdown(); } catch { }
                    };

                    splash.Show();
                    created = splash;
                    try { ready.Set(); } catch { }

                    Dispatcher.Run();
                }
                catch
                {
                    created = null;
                    try { ready.Set(); } catch { }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "SplashScreenUI";
            thread.Start();

            // Wait briefly for the window to come up so the user sees feedback before
            // heavy init begins. On timeout we still return the instance if it appears
            // later; worst case the splash is simply skipped.
            ready.Wait(TimeSpan.FromSeconds(5));
            return created;
        }

        /// <summary>
        /// Update the progress bar target and status text. Safe to call from any thread.
        /// </summary>
        /// <param name="progress">Progress value from 0.0 to 1.0</param>
        /// <param name="status">Status message to display</param>
        public void SetProgress(double progress, string status)
        {
            if (!Dispatcher.CheckAccess())
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(() => SetProgress(progress, status));
                return;
            }
            if (_closing) return;

            TxtStatus.Text = status;
            _targetProgress = Math.Min(1.0, Math.Max(0.0, progress));
        }

        /// <summary>
        /// Close the splash screen with a fade-out animation. Safe to call from any
        /// thread. The optional callback fires after the window has closed — note it
        /// runs ON THE SPLASH THREAD, so callers must marshal to their own dispatcher.
        /// </summary>
        public void FadeOutAndClose(Action? afterClosed = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(() => FadeOutAndClose(afterClosed));
                else
                    afterClosed?.Invoke();
                return;
            }
            if (_closing) { afterClosed?.Invoke(); return; }
            _closing = true;

            // Drop Topmost first so windows/dialogs appearing behind the fade
            // (What's New, Age Verification) aren't hidden under the splash.
            try { Topmost = false; } catch { }

            if (afterClosed != null)
                Closed += (_, _) => { try { afterClosed(); } catch { } };

            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(200)
            };

            fadeOut.Completed += (s, e) => { try { Close(); } catch { } };
            BeginAnimation(OpacityProperty, fadeOut);
        }

        /// <summary>
        /// Close the splash immediately, no animation. Safe to call from any thread.
        /// Used by early-exit paths (second instance) and error handlers.
        /// </summary>
        public void CloseImmediate()
        {
            if (!Dispatcher.CheckAccess())
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(CloseImmediate);
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

            ProgressScale.ScaleX = _displayedProgress;
        }

        private void ReassureTick()
        {
            if (_closing) return;
            _reassureStage++;
            switch (_reassureStage)
            {
                case 1:
                    TxtHint.Text = "Still loading... the app is fine and will open on its own.";
                    break;
                case 2:
                    TxtHint.Text = "Almost there. Large libraries can take a minute to warm up.";
                    _reassureTimer?.Stop();
                    break;
            }
        }

        private void StartShimmer()
        {
            try
            {
                // Sweep the highlight band across the full bar width forever. The bar is
                // window width minus 30+30 margin; use ActualWidth of the track's parent
                // via this window instead of hardcoding.
                double sweep = Math.Max(200, ActualWidth - 60);
                var anim = new DoubleAnimation
                {
                    From = -90,
                    To = sweep,
                    Duration = TimeSpan.FromSeconds(1.6),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                ShimmerTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
            }
            catch { }
        }
    }
}
