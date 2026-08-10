using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class SchedulerFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true;

        public SchedulerFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // Tracks WHICH AppSettings instance the hook is attached to, so a cloud restore - which
        // SWAPS the instance - can be followed instead of leaving this permanently-mounted rack
        // panel listening to, and displaying, the discarded object. See ISettingsRebindable.
        private SettingsHook? _settingsHook;

        private void OnLoaded(object sender, RoutedEventArgs e) => RebindToCurrentSettings();

        private void OnUnloaded(object sender, RoutedEventArgs e) => _settingsHook?.Unhook();

        /// <inheritdoc/>
        public void RebindToCurrentSettings()
        {
            (_settingsHook ??= new SettingsHook(OnSettingsPropertyChanged)).Rebind();
            LoadFromSettings();
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
