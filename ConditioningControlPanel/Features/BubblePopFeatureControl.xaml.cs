using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class BubblePopFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public BubblePopFeatureControl()
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
            // The egg hint names the active persona; the rack hosts this control permanently,
            // so a mod switch must repaint it (a popup instance never lived long enough to care).
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged -= OnSettingsPropertyChanged;
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
        }

        private void OnModChanged(object? sender, Models.ModPackage mod)
        {
            Dispatcher.BeginInvoke(new Action(LoadFromSettings));
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.BubblesEnabled;
                SliderFreq.Value = s.BubblesFrequency;
                TxtFreq.Text = s.BubblesFrequency.ToString();
                SliderVolume.Value = s.BubblesVolume;
                TxtVolume.Text = $"{s.BubblesVolume}%";
                SliderSpeed.Value = s.BubbleSpeedBoost;
                TxtSpeed.Text = $"+{s.BubbleSpeedBoost}%";
                ChkSolidMode.IsChecked = s.BubbleSharedHost;

                // Easter-egg hint (companion auto-pops a lingering effect bubble) — name the active persona.
                var persona = App.Mods?.ActiveModId switch
                {
                    "builtin-bambisleep" => "Bambi",
                    "builtin-sissyhypno" => "your bimbo",
                    "builtin-locked" => "Circe",
                    _ => "your companion"
                };
                TxtTriggerEggHint.Text = $"careful — {persona} loves these…";

                ChkTriggers.IsChecked = s.BubbleTriggersEnabled;
                TriggerOptionsPanel.Visibility = s.BubbleTriggersEnabled
                    ? Visibility.Visible : Visibility.Collapsed;
                SliderTriggerChance.Value = s.BubbleTriggerChance;
                TxtTriggerChance.Text = $"{s.BubbleTriggerChance}%";
                var ids = s.BubbleTriggerVariants ?? new System.Collections.Generic.List<string>();
                ChkTypeFlash.IsChecked = ids.Contains("flash");
                ChkTypeSubliminal.IsChecked = ids.Contains("subliminal");
                ChkTypePink.IsChecked = ids.Contains("pink");
                ChkTypeSpiral.IsChecked = ids.Contains("spiral");
                ChkTypeGlitch.IsChecked = ids.Contains("glitch");
                ChkTypeCascade.IsChecked = ids.Contains("htlink");
                ChkTypeVideo.IsChecked = ids.Contains("video");
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.BubblesEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BubblesFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.BubblesVolume) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleSpeedBoost) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleTriggersEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleTriggerChance) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleTriggerVariants))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var on = ChkEnable.IsChecked ?? false;
            s.BubblesEnabled = on;
            App.Settings?.Save();

            // Live-apply: start/stop bubble service if engine is running
            if (App.IsEngineRunning)
            {
                if (on)
                    App.Bubbles?.Start();
                else
                    App.Bubbles?.Stop();
            }
        }

        private void SliderFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFreq.Text = v.ToString();
            s.BubblesFrequency = v;
            try { App.Bubbles?.RefreshFrequency(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Bubbles RefreshFrequency failed"); }
            App.Settings?.Save();
        }

        private void SliderVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtVolume.Text = $"{v}%";
            s.BubblesVolume = v;
            App.Settings?.Save();
        }

        private void SliderSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtSpeed.Text = $"+{v}%";
            s.BubbleSpeedBoost = v;
            App.Settings?.Save();
        }

        private void ChkSolidMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BubbleSharedHost = ChkSolidMode.IsChecked ?? false;
            App.Settings?.Save();

            // The render path is latched per Start->Stop session, so bounce a live bubble service to
            // pick up the new mode (no-op when bubbles aren't currently running).
            if (App.IsEngineRunning && s.BubblesEnabled && App.Bubbles?.IsRunning == true)
            {
                App.Bubbles.Stop();
                App.Bubbles.Start();
            }
        }

        private void ChkTriggers_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var on = ChkTriggers.IsChecked ?? false;
            s.BubbleTriggersEnabled = on;
            TriggerOptionsPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            App.Settings?.Save();
        }

        private void SliderTriggerChance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtTriggerChance.Text = $"{v}%";
            s.BubbleTriggerChance = v;
            App.Settings?.Save();
        }

        private void TriggerType_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            if (sender is not CheckBox cb || cb.Tag is not string id) return;

            var ids = new System.Collections.Generic.List<string>(
                s.BubbleTriggerVariants ?? new System.Collections.Generic.List<string>());
            var on = cb.IsChecked ?? false;
            if (on) { if (!ids.Contains(id)) ids.Add(id); }
            else ids.Remove(id);
            s.BubbleTriggerVariants = ids;   // reassign so the setter fires change notification
            App.Settings?.Save();
        }
    }
}
