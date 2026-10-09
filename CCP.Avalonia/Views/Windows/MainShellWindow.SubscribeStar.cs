// PORTED from ConditioningControlPanel/MainWindow/MainWindow.SubscribeStar.cs (121 lines), spread where this
// head keeps each half:
//
//   InitializeSubscribeStarTab   AccountSettingsSection subscribes to AccountSeed.SubscribeStar.TierChanged while
//                                attached; App.axaml.cs repaints the veils/vault on it; MainShellWindow.Patreon.cs
//                                InitializePremiumCelebration hooks the celebration.
//   OnSubscribeStarTierChanged   Those three handlers (each posts to the UI thread, as WPF BeginInvoke does).
//   UpdateSubscribeStarUI        AccountSettingsSection.RefreshProviders ("SubscribeStar" card), through Loc.
//   BtnSubscribeStarLogin_Click  AccountSettingsSection.ProviderClickAsync("substar"): sign out (the whole account
//                                when no provider is left) or the unified login dialog.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Deliberately empty - see the header.
    }
}
