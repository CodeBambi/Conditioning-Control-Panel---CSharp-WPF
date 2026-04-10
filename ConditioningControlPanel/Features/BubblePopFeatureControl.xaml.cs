using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class BubblePopFeatureControl : UserControl
    {
        private const int UnlockLevel = 20;
        private bool _isLoading;

        public BubblePopFeatureControl()
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
                ChkEnable.IsChecked = s.BubblesEnabled;
                SliderFreq.Value = s.BubblesFrequency;
                TxtFreq.Text = s.BubblesFrequency.ToString();
                SliderVolume.Value = s.BubblesVolume;
                TxtVolume.Text = $"{s.BubblesVolume}%";
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
            if (e.PropertyName == nameof(Models.AppSettings.BubblesEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BubblesFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.BubblesVolume))
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
            var on = ChkEnable.IsChecked ?? false;
            s.BubblesEnabled = on;
            App.Settings?.Save();
            // Note: Start/Stop of the Bubbles service is driven by the engine-running
            // state held on MainWindow, not by the popup. We only persist the setting.
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
    }
}
