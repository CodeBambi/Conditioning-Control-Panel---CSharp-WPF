using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Features
{
    public partial class PinkFilterFeatureControl : UserControl
    {
        private bool _isLoading = true;
        private bool _monitorPopulating; // guards the monitor combo while it is rebuilt

        public PinkFilterFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

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
                ChkEnable.IsChecked = s.PinkFilterEnabled;
                SliderOpacity.Value = s.PinkFilterOpacity;
                TxtOpacity.Text = $"{s.PinkFilterOpacity}%";
                UpdateSwatch();
                PopulateMonitors();
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.PinkFilterEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.PinkFilterOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.PinkFilterColor) ||
                e.PropertyName == nameof(Models.AppSettings.PinkFilterTargetMonitor))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.PinkFilterEnabled = ChkEnable.IsChecked ?? false;
            App.Settings?.Save();
            try { App.Overlay?.RefreshOverlays(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "PinkFilter toggle: RefreshOverlays failed"); }
        }

        private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtOpacity.Text = $"{v}%";
            s.PinkFilterOpacity = v;
            App.Settings?.Save();
            try { App.Overlay?.RefreshOverlays(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "PinkFilter opacity: RefreshOverlays failed"); }
        }

        // ── Display monitor picker (#639) ─────────────────────────────────

        /// <summary>Rebuild the monitor dropdown from the current display topology and select the
        /// entry matching the saved <see cref="Models.AppSettings.PinkFilterTargetMonitor"/>. A saved
        /// index that no longer exists (unplugged monitor) matches nothing and shows "Default"
        /// WITHOUT writing back (the populate guard blocks SelectionChanged), so the target survives
        /// a reconnect.</summary>
        private void PopulateMonitors()
        {
            if (CmbMonitor == null) return;
            int saved = App.Settings?.Current?.PinkFilterTargetMonitor ?? App.MonitorTargetFollowGlobal;
            _monitorPopulating = true;
            try
            {
                CmbMonitor.Items.Clear();
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_default"), Tag = App.MonitorTargetFollowGlobal });
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_all"), Tag = App.MonitorTargetAll });

                var screens = App.GetAllScreensCached();
                string monitorLabel = Loc.Get("monitor_label");
                string primaryMarker = Loc.Get("monitor_primary_marker");
                for (int i = 0; i < screens.Length; i++)
                {
                    var b = screens[i].Bounds;
                    string prefix = screens[i].Primary ? primaryMarker + ", " : "";
                    CmbMonitor.Items.Add(new ComboBoxItem
                    {
                        Content = $"{monitorLabel} {i + 1} ({prefix}{b.Width}x{b.Height})",
                        Tag = i
                    });
                }

                ComboBoxItem? match = null;
                foreach (ComboBoxItem it in CmbMonitor.Items)
                    if (it.Tag is int t && t == saved) { match = it; break; }
                CmbMonitor.SelectedItem = match ?? (CmbMonitor.Items.Count > 0 ? CmbMonitor.Items[0] : null);
            }
            finally { _monitorPopulating = false; }
        }

        // Re-enumerate on open so a monitor plugged in since load appears without reopening the card.
        private void CmbMonitor_DropDownOpened(object sender, EventArgs e)
        {
            App.InvalidateScreenCache();
            PopulateMonitors();
        }

        private void CmbMonitor_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_monitorPopulating || _isLoading) return;
            if (CmbMonitor.SelectedItem is not ComboBoxItem item || item.Tag is not int target) return;

            var s = App.Settings?.Current;
            if (s == null) return;
            if (s.PinkFilterTargetMonitor == target) return;

            s.PinkFilterTargetMonitor = target;
            App.Settings?.Save();

            // Compositor picks the new target up next frame (per-monitor ShouldRenderOnScreen);
            // RefreshOverlays reconciles the legacy per-screen windows.
            try { App.Overlay?.RefreshOverlays(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "PinkFilter monitor: RefreshOverlays failed"); }
        }

        private void BtnChooseColor_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            var (er, eg, eb) = EffectiveColor();
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(er, eg, eb)
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            s.PinkFilterColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            App.Settings?.Save();
            UpdateSwatch();
            ApplyColorLive();
        }

        private void BtnResetColor_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            s.PinkFilterColor = ""; // empty = default (mod / hot pink)
            App.Settings?.Save();
            UpdateSwatch();
            ApplyColorLive();
        }

        // Pushes the freshly picked color to a tint that's already on screen, then keeps
        // the reconcilers in sync. RefreshFilterColor is a no-op when nothing is showing.
        private void ApplyColorLive()
        {
            try
            {
                App.Overlay?.RefreshFilterColor();
                App.Overlay?.RefreshOverlays();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "PinkFilter color: refresh failed"); }
        }

        private void UpdateSwatch()
        {
            var (r, g, b) = EffectiveColor();
            ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        // The color the tint actually renders: the user's pick if set, else the active
        // mod's filter color, else hot pink. Mirrors OverlayService.GetFilterRgb.
        private static (byte R, byte G, byte B) EffectiveColor()
        {
            var custom = App.Settings?.Current?.PinkFilterColor;
            if (TryParseHex(custom, out var rgb)) return rgb;
            return App.Mods?.GetFilterColorRgb() ?? ((byte)255, (byte)105, (byte)180);
        }

        private static bool TryParseHex(string? hex, out (byte R, byte G, byte B) rgb)
        {
            rgb = (255, 105, 180);
            if (string.IsNullOrWhiteSpace(hex)) return false;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length != 6) return false;
            try
            {
                rgb = (Convert.ToByte(hex.Substring(0, 2), 16),
                       Convert.ToByte(hex.Substring(2, 2), 16),
                       Convert.ToByte(hex.Substring(4, 2), 16));
                return true;
            }
            catch { return false; }
        }
    }
}
