using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Scheduler panel, ported from the WPF head, settings logic restored against
    /// <see cref="CoreSettings"/>. Every handler round-trips <c>CoreSettings.Current.Scheduler*</c>
    /// and saves, exactly as the WPF original round-tripped <c>App.Settings</c>.
    ///
    /// <para>The WPF <c>SettingsHook</c>/<c>ISettingsRebindable</c> pair is inlined here: a cloud
    /// restore or a factory reset SWAPS the settings instance rather than mutating it, so the
    /// PropertyChanged subscription is tracked by instance and re-pointed on
    /// <c>SettingsService.CurrentReplaced</c>. Without that this permanently-mounted rack panel
    /// would keep showing, and writing to, the discarded object.</para>
    ///
    /// <para>The <c>_isLoading</c> guard is not optional: Avalonia raises
    /// <c>IsCheckedChanged</c> on a programmatic set exactly as WPF raised <c>Checked</c>, so a
    /// seed without it saves the markup defaults over the user's file.</para>
    /// </summary>
    public partial class SchedulerFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public SchedulerFeatureControl()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and everything below reads them.
            InitializeComponent();

            ChkEnabled.IsCheckedChanged += ChkEnabled_Changed;
            TxtStart.LostFocus += TxtTime_LostFocus;
            TxtEnd.LostFocus += TxtTime_LostFocus;
            DayMon.IsCheckedChanged += Day_Changed;
            DayTue.IsCheckedChanged += Day_Changed;
            DayWed.IsCheckedChanged += Day_Changed;
            DayThu.IsCheckedChanged += Day_Changed;
            DayFri.IsCheckedChanged += Day_Changed;
            DaySat.IsCheckedChanged += Day_Changed;
            DaySun.IsCheckedChanged += Day_Changed;

            LoadFromSettings();
        }

        // ---- settings instance tracking (WPF: SettingsHook + ISettingsRebindable) --------------

        private AppSettings? _hooked;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            RebindToCurrentSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            Unhook();
            base.OnDetachedFromVisualTree(e);
        }

        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(RebindToCurrentSettings);

        private void RebindToCurrentSettings()
        {
            Unhook();
            _hooked = CoreSettings.Current;
            _hooked.PropertyChanged += OnSettingsPropertyChanged;
            LoadFromSettings();
        }

        private void Unhook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsPropertyChanged;
            _hooked = null;
        }

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
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
            if (e.PropertyName?.StartsWith("Scheduler", StringComparison.Ordinal) == true)
                Dispatcher.UIThread.Post(LoadFromSettings);
        }

        private void ChkEnabled_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.SchedulerEnabled = ChkEnabled.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void TxtTime_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            s.SchedulerStartTime = TxtStart.Text ?? string.Empty;
            s.SchedulerEndTime = TxtEnd.Text ?? string.Empty;
            CoreSettings.Save();
        }

        private void Day_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            s.SchedulerMonday = DayMon.IsChecked ?? true;
            s.SchedulerTuesday = DayTue.IsChecked ?? true;
            s.SchedulerWednesday = DayWed.IsChecked ?? true;
            s.SchedulerThursday = DayThu.IsChecked ?? true;
            s.SchedulerFriday = DayFri.IsChecked ?? true;
            s.SchedulerSaturday = DaySat.IsChecked ?? true;
            s.SchedulerSunday = DaySun.IsChecked ?? true;
            CoreSettings.Save();
        }
    }
}
