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
        private bool _isLoading = true;

        public SystemFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // Application.Current.MainWindow is null when MainWindow is hidden to tray;
        // App.MainWindowRef is set once in OnStartup and stays valid for the app lifetime.
        private MainWindow? Main => App.MainWindowRef ?? (Application.Current?.MainWindow as MainWindow);

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
                ChkFillAllMon.IsChecked = s.FillAllMonitorsWithVideo;
                ChkVideoGpuDecode.IsChecked = s.VideoForceHardwareDecoding;
                ChkVideoBlurBg.IsChecked = s.VideoBlurredBackgroundEnabled;
                ChkBrowserVideoEngine.IsChecked = s.BrowserVideoEngineEnabled;
                // Startup group + offline mode: read-only mirrors since Phase 2 of the UX
                // restructure. Both were second live editors of settings whose real owner is the
                // Settings door - the startup four because MainWindow addresses the dashboard copy
                // by name, offline mode because its two-way sync had to be spelled twice. Painting
                // is all that is left; the PropertyChanged subscription keeps it current while the
                // popup is open.
                TxtStartupGroupState.Text = DescribeStartupGroup(s);
                TxtOfflineModeState.Text = Loc.Get(s.OfflineMode ? "set2_chip_on" : "set2_chip_off");

                TxtStartupVideo.Text = string.IsNullOrEmpty(s.StartupVideoPath)
                    ? Loc.Get("label_random")
                    : Path.GetFileName(s.StartupVideoPath);

                // Panic key: read-only mirrors since Phase 2 of the UX restructure. The toggle and
                // the rebind button that used to live here were the second live editor of
                // PanicKeyEnabled / PanicKey (the other is Settings -> Devices), which meant the
                // double-confirm and the keyboard-hook start/stop were spelled twice. Painting is
                // all that is left, and the PropertyChanged subscription above keeps it current.
                TxtPanicKeyState.Text = $"🔑 {s.PanicKey}";
                TxtNoPanicState.Text = Loc.Get(s.PanicKeyEnabled ? "set2_chip_off" : "set2_chip_on");
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.DualMonitorEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.FillAllMonitorsWithVideo) ||
                e.PropertyName == nameof(Models.AppSettings.VideoForceHardwareDecoding) ||
                e.PropertyName == nameof(Models.AppSettings.VideoBlurredBackgroundEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BrowserVideoEngineEnabled) ||
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
            App.Logger?.Information("Force video GPU decode set to {Enabled} (System popup)", s.VideoForceHardwareDecoding);
        }

        private void ChkVideoBlurBg_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.VideoBlurredBackgroundEnabled = ChkVideoBlurBg.IsChecked ?? true;
            App.Settings?.Save();
            App.Logger?.Information("Blurred video background set to {Enabled} (System popup)", s.VideoBlurredBackgroundEnabled);
        }

        private void ChkBrowserVideoEngine_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BrowserVideoEngineEnabled = ChkBrowserVideoEngine.IsChecked ?? false;
            App.Settings?.Save();
            App.Logger?.Information("Browser video engine set to {Enabled} (System popup)", s.BrowserVideoEngineEnabled);
        }

        /// <summary>
        /// One line summarising the four startup switches for the read-only row. Each is named only
        /// when it is ON, so the common "nothing special" case reads as a single calm phrase instead
        /// of four "Off"s.
        /// </summary>
        private static string DescribeStartupGroup(Models.AppSettings s)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Services.StartupManager.IsRegistered()) parts.Add(Loc.Get("setting_win_start"));
            if (s.StartMinimized) parts.Add(Loc.Get("setting_start_hidden"));
            if (s.AutoStartEngine) parts.Add(Loc.Get("setting_auto_run"));
            if (s.ForceVideoOnLaunch) parts.Add(Loc.Get("setting_vid_launch"));
            return parts.Count == 0
                ? Loc.Get("set2_startup_group_none")
                : string.Join(" · ", parts);
        }

        // ChkVidLaunch_Changed / ChkAutoRun_Changed / ChkStartHidden_Changed / ChkWinStart_Changed
        // lived here. They were the second live editor of ForceVideoOnLaunch, AutoStartEngine,
        // StartMinimized and RunOnStartup - the first being the dashboard copies that MainWindow
        // addresses by x:Name. Phase 2 moved those copies into Settings -> General (one editor
        // each) and left this popup with the read-out above.
        // MainWindow.RequestToggleWindowsStartup survives: it is the safe way for any future
        // non-UI caller to flip the OS shortcut and the checkbox together.

        // ChkNoPanic_Changed lived here. It was a full second implementation of the panic-key
        // kill switch - double-confirm dialog, deferred revert, then Main.SyncNoPanicState() - for a
        // toggle that had a twin on the dashboard. Phase 2 gave PanicKeyEnabled one editor
        // (Settings -> Devices) and this control keeps only the read-out.

        // ChkOfflineMode_Changed lived here: a second copy of the offline-mode flow, username
        // prompt and all. Settings -> Data owns OfflineMode now (Phase 2); MainWindow's
        // ApplyOfflineMode / SyncOfflineModeState still exist for non-UI callers.

        // BtnPanicKey_Click lived here: a second launcher for MainWindow's panic-key capture,
        // with its own "Press any key..." state machine and a one-shot PropertyChanged listener to
        // confirm the new binding. Both launchers had to agree on that state, which is exactly the
        // kind of duplication Phase 2 removed - the rebind button is in Settings -> Devices.
        private void BtnOpenDeviceSettings_Click(object sender, RoutedEventArgs e) => Main?.OpenDeviceSettings();

        private void BtnOpenGeneralSettings_Click(object sender, RoutedEventArgs e) => Main?.OpenAppSettingsSection("general");

        private void BtnOpenDataSettings_Click(object sender, RoutedEventArgs e) => Main?.OpenAppSettingsSection("data");

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

        // BtnSelectStartupVideo_Click / BtnClearStartupVideo_Click lived here and were the second
        // editor of StartupVideoPath. The picker is in Settings -> General now (Phase 2); this
        // popup shows the current pick and links there.
    }
}
