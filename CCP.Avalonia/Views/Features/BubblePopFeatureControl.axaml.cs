using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Bubble Pop panel, ported from the WPF head, settings logic restored against
    /// <see cref="CoreSettings"/>. Every toggle and slider round-trips the AppSettings property
    /// the WPF original wrote and saves; the trigger-options reveal and the seven trigger-type
    /// checkboxes drive <c>BubbleTriggerVariants</c> the same way.
    ///
    /// <para>The WPF <c>SettingsHook</c>/<c>ISettingsRebindable</c> pair is inlined: a cloud
    /// restore SWAPS the settings instance, so the PropertyChanged subscription is tracked by
    /// instance and re-pointed on <c>SettingsService.CurrentReplaced</c>.</para>
    ///
    /// <para>The easter-egg hint names the active persona from <see cref="CoreMods.ActiveModId"/>,
    /// and <see cref="CoreMods.ModChanged"/> repaints it - the rack hosts this control
    /// permanently, so a mod switch has to be followed.</para>
    /// </summary>
    public partial class BubblePopFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public BubblePopFeatureControl()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and everything below reads them.
            InitializeComponent();
            // WPF ApplyFeatureArt: hero + side plates from features/Bubble_pop.png, mod override first, repainted on a mod switch.
            Helpers.ModArt.BindFeaturePlates(this, "features/Bubble_pop.png", HeroArt, SideArt);

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            SliderFreq.ValueChanged += SliderFreq_Changed;
            SliderVolume.ValueChanged += SliderVolume_Changed;
            SliderSize.ValueChanged += SliderSize_Changed;
            SliderSpeed.ValueChanged += SliderSpeed_Changed;
            ChkSolidMode.IsCheckedChanged += ChkSolidMode_Changed;
            ChkTriggers.IsCheckedChanged += ChkTriggers_Changed;
            SliderTriggerChance.ValueChanged += SliderTriggerChance_Changed;
            foreach (var box in TriggerTypeBoxes()) box.IsCheckedChanged += TriggerType_Changed;
            ChkBubbleGazePop.IsCheckedChanged += ChkBubbleGazePop_Changed;
            ChkBrainDrainBubble.IsCheckedChanged += ChkBrainDrainBubble_Changed;
            CmbMotion.SelectionChanged += CmbMotion_Changed;

            LoadFromSettings();

            // The hero and side plates repaint themselves on a mod switch (ModArt.BindFeaturePlates).
        }

        /// <summary>The seven effect boxes, each carrying its variant id in <c>Tag</c>.</summary>
        private IEnumerable<CheckBox> TriggerTypeBoxes()
        {
            yield return ChkTypeFlash;
            yield return ChkTypeSubliminal;
            yield return ChkTypePink;
            yield return ChkTypeSpiral;
            yield return ChkTypeGlitch;
            yield return ChkTypeCascade;
            yield return ChkTypeVideo;
        }

        // ---- settings instance tracking (WPF: SettingsHook + ISettingsRebindable) --------------

        private AppSettings? _hooked;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            CoreMods.ModChanged += OnModChanged;
            Overlays.BubbleOverlay.XpBudgetChanged += UpdateAmbientXpBudgetLine;   // WPF AmbientXpBudgetChanged
            Platform.PrizeOwnership.Changed += OnGrantsChanged;
            // WPF OnLoaded: the Get it row names its prize and tells the box when to re-measure.
            RowGetBubblesV2.RowChanged -= OnGetRowChanged;
            RowGetBubblesV2.RowChanged += OnGetRowChanged;
            RowGetBubblesV2.Configure(Platform.V2PurchaseRule.BubblesPrizeId, "v2_get_bubble_blurb", "label_bubbles_v2_box");
            RebindToCurrentSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            CoreMods.ModChanged -= OnModChanged;
            Overlays.BubbleOverlay.XpBudgetChanged -= UpdateAmbientXpBudgetLine;
            Platform.PrizeOwnership.Changed -= OnGrantsChanged;
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

        /// <summary>
        /// ModChanged can be raised off the UI thread, so the repaint is marshalled. The persona
        /// line is the only thing here that changes answer on a mod switch (WPF also repainted the
        /// two art plates, which this head does not draw).
        /// </summary>
        private void OnModChanged(object? sender, ModPackage mod) =>
            Dispatcher.UIThread.Post(LoadFromSettings);

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.BubblesEnabled;
                SliderFreq.Value = s.BubblesFrequency;
                TxtFreq.Text = s.BubblesFrequency.ToString();
                SliderVolume.Value = s.BubblesVolume;
                TxtVolume.Text = $"{s.BubblesVolume}%";
                SliderSize.Value = s.BubblesSize;
                TxtSize.Text = $"{s.BubblesSize}%";
                SliderSpeed.Value = s.BubbleSpeedBoost;
                TxtSpeed.Text = $"+{s.BubbleSpeedBoost}%";
                ChkSolidMode.IsChecked = s.BubbleSharedHost;

                // Easter-egg hint (companion auto-pops a lingering effect bubble) — name the active persona.
                var persona = CoreMods.ActiveModId switch
                {
                    "builtin-bambisleep" => "Bambi",
                    "builtin-sissyhypno" => "your bimbo",
                    "builtin-locked" => "Circe",
                    _ => "your companion"
                };
                TxtTriggerEggHint.Text = Loc.GetF("label_trigger_bubbles_egg_hint", persona);

                ChkTriggers.IsChecked = s.BubbleTriggersEnabled;
                TriggerOptionsPanel.IsVisible = s.BubbleTriggersEnabled;
                SliderTriggerChance.Value = s.BubbleTriggerChance;
                TxtTriggerChance.Text = $"{s.BubbleTriggerChance}%";
                var ids = s.BubbleTriggerVariants ?? new List<string>();
                foreach (var box in TriggerTypeBoxes())
                    box.IsChecked = box.Tag is string id && ids.Contains(id);
                ChkBubbleGazePop.IsChecked = s.BubbleGazePopEnabled;
                ChkBrainDrainBubble.IsChecked = s.BubbleBrainDrainEnabled;
                RebuildMotionPicker();
                UpdateGazeHint();
                UpdateAmbientXpBudgetLine();
            }
            finally { _isLoading = false; }
        }

        /// <summary>
        /// "Ambient bubble XP: N/300 today" (#1019/#1026). Ambient pops stop paying once the daily
        /// bucket is spent; before this line the ceiling was completely invisible.
        /// </summary>
        private void UpdateAmbientXpBudgetLine()
        {
            TxtAmbientXpBudget.Text = Loc.GetF("label_ambient_bubble_xp_budget",
                AmbientBubbleXp.PaidToday(CoreSettings.Current), AmbientBubbleXp.DailyXpCap);
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.BubblesEnabled) ||
                e.PropertyName == nameof(AppSettings.BubblesFrequency) ||
                e.PropertyName == nameof(AppSettings.BubblesVolume) ||
                e.PropertyName == nameof(AppSettings.BubblesSize) ||
                e.PropertyName == nameof(AppSettings.BubbleSpeedBoost) ||
                e.PropertyName == nameof(AppSettings.BubbleGazePopEnabled) ||
                e.PropertyName == nameof(AppSettings.BubbleMotionStyle) ||
                e.PropertyName == nameof(AppSettings.BubbleBrainDrainEnabled) ||
                e.PropertyName == nameof(AppSettings.BubbleTriggersEnabled) ||
                e.PropertyName == nameof(AppSettings.BubbleTriggerChance) ||
                e.PropertyName == nameof(AppSettings.BubbleTriggerVariants))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.BubblesEnabled = ChkEnable.IsChecked ?? false;
            CoreSettings.Save();

            // Live-apply: start/stop the bubble service if the engine is running.
            if (CoreSession.IsEngineRunning)
            {
                if (CoreSettings.Current.BubblesEnabled) CoreBubbles.Start(); else CoreBubbles.Stop();
            }
        }

        private void SliderFreq_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtFreq.Text = v.ToString();
            CoreSettings.Current.BubblesFrequency = v;
            CoreBubbles.RefreshFrequency();
            CoreSettings.Save();
        }

        private void SliderVolume_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtVolume.Text = $"{v}%";
            CoreSettings.Current.BubblesVolume = v;
            CoreSettings.Save();
        }

        private void SliderSize_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtSize.Text = $"{v}%";
            CoreSettings.Current.BubblesSize = v;
            CoreSettings.Save();
            // No live-apply hook: size is read when each bubble is CONSTRUCTED, so the change
            // shows on the next spawn without disturbing the ones already drifting. Restarting the
            // service to resize mid-flight would pop the field out from under the user.
        }

        private void SliderSpeed_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtSpeed.Text = $"+{v}%";
            CoreSettings.Current.BubbleSpeedBoost = v;
            CoreSettings.Save();
        }

        private void ChkSolidMode_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.BubbleSharedHost = ChkSolidMode.IsChecked ?? false;
            CoreSettings.Save();

            // The render path is latched per Start->Stop session, so a live bubble service has to
            // be bounced to pick up the new mode. Two of WPF's three conjuncts are real here; the
            // third ("is it actually running") is the service's own.
            if (CoreSession.IsEngineRunning && CoreSettings.Current.BubblesEnabled)
            {
                CoreBubbles.Stop();
                CoreBubbles.Start();
            }
        }

        private void ChkTriggers_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var on = ChkTriggers.IsChecked ?? false;
            CoreSettings.Current.BubbleTriggersEnabled = on;
            TriggerOptionsPanel.IsVisible = on;
            CoreSettings.Save();
        }

        private void SliderTriggerChance_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtTriggerChance.Text = $"{v}%";
            CoreSettings.Current.BubbleTriggerChance = v;
            CoreSettings.Save();
        }

        private void TriggerType_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not CheckBox cb || cb.Tag is not string id) return;

            var s = CoreSettings.Current;
            var ids = new List<string>(s.BubbleTriggerVariants ?? new List<string>());
            if (cb.IsChecked ?? false) { if (!ids.Contains(id)) ids.Add(id); }
            else ids.Remove(id);
            s.BubbleTriggerVariants = ids;   // reassign so the setter fires change notification
            CoreSettings.Save();
        }

        // ---- Stare to pop (WPF ChkBubbleGazePop_Changed + UpdateGazeHint) -----------------------

        /// <summary>The camera can feed a dwell right now: running, on a stored calibration. A seam
        /// for tests.</summary>
        internal static Func<bool> GazeReady = () =>
            Platform.WebcamTracker.Instance is { IsRunning: true, Calibration: not null };

        /// <summary>"Stare to pop" is a stored preference; it does nothing until the camera runs on
        /// a stored calibration. Without this line the toggle reads as broken, so the row stays live
        /// and gains a hint rather than being hidden or disabled (WPF UpdateGazeHint).</summary>
        private void UpdateGazeHint()
        {
            try { TxtBubbleGazeHint.IsVisible = !GazeReady(); }
            catch { TxtBubbleGazeHint.IsVisible = true; }
        }

        /// <summary>The bubble twin of the Flashes page's switch: writes the preference. The dwell
        /// reads it through <see cref="Overlays.BubbleOverlay.GazeTargets"/>.</summary>
        private void ChkBubbleGazePop_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.BubbleGazePopEnabled = ChkBubbleGazePop.IsChecked ?? false;
            CoreSettings.Save();
            UpdateGazeHint();
        }

        // ---- Bubbles v2 motion picker (WPF BubblePopFeatureControl.xaml.cs RebuildMotionPicker) ----

        /// <summary>Grants can change off the UI thread (a sync); the rebuild is posted.</summary>
        // The Get it row decides its own visibility; the box only needs to know whether anything is
        // left in it (WPF OnGetRowChanged).
        private void OnGetRowChanged(object? sender, EventArgs e)
        {
            var was = _isLoading;
            _isLoading = true;
            try { RebuildMotionPicker(); }
            finally { _isLoading = was; }
        }

        private void OnGrantsChanged() => Dispatcher.UIThread.Post(() =>
        {
            var was = _isLoading;
            _isLoading = true;
            try { RebuildMotionPicker(); }
            finally { _isLoading = was; }
        });

        private static readonly global::Avalonia.Media.IBrush V2Brush =
            new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#FFE08A"));

        /// <summary>Float up always; Rain / Spiral In only when owned (each wearing a v2 pill); Mix
        /// once any v2 style is owned. With nothing owned the whole box stays collapsed: a one-item
        /// picker is configuration with no capability behind it. The Brain Drain bubble arrives
        /// with the same prizes. Callers hold <c>_isLoading</c>.</summary>
        private void RebuildMotionPicker()
        {
            bool rain = Platform.PrizeOwnership.IsGranted(AmbientBubbleMotion.RainGrant);
            bool spiral = Platform.PrizeOwnership.IsGranted(AmbientBubbleMotion.SpiralInGrant);
            ChkBrainDrainBubble.IsVisible = rain || spiral;
            MotionRow.IsVisible = rain || spiral;
            // The BOX stays up while the Get it row has something to offer (WPF RebuildMotionPicker).
            V2Box.IsVisible = rain || spiral || !RowGetBubblesV2.IsRowHidden;

            CmbMotion.Items.Clear();
            CmbMotion.Items.Add(MotionItem(BubbleMotionStyle.FloatUp, "bubble_motion_float_up", v2: false));
            if (rain) CmbMotion.Items.Add(MotionItem(BubbleMotionStyle.Rain, "bubble_motion_rain", v2: true));
            if (spiral) CmbMotion.Items.Add(MotionItem(BubbleMotionStyle.SpiralIn, "bubble_motion_spiral_in", v2: true));
            if (rain || spiral) CmbMotion.Items.Add(MotionItem(BubbleMotionStyle.Mix, "bubble_motion_mix", v2: false));

            // An unowned (absent) style shows as Float up; the setting is left alone.
            var style = CoreSettings.Current.BubbleMotionStyle;
            ComboBoxItem? pick = null;
            foreach (var item in CmbMotion.Items)
                if (item is ComboBoxItem { Tag: BubbleMotionStyle tag } cbi && tag == style) pick = cbi;
            CmbMotion.SelectedItem = pick ?? CmbMotion.Items[0];
        }

        private static ComboBoxItem MotionItem(BubbleMotionStyle style, string key, bool v2)
        {
            var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = Loc.Get(key), VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center });
            if (v2)
                row.Children.Add(new Border
                {
                    Margin = new Thickness(7, 0, 0, 0), Padding = new Thickness(5, 1, 6, 2), CornerRadius = new CornerRadius(7),
                    Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromArgb(0xD9, 0x1A, 0x1A, 0x2E)),
                    BorderBrush = V2Brush, BorderThickness = new Thickness(1), IsHitTestVisible = false,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Child = new TextBlock { Text = Loc.Get("badge_v2"), Foreground = V2Brush, FontSize = 9, FontWeight = global::Avalonia.Media.FontWeight.Bold },
                });
            return new ComboBoxItem { Content = row, Tag = style };
        }

        /// <summary>WPF CmbMotion_Changed: the style is read at each spawn, so the next bubble wears it.</summary>
        private void CmbMotion_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (CmbMotion.SelectedItem is not ComboBoxItem { Tag: BubbleMotionStyle style }) return;
            var s = CoreSettings.Current;
            if (s.BubbleMotionStyle == style) return;
            s.BubbleMotionStyle = style;
            CoreSettings.Save();
        }

        /// <summary>The Brain Drain bubble (Bubbles v2). Default ON, so owning the prize is the only
        /// opt-in; this row is the way back out. It grants nothing by itself: the roll also asks the
        /// grants every time (<see cref="Services.Chaos.BrainDrainBubble.RollPool"/>).</summary>
        private void ChkBrainDrainBubble_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.BubbleBrainDrainEnabled = ChkBrainDrainBubble.IsChecked ?? false;
            CoreSettings.Save();
        }
    }
}
