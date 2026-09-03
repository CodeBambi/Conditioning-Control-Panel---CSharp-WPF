// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.SpiralRoom.cs (157 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (10):
//   private const string SpiralRailAnonymousLabel
//   private static readonly Brush SpiralRailAnonymousBrush
//   private bool _spiralRoomWired
//   private void InitializeSpiralRoom(…)
//   private void OnSpiralRoomPhaseChanged(…)
//   private void OnSpiralRoomBlockChanged(…)
//   private void OnSpiralRoomLanguageChanged(…)
//   internal void RefreshSpiralRailEntry(…)
//   private void BtnNavSpiral_Click(…)
//   internal void BeginSpiralFirstLight(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.SpiralRoom.cs; wired when they move to Core.
        private void BtnNavSpiral_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
