using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// SETTINGS · MONITORS (nav rework, REHOME lane). The monitor picker and the four video rows
    /// lifted out of the retired Home System popup (<c>Features/SystemFeatureControl</c>). Same
    /// settings keys and the same write-then-save idiom; <c>_isLoading</c> starts true so the
    /// first paint never echoes a write.
    /// </summary>
    public partial class MonitorsSettingsSection : UserControl
    {
        private bool _isLoading = true;

        public MonitorsSettingsSection()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadFromSettings();
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged -= OnSettingsPropertyChanged;
                inpc.PropertyChanged += OnSettingsPropertyChanged;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged -= OnSettingsPropertyChanged;
        }

        internal void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkFillAllMon.IsChecked = s.FillAllMonitorsWithVideo;
                ChkVideoGpuDecode.IsChecked = s.VideoForceHardwareDecoding;
                ChkVideoBlurBg.IsChecked = s.VideoBlurredBackgroundEnabled;
                ChkBrowserVideoEngine.IsChecked = s.BrowserVideoEngineEnabled;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.FillAllMonitorsWithVideo) ||
                e.PropertyName == nameof(Models.AppSettings.VideoForceHardwareDecoding) ||
                e.PropertyName == nameof(Models.AppSettings.VideoBlurredBackgroundEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BrowserVideoEngineEnabled))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkFillAllMon_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FillAllMonitorsWithVideo = ChkFillAllMon.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkVideoGpuDecode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.VideoForceHardwareDecoding = ChkVideoGpuDecode.IsChecked ?? false;
            App.Settings?.Save();
            App.Logger?.Information("Force video GPU decode set to {Enabled} (Settings > Monitors)", s.VideoForceHardwareDecoding);
        }

        private void ChkVideoBlurBg_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.VideoBlurredBackgroundEnabled = ChkVideoBlurBg.IsChecked ?? true;
            App.Settings?.Save();
            App.Logger?.Information("Blurred video background set to {Enabled} (Settings > Monitors)", s.VideoBlurredBackgroundEnabled);
        }

        private void ChkBrowserVideoEngine_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BrowserVideoEngineEnabled = ChkBrowserVideoEngine.IsChecked ?? false;
            App.Settings?.Save();
            App.Logger?.Information("Browser video engine set to {Enabled} (Settings > Monitors)", s.BrowserVideoEngineEnabled);
        }
    }
}
