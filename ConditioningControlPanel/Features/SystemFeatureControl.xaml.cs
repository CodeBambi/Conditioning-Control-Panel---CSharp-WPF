using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Features
{
    public partial class SystemFeatureControl : UserControl
    {
        private bool _isLoading;

        public SystemFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private MainWindow? Main => Application.Current?.MainWindow as MainWindow;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadFromSettings();
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += OnSettingsPropertyChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged -= OnSettingsPropertyChanged;
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkMultiMon.IsChecked = s.DualMonitorEnabled;
                ChkWinStart.IsChecked = Services.StartupManager.IsRegistered();
                ChkVidLaunch.IsChecked = s.ForceVideoOnLaunch;
                ChkAutoRun.IsChecked = s.AutoStartEngine;
                ChkStartHidden.IsChecked = s.StartMinimized;
                ChkNoPanic.IsChecked = !s.PanicKeyEnabled;
                ChkOfflineMode.IsChecked = s.OfflineMode;

                TxtStartupVideo.Text = string.IsNullOrEmpty(s.StartupVideoPath)
                    ? Loc.Get("label_random")
                    : Path.GetFileName(s.StartupVideoPath);

                BtnPanicKey.Content = $"🔑 {s.PanicKey}";
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.DualMonitorEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.ForceVideoOnLaunch) ||
                e.PropertyName == nameof(Models.AppSettings.AutoStartEngine) ||
                e.PropertyName == nameof(Models.AppSettings.StartMinimized) ||
                e.PropertyName == nameof(Models.AppSettings.PanicKeyEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.PanicKey) ||
                e.PropertyName == nameof(Models.AppSettings.OfflineMode) ||
                e.PropertyName == nameof(Models.AppSettings.StartupVideoPath) ||
                e.PropertyName == nameof(Models.AppSettings.RunOnStartup))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        // ---- Simple local toggles (write directly to settings) ----

        private void ChkMultiMon_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.DualMonitorEnabled = ChkMultiMon.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkVidLaunch_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.ForceVideoOnLaunch = ChkVidLaunch.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkAutoRun_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AutoStartEngine = ChkAutoRun.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkStartHidden_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.StartMinimized = ChkStartHidden.IsChecked ?? false;
            App.Settings?.Save();
        }

        // ---- Complex toggles delegated to MainWindow helpers ----

        private void ChkWinStart_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var enable = ChkWinStart.IsChecked ?? false;
            var actual = Main?.RequestToggleWindowsStartup(enable) ?? enable;
            if (actual != enable)
            {
                // Revert to reflect the authoritative StartupManager state.
                _isLoading = true;
                try { ChkWinStart.IsChecked = actual; }
                finally { _isLoading = false; }
            }
        }

        private void ChkNoPanic_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            Main?.RequestToggleNoPanic(ChkNoPanic.IsChecked ?? false);
            // The handler may revert if cancelled; reload from settings to stay in sync.
            Dispatcher.BeginInvoke(new Action(LoadFromSettings));
        }

        private void ChkOfflineMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            Main?.RequestToggleOfflineMode(ChkOfflineMode.IsChecked ?? false);
            Dispatcher.BeginInvoke(new Action(LoadFromSettings));
        }

        private void BtnPanicKey_Click(object sender, RoutedEventArgs e)
        {
            Main?.RequestBeginPanicKeyCapture();
        }

        // ---- Asset folder / startup video ----

        private void BtnPickAssets_Click(object sender, RoutedEventArgs e)
        {
            Main?.RequestPickAssetsFolder();
        }

        private void BtnOpenAssets_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = App.EffectiveAssetsPath;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open assets folder from popup");
            }
        }

        private void BtnSelectStartupVideo_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.Get("title_select_startup_video"),
                Filter = "Video Files|*.mp4;*.mov;*.avi;*.wmv;*.mkv;*.webm|All Files|*.*",
                InitialDirectory = Path.Combine(App.EffectiveAssetsPath ?? "", "videos")
            };

            if (dialog.ShowDialog() == true)
            {
                s.StartupVideoPath = dialog.FileName;
                TxtStartupVideo.Text = Path.GetFileName(dialog.FileName);
                App.Settings?.Save();
                App.Logger?.Information("Startup video set to: {Path}", dialog.FileName);
            }
        }

        private void BtnClearStartupVideo_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            s.StartupVideoPath = null;
            TxtStartupVideo.Text = Loc.Get("label_random");
            App.Settings?.Save();
            App.Logger?.Information("Startup video cleared - will use random");
        }
    }
}
