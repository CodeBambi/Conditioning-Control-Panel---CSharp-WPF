using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS · ACCOUNT, ported from the WPF head.
    ///
    /// On WPF every Click is a one-line forward to the identically-named MainWindow handler and
    /// <c>RefreshTierBadge</c> reads <c>App.IsLoggedIn</c> / <c>App.Patreon</c>. None of that is
    /// reachable from this head, so the handlers are stubs and the tier card keeps its signed-out
    /// markup defaults. The x:Names are preserved so wiring is mechanical once a host exists.
    /// </summary>
    public partial class AccountSettingsSection : UserControl
    {
        public AccountSettingsSection()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // ponytail: needs ConditioningControlPanel/Services/Account/PatreonService.cs (the tier and
        // the entitlement) plus an App.IsLoggedIn twin. Neither is in Core and this head seeds no
        // account seam at all, so the badge stays at its signed-out markup default - which is the
        // honest reading of "we cannot tell whether you are signed in".
        internal void RefreshTierBadge() { }

        // ponytail: needs ConditioningControlPanel/MainWindow/MainWindow.Patreon.cs and its
        // SubscribeStar / CloudBackup siblings, all of which drive PatreonService and the WPF
        // OAuth browser flow. CCP.Avalonia/Views/Dialogs/LoginDialog.axaml.cs is this head's
        // device-code prompt and is the surface to hang these on once a provider exists.
        private void BtnPatreonLogin_Click(object? sender, RoutedEventArgs e) { }
        private void BtnSubscribeStarLogin_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDiscordLogin_Click(object? sender, RoutedEventArgs e) { }
        private void BtnLinkPatreon_Click(object? sender, RoutedEventArgs e) { }
        private void BtnLinkDiscord_Click(object? sender, RoutedEventArgs e) { }
        private void BtnBackupSettingsNow_Click(object? sender, RoutedEventArgs e) { }
        private void BtnRestoreSettings_Click(object? sender, RoutedEventArgs e) { }
        private void BtnExportData_Click(object? sender, RoutedEventArgs e) { }
        private void BtnPrivacyPolicy_Click(object? sender, RoutedEventArgs e) { }
        private void BtnVisitPatreon_Click(object? sender, RoutedEventArgs e) { }
    }
}
