using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The two doors the launcher needs on the panel: a way to tuck the panel away with the engine
    /// still running, and the one real exit path, promoted from the tray's Exit lambda so the
    /// launcher and the tray leave through the same door.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// When the engine last started (UTC), or null while it is stopped. The launcher's status
        /// line reads it; the field itself belongs to MainWindow.StartStop.cs.
        /// </summary>
        public DateTime? EngineStartedUtc => _emiEngineStartedUtc;

        /// <summary>
        /// Hide the panel to the tray for the launcher. Same as close-to-tray, minus nothing: the
        /// engine, the session and every overlay keep going. The tray icon stays the way back.
        /// </summary>
        /// <param name="quiet">True at boot, where the panel was never on screen and the tray's
        /// one-time "minimized" balloon would be wrong.</param>
        public void HideForLauncher(bool quiet = false)
        {
            if (App.Lockdown?.IsActive == true) return;
            try { _trayIcon?.MinimizeToTray(balloon: !quiet); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Launcher] MinimizeToTray failed; hiding directly"); Hide(); }
            // Whatever a fade left behind, the next show (the tray, a remote, a game) is opaque.
            RestoreRootOpacity();
        }

        // ---- the crossfade with the launcher ----
        // Window.Opacity is dead with AllowsTransparency off, so the fades run on RootGrid. The
        // one rule: RootGrid is never left under 1 while the window is on screen. Every fade
        // restores it in Completed and again from a guard timer, and HideForLauncher restores it
        // on the way out, so ShowFromTray from the tray itself always shows a whole panel.

        private const int LauncherFadeInMs = 260;
        private const int LauncherFadeOutMs = 180;
        private const int LauncherFadeGuardMs = 150;
        private DispatcherTimer? _launcherFadeGuard;
        private bool _launcherFadeOutPending;

        /// <summary>The launcher opening the panel: show it from the tray, then fade it up.</summary>
        internal void ShowFromLauncher()
        {
            // A panel already on screen (a footer link while it is up) is not re-faded.
            bool animate = MotionFx.AllowTransitions && !IsVisible;
            if (animate) RootGrid.Opacity = 0;
            try { ShowFromTray(); }
            finally
            {
                if (animate) FadeRoot(0, 1, LauncherFadeInMs, null);
                else RestoreRootOpacity();
            }
        }

        /// <summary>
        /// The panel's way back to the launcher: fade to nothing, then <paramref name="then"/>
        /// (which hides). Runs <paramref name="then"/> at once under reduced motion or when the
        /// panel is not on screen. A second press while a fade-out is in flight is ignored.
        /// </summary>
        internal void FadeOutForLauncher(Action then)
        {
            if (_launcherFadeOutPending) return;
            if (!MotionFx.AllowTransitions || !IsVisible) { then(); return; }
            _launcherFadeOutPending = true;
            FadeRoot(1, 0, LauncherFadeOutMs, () => { _launcherFadeOutPending = false; then(); });
        }

        private void FadeRoot(double from, double to, int ms, Action? then)
        {
            bool done = false;
            void Finish()
            {
                if (done) return;
                done = true;
                _launcherFadeGuard?.Stop();
                try { then?.Invoke(); }
                catch (Exception ex) { App.Logger?.Error(ex, "[Launcher] step after panel fade failed"); }
                finally { RestoreRootOpacity(); }
            }

            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new QuadraticEase { EasingMode = to > from ? EasingMode.EaseOut : EasingMode.EaseIn },
            };
            anim.Completed += (_, _) => Finish();

            _launcherFadeGuard?.Stop();
            _launcherFadeGuard = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(ms + LauncherFadeGuardMs),
            };
            _launcherFadeGuard.Tick += (_, _) => Finish();
            _launcherFadeGuard.Start();

            RootGrid.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void RestoreRootOpacity()
        {
            try
            {
                _launcherFadeOutPending = false;
                RootGrid.BeginAnimation(UIElement.OpacityProperty, null);
                RootGrid.Opacity = 1;
            }
            catch (Exception ex) { App.Logger?.Debug(ex, "[Launcher] RootGrid opacity restore failed"); }
        }

        /// <summary>
        /// Leave the process the way the tray's Exit does. Refused during Lockdown. Stops the
        /// engine, kills audio, disposes the overlay, restores the session and saves before
        /// <see cref="Application.Shutdown"/>.
        /// </summary>
        public void RequestExit()
        {
            if (App.Lockdown?.IsActive == true) return;

            _exitRequested = true;
            if (_isRunning) StopEngine();

            App.KillAllAudio();

            try { App.Overlay?.Dispose(); }
            catch { }

            EnsureSessionRestoredForExit();
            SaveSettings();
            Application.Current.Shutdown();
        }

        /// <summary>The panel's title-bar "back to client" button.</summary>
        private void BtnBackToLauncher_Click(object sender, RoutedEventArgs e)
        {
            Services.Launcher.LauncherHost.BackToLauncher();
        }
    }
}
