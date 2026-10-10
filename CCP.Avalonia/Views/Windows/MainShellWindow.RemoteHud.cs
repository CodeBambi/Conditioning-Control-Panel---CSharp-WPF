// PORTED from ConditioningControlPanel/MainWindow/MainWindow.RemoteHud.cs (7.1.5).
// Remote Control v2: the subject's HUD pill (RemoteHudWindow). Created when a controller connects,
// disposed when the shell closes (an unowned visible window would keep the process alive).
using System;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.RemoteHud;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private RemoteHudWindow? _remoteHud;
        internal RemoteHudWindow? RemoteHud => _remoteHud;

        /// <summary>Creates the HUD on first need and lets it re-read the service.</summary>
        private void EnsureRemoteHud()
        {
            try
            {
                _remoteHud ??= new RemoteHudWindow(RemoteControlTabView.Relay.Value, StopFromRemoteHud, () => this);
                _remoteHud.Refresh();
            }
            catch (Exception ex) { Serilog.Log.Warning("RemoteHud: could not show: {E}", ex.Message); }
        }

        /// <summary>Lets the HUD re-read the service (it hides itself when nobody holds the remote).</summary>
        private void RefreshRemoteHud()
        {
            try { _remoteHud?.Refresh(); } catch (Exception ex) { Serilog.Log.Debug("RemoteHud refresh failed: {E}", ex.Message); }
        }

        /// <summary>Closes the HUD for good.</summary>
        private void DisposeRemoteHud()
        {
            var hud = _remoteHud;
            _remoteHud = null;
            try { hud?.Dispose(); } catch (Exception ex) { Serilog.Log.Debug("RemoteHud dispose failed: {E}", ex.Message); }
        }

        /// <summary>
        /// The HUD's Stop, local half: exactly what the panic key would do right now. With the panic key on,
        /// the real panic press; with it off, only the leash safety a leashed panic-off press gets; otherwise
        /// nothing local (the signal still stops the remote's effects). Never more permissive than the key.
        /// </summary>
        internal void StopFromRemoteHud()
        {
            if (RemoteHudRules.LocalPanicAllowed(CoreSettings.Current.PanicKeyEnabled))
            {
                Serilog.Log.Information("[RemoteHud] Stop pressed - running the panic path");
                HandlePanicKeyPress(DateTime.Now);
            }
            else if (Platform.LeashHead.IsLeashed)
            {
                Serilog.Log.Information("[RemoteHud] Stop pressed with the panic key off - leash safety only");
                Platform.LeashTaskHost.OnPanicPress(panicRuns: false);
            }
            else Serilog.Log.Information("[RemoteHud] Stop pressed with the panic key off - signal only");
        }
    }
}
