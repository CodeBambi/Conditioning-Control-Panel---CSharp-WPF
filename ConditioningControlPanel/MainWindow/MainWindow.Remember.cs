using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;

namespace ConditioningControlPanel
{
    /// <summary>
    /// One-slot "Remember" snapshot: the conditioning config (as a Preset, which is
    /// progression-safe by construction) plus the premium toggle states + browser mute.
    /// XP/level/streak are never captured, so a recall can't roll back progression.
    /// </summary>
    public class RememberedConfig
    {
        public Models.Preset? Preset { get; set; }
        public bool Takeover { get; set; }
        public bool Awareness { get; set; }
        public bool Haptics { get; set; }
        public bool BrowserMuted { get; set; }
    }

    // Header "Remember" button: snapshot the current setup and recall it in one tap.
    public partial class MainWindow
    {
        internal void BtnRemember_Click(object sender, RoutedEventArgs e)
        {
            // Both directions are refused mid-session, for two different reasons:
            // RECALL rewrites ~40 prescribed fields via Preset.ApplyTo, wiping the dose the
            // session is running. SNAPSHOT is subtler but just as wrong - it would capture the
            // SESSION's values as "the setup I like", quietly replacing the user's own slot with
            // whatever the program prescribed. Neither is meaningful until the session ends.
            // This button sits in the bottom bar right next to the live session timer, which is
            // exactly why it needs the loud refusal rather than a greyed-out no-op.
            if (RefuseActionIfSessionLocked("remember")) return;

            var json = App.Settings?.Current?.RememberedConfigJson;
            if (string.IsNullOrEmpty(json)) SnapshotRememberedConfig();
            else RecallRememberedConfig();
        }

        // Right-click re-saves the current setup over the slot (overwrite).
        internal void BtnRemember_RightClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (RefuseActionIfSessionLocked("remember-overwrite")) return;
            SnapshotRememberedConfig();
        }

        private void SnapshotRememberedConfig()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var rc = new RememberedConfig
            {
                Preset = Models.Preset.FromSettings(s, "Remembered"),
                Takeover = BambiTakeoverTab?.ChkAutonomyEnabled?.IsChecked == true,
                Awareness = AwarenessTab?.ChkAwarenessMaster?.IsChecked == true,
                Haptics = HapticsTab?.ChkHapticsEnabled?.IsChecked == true,
                BrowserMuted = s.BrowserVideoMuted
            };
            s.RememberedConfigJson = JsonConvert.SerializeObject(rc);
            App.Settings?.Save();
            SyncRememberButton();
        }

        private void RecallRememberedConfig()
        {
            var s = App.Settings?.Current;
            var json = s?.RememberedConfigJson;
            if (s == null || string.IsNullOrEmpty(json)) return;

            RememberedConfig? rc;
            try { rc = JsonConvert.DeserializeObject<RememberedConfig>(json); }
            catch { return; }
            if (rc == null) return;

            // Conditioning config (Preset.ApplyTo touches only config, never progression).
            rc.Preset?.ApplyTo(s);

            // Browser mute.
            s.BrowserVideoMuted = rc.BrowserMuted;
            if (_browser != null) _browser.IsAudioMuted = rc.BrowserMuted;
            SyncBrowserMuteIcon();

            // Premium toggles — routed through the tab checkboxes so consent / gating /
            // service-start all run exactly as a manual toggle would.
            SetPremiumFeature(PremiumFeature.Takeover, rc.Takeover);
            SetPremiumFeature(PremiumFeature.Awareness, rc.Awareness);
            SetPremiumFeature(PremiumFeature.Haptics, rc.Haptics);

            App.Settings?.Save();
            RefreshPremiumRail();
        }

        private void SetPremiumFeature(PremiumFeature f, bool target)
        {
            System.Windows.Controls.CheckBox? cb = f switch
            {
                PremiumFeature.Takeover => BambiTakeoverTab?.ChkAutonomyEnabled,
                PremiumFeature.Awareness => AwarenessTab?.ChkAwarenessMaster,
                PremiumFeature.Haptics => HapticsTab?.ChkHapticsEnabled,
                _ => null
            };
            if (cb != null && (cb.IsChecked == true) != target)
                cb.IsChecked = target;
        }

        /// <summary>Updates the Remember button glyph/tooltip to reflect a filled slot.</summary>
        internal void SyncRememberButton()
        {
            var filled = !string.IsNullOrEmpty(App.Settings?.Current?.RememberedConfigJson);
            if (TxtRememberIcon != null) TxtRememberIcon.Text = filled ? "★" : "☆";
            if (BtnRemember != null)
                BtnRemember.ToolTip = filled
                    ? "Recall my saved setup (right-click to re-save)"
                    : "Remember my current setup";
        }
    }
}
