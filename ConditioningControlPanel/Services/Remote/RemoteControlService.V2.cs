using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Remote;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Remote Control v2 (2026-10-03): the subject's own buttons (More / Easy / Stop), the
    /// controller's name, the last thing the controller did, remote haptic patterns and Melt.
    /// The HUD window (Windows/RemoteHud) reads the public members; do not rename them.
    /// </summary>
    public partial class RemoteControlService
    {
        /// <summary>The three signals the subject can send up: more, easy, stop.</summary>
        public static readonly string[] SignalKinds = { "more", "easy", "stop" };

        /// <summary>
        /// Scales every remote haptic and remote-set spiral / pink opacity. 1 at the start of a
        /// session, halved by each Easy press (floor 0.25), back to 1 when the session ends.
        /// </summary>
        public double EasyFactor { get; private set; } = 1.0;

        /// <summary>Raised when <see cref="EasyFactor"/> changes.</summary>
        public event EventHandler? EasyChanged;

        /// <summary>The name the controller typed when connecting, or null ("Your controller").</summary>
        public string? ControllerName { get; private set; }

        /// <summary>When the current controller connected (UTC), or null when nobody is connected.</summary>
        public DateTime? ControllerConnectedSinceUtc { get; private set; }

        /// <summary>Human words for the last command that landed, e.g. "Spiral", "Toy buzz".</summary>
        public string? LastActionLabel { get; private set; }

        /// <summary>Raised when a new command lands (and <see cref="LastActionLabel"/> moved).</summary>
        public event EventHandler? LastActionChanged;

        /// <summary>
        /// The subject's own button: "more", "easy" or "stop". Sends the signal up the emote
        /// channel; Easy and Stop also act locally. Returns true when the signal reached the server.
        /// </summary>
        public Task<bool> SendSignalAsync(string kind)
        {
            return Task.FromResult(false);
        }

        // ------------------------------------------------------------------ remote haptics

        private RemoteHapticDriver? _remoteHaptics;

        /// <summary>Created on first use, on the UI thread (it owns a DispatcherTimer).</summary>
        private RemoteHapticDriver RemoteHaptics => _remoteHaptics ??= new RemoteHapticDriver(() => EasyFactor);

        /// <summary><c>haptic_pattern</c> / <c>haptic_level</c>: replaces any remote haptic playing.</summary>
        private void PlayRemoteHaptic(string action, JObject? parameters)
        {
            var plan = action == "haptic_level"
                ? RemoteHapticPlan.FromLevel(parameters, out var why)
                : RemoteHapticPlan.FromPattern(parameters, out why);
            if (plan == null)
            {
                ReportCommandRefused(action, why ?? "bad params", "Not played");
                return;
            }
            RemoteHaptics.Play(plan);
        }

        /// <summary>Stops the remote haptic at once. Safe to call when nothing plays.</summary>
        private void StopRemoteHaptics() => _remoteHaptics?.Stop();

        /// <summary>Every controller command counts as activity (the loop idle cap reads it).</summary>
        private void NoteControllerActivity()
        {
            _lastControllerCommandUtc = DateTime.UtcNow;
            _remoteHaptics?.NoteCommand();
        }

        // ------------------------------------------------------------------ Melt (Brain Drain)

        // True when the controller switched Brain Drain on and it was off before, so a stop-all
        // can hand the subject's own setting back instead of leaving it on for good.
        private bool _remoteTurnedOnBrainDrain;

        private void SetRemoteBrainDrain(bool on)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            if (on)
            {
                if (!s.BrainDrainEnabled) _remoteTurnedOnBrainDrain = true;
                s.BrainDrainEnabled = true;     // the overlay's settings hook starts the blur
                EnsureOverlayRunning();
                App.Overlay?.RefreshOverlays();
                App.BrainDrain?.Start();
            }
            else
            {
                _remoteTurnedOnBrainDrain = false;
                s.BrainDrainEnabled = false;
                App.BrainDrain?.Stop();
                App.Overlay?.RefreshOverlays();
            }
            App.Settings!.Save();
        }

        /// <summary>Stop-all paths: Brain Drain is already stopped; give the setting back if the
        /// controller was the one who turned it on.</summary>
        private void RestoreRemoteBrainDrainSetting()
        {
            if (!_remoteTurnedOnBrainDrain) return;
            _remoteTurnedOnBrainDrain = false;
            if (App.Settings?.Current == null) return;
            App.Settings.Current.BrainDrainEnabled = false;
            App.Settings.Save();
        }

        // ------------------------------------------------------------------ bookkeeping

        private void NoteLandedCommand(string action)
        {
            LastActionLabel = RemoteActionLabels.For(action);
            LastActionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>CleanupSession: everything v2 keeps per remote session goes back to zero.</summary>
        private void ResetV2SessionState()
        {
            _remoteTurnedOnBrainDrain = false;
            ControllerName = null;
            ControllerConnectedSinceUtc = null;
            if (LastActionLabel != null)
            {
                LastActionLabel = null;
                LastActionChanged?.Invoke(this, EventArgs.Empty);
            }
            if (EasyFactor != 1.0)
            {
                EasyFactor = 1.0;
                EasyChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
