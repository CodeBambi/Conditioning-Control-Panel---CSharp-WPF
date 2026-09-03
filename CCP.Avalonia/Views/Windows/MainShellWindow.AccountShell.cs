// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.AccountShell.cs (506 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (24):
//   internal void BtnPatreonExclusives_Click(…)
//   private static bool IsVisualDescendant(…)
//   internal void ShowAccountSettings(…)
//   internal void ShowAppInfoPopup(…)
//   private void BtnAwareness_Click(…)
//   private async void BtnQuickPatreonLogin_Click(…)
//   private async Task HandleQuickPatreonLoginAsync(…)
//   private void UpdateQuickPatreonUI(…)
//   private async void BtnQuickDiscordLogin_Click(…)
//   private async Task HandleDiscordLoginAsync(…)
//   private void SetDiscordButtonsEnabled(…)
//   private void SetDiscordButtonsContent(…)
//   private void UpdateQuickDiscordUI(…)
//   internal void BtnDiscord_Click(…)
//   internal void ChkDiscordRichPresence_Changed(…)
//   private bool _syncingLanguageSelectors
//   private void InitializeLanguageSelector(…)
//   private void PopulateLanguageCombo(…)
//   private void CmbLanguagePill_SelectionChanged(…)
//   internal void ApplyLanguageSelection(…)
//   private void SyncLanguageSelectors(…)
//   internal async void BtnCheckUpdates_Click(…)
//   private async void BtnUpdateAvailable_Click(…)
//   public void ShowUpdateAvailableButton(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.AccountShell.cs; wired when they move to Core.
        private void BtnAwareness_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.AccountShell.cs; wired when they move to Core.
        private void BtnPatreonExclusives_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.AccountShell.cs; wired when they move to Core.
        private void BtnUpdateAvailable_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.AccountShell.cs; wired when they move to Core.
        private void CmbLanguagePill_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e) { }

    }
}
