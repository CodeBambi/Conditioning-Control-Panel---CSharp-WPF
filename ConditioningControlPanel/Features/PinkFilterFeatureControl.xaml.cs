using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class PinkFilterFeatureControl : UserControl
    {
        private const int UnlockLevel = 10;
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
            RefreshLockState();
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += OnSettingsPropertyChanged;
            if (App.Progression != null)
                App.Progression.LevelUp += OnLevelUp;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged -= OnSettingsPropertyChanged;
            if (App.Progression != null)
                App.Progression.LevelUp -= OnLevelUp;
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

        private void RefreshLockState()
        {
            var unlocked = App.Settings?.Current?.IsLevelUnlocked(UnlockLevel) ?? false;
            LockedPanel.Visibility = unlocked ? Visibility.Collapsed : Visibility.Visible;
            UnlockedPanel.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.PinkFilterEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.PinkFilterOpacity))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
            else if (e.PropertyName == nameof(Models.AppSettings.PlayerLevel) ||
                     e.PropertyName == nameof(Models.AppSettings.HighestLevelEver))
            {
                Dispatcher.BeginInvoke(new Action(RefreshLockState));
            }
        }

        private void OnLevelUp(object? sender, int newLevel)
        {
            Dispatcher.BeginInvoke(new Action(RefreshLockState));
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
