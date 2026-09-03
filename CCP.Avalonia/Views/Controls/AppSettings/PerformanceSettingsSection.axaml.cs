using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS ▸ PERFORMANCE, ported from the WPF head.
    ///
    /// Every handler on the WPF control either forwards to <c>MainWindow</c> or writes
    /// <c>App.Settings.Current</c> and saves; the DND picker enumerates windowed processes through
    /// <c>Services/UI/DoNotDisturbGuard</c> (Win32). None of that exists on this head yet, so the
    /// handlers are stubs and the view is a rendering proof. The WPF <c>_isLoading</c> seed guard
    /// is omitted for the same reason: there is nothing to seed from and nothing to guard.
    /// </summary>
    public partial class PerformanceSettingsSection : UserControl
    {
        public PerformanceSettingsSection()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // ponytail: needs App.Settings + MainWindow.Chk*_Changed forwards, wired when they move to Core
        private void ChkPerformanceMode_Changed(object? sender, RoutedEventArgs e) { }
        private void ChkAutoPerformance_Changed(object? sender, RoutedEventArgs e) { }
        private void ChkUnifiedOverlay_Changed(object? sender, RoutedEventArgs e) { }
        private void ChkVideoHwDecode_Changed(object? sender, RoutedEventArgs e) { }

        // ponytail: needs MainWindow.CmbMotionLevel_SelectionChanged (stops ambient loops), wired when it moves to Core
        private void CmbMotionLevel_SelectionChanged(object? sender, SelectionChangedEventArgs e) { }

        // ponytail: needs App.Settings + DoNotDisturbGuard.Parse/FormatProcessList, wired when they move to Core
        private void TxtDndProcesses_LostFocus(object? sender, RoutedEventArgs e) { }
        private void ChkDndSuppressVideos_Changed(object? sender, RoutedEventArgs e) { }
        private void ChkDndSuppressFlashes_Changed(object? sender, RoutedEventArgs e) { }

        // ponytail: needs DoNotDisturbGuard.RunningWindowedProcesses (Win32 window enumeration); per-platform in the head
        private void BtnDndPickApp_Click(object? sender, RoutedEventArgs e) { }
    }
}
