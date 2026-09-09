using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// The app-wide "Show content on" picker: All monitors / Primary only / one named monitor.
    ///
    /// <para><b>Why it exists.</b> Since the 6.8 UI rework the only reachable multi-monitor control
    /// was the <c>ChkMultiMon</c> checkbox in the System popup on Home - all screens or the Windows
    /// primary, nothing else, and nothing at all on the Settings door. A user on multiple monitors
    /// who wanted everything on ONE specific screen had no control that could say so, and the one
    /// monitor dropdown the Ctrl+K palette does list ("Webcam monitor", Settings > Devices) picks
    /// the screen the gaze pipeline is calibrated to - it never moves content. Reported in
    /// ask-support 2026-09-08.</para>
    ///
    /// <para><b>Two settings, one control.</b> Selecting writes both
    /// <see cref="Models.AppSettings.DualMonitorEnabled"/> (so every existing reader and every
    /// preset keeps working untouched) and
    /// <see cref="Models.AppSettings.GlobalTargetMonitor"/> (which adds the "that one screen" case
    /// the boolean cannot express). Mapping:
    /// <list type="bullet">
    ///   <item>All monitors -> DualMonitor = true, target = -1</item>
    ///   <item>Primary only -> DualMonitor = false, target = -1 (exactly the legacy behaviour)</item>
    ///   <item>Monitor N     -> DualMonitor = false, target = N</item>
    /// </list>
    /// Both instances of this control (Settings and the System popup) repaint from
    /// AppSettings.PropertyChanged, so they and the checkbox can never drift apart.</para>
    ///
    /// <para>A saved index whose monitor is unplugged deliberately matches no row: the combo shows
    /// the fallback and the setting is NOT rewritten, so plugging the screen back in restores the
    /// choice. <c>App.ResolveScreens</c> does the same on the render side (and warns once).</para>
    /// </summary>
    public partial class MonitorTargetPicker : UserControl
    {
        /// <summary>Tag for "All monitors". Not a screen index; see the class remarks.</summary>
        private const int TagAllMonitors = -2;
        /// <summary>Tag for "Primary monitor only".</summary>
        private const int TagPrimaryOnly = -1;

        private bool _populating;

        public MonitorTargetPicker()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Populate();
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += OnSettingsPropertyChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged -= OnSettingsPropertyChanged;
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Models.AppSettings.DualMonitorEnabled) &&
                e.PropertyName != nameof(Models.AppSettings.GlobalTargetMonitor)) return;
            try { Dispatcher.BeginInvoke(new Action(Populate)); }
            catch (Exception ex) { App.Logger?.Debug("MonitorTargetPicker repaint failed: {E}", ex.Message); }
        }

        /// <summary>Rebuild the rows from the live display topology and select the saved choice.</summary>
        private void Populate()
        {
            if (CmbContentMonitor == null) return;
            var s = App.Settings?.Current;
            int saved = s == null
                ? TagPrimaryOnly
                : (s.GlobalTargetMonitor >= 0 ? s.GlobalTargetMonitor
                                              : (s.DualMonitorEnabled ? TagAllMonitors : TagPrimaryOnly));

            _populating = true;
            try
            {
                CmbContentMonitor.Items.Clear();
                CmbContentMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_all"), Tag = TagAllMonitors });
                CmbContentMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_primary_only"), Tag = TagPrimaryOnly });

                // CLAUDE.md known issue 5: the enumeration can come back empty mid display
                // transition. An empty list simply means the two fixed rows are the only choices.
                var screens = App.GetAllScreensCached();
                string monitorLabel = Loc.Get("monitor_label");
                string primaryMarker = Loc.Get("monitor_primary_marker");
                for (int i = 0; i < screens.Length; i++)
                {
                    var b = screens[i].Bounds;
                    string prefix = screens[i].Primary ? primaryMarker + ", " : "";
                    CmbContentMonitor.Items.Add(new ComboBoxItem
                    {
                        Content = $"{monitorLabel} {i + 1} ({prefix}{b.Width}x{b.Height})",
                        Tag = i
                    });
                }

                ComboBoxItem? match = null;
                foreach (ComboBoxItem it in CmbContentMonitor.Items)
                    if (it.Tag is int t && t == saved) { match = it; break; }
                // No match = the picked monitor is unplugged. Show "Primary only" (what the app is
                // actually doing right now) without writing anything back.
                CmbContentMonitor.SelectedItem = match ?? CmbContentMonitor.Items[1];
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "MonitorTargetPicker.Populate failed"); }
            finally { _populating = false; }
        }

        // Re-enumerate on open so a monitor plugged in since load shows up without reopening the page.
        private void CmbContentMonitor_DropDownOpened(object sender, EventArgs e)
        {
            App.InvalidateScreenCache();
            Populate();
        }

        private void CmbContentMonitor_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_populating) return;
            if (CmbContentMonitor.SelectedItem is not ComboBoxItem item || item.Tag is not int tag) return;

            var s = App.Settings?.Current;
            if (s == null) return;

            bool dual = tag == TagAllMonitors;
            int target = tag >= 0 ? tag : -1;
            if (s.DualMonitorEnabled == dual && s.GlobalTargetMonitor == target) return;

            s.DualMonitorEnabled = dual;
            s.GlobalTargetMonitor = target;
            App.Settings?.Save();
            App.Logger?.Information("Content monitor set to {Choice} (DualMonitor={Dual}, target={Target})",
                tag == TagAllMonitors ? "all monitors" : tag == TagPrimaryOnly ? "primary only" : $"monitor {tag + 1}",
                dual, target);

            RefreshLiveSurfaces();
        }

        /// <summary>
        /// Rebuild the surfaces that were created for the OLD screen set. Restores the mid-session
        /// refresh that died with ChkDualMon in the 6.8 rework (MainWindow.UiUpdates.cs notes it as
        /// a known gap): without this a change only takes effect the next time an effect starts.
        /// </summary>
        private static void RefreshLiveSurfaces()
        {
            try { App.Overlay?.RefreshForDualMonitorChange(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Content monitor: overlay refresh failed"); }

            try { if (App.BouncingText?.IsRunning == true) App.BouncingText.Restart(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Content monitor: bouncing text restart failed"); }
        }
    }
}
