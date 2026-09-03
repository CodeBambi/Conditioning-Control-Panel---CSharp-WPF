using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Pink Filter settings panel, ported from the WPF head, against <see cref="CoreSettings"/>.
    ///
    /// <para>WPF's <c>ISettingsRebindable</c> + <c>SettingsHook</c> pair is reproduced inline: a
    /// cloud restore SWAPS the AppSettings instance, so the PropertyChanged subscription is
    /// tracked per instance and re-pointed on <c>SettingsService.CurrentReplaced</c>.</para>
    ///
    /// <para>The monitor picker enumerates real displays through <c>TopLevel.Screens</c>, which is
    /// cross-platform, so it needs no head service. It is filled on attach rather than in the
    /// constructor: a detached control has no TopLevel and therefore no screen list.</para>
    /// </summary>
    public partial class PinkFilterFeatureControl : UserControl
    {
        // ponytail: local copy of App.MonitorTargetFollowGlobal / App.MonitorTargetAll
        // (ConditioningControlPanel/App.ScreenResolver.cs), still in the WPF head. They are the
        // sentinels persisted in AppSettings.PinkFilterTargetMonitor, so both heads must agree.
        private const int MonitorTargetFollowGlobal = -1;
        private const int MonitorTargetAll = -2;

        private bool _isLoading = true;
        private bool _monitorPopulating;
        private AppSettings? _hooked;

        public PinkFilterFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            SliderOpacity.ValueChanged += SliderOpacity_Changed;
            CmbMonitor.DropDownOpened += (_, _) => PopulateMonitors();
            CmbMonitor.SelectionChanged += CmbMonitor_Changed;
            BtnChooseColor.Click += (_, _) =>
            {
                // ponytail: needs a colour picker. WPF opens System.Windows.Forms.ColorDialog;
                // Avalonia's equivalent is the Avalonia.Controls.ColorPicker package, which is
                // NOT referenced by CCP.Avalonia.csproj (a csproj edit is the coordinator's call).
            };
            BtnResetColor.Click += BtnResetColor_Click;

            LoadFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += RebindToCurrentSettings;
            RebindToCurrentSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= RebindToCurrentSettings;
            Unhook();
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>WPF's <c>ISettingsRebindable.RebindToCurrentSettings</c>: detach from whichever
        /// instance we were on, attach to the live one, repaint from it.</summary>
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
                ChkEnable.IsChecked = s.PinkFilterEnabled;
                SliderOpacity.Value = s.PinkFilterOpacity;
                TxtOpacity.Text = $"{s.PinkFilterOpacity}%";
                UpdateSwatch();
                PopulateMonitors();
            }
            finally { _isLoading = false; }
        }

        /// <summary>Reflects external writes (Ramp, presets, the session engine) back into the
        /// panel. Marshalled: those writers are not all on the UI thread.</summary>
        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.PinkFilterEnabled) ||
                e.PropertyName == nameof(AppSettings.PinkFilterOpacity) ||
                e.PropertyName == nameof(AppSettings.PinkFilterColor) ||
                e.PropertyName == nameof(AppSettings.PinkFilterTargetMonitor))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var want = ChkEnable.IsChecked ?? false;
            if (s.PinkFilterEnabled == want) return;   // an echo of the seed must not save
            s.PinkFilterEnabled = want;
            CoreSettings.Save();
            // ponytail: WPF then calls App.Overlay.RefreshOverlays()
            // (ConditioningControlPanel/Services/Notifications/OverlayService.cs), still in the WPF
            // head - the tint windows are Win32 layered windows with no port yet.
        }

        private void SliderOpacity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtOpacity.Text = $"{v}%";
            CoreSettings.Current.PinkFilterOpacity = v;
            CoreSettings.Save();
            // ponytail: WPF then calls App.Overlay.RefreshOverlays() - see ChkEnable_Changed.
        }

        // ── Display monitor picker (#639) ─────────────────────────────────

        /// <summary>Rebuild the monitor dropdown from the current display topology and select the
        /// entry matching the saved <see cref="AppSettings.PinkFilterTargetMonitor"/>. A saved index
        /// that no longer exists (unplugged monitor) matches nothing and shows "Default" WITHOUT
        /// writing back (the populate guard blocks SelectionChanged), so the target survives a
        /// reconnect.</summary>
        private void PopulateMonitors()
        {
            int saved = CoreSettings.Current.PinkFilterTargetMonitor;
            _monitorPopulating = true;
            try
            {
                CmbMonitor.Items.Clear();
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_default"), Tag = MonitorTargetFollowGlobal });
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_all"), Tag = MonitorTargetAll });

                var screens = ScreenList.Enumerate(this);
                string monitorLabel = Loc.Get("monitor_label");
                string primaryMarker = Loc.Get("monitor_primary_marker");
                for (int i = 0; i < screens.Count; i++)
                {
                    var b = screens[i].Bounds;
                    string prefix = screens[i].IsPrimary ? primaryMarker + ", " : "";
                    CmbMonitor.Items.Add(new ComboBoxItem
                    {
                        Content = $"{monitorLabel} {i + 1} ({prefix}{b.Width}x{b.Height})",
                        Tag = i,
                    });
                }

                ComboBoxItem? match = null;
                foreach (var obj in CmbMonitor.Items)
                    if (obj is ComboBoxItem it && it.Tag is int t && t == saved) { match = it; break; }
                CmbMonitor.SelectedItem = match ?? (CmbMonitor.Items.Count > 0 ? CmbMonitor.Items[0] : null);
            }
            finally { _monitorPopulating = false; }
        }

        private void CmbMonitor_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_monitorPopulating || _isLoading) return;
            if (CmbMonitor.SelectedItem is not ComboBoxItem item || item.Tag is not int target) return;

            var s = CoreSettings.Current;
            if (s.PinkFilterTargetMonitor == target) return;

            s.PinkFilterTargetMonitor = target;
            CoreSettings.Save();
            // ponytail: WPF then calls App.Overlay.RefreshOverlays() - see ChkEnable_Changed.
        }

        private void BtnResetColor_Click(object? sender, RoutedEventArgs e)
        {
            CoreSettings.Current.PinkFilterColor = ""; // empty = default (mod / hot pink)
            CoreSettings.Save();
            UpdateSwatch();
            // ponytail: WPF then calls App.Overlay.RefreshFilterColor() + RefreshOverlays() to push
            // the colour into a tint already on screen - see ChkEnable_Changed.
        }

        private void UpdateSwatch()
        {
            var (r, g, b) = EffectiveColor();
            ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        /// <summary>The colour the tint renders: the user's pick if set, else the active mod's
        /// filter colour, which unseeded is the built-in default manifest's.</summary>
        private static (byte R, byte G, byte B) EffectiveColor() =>
            CoreMods.TryParseHexColor(CoreSettings.Current.PinkFilterColor, out var rgb)
                ? rgb
                : CoreMods.GetFilterColorRgb();
    }

    /// <summary>
    /// The display topology, the Avalonia way. WPF reads <c>App.GetAllScreensCached()</c> (a
    /// WinForms <c>Screen[]</c> behind a cache); here it is <c>TopLevel.Screens</c>, which is
    /// cross-platform and already cached by the windowing backend, so the cache-plus-invalidate
    /// pair the WPF head carries has nothing left to do.
    ///
    /// <para>Empty is a normal answer: a control that is not attached yet, and a headless render,
    /// both have no TopLevel. The picker then shows only its two fixed entries, which is what WPF
    /// shows on a machine reporting no displays.</para>
    /// </summary>
    internal static class ScreenList
    {
        public static IReadOnlyList<Screen> Enumerate(Visual host)
        {
            try
            {
                var screens = TopLevel.GetTopLevel(host)?.Screens;
                if (screens?.All is { } all) return all;
            }
            catch (Exception ex)
            {
                Log.Debug("ScreenList.Enumerate: {E}", ex.Message);
            }
            return Array.Empty<Screen>();
        }
    }
}
