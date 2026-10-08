using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// The app-wide "Show content on" picker, ported from ConditioningControlPanel/Features/
    /// MonitorTargetPicker.xaml.cs. Selecting writes BOTH <see cref="AppSettings.DualMonitorEnabled"/>
    /// and <see cref="AppSettings.GlobalTargetMonitor"/>: All -> (true, -1), Primary -> (false, -1),
    /// Monitor N -> (false, N). A saved index whose monitor is unplugged matches no row: the combo
    /// shows "Primary only" and the setting is NOT rewritten, so a reconnect restores the choice.
    /// </summary>
    public partial class MonitorTargetPicker : UserControl
    {
        internal const int TagAllMonitors = -2;
        internal const int TagPrimaryOnly = -1;

        private bool _populating;
        private INotifyPropertyChanged? _watched;

        public MonitorTargetPicker()
        {
            InitializeComponent();
            CmbContentMonitor.DropDownOpened += (_, _) => Populate();   // WPF re-enumerates on open
            CmbContentMonitor.SelectionChanged += CmbContentMonitor_Changed;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Populate();
            _watched = CoreSettings.Current;
            _watched.PropertyChanged += OnSettingsPropertyChanged;
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_watched is not null) _watched.PropertyChanged -= OnSettingsPropertyChanged;
            _watched = null;
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Populate);

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AppSettings.DualMonitorEnabled) or nameof(AppSettings.GlobalTargetMonitor))
                Dispatcher.UIThread.Post(Populate);
        }

        /// <summary>Rebuild the rows from the live display topology and select the saved choice.</summary>
        internal void Populate()
        {
            var s = CoreSettings.Current;
            int saved = s.GlobalTargetMonitor >= 0 ? s.GlobalTargetMonitor
                      : s.DualMonitorEnabled ? TagAllMonitors : TagPrimaryOnly;
            _populating = true;
            try
            {
                CmbContentMonitor.Items.Clear();
                CmbContentMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_all"), Tag = TagAllMonitors });
                CmbContentMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_primary_only"), Tag = TagPrimaryOnly });

                var screens = ScreenList.Enumerate(this);
                string monitorLabel = Loc.Get("monitor_label");
                string primaryMarker = Loc.Get("monitor_primary_marker");
                for (int i = 0; i < screens.Count; i++)
                {
                    var b = screens[i].Bounds;
                    string prefix = screens[i].IsPrimary ? primaryMarker + ", " : "";
                    CmbContentMonitor.Items.Add(new ComboBoxItem
                    {
                        Content = $"{monitorLabel} {i + 1} ({prefix}{b.Width}x{b.Height})",
                        Tag = i,
                    });
                }

                ComboBoxItem? match = null;
                foreach (var obj in CmbContentMonitor.Items)
                    if (obj is ComboBoxItem it && it.Tag is int t && t == saved) { match = it; break; }
                CmbContentMonitor.SelectedItem = match ?? CmbContentMonitor.Items[1];
            }
            catch (Exception ex) { Log.Warning(ex, "MonitorTargetPicker.Populate failed"); }
            finally { _populating = false; }
        }

        private void CmbContentMonitor_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_populating) return;
            if (CmbContentMonitor.SelectedItem is not ComboBoxItem item || item.Tag is not int tag) return;

            var s = CoreSettings.Current;
            bool dual = tag == TagAllMonitors;
            int target = tag >= 0 ? tag : -1;
            if (s.DualMonitorEnabled == dual && s.GlobalTargetMonitor == target) return;

            s.DualMonitorEnabled = dual;
            s.GlobalTargetMonitor = target;
            CoreSettings.Save();
            Log.Information("Content monitor set to {Choice} (DualMonitor={Dual}, target={Target})",
                tag == TagAllMonitors ? "all monitors" : tag == TagPrimaryOnly ? "primary only" : $"monitor {tag + 1}",
                dual, target);

            // WPF RefreshLiveSurfaces: rebuild what was created for the OLD screen set.
            try
            {
                PinkFilterOverlay.Refresh(this);
                SpiralOverlay.Refresh(this);
                BouncingTextOverlay.Restart();
            }
            catch (Exception ex) { Log.Warning(ex, "Content monitor: live overlay refresh failed"); }
        }
    }
}
