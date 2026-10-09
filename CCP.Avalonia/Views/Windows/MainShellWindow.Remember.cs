// Ported from ConditioningControlPanel/MainWindow/MainWindow.Remember.cs: the bottom bar's one-slot
// "Remember" button. Click snapshots the current setup into AppSettings.RememberedConfigJson when the
// slot is empty and recalls it when filled; right-click re-saves over the slot. Both directions are
// refused mid-session (WPF Remember.cs:33/44).
//
// Differences, both deliberate:
//   * Browser mute: the saved flag is written exactly as WPF does, but not applied to a live web
//     view - WebHost has no mute (MainShellWindow.Browser.cs, "REFUSED ... the mute pair").
//   * Tooltips are localized (WPF hardcodes English) and re-applied on a language change.
// WPF's RefreshDashboardRail after a recall becomes CoreEngine.Reconcile, as PresetsTabView.LoadPreset does.

using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF MainWindow.xaml.cs:3508 (SyncRememberButton at startup), plus the language hook.</summary>
        private void InitializeRememberButton()
        {
            SyncRememberButton();
            EventHandler onLanguage = (_, _) => SyncRememberButton();
            LocalizationManager.Instance.LanguageChanged += onLanguage;
            Closed += (_, _) => LocalizationManager.Instance.LanguageChanged -= onLanguage;
        }

        internal void BtnRemember_Click(object? sender, RoutedEventArgs e)
        {
            if (RefuseActionIfSessionLocked("remember")) return;
            if (string.IsNullOrEmpty(CoreSettings.Current.RememberedConfigJson)) SnapshotRememberedConfig();
            else RecallRememberedConfig();
        }

        /// <summary>Right-click re-saves the current setup over the slot (overwrite).</summary>
        internal void BtnRemember_RightClick(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Right) return;
            e.Handled = true;
            if (RefuseActionIfSessionLocked("remember-overwrite")) return;
            SnapshotRememberedConfig();
        }

        private void SnapshotRememberedConfig()
        {
            var s = CoreSettings.Current;
            var rc = new RememberedConfig
            {
                Preset = Models.Preset.FromSettings(s, "Remembered"),
                Takeover = Named<Tabs.BambiTakeoverTabView>("BambiTakeoverTab")?.ChkAutonomyEnabled.IsChecked == true,
                Awareness = Named<Tabs.AwarenessTabView>("AwarenessTab")?.ChkAwarenessMaster.IsChecked == true,
                Haptics = StudioRack?.PanelHaptics.ChkHapticsEnabled.IsChecked == true,
                BrowserMuted = s.BrowserVideoMuted
            };
            s.RememberedConfigJson = JsonConvert.SerializeObject(rc);
            CoreSettings.Save();
            SyncRememberButton();
        }

        private void RecallRememberedConfig()
        {
            var s = CoreSettings.Current;
            var json = s.RememberedConfigJson;
            if (string.IsNullOrEmpty(json)) return;

            RememberedConfig? rc;
            try { rc = JsonConvert.DeserializeObject<RememberedConfig>(json); }
            catch (Exception ex) { Log.Debug("Remember slot unreadable: {Error}", ex.Message); return; }
            if (rc == null) return;

            // Conditioning config (Preset.ApplyTo touches only config, never progression).
            if (rc.Preset != null)
            {
                // Mid-Lockdown the strict flags stay on (#1282).
                bool strictBefore = s.StrictLockEnabled, bubbleStrictBefore = s.BubbleCountStrictLock;
                rc.Preset.ApplyTo(s);
                ConditioningControlPanel.Services.LockdownStrictHold.RestoreAfterApply(s, strictBefore, bubbleStrictBefore);
            }

            s.BrowserVideoMuted = rc.BrowserMuted;

            // Premium toggles go through the tab checkboxes so consent / gating / service start run
            // exactly as a manual toggle would (WPF SetPremiumFeature).
            SetRememberedToggle(Named<Tabs.BambiTakeoverTabView>("BambiTakeoverTab")?.ChkAutonomyEnabled, rc.Takeover);
            SetRememberedToggle(Named<Tabs.AwarenessTabView>("AwarenessTab")?.ChkAwarenessMaster, rc.Awareness);
            SetRememberedToggle(StudioRack?.PanelHaptics.ChkHapticsEnabled, rc.Haptics);

            CoreSettings.Save();
            CoreEngine.Reconcile();
        }

        private static void SetRememberedToggle(CheckBox? cb, bool target)
        {
            if (cb != null && (cb.IsChecked == true) != target) cb.IsChecked = target;
        }

        /// <summary>Remember button glyph/tooltip reflect a filled slot (WPF SyncRememberButton).</summary>
        internal void SyncRememberButton()
        {
            var filled = !string.IsNullOrEmpty(CoreSettings.Current.RememberedConfigJson);
            if (Named<TextBlock>("TxtRememberIcon") is { } icon) icon.Text = filled ? "★" : "☆";
            if (Named<Button>("BtnRemember") is { } btn)
                ToolTip.SetTip(btn, Loc.Get(filled ? "tooltip_remember_recall" : "tooltip_remember_save"));
        }
    }
}
