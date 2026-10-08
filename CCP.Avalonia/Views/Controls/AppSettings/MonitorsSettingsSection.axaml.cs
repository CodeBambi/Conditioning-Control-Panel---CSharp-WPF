using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS · MONITORS, PORTED from WPF 7.1.5 MonitorsSettingsSection (nav rework, REHOME lane).
    /// The monitor picker and the four video rows lifted out of the retired Home System popup. Same
    /// settings keys and the same write-then-save idiom; <c>_isLoading</c> starts true so the first
    /// paint never echoes a write. Follows <see cref="AppSettings.PropertyChanged"/> so the second
    /// GPU-decode editor on this page (Performance) and this one cannot drift.
    /// </summary>
    public partial class MonitorsSettingsSection : UserControl
    {
        private bool _isLoading = true;
        private Models.AppSettings? _hooked;

        public MonitorsSettingsSection()
        {
            InitializeComponent();
            ChkFillAllMon.IsCheckedChanged += ChkFillAllMon_Changed;
            ChkVideoGpuDecode.IsCheckedChanged += ChkVideoGpuDecode_Changed;
            ChkVideoBlurBg.IsCheckedChanged += ChkVideoBlurBg_Changed;
            ChkBrowserVideoEngine.IsCheckedChanged += ChkBrowserVideoEngine_Changed;
            LoadFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            Rebind();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            Unhook();
            base.OnDetachedFromVisualTree(e);
        }

        // A cloud restore or a factory reset swaps the instance; follow the new one.
        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(Rebind);

        private void Rebind()
        {
            Unhook();
            _hooked = CoreSettings.Current;
            _hooked.PropertyChanged += OnSettingsPropertyChanged;
            LoadFromSettings();
        }

        private void Unhook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsPropertyChanged;
            _hooked = null;
        }

        internal void LoadFromSettings()
        {
            var s = CoreSettings.Current;
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
            if (e.PropertyName is nameof(Models.AppSettings.FillAllMonitorsWithVideo)
                or nameof(Models.AppSettings.VideoForceHardwareDecoding)
                or nameof(Models.AppSettings.VideoBlurredBackgroundEnabled)
                or nameof(Models.AppSettings.BrowserVideoEngineEnabled))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        private void ChkFillAllMon_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.FillAllMonitorsWithVideo = ChkFillAllMon.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void ChkVideoGpuDecode_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            s.VideoForceHardwareDecoding = ChkVideoGpuDecode.IsChecked ?? false;
            CoreSettings.Save();
            Log.Information("Force video GPU decode set to {Enabled} (Settings > Monitors)", s.VideoForceHardwareDecoding);
        }

        private void ChkVideoBlurBg_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            s.VideoBlurredBackgroundEnabled = ChkVideoBlurBg.IsChecked ?? true;
            CoreSettings.Save();
            Log.Information("Blurred video background set to {Enabled} (Settings > Monitors)", s.VideoBlurredBackgroundEnabled);
        }

        private void ChkBrowserVideoEngine_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            s.BrowserVideoEngineEnabled = ChkBrowserVideoEngine.IsChecked ?? false;
            CoreSettings.Save();
            Log.Information("Browser video engine set to {Enabled} (Settings > Monitors)", s.BrowserVideoEngineEnabled);
        }
    }
}
