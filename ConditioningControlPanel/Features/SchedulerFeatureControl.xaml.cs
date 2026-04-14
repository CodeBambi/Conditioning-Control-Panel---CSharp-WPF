using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class SchedulerFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public SchedulerFeatureControl()
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
                ChkEnabled.IsChecked = s.SchedulerEnabled;
                TxtStart.Text = s.SchedulerStartTime ?? "00:00";
                TxtEnd.Text = s.SchedulerEndTime ?? "22:00";
                DayMon.IsChecked = s.SchedulerMonday;
                DayTue.IsChecked = s.SchedulerTuesday;
                DayWed.IsChecked = s.SchedulerWednesday;
                DayThu.IsChecked = s.SchedulerThursday;
                DayFri.IsChecked = s.SchedulerFriday;
                DaySat.IsChecked = s.SchedulerSaturday;
                DaySun.IsChecked = s.SchedulerSunday;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName?.StartsWith("Scheduler") == true)
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
        }

        private void ChkEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SchedulerEnabled = ChkEnabled.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void TxtTime_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SchedulerStartTime = TxtStart.Text;
            s.SchedulerEndTime = TxtEnd.Text;
            App.Settings?.Save();
        }

        private void Day_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SchedulerMonday = DayMon.IsChecked ?? true;
            s.SchedulerTuesday = DayTue.IsChecked ?? true;
            s.SchedulerWednesday = DayWed.IsChecked ?? true;
            s.SchedulerThursday = DayThu.IsChecked ?? true;
            s.SchedulerFriday = DayFri.IsChecked ?? true;
            s.SchedulerSaturday = DaySat.IsChecked ?? true;
            s.SchedulerSunday = DaySun.IsChecked ?? true;
            App.Settings?.Save();
        }
    }
}
