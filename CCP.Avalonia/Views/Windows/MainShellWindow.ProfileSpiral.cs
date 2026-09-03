// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.ProfileSpiral.cs (231 lines).
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
//   private bool _profileSpiralWired
//   private static bool SpiralWithheld
//   private void WireProfileSpiral(…)
//   private void UnwireProfileSpiral(…)
//   private void OnSpiralBlockChanged(…)
//   internal void RefreshSpiralGlyphMotion(…)
//   internal void RefreshProfileSpiralPlate(…)
//   internal void RefreshProfileMenuSpiral(…)
//   private static string BuildSpiralSummary(…)
//   internal void OpenSpiralMapFromProfile(…)
//   private void ProfileMenuSpiral_Click(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.ProfileSpiral.cs; wired when they move to Core.
        private void ProfileMenuSpiral_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
