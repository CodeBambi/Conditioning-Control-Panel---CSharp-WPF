using System;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Pink Filter settings panel, ported from the WPF head. <see cref="PinkFilterSettings"/>
    /// stands in for <c>App.Settings.Current</c>. Save, the overlay refresh, the mod colour/art
    /// repaint, the WinForms ColorDialog and the screen enumeration are stubbed: the monitor combo
    /// carries the two fixed entries plus one placeholder monitor line built with the same loc keys
    /// and format the WPF code-behind uses.
    /// </summary>
    public partial class PinkFilterFeatureControl : UserControl
    {
        // ponytail: local copy of App.ScreenResolver's sentinels, reuse when they move to Core
        private const int MonitorTargetFollowGlobal = -1;
        private const int MonitorTargetAll = -2;

        private bool _isLoading = true;
        private bool _monitorPopulating;
        private readonly PinkFilterSettings _s = new();

        public PinkFilterFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            // ponytail: needs App.Overlay.RefreshOverlays / RefreshFilterColor, wired when it moves to Core
            ChkEnable.IsCheckedChanged += (_, _) => { if (_isLoading) return; _s.PinkFilterEnabled = ChkEnable.IsChecked ?? false; Save(); };
            SliderOpacity.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtOpacity.Text = $"{v}%"; _s.PinkFilterOpacity = v; Save(); };
            CmbMonitor.DropDownOpened += (_, _) => PopulateMonitors();
            CmbMonitor.SelectionChanged += (_, _) =>
            {
                if (_monitorPopulating || _isLoading) return;
                if (CmbMonitor.SelectedItem is not ComboBoxItem item || item.Tag is not int target) return;
                if (_s.PinkFilterTargetMonitor == target) return;
                _s.PinkFilterTargetMonitor = target;
                Save();
            };
            // ponytail: needs a colour picker (WinForms ColorDialog on WPF); no cross-platform picker yet
            BtnChooseColor.Click += (_, _) => { };
            BtnResetColor.Click += (_, _) =>
            {
                _s.PinkFilterColor = ""; // empty = default (mod / hot pink)
                Save();
                UpdateSwatch();
            };

            LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = _s.PinkFilterEnabled;
                SliderOpacity.Value = _s.PinkFilterOpacity;
                TxtOpacity.Text = $"{_s.PinkFilterOpacity}%";
                UpdateSwatch();
                PopulateMonitors();
            }
            finally { _isLoading = false; }
        }

        private void PopulateMonitors()
        {
            int saved = _s.PinkFilterTargetMonitor;
            _monitorPopulating = true;
            try
            {
                CmbMonitor.Items.Clear();
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_default"), Tag = MonitorTargetFollowGlobal });
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_all"), Tag = MonitorTargetAll });

                // ponytail: needs the screen list (App.GetAllScreensCached on WPF); one placeholder
                // monitor in the exact WPF format so the row renders with a real string.
                string monitorLabel = Loc.Get("monitor_label");
                string primaryMarker = Loc.Get("monitor_primary_marker");
                CmbMonitor.Items.Add(new ComboBoxItem { Content = $"{monitorLabel} 1 ({primaryMarker}, 1920x1080)", Tag = 0 });

                ComboBoxItem? match = null;
                foreach (var obj in CmbMonitor.Items)
                    if (obj is ComboBoxItem it && it.Tag is int t && t == saved) { match = it; break; }
                CmbMonitor.SelectedItem = match ?? (CmbMonitor.Items.Count > 0 ? CmbMonitor.Items[0] : null);
            }
            finally { _monitorPopulating = false; }
        }

        private void UpdateSwatch()
        {
            var (r, g, b) = EffectiveColor();
            ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        // The colour the tint renders: the user's pick if set, else hot pink.
        // ponytail: the mod fallback (App.Mods.GetFilterColorRgb) is skipped until ModService moves to Core.
        private (byte R, byte G, byte B) EffectiveColor()
        {
            if (TryParseHex(_s.PinkFilterColor, out var rgb)) return rgb;
            return (255, 105, 180);
        }

        // ponytail: third copy of the hex->RGB parse (WPF PinkFilterFeatureControl, ModService.ParseHexColor);
        // hoist to CCP.Core and call it from both heads when AppSettings crosses. Not done here: this PR adds no shared files.
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

        // ponytail: needs App.Settings.Save(), wired when AppSettings moves to Core
        private static void Save() { }
    }

    /// <summary>Placeholder for the AppSettings slice this panel reads. Property names match
    /// Models.AppSettings; values are the WPF XAML defaults.</summary>
    public sealed class PinkFilterSettings
    {
        public bool PinkFilterEnabled { get; set; }
        public int PinkFilterOpacity { get; set; } = 10;
        public string PinkFilterColor { get; set; } = "";
        public int PinkFilterTargetMonitor { get; set; } = -1;
    }
}
