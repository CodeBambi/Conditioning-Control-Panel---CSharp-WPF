// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs (1067 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (28):
//   private void BtnStart_Click(…)
//   private void BtnStartMenu_Click(…)
//   private void MenuStartNormal_Click(…)
//   private void MenuJumpRightIn_Click(…)
//   internal void RandomizeAndStart(…)
//   private static readonly string[] FlashImageExtensions
//   private static readonly string[] WallpaperImageExtensions
//   private static bool FolderHasAnyMedia(…)
//   private static bool IsLocalOnlyMediaSource(…)
//   private static void OpenPackCatalogue(…)
//   private void WarnIfFlashLibraryEmpty(…)
//   internal void WarnIfWallpaperLibraryEmpty(…)
//   public void StartEngine(…)
//   private bool _stopInProgress
//   private DateTime? _emiEngineStartedUtc
//   public void StopEngine(…)
//   private void StopEngineCore(…)
//   private void StartRampTimer(…)
//   private void StopRampTimer(…)
//   private void RampTimer_Tick(…)
//   private void CheckSchedulerOnStartup(…)
//   private void CheckSchedulerAfterSettingsChange(…)
//   private void SchedulerTimer_Tick(…)
//   private bool IsInScheduledTimeWindow(…)
//   private void ApplySettingsLive(…)
//   private void UpdateStartButton(…)
//   private void UpdateStartButtonForRemoteControl(…)
//   private static T? FindVisualChild<T>(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.StartStop.cs; wired when they move to Core.
        private void BtnStartMenu_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.StartStop.cs; wired when they move to Core.
        private void BtnStart_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.StartStop.cs; wired when they move to Core.
        private void MenuJumpRightIn_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.StartStop.cs; wired when they move to Core.
        private void MenuStartNormal_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
