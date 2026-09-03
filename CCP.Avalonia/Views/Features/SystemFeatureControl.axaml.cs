using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// System panel, ported from the WPF head, settings logic restored against
    /// <see cref="CoreSettings"/>. The five live toggles write and save exactly as WPF's did, and
    /// the five read-out rows are painted by <see cref="LoadFromSettings"/> from the real settings
    /// and kept current by the PropertyChanged subscription, as on WPF.
    ///
    /// <para>The read-outs are read-only by design since Phase 2 of the UX restructure: Settings ▸
    /// General owns the startup four and the startup video, Settings ▸ Devices owns the panic key,
    /// Settings ▸ Data owns offline mode. This panel shows them and links there.</para>
    ///
    /// <para>What is still head-side is named at each handler: the four "configure in settings"
    /// buttons and the assets-folder picker need the shell's navigation, which is stubbed in
    /// <c>MainShellWindow</c> on this head too. Opening the assets folder works: it is
    /// <see cref="CorePaths.EffectiveAssets"/> plus the platform launcher.</para>
    /// </summary>
    public partial class SystemFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public SystemFeatureControl()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and everything below reads them.
            InitializeComponent();

            ChkMultiMon.IsCheckedChanged += ChkMultiMon_Changed;
            ChkFillAllMon.IsCheckedChanged += ChkFillAllMon_Changed;
            ChkVideoGpuDecode.IsCheckedChanged += ChkVideoGpuDecode_Changed;
            ChkVideoBlurBg.IsCheckedChanged += ChkVideoBlurBg_Changed;
            ChkBrowserVideoEngine.IsCheckedChanged += ChkBrowserVideoEngine_Changed;

            BtnOpenGeneralSettings.Click += BtnOpenGeneralSettings_Click;
            BtnOpenGeneralSettingsVideo.Click += BtnOpenGeneralSettings_Click;
            BtnOpenDeviceSettingsNoPanic.Click += BtnOpenDeviceSettings_Click;
            BtnOpenDataSettings.Click += BtnOpenDataSettings_Click;
            BtnPickAssets.Click += BtnPickAssets_Click;
            BtnOpenAssets.Click += BtnOpenAssets_Click;

            LoadFromSettings();
        }

        // ---- settings instance tracking (WPF hooked App.Settings.Current directly here) --------

        private AppSettings? _hooked;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            RebindToCurrentSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            Unhook();
            base.OnDetachedFromVisualTree(e);
        }

        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(RebindToCurrentSettings);

        private void RebindToCurrentSettings()
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

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
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
                // Settings door. Painting is all that is left; the PropertyChanged subscription
                // keeps it current while the panel is mounted.
                TxtStartupGroupState.Text = DescribeStartupGroup(s);
                TxtOfflineModeState.Text = Loc.Get(s.OfflineMode ? "set2_chip_on" : "set2_chip_off");

                TxtStartupVideo.Text = string.IsNullOrEmpty(s.StartupVideoPath)
                    ? Loc.Get("label_random")
                    : Path.GetFileName(s.StartupVideoPath);

                // Panic key: read-only mirror for the same reason. Settings ▸ Devices owns the
                // toggle, the rebind and the keyboard hook.
                TxtPanicKeyState.Text = $"🔑 {s.PanicKey}";
                TxtNoPanicState.Text = Loc.Get(s.PanicKeyEnabled ? "set2_chip_off" : "set2_chip_on");
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.DualMonitorEnabled) ||
                e.PropertyName == nameof(AppSettings.FillAllMonitorsWithVideo) ||
                e.PropertyName == nameof(AppSettings.VideoForceHardwareDecoding) ||
                e.PropertyName == nameof(AppSettings.VideoBlurredBackgroundEnabled) ||
                e.PropertyName == nameof(AppSettings.BrowserVideoEngineEnabled) ||
                e.PropertyName == nameof(AppSettings.ForceVideoOnLaunch) ||
                e.PropertyName == nameof(AppSettings.AutoStartEngine) ||
                e.PropertyName == nameof(AppSettings.StartMinimized) ||
                e.PropertyName == nameof(AppSettings.PanicKeyEnabled) ||
                e.PropertyName == nameof(AppSettings.PanicKey) ||
                e.PropertyName == nameof(AppSettings.OfflineMode) ||
                e.PropertyName == nameof(AppSettings.StartupVideoPath) ||
                e.PropertyName == nameof(AppSettings.RunOnStartup))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        // ---- Simple local toggles (write directly to settings) ----

        private void ChkMultiMon_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.DualMonitorEnabled = ChkMultiMon.IsChecked ?? false;
            CoreSettings.Save();
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
            Log.Information("Force video GPU decode set to {Enabled} (System popup)", s.VideoForceHardwareDecoding);
        }

        private void ChkVideoBlurBg_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            s.VideoBlurredBackgroundEnabled = ChkVideoBlurBg.IsChecked ?? true;
            CoreSettings.Save();
            Log.Information("Blurred video background set to {Enabled} (System popup)", s.VideoBlurredBackgroundEnabled);
        }

        private void ChkBrowserVideoEngine_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            s.BrowserVideoEngineEnabled = ChkBrowserVideoEngine.IsChecked ?? false;
            CoreSettings.Save();
            Log.Information("Browser video engine set to {Enabled} (System popup)", s.BrowserVideoEngineEnabled);
        }

        /// <summary>
        /// One line summarising the four startup switches for the read-only row. Each is named only
        /// when it is ON, so the common "nothing special" case reads as a single calm phrase instead
        /// of four "Off"s.
        /// </summary>
        private static string DescribeStartupGroup(AppSettings s)
        {
            var parts = new List<string>();
            // ponytail: WPF asks Services.StartupManager.IsRegistered() (a Windows Startup-folder
            // shortcut) rather than the stored flag. No equivalent on this head; the stored value
            // stands in, exactly as GeneralSettingsSection does for the same switch.
            if (s.RunOnStartup) parts.Add(Loc.Get("setting_win_start"));
            if (s.StartMinimized) parts.Add(Loc.Get("setting_start_hidden"));
            if (s.AutoStartEngine) parts.Add(Loc.Get("setting_auto_run"));
            if (s.ForceVideoOnLaunch) parts.Add(Loc.Get("setting_vid_launch"));
            return parts.Count == 0
                ? Loc.Get("set2_startup_group_none")
                : string.Join(" · ", parts);
        }

        // ponytail: the four navigation buttons need the shell's OpenAppSettingsSection /
        // OpenDeviceSettings / RequestPickAssetsFolder. Both heads still owe them: they are
        // commented-out stubs in CCP.Avalonia/Views/Windows/MainShellWindow.Settings.cs,
        // .LabTab.cs and .axaml.cs, and live methods on MainWindow in the WPF head.
        private void BtnOpenDeviceSettings_Click(object? sender, RoutedEventArgs e) { }

        private void BtnOpenGeneralSettings_Click(object? sender, RoutedEventArgs e) { }

        private void BtnOpenDataSettings_Click(object? sender, RoutedEventArgs e) { }

        private void BtnPickAssets_Click(object? sender, RoutedEventArgs e) { }

        private void BtnOpenAssets_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = CorePaths.EffectiveAssets;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    // UseShellExecute hands the folder to the platform's file manager - xdg-open
                    // on Linux, Explorer on Windows. Same call the WPF original made.
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to open assets folder from popup");
            }
        }
    }
}
