using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Features
{
    public partial class PinkFilterFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true;
        private bool _monitorPopulating; // guards the monitor combo while it is rebuilt

        public PinkFilterFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // Tracks WHICH AppSettings instance the hook is attached to, so a cloud restore - which
        // SWAPS the instance - can be followed instead of leaving this permanently-mounted rack
        // panel listening to, and displaying, the discarded object. See ISettingsRebindable.
        private SettingsHook? _settingsHook;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RebindToCurrentSettings();
            // The swatch falls through to the mod's filter colour when the user has no custom
            // hex, and the hero/side plates are mod art; the rack hosts this control permanently,
            // so a mod switch must repaint them (a popup instance never lived long enough to care).
            ApplyFeatureArt();
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _settingsHook?.Unhook();
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
        }

        /// <inheritdoc/>
        public void RebindToCurrentSettings()
        {
            (_settingsHook ??= new SettingsHook(OnSettingsPropertyChanged)).Rebind();
            LoadFromSettings();
        }

        /// <summary>
        /// ModChanged can be raised off the UI thread, so every body it reaches is marshalled.
        /// One handler, both repaints - the swatch and the two art plates change answer on
        /// exactly the same event.
        /// </summary>
        private void OnModChanged(object? sender, Models.ModPackage mod)
        {
            Dispatcher.BeginInvoke(new Action(() => { UpdateSwatch(); ApplyFeatureArt(); }));
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

        // =====================================================================================
        //  feature art (mod-aware)
        // =====================================================================================

        /// <summary>
        /// This page's art under <c>Resources/features/</c>. Verbatim the file the XAML already
        /// declares as its pack:// default on both plates - naming it here changes WHICH lookup
        /// runs, never WHICH file is asked for.
        /// </summary>
        private const string FeatureArtPath = "features/Pink_filter.png";

        /// <summary>
        /// Pushes the (possibly mod-overridden) feature art into the 72px hero plate and the tall
        /// side plate. Both plates author a pack:// default in XAML, so a null resolve here leaves
        /// the built-in art standing rather than blanking the plate - the same degrade rule
        /// <c>RemoteControlTabView.ApplyFeatureArt</c> follows.
        ///
        /// <para>Two widths, not one: the hero is 240px wide and the side plate is a full-height
        /// column, and <see cref="Services.ModResourceResolver.ResolveImageDecoded"/> keys its cache on the
        /// width, so each is decoded once for the whole session per mod.</para>
        ///
        /// <para>The brushes are mutated in place. Swapping the <c>Border.Background</c> object
        /// would work too and would throw away the XAML-declared Stretch/AlignmentX/Opacity with
        /// it; a frozen brush would silently never repaint at all, which is why they are named
        /// rather than declared inline as literals.</para>
        /// </summary>
        private void ApplyFeatureArt()
        {
            try
            {
                var hero = Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 480);
                if (hero != null && HeroArtBrush is { IsFrozen: false }) HeroArtBrush.ImageSource = hero;

                var side = Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 800);
                if (side != null && SideArtBrush is { IsFrozen: false }) SideArtBrush.ImageSource = side;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PinkFilterFeatureControl.ApplyFeatureArt: {E}", ex.Message);
            }
        }

    }
}
