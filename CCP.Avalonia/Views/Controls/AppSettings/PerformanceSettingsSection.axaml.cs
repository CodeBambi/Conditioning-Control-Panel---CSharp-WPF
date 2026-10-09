using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
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
    /// A motion-level change re-evaluates the loaded ambient loops through
    /// <see cref="global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.MotionGateChanged"/>, as WPF's
    /// MainWindow.CmbMotionLevel_SelectionChanged did. The DND app picker and the guard read X11
    /// window owners (Platform/X11Windows); the guard itself is Core DndGuard.
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
                // Back Room effects intensity: ordinal IS the item index (Calm=0, Normal=1, Full=2).
                var fx = (int)s.BackRoomFxIntensity;
                CmbBackRoomFxIntensity.SelectedIndex = fx >= 0 && fx < CmbBackRoomFxIntensity.ItemCount ? fx : 1;
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
            // The tier feeds Env.AllowAmbientLoops/AllowGlow, so running loops re-read it now
            // (improvement: WPF re-evaluated only on the next activation).
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
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
            CoreSettings.Save();
            // WPF stops the running ambient loops here (Reduced/Off) and re-arms them on Full.
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
        }

        /// <summary>No shell twin on WPF either: the Back Room reads it off settings on every fire.</summary>
        private void CmbBackRoomFxIntensity_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            int i = CmbBackRoomFxIntensity.SelectedIndex;
            if (i < 0) return;
            CoreSettings.Current.BackRoomFxIntensity = (global::ConditioningControlPanel.Services.BackRoom.BackRoomFxIntensity)i;
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

        /// <summary>The picker's source (X11 window owners). A seam so tests never read the desktop.</summary>
        internal static Func<List<string>> RunningApps = Platform.X11Windows.RunningWindowedProcesses;

        /// <summary>
        /// WPF BtnDndPickApp_Click (:172): a menu of every process that owns a window; already-listed
        /// ones are ticked and inert, a pick appends to the list.
        /// </summary>
        private void BtnDndPickApp_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var running = RunningApps();
                Log.Information("[DND] app picker opened: {Apps}", string.Join(", ", running));
                var menu = new ContextMenu { Placement = PlacementMode.Bottom, MaxHeight = 420 };
                if (running.Count == 0)
                {
                    menu.Items.Add(new MenuItem { Header = Loc.Get("set2_dnd_pick_empty"), IsEnabled = false });
                }
                else
                {
                    var already = CoreSettings.Current.DndProcessList ?? new List<string>();
                    foreach (var name in running)
                    {
                        // A TextBlock header: a bare string would lose its first '_' as an access key.
                        var item = new MenuItem { Header = new TextBlock { Text = name } };
                        if (already.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            item.ToggleType = MenuItemToggleType.CheckBox;
                            item.IsChecked = true;
                            item.IsEnabled = false;
                        }
                        else
                        {
                            var picked = name;
                            item.Click += (_, _) => AddDndProcess(picked);
                        }
                        menu.Items.Add(item);
                    }
                }
                PickerMenu = menu;
                menu.Open(BtnDndPickApp);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[DND] app picker failed to open");
            }
        }

        /// <summary>The last menu the picker opened (tests read it).</summary>
        internal ContextMenu? PickerMenu { get; private set; }

        /// <summary>Appends one picked process and repaints the box. Re-parses the BOX, not the stored
        /// list, so an edit not yet blurred out of is kept (WPF AddDndProcess :223).</summary>
        private void AddDndProcess(string processName)
        {
            var list = DndProcessList.Parse(TxtDndProcesses.Text);
            var name = DndProcessList.Normalize(processName);
            if (name.Length == 0) return;
            if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
            CoreSettings.Current.DndProcessList = list;
            CoreSettings.Save();
            _isLoading = true;
            try { TxtDndProcesses.Text = DndProcessList.Format(list); }
            finally { _isLoading = false; }
        }
    }
}
