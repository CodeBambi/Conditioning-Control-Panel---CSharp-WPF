// The tube's two CCBill gates, ported from WPF:
//   - the personality submenu behind ExplicitContentAcknowledgementDialog
//     (ConditioningControlPanel/AvatarTube/AvatarTubeWindow.ChatInput.cs:1191 PopulatePersonalityMenu,
//     :1284 PersonalityMenuItem_Click, remote lock at :1486), and
//   - ContentPolicyWarningDialog on the moderation counter's WarningTriggered
//     (ConditioningControlPanel/AvatarTube/AvatarTubeWindow.xaml.cs:374 WireModerationCounter, :396).

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Moderation;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private IModerationCounter? _warningCounter;

        private void WireContentPolicyWarning()
        {
            _warningCounter = CoreModerationLog.Counter;
            if (_warningCounter != null) _warningCounter.WarningTriggered += OnWarningTriggered;
        }

        private void UnwireContentPolicyWarning()
        {
            if (_warningCounter != null) _warningCounter.WarningTriggered -= OnWarningTriggered;
            _warningCounter = null;
        }

        private void OnWarningTriggered(ModerationCounterState state) =>
            Dispatcher.UIThread.Post(() => _ = ShowContentPolicyWarningAsync(state.HitsInLastTenMinutes));

        private async Task ShowContentPolicyWarningAsync(int hits)
        {
            try
            {
                // WPF Owner = _parentWindow. ShowDialogSafe falls back to any visible window, or shows it
                // unowned: with the shell and tube hidden in the tray a plain ShowDialog would throw.
                await new ContentPolicyWarningDialog(hits).ShowDialogSafe<bool>(_parentWindow);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AvatarTubeWindow: failed to show ContentPolicyWarningDialog");
            }
        }

        private static IBrush AccentBrush => new SolidColorBrush(Color.TryParse(CoreMods.AccentColorHex, out var c) ? c : Color.Parse("#FF69B4"));

        private static string PersonalityName(string name) => CoreMods.Service?.GetPersonalityDisplayName(name) ?? name;

        /// <summary>WPF PopulatePersonalityMenu, plus the remote-controller lock UpdateQuickMenuState applies.</summary>
        private void PopulatePersonalityMenu()
        {
            var root = this.FindControl<MenuItem>("MenuItemPersonality");
            if (root == null) return;
            root.Items.Clear();
            var darkBg = this.TryFindResource("PanelBgBrush", out var bg) ? bg as IBrush : null;
            var settings = CoreSettings.Current;

            if (settings.CompanionPrompt?.UseCustomPrompt == true)
            {
                var orange = new SolidColorBrush(Color.FromRgb(255, 165, 0));
                root.Header = Loc.Get("label_personality_custom_prompt");
                root.Foreground = orange;
                root.Items.Add(new MenuItem { Header = new TextBlock { Text = Loc.Get("menu_custom_prompt_active") }, Foreground = orange, Background = darkBg, IsEnabled = false });
                root.Items.Add(new Separator { Background = AccentBrush });
                var disable = new MenuItem { Header = new TextBlock { Text = Loc.Get("menu_disable_custom_prompt") }, Foreground = Brushes.White, Background = darkBg };
                disable.Click += (_, _) =>
                {
                    // Both halves, or the Companion tab keeps reading back "Custom: <name>".
                    PersonalityService.ClearCustomPromptOverride(CoreSettings.Current);
                    CoreSettings.Save();
                    PopulatePersonalityMenu();
                    Giggle(Loc.Get("avatar_back_to_presets"));
                };
                root.Items.Add(disable);
            }
            else
            {
                root.Foreground = AccentBrush;
                var activeId = settings.ActivePersonalityPresetId ?? PersonalityPresets.NeutralDefaultId;
                foreach (var preset in PersonalityService.Shared.GetAllPresets().Where(p => p.Id != PersonalityPresets.SlutModeId))
                {
                    var item = new MenuItem
                    {
                        // A TextBlock, not a string: Avalonia reads "_" in a string header as an access key.
                        Header = new TextBlock { Text = $"{(preset.Id == activeId ? "☑" : "☐")} {PersonalityName(preset.Name)}" },
                        Tag = preset.Id,
                        Background = darkBg,
                        Foreground = preset.Id == activeId ? AccentBrush : Brushes.White,
                    };
                    item.Click += (_, _) => _ = ActivatePresetFromMenuAsync(preset.Id);
                    root.Items.Add(item);
                }
                root.Header = Loc.GetF("avatar_personality_format", PersonalityName(PersonalityService.Shared.GetActivePreset()?.Name ?? "BambiSprite"));
            }

            // Lock while a remote controller is connected (WPF ChatInput.cs:1486).
            var remote = RemoteControlTabView.Relay.IsValueCreated && RemoteControlTabView.Relay.Value.ControllerConnected;
            root.IsEnabled = !remote;
            if (remote) root.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x70));
        }

        /// <summary>WPF PersonalityMenuItem_Click: the CCBill acknowledgement gate, then the switch.</summary>
        internal async Task ActivatePresetFromMenuAsync(string presetId)
        {
            var preset = PersonalityService.Shared.GetPresetById(presetId);
            if (preset == null) return;

            var settings = CoreSettings.Current;
            if (ExplicitContentGate.RequiresAcknowledgement(preset, settings.SlutModeEnabled))
            {
                var promptSettings = settings.CompanionPrompt;
                if (!ExplicitContentGate.IsAlreadyAcknowledged(promptSettings))
                {
                    // Cancel, Esc and the close box all yield false: nothing switches.
                    if (!await new ExplicitContentAcknowledgementDialog().ShowDialog<bool>(this)) return;
                    if (promptSettings != null)
                    {
                        ExplicitContentGate.MarkAcknowledged(promptSettings);
                        CoreSettings.Save();
                    }
                }
            }

            if (PersonalityService.Shared.SetActivePreset(presetId))
            {
                PopulatePersonalityMenu();
                Giggle(CoreMods.MakeModAware($"Now using {PersonalityName(preset.Name)}~ *giggles*"));
            }
        }
    }
}
