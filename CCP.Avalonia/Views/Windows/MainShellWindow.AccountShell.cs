// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.AccountShell.cs (506 lines).
//
// Most account/update members still need services that are not on this head. Language is different:
// CoreSettings and LocalizationManager are already cross-platform, so the two selectors share the
// WPF head's single writer and persistence path here.

using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.AccountShell.cs; wired when they move to Core.
        private void BtnAwareness_Click(object? sender, RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.AccountShell.cs; wired when they move to Core.
        private void BtnPatreonExclusives_Click(object? sender, RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.AccountShell.cs; wired when they move to Core.
        private void BtnUpdateAvailable_Click(object? sender, RoutedEventArgs e) { }

        private bool _syncingLanguageSelectors;

        /// <summary>Populates the chrome pill after the saved language has been loaded.</summary>
        private void InitializeLanguageSelector()
        {
            PopulateLanguageCombo(Named<ComboBox>("CmbLanguagePill"), shortLabels: true);
            SyncLanguageSelectors(CoreSettings.Current.Language);
        }

        private void PopulateLanguageCombo(ComboBox? combo, bool shortLabels)
        {
            if (combo is null) return;

            _syncingLanguageSelectors = true;
            try
            {
                combo.Items.Clear();
                var current = CoreSettings.Current.Language;
                var selectedIndex = 0;

                for (var i = 0; i < LocalizationManager.AvailableLanguages.Length; i++)
                {
                    var (code, displayName, shortName) = LocalizationManager.AvailableLanguages[i];
                    var item = new ComboBoxItem
                    {
                        Content = shortLabels ? $"🌐 {shortName}" : displayName,
                        Tag = code,
                    };
                    ToolTip.SetTip(item, displayName);
                    combo.Items.Add(item);
                    if (code == current) selectedIndex = i;
                }

                combo.SelectedIndex = selectedIndex;
            }
            finally
            {
                _syncingLanguageSelectors = false;
            }
        }

        private void CmbLanguagePill_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_syncingLanguageSelectors) return;
            if (Named<ComboBox>("CmbLanguagePill")?.SelectedItem is not ComboBoxItem selected) return;
            ApplyLanguageSelection(selected.Tag as string);
        }

        /// <summary>
        /// The single writer of the saved language. Both selectors call this method, then both are
        /// re-selected under the guard so Avalonia's SelectionChanged event cannot save an echo.
        /// </summary>
        internal void ApplyLanguageSelection(string? langCode)
        {
            if (_syncingLanguageSelectors) return;
            var code = string.IsNullOrWhiteSpace(langCode) ? "en" : langCode!;
            var settings = CoreSettings.Current;

            if (settings.Language != code)
            {
                settings.Language = code;
                LocalizationManager.Instance.SetLanguage(code);
                CoreSettings.Save();

                // Match WPF: the live bindings update now, while code-behind strings finish on a
                // restart. This control is absent on the headless view paths, so the notification
                // remains safe before a Window has been shown.
                var banner = Named<TextBlock>("TxtBannerSecondary");
                if (banner is not null)
                {
                    banner.Text = Loc.Get("msg_restart_to_apply");
                    banner.Opacity = 1;
                    banner.IsHitTestVisible = true;
                }
            }

            SyncLanguageSelectors(code);
        }

        private void SyncLanguageSelectors(string langCode)
        {
            _syncingLanguageSelectors = true;
            try
            {
                Select(Named<ComboBox>("CmbLanguagePill"));
                var general = AppSettingsPage?.FindControl<GeneralSettingsSection>("SectionGeneral");
                Select(general?.LanguageSelector);
            }
            finally
            {
                _syncingLanguageSelectors = false;
            }

            void Select(ComboBox? combo)
            {
                if (combo is null) return;
                foreach (var item in combo.Items)
                {
                    if (item is ComboBoxItem cbi && (cbi.Tag as string) == langCode)
                    {
                        combo.SelectedItem = cbi;
                        return;
                    }
                }
            }
        }
    }
}
