using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class FlashFeatureControl : UserControl
    {
        private bool _isLoading;

        public FlashFeatureControl()
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
                ChkEnable.IsChecked = s.FlashEnabled;
                SliderFrequency.Value = s.FlashFrequency;
                TxtFrequency.Text = s.FlashFrequency.ToString();
                SliderImages.Value = s.SimultaneousImages;
                TxtImages.Text = s.SimultaneousImages.ToString();
                SliderMaxOnScreen.Value = s.HydraLimit;
                TxtMaxOnScreen.Text = s.HydraLimit.ToString();
                ChkClickable.IsChecked = s.FlashClickable;
                ChkCorruption.IsChecked = s.CorruptionMode;
                ChkHydraLinked.IsChecked = s.HydraLinkedTiming;
                ChkGlow.IsChecked = s.FlashGlowEnabled;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Reload on any flash-related property; the set is small.
            if (e.PropertyName == nameof(Models.AppSettings.FlashEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.FlashFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.SimultaneousImages) ||
                e.PropertyName == nameof(Models.AppSettings.HydraLimit) ||
                e.PropertyName == nameof(Models.AppSettings.FlashClickable) ||
                e.PropertyName == nameof(Models.AppSettings.CorruptionMode) ||
                e.PropertyName == nameof(Models.AppSettings.HydraLinkedTiming) ||
                e.PropertyName == nameof(Models.AppSettings.FlashGlowEnabled))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FlashEnabled = ChkEnable.IsChecked ?? false;
            App.Settings?.Save();
            // Note: Start/Stop of App.Flash is handled by MainWindow when the
            // engine is running. The popup just persists the setting.
        }

        private void SliderFrequency_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFrequency.Text = v.ToString();
            s.FlashFrequency = v;
            try { App.Flash?.RefreshSchedule(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Flash RefreshSchedule failed"); }
            App.Settings?.Save();
        }

        private void SliderImages_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtImages.Text = v.ToString();
            s.SimultaneousImages = v;
            App.Settings?.Save();
        }

        private void SliderMaxOnScreen_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtMaxOnScreen.Text = v.ToString();
            s.HydraLimit = v;
            App.Settings?.Save();
        }

        private void ChkClickable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FlashClickable = ChkClickable.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkCorruption_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.CorruptionMode = ChkCorruption.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkHydraLinked_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.HydraLinkedTiming = ChkHydraLinked.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkGlow_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FlashGlowEnabled = ChkGlow.IsChecked ?? false;
            App.Settings?.Save();
        }
    }
}
