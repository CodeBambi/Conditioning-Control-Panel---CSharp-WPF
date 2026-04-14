using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class VisualsFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public VisualsFeatureControl()
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
                SliderSize.Value = s.ImageScale;
                TxtSize.Text = $"{s.ImageScale}%";
                SliderOpacity.Value = s.FlashOpacity;
                TxtOpacity.Text = $"{s.FlashOpacity}%";
                SliderFade.Value = s.FadeDuration;
                TxtFade.Text = $"{s.FadeDuration}%";
                SliderDuration.Value = s.FlashDuration;
                TxtDuration.Text = $"{s.FlashDuration}s";
                ChkAudio.IsChecked = s.FlashAudioEnabled;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.ImageScale) ||
                e.PropertyName == nameof(Models.AppSettings.FlashOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.FadeDuration) ||
                e.PropertyName == nameof(Models.AppSettings.FlashDuration) ||
                e.PropertyName == nameof(Models.AppSettings.FlashAudioEnabled))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void SliderSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtSize.Text = $"{v}%";
            s.ImageScale = v;
            App.Settings?.Save();
        }

        private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtOpacity.Text = $"{v}%";
            s.FlashOpacity = v;
            App.Settings?.Save();
        }

        private void SliderFade_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFade.Text = $"{v}%";
            s.FadeDuration = v;
            App.Settings?.Save();
        }

        private void SliderDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtDuration.Text = $"{v}s";
            s.FlashDuration = v;
            App.Settings?.Save();
        }

        private void ChkAudio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FlashAudioEnabled = ChkAudio.IsChecked ?? false;
            App.Settings?.Save();
        }
    }
}
