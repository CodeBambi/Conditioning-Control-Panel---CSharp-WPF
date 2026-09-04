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
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
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
            BtnChooseColor.Click += BtnChooseColor_Click;
            BtnResetColor.Click += BtnResetColor_Click;

            // ponytail: WPF also repaints a mod-aware hero and side plate here and on ModChanged
            // (ApplyFeatureArt, Resources/features/Pink_filter.png). The port dropped both
            // silently. CoreModArt.OverridePath("features/Pink_filter.png") answers the override
            // half now and the built-in ships at
            // avares://CCP.Avalonia/Resources/features/Pink_filter.png, so the only thing still
            // missing is an x:Name on the two plate Borders in PinkFilterFeatureControl.axaml -
            // without one this file has no control to paint. See BubbleCountFeatureControl for
            // the full note and TubeFitDialog.TryLoadImage for the decode.

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
            PinkFilterOverlay.Refresh(this);
        }

        private void SliderOpacity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtOpacity.Text = $"{v}%";
            CoreSettings.Current.PinkFilterOpacity = v;
            CoreSettings.Save();
            PinkFilterOverlay.Refresh(this);
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
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_default"), Tag = MonitorTarget.FollowGlobal });
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_all"), Tag = MonitorTarget.All });

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
            PinkFilterOverlay.Refresh(this);
        }

        /// <summary>
        /// The tint colour pick. WPF opened a blocking <c>System.Windows.Forms.ColorDialog</c>
        /// seeded with the effective colour; this awaits <see cref="ColorPickerDialog"/> instead,
        /// which needs a non-null owner Window. Cancel answers null and writes nothing, matching
        /// WPF's early return on anything but DialogResult.OK. The stored form is the WPF one,
        /// <c>#RRGGBB</c> - <c>CoreMods.TryParseHexColor</c> parses exactly that back.
        /// </summary>
        private async void BtnChooseColor_Click(object? sender, RoutedEventArgs e)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null) return;   // detached, or a headless render: nothing to own the modal

            var (er, eg, eb) = EffectiveColor();
            var picked = await ColorPickerDialog.PickAsync(owner, Color.FromRgb(er, eg, eb));
            if (picked is not { } c) return;

            CoreSettings.Current.PinkFilterColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            CoreSettings.Save();
            UpdateSwatch();
            PinkFilterOverlay.Refresh(this);   // WPF: RefreshFilterColor() + RefreshOverlays()
        }

        private void BtnResetColor_Click(object? sender, RoutedEventArgs e)
        {
            CoreSettings.Current.PinkFilterColor = ""; // empty = default (mod / hot pink)
            CoreSettings.Save();
            UpdateSwatch();
            PinkFilterOverlay.Refresh(this);   // WPF: RefreshFilterColor() + RefreshOverlays()
        }

        private void UpdateSwatch()
        {
            var (r, g, b) = EffectiveColor();
            ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        /// <summary>The colour the tint renders. One copy, on the host that paints it.</summary>
        private static (byte R, byte G, byte B) EffectiveColor() => PinkFilterOverlay.EffectiveColor();
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
