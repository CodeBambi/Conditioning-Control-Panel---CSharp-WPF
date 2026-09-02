using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS ▸ DATA, ported from the WPF head: offline mode, phrase backup, the cloud-backup
    /// signpost and the Danger Zone.
    ///
    /// On WPF the first three rows forward to MainWindow partials, and the factory reset is a
    /// double-confirmed (WarningDialog + typed keyword) settings-only wipe followed by a
    /// <c>cmd.exe</c> relaunch. All of it reaches <c>App.*</c>, WPF dialogs or Win32, so every
    /// handler here is a stub. A stub that silently pretended to reset would be worse than none,
    /// so the button draws but does nothing until the reset lives in Core.
    /// </summary>
    public partial class DataSettingsSection : UserControl
    {
        public DataSettingsSection()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // ponytail: needs MainWindow.ChkOfflineMode_Changed / PresetIO partials, wired when they move to Core
        private void ChkOfflineMode_Changed(object? sender, RoutedEventArgs e) { }
        private void BtnExportPhrases_Click(object? sender, RoutedEventArgs e) { }
        private void BtnImportPhrases_Click(object? sender, RoutedEventArgs e) { }

        // ponytail: needs the AppSettings host's FocusSection("account"), wired when the settings shell is ported
        private void BtnGoToAccountBackup_Click(object? sender, RoutedEventArgs e) { }

        // ponytail: needs App.Settings.SealForReset, App.Lockdown, WarningDialog, InputDialog and a
        // per-platform relaunch; wired when the factory reset moves to Core
        private void BtnFactoryReset_Click(object? sender, RoutedEventArgs e) { }
    }
}
