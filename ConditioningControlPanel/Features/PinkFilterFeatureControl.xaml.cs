using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class PinkFilterFeatureControl : UserControl
    {
        private bool _isLoading;

        public PinkFilterFeatureControl()
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
                ChkEnable.IsChecked = s.PinkFilterEnabled;
                SliderOpacity.Value = s.PinkFilterOpacity;
                TxtOpacity.Text = $"{s.PinkFilterOpacity}%";
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.PinkFilterEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.PinkFilterOpacity))
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
    }
}
