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

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            SliderFreq.ValueChanged += SliderFreq_Changed;
            SliderVolume.ValueChanged += SliderVolume_Changed;
            SliderSize.ValueChanged += SliderSize_Changed;
            SliderSpeed.ValueChanged += SliderSpeed_Changed;
            ChkSolidMode.IsCheckedChanged += ChkSolidMode_Changed;
            ChkTriggers.IsCheckedChanged += ChkTriggers_Changed;
            SliderTriggerChance.ValueChanged += SliderTriggerChance_Changed;
            foreach (var box in TriggerTypeBoxes()) box.IsCheckedChanged += TriggerType_Changed;

            LoadFromSettings();

            // ponytail: WPF also repaints the hero and side art plates here (ApplyFeatureArt).
            // Needs Services.ModResourceResolver.ResolveImageDecoded
            // (ConditioningControlPanel/Services/, still in the WPF head) AND a named ImageBrush
            // in the .axaml, which Avalonia rejects (x:Name on a brush is AVLN2000); the port
            // draws a static wash instead, so there is nothing here to repaint.
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
            RebindToCurrentSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            CoreMods.ModChanged -= OnModChanged;
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
                TxtTriggerEggHint.Text = $"careful — {persona} loves these…";

                ChkTriggers.IsChecked = s.BubbleTriggersEnabled;
                TriggerOptionsPanel.IsVisible = s.BubbleTriggersEnabled;
                SliderTriggerChance.Value = s.BubbleTriggerChance;
                TxtTriggerChance.Text = $"{s.BubbleTriggerChance}%";
                var ids = s.BubbleTriggerVariants ?? new List<string>();
                foreach (var box in TriggerTypeBoxes())
                    box.IsChecked = box.Tag is string id && ids.Contains(id);
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
            // ponytail: needs Services.BubbleService.AmbientBubbleDailyXpCap and
            // AmbientBubbleXpPaidToday() (ConditioningControlPanel/Services/BubbleService.cs, still
            // in the WPF head) - the paid-today counter is on AppSettings in Core, but the 300 cap
            // and the clamp are not, and duplicating the constant here would put it in two places.
            // WPF also repaints this from BubbleService.AmbientXpBudgetChanged, which is the same
            // head-side class.
            TxtAmbientXpBudget.Text = Loc.GetF("label_ambient_bubble_xp_budget", 0, 300);
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.BubblesEnabled) ||
                e.PropertyName == nameof(AppSettings.BubblesFrequency) ||
                e.PropertyName == nameof(AppSettings.BubblesVolume) ||
                e.PropertyName == nameof(AppSettings.BubblesSize) ||
                e.PropertyName == nameof(AppSettings.BubbleSpeedBoost) ||
                e.PropertyName == nameof(AppSettings.BubbleGazePopEnabled) ||
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
            // ponytail: WPF also live-applies here - App.Bubbles.Start()/Stop() when
            // App.IsEngineRunning. Needs BubbleService (ConditioningControlPanel/Services/) and the
            // engine-running flag off App, both still in the WPF head.
        }

        private void SliderFreq_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtFreq.Text = v.ToString();
            CoreSettings.Current.BubblesFrequency = v;
            // ponytail: WPF also calls App.Bubbles.RefreshFrequency() here. Needs BubbleService
            // (ConditioningControlPanel/Services/), still in the WPF head.
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
            // ponytail: the render path is latched per Start->Stop session, so WPF bounces a live
            // bubble service here to pick up the new mode. Needs BubbleService
            // (ConditioningControlPanel/Services/) and App.IsEngineRunning, still in the WPF head.
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

        // ponytail: WPF also carries the "stare to pop" row here - ChkBubbleGazePop_Changed
        // (BubbleGazePopEnabled, which is in Core) plus TxtBubbleGazeHint, whose visibility asks
        // App.Webcam.IsRunning / .Calibration and Services.WebcamTrackingService.IsConsentCurrent()
        // in the WPF head. Neither control exists in this port's .axaml, so there is nothing to
        // wire until that card is ported.
    }
}
