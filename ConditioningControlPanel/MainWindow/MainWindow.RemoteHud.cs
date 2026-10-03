using System;
using System.Windows.Interop;
using ConditioningControlPanel.RemoteHud;

namespace ConditioningControlPanel
{
    // Remote Control v2: the subject's HUD pill (Windows/RemoteHud). Created when a controller
    // connects, disposed when the session ends and on a real exit.
    public partial class MainWindow
    {
        private RemoteHudWindow? _remoteHud;

        /// <summary>Creates the HUD on first need and lets it re-read the service.</summary>
        private void EnsureRemoteHud()
        {
            try
            {
                var service = App.RemoteControl;
                if (service == null) return;
                _remoteHud ??= new RemoteHudWindow(service, StopFromRemoteHud, () => new WindowInteropHelper(this).Handle);
                _remoteHud.Refresh();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("RemoteHud: could not show: {E}", ex.Message);
            }
        }

        /// <summary>Lets the HUD re-read the service (it hides itself when nobody holds the remote).</summary>
        private void RefreshRemoteHud()
        {
            try { _remoteHud?.Refresh(); } catch (Exception ex) { App.Logger?.Debug("RemoteHud refresh failed: {E}", ex.Message); }
        }

        /// <summary>Closes the HUD for good. An unowned visible window would hold OnLastWindowClose open.</summary>
        private void DisposeRemoteHud()
        {
            var hud = _remoteHud;
            _remoteHud = null;
            try { hud?.Dispose(); } catch (Exception ex) { App.Logger?.Debug("RemoteHud dispose failed: {E}", ex.Message); }
        }

        /// <summary>
        /// The HUD's Stop, local half: exactly what the panic key would do right now. With the panic
        /// key on, the real panic press; with it off, only the leash safety a leashed panic-off press
        /// gets; otherwise nothing local (the signal still stops the remote's effects).
        /// </summary>
        private void StopFromRemoteHud()
        {
            var settings = App.Settings?.Current;
            if (RemoteHudRules.LocalPanicAllowed(settings?.PanicKeyEnabled ?? true))
            {
                App.Logger?.Information("[RemoteHud] Stop pressed - running the panic path");
                HandlePanicKeyPress();
            }
            else if (Controls.Leash.LeashSurfaces.IsLeashed)
            {
                App.Logger?.Information("[RemoteHud] Stop pressed with the panic key off - leash safety only");
                LeashOnPanicPress(panicRuns: false);
            }
            else
            {
                App.Logger?.Information("[RemoteHud] Stop pressed with the panic key off - signal only");
            }
        }
    }
}
