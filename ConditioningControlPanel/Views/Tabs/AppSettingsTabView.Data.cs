using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Compat passthroughs for SETTINGS ▸ DATA (Workshop-cell precedent, see the header of
    /// AppSettingsTabView.xaml.cs).
    ///
    /// <para><c>ChkOfflineMode</c> is the interesting one: three MainWindow methods
    /// (<c>RequestToggleOfflineMode</c>, <c>ApplyOfflineMode</c>, <c>SyncOfflineModeState</c>) set it
    /// by name specifically so the two-way sync - DisconnectNetworkServices, the login-button
    /// greying, the update banner - runs exactly once per change. That is why the control moved with
    /// its x:Name instead of being rebuilt, and why the twin in
    /// Features/SystemFeatureControl.xaml became a read-only mirror.</para>
    ///
    /// <para>The phrase-backup buttons and the Danger Zone are NOT exposed here: no MainWindow
    /// partial addresses them by name. The Ctrl+K palette finds them by walking the visual tree
    /// (Windows/SettingsPaletteWindow.xaml.cs), which crosses UserControl namescopes, so a
    /// passthrough would buy nothing.</para>
    /// </summary>
    public partial class AppSettingsTabView
    {
        /// <summary>Read/written by MainWindow.Settings.cs (Load/Save), .UiUpdates.cs
        /// (ChkOfflineMode_Changed) and .xaml.cs (RequestToggleOfflineMode / ApplyOfflineMode /
        /// SyncOfflineModeState).</summary>
        internal CheckBox ChkOfflineMode => SectionData.ChkOfflineMode;
    }
}
