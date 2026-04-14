using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class MindWipeFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public MindWipeFeatureControl()
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
                ChkEnable.IsChecked = s.MindWipeEnabled;
                SliderFreq.Value = s.MindWipeFrequency;
                TxtFreq.Text = $"{s.MindWipeFrequency}/h";
                SliderVolume.Value = s.MindWipeVolume;
                TxtVolume.Text = $"{s.MindWipeVolume}%";
                ChkLoop.IsChecked = s.MindWipeLoop;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.MindWipeEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.MindWipeFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.MindWipeVolume) ||
                e.PropertyName == nameof(Models.AppSettings.MindWipeLoop))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.MindWipeEnabled = ChkEnable.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFreq.Text = $"{v}/h";
            s.MindWipeFrequency = v;
            try { App.MindWipe?.UpdateSettings(s.MindWipeFrequency, s.MindWipeVolume / 100.0); }
            catch (Exception ex) { App.Logger?.Warning(ex, "MindWipe UpdateSettings failed"); }
            App.Settings?.Save();
        }

        private void SliderVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtVolume.Text = $"{v}%";
            s.MindWipeVolume = v;
            try { App.MindWipe?.UpdateSettings(s.MindWipeFrequency, s.MindWipeVolume / 100.0); }
            catch (Exception ex) { App.Logger?.Warning(ex, "MindWipe UpdateSettings failed"); }
            App.Settings?.Save();
        }

        private void ChkLoop_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var looping = ChkLoop.IsChecked ?? false;
            s.MindWipeLoop = looping;
            try
            {
                if (looping)
                    App.MindWipe?.StartLoop(s.MindWipeVolume / 100.0);
                else
                    App.MindWipe?.StopLoop();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "MindWipe loop toggle failed"); }
            App.Settings?.Save();
        }

        private void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            try { App.MindWipe?.TriggerOnce(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "MindWipe TriggerOnce failed"); }
        }
    }
}
