using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class SubliminalFeatureControl : UserControl
    {
        private bool _isLoading;

        public SubliminalFeatureControl()
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
                ChkEnable.IsChecked = s.SubliminalEnabled;
                SliderPerMin.Value = s.SubliminalFrequency;
                TxtPerMin.Text = s.SubliminalFrequency.ToString();
                SliderFrames.Value = s.SubliminalDuration;
                TxtFrames.Text = s.SubliminalDuration.ToString();
                SliderOpacity.Value = s.SubliminalOpacity;
                TxtOpacity.Text = $"{s.SubliminalOpacity}%";
                ChkWhispers.IsChecked = s.SubAudioEnabled;
                SliderWhisperVol.Value = s.SubAudioVolume;
                TxtWhisperVol.Text = $"{s.SubAudioVolume}%";
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.SubliminalEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalDuration) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.SubAudioEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.SubAudioVolume))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SubliminalEnabled = ChkEnable.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderPerMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtPerMin.Text = v.ToString();
            s.SubliminalFrequency = v;
            App.Settings?.Save();
        }

        private void SliderFrames_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFrames.Text = v.ToString();
            s.SubliminalDuration = v;
            App.Settings?.Save();
        }

        private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtOpacity.Text = $"{v}%";
            s.SubliminalOpacity = v;
            App.Settings?.Save();
        }

        private void ChkWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SubAudioEnabled = ChkWhispers.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderWhisperVol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtWhisperVol.Text = $"{v}%";
            s.SubAudioVolume = v;
            App.Settings?.Save();
        }

        private void BtnManageMessages_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var dialog = new TextEditorDialog("Subliminal Messages", s.SubliminalPool)
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                s.SubliminalPool = dialog.ResultData;
                App.Settings?.Save();
                App.Logger?.Information("Subliminal pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        private void BtnAdvanced_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ColorEditorDialog
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }
    }
}
