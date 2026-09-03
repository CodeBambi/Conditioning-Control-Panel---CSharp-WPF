// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.Remember.cs (119 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (11):
//   public Models.Preset? Preset
//   public bool Takeover
//   public bool Awareness
//   public bool Haptics
//   public bool BrowserMuted
//   internal void BtnRemember_Click(…)
//   internal void BtnRemember_RightClick(…)
//   private void SnapshotRememberedConfig(…)
//   private void RecallRememberedConfig(…)
//   private void SetPremiumFeature(…)
//   internal void SyncRememberButton(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.Remember.cs; wired when they move to Core.
        private void BtnRemember_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.Remember.cs; wired when they move to Core.
        private void BtnRemember_RightClick(object? sender, global::Avalonia.Input.PointerReleasedEventArgs e) { }

    }
}
