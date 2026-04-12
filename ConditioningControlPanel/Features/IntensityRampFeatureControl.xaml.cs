using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class IntensityRampFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public IntensityRampFeatureControl()
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
                ChkEnabled.IsChecked = s.IntensityRampEnabled;
                SliderDuration.Value = s.RampDurationMinutes;
                TxtDuration.Text = $"{s.RampDurationMinutes} min";
                SliderMultiplier.Value = s.SchedulerMultiplier;
                TxtMultiplier.Text = $"{s.SchedulerMultiplier:F1}x";
                ChkEndAt.IsChecked = s.EndSessionOnRampComplete;
                ChkLinkFlash.IsChecked = s.RampLinkFlashOpacity;
                ChkLinkSpiral.IsChecked = s.RampLinkSpiralOpacity;
                ChkLinkPink.IsChecked = s.RampLinkPinkFilterOpacity;
                ChkLinkMaster.IsChecked = s.RampLinkMasterAudio;
                ChkLinkSub.IsChecked = s.RampLinkSubliminalAudio;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.IntensityRampEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.RampDurationMinutes) ||
                e.PropertyName == nameof(Models.AppSettings.SchedulerMultiplier) ||
                e.PropertyName == nameof(Models.AppSettings.EndSessionOnRampComplete) ||
                e.PropertyName == nameof(Models.AppSettings.RampLinkFlashOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.RampLinkSpiralOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.RampLinkPinkFilterOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.RampLinkMasterAudio) ||
                e.PropertyName == nameof(Models.AppSettings.RampLinkSubliminalAudio))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.IntensityRampEnabled = ChkEnabled.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtDuration.Text = $"{v} min";
            s.RampDurationMinutes = v;
            App.Settings?.Save();
        }

        private void SliderMultiplier_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = e.NewValue;
            TxtMultiplier.Text = $"{v:F1}x";
            s.SchedulerMultiplier = v;
            App.Settings?.Save();
        }

        private void ChkEndAt_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.EndSessionOnRampComplete = ChkEndAt.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void Link_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.RampLinkFlashOpacity = ChkLinkFlash.IsChecked ?? false;
            s.RampLinkSpiralOpacity = ChkLinkSpiral.IsChecked ?? false;
            s.RampLinkPinkFilterOpacity = ChkLinkPink.IsChecked ?? false;
            s.RampLinkMasterAudio = ChkLinkMaster.IsChecked ?? false;
            s.RampLinkSubliminalAudio = ChkLinkSub.IsChecked ?? false;
            App.Settings?.Save();
        }
    }
}
