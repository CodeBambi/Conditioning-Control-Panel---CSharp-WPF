using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS ▸ PERFORMANCE, ported from the WPF head, settings logic restored against
    /// <see cref="CoreSettings"/>. Five forwards that on WPF hop through MainWindow only to write
    /// a setting write it here directly; the do-not-disturb rows are the same live editors they
    /// were. The <c>_isLoading</c> seed guard is kept: Avalonia raises IsCheckedChanged on a
    /// programmatic set exactly as WPF raised Checked, and a seed without it saves defaults over
    /// the user's file.
    ///
    /// Still a stub, named: the motion-level change on WPF also stops the ambient loops through
    /// MainWindow (they are FX partials, not on this head), and the DND app picker enumerates
    /// windows through Win32.
    /// </summary>
    public partial class PerformanceSettingsSection : UserControl
    {
        private bool _isLoading = true;

        public PerformanceSettingsSection()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and the seed below reads them.
            InitializeComponent();
            SyncFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            base.OnDetachedFromVisualTree(e);
        }

        // A cloud restore or a factory reset swaps the instance; repaint from it, on the UI thread.
        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

        internal void SyncFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkPerformanceMode.IsChecked = s.PerformanceMode;
                ChkAutoPerformance.IsChecked = s.AutoPerformanceMode;
                ChkUnifiedOverlay.IsChecked = s.UnifiedOverlayHost;
                ChkVideoHwDecode.IsChecked = s.VideoForceHardwareDecoding;
                // MotionLevel's ordinal IS the item index (Full=0, Reduced=1, Off=2). Clamped rather
                // than trusted so a settings file from a future build cannot throw here.
                var index = (int)s.MotionLevel;
                CmbMotionLevel.SelectedIndex = index >= 0 && index < CmbMotionLevel.ItemCount ? index : 0;
                // The textbox is a VIEW of the normalised list, repainted from settings rather than
                // left holding whatever was last typed.
                TxtDndProcesses.Text = DndProcessList.Format(s.DndProcessList);
                ChkDndSuppressVideos.IsChecked = s.DndSuppressVideos;
                ChkDndSuppressFlashes.IsChecked = s.DndSuppressFlashes;
            }
            finally { _isLoading = false; }
        }

        private void ChkPerformanceMode_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.PerformanceMode = ChkPerformanceMode.IsChecked ?? false;
            Log.Information("Performance mode set to {Enabled}", CoreSettings.Current.PerformanceMode);
            CoreSettings.Save();
        }

        private void ChkAutoPerformance_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.AutoPerformanceMode = ChkAutoPerformance.IsChecked ?? true;
            Log.Information("Auto performance mode set to {Enabled}", CoreSettings.Current.AutoPerformanceMode);
            CoreSettings.Save();
        }

        private void ChkUnifiedOverlay_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.UnifiedOverlayHost = ChkUnifiedOverlay.IsChecked ?? true;
            Log.Information("Unified overlay renderer set to {Enabled}", CoreSettings.Current.UnifiedOverlayHost);
            CoreSettings.Save();
        }

        private void ChkVideoHwDecode_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.VideoForceHardwareDecoding = ChkVideoHwDecode.IsChecked ?? false;
            Log.Information("Force video hardware decoding set to {Enabled}", CoreSettings.Current.VideoForceHardwareDecoding);
            CoreSettings.Save();
        }

        private void CmbMotionLevel_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var level = CmbMotionLevel.SelectedIndex switch
            {
                1 => MotionLevel.Reduced,
                2 => MotionLevel.Off,
                _ => MotionLevel.Full,
            };
            CoreSettings.Current.MotionLevel = level;
            Log.Information("Motion level set to {Level}", level);
            // ponytail: WPF also stops the ambient loops here (season shimmer, skill tree, program
            // banner) through MainWindow's FX partials, which are not on this head.
            CoreSettings.Save();
        }

        private void TxtDndProcesses_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var parsed = DndProcessList.Parse(TxtDndProcesses.Text);
            CoreSettings.Current.DndProcessList = parsed;
            CoreSettings.Save();
            _isLoading = true;
            try { TxtDndProcesses.Text = DndProcessList.Format(parsed); }
            finally { _isLoading = false; }
        }

        private void ChkDndSuppressVideos_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.DndSuppressVideos = ChkDndSuppressVideos.IsChecked == true;
            CoreSettings.Save();
        }

        private void ChkDndSuppressFlashes_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.DndSuppressFlashes = ChkDndSuppressFlashes.IsChecked == true;
            CoreSettings.Save();
        }

        // ponytail: needs DoNotDisturbGuard.RunningWindowedProcesses (Win32 window enumeration); per-platform in the head
        private void BtnDndPickApp_Click(object? sender, RoutedEventArgs e) { }
    }
}
