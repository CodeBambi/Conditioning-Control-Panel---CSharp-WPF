using System;
using System.Windows;

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
