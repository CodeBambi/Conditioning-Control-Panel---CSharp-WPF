// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs: the two limits (:706-783),
// BtnLimits_Click, RefreshLimits, Wanted, PaintLimits, PaintPending, LimitSlider_ValueChanged and
// ChkRelock_Changed, on Core TabLimits / LimitChange. Down applies now; up waits a day.
using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private void BtnLimits_Click(object? sender, RoutedEventArgs e) => LimitsCard.IsVisible = !LimitsCard.IsVisible;

        internal void RefreshLimits()
        {
            var caps = ChasterHead.Service?.Caps ?? TabLimits.Default;
            var settings = CoreSettings.Current;
            _loading = true;
            try
            {
                // The sliders sit where the player put them: a raise still waiting shows at its
                // waiting figure, and the line under the number says when it lands.
                SliderDayLimit.Value = Wanted(settings.ChasterDayLimit, caps.DailySeconds / 60);
                SliderBacklogLimit.Value = Wanted(settings.ChasterBacklogLimit, caps.BacklogSeconds / 60);
                ChkRelock.IsChecked = settings.ChasterRelockPastEnd;
            }
            finally { _loading = false; }
            PaintLimits(caps);
        }

        private static int Wanted(LimitSetting setting, int inForce)
        {
            var settled = LimitChange.Settle(setting, DateTime.UtcNow);
            return settled.HasPending ? settled.PendingMinutes : inForce;
        }

        private void PaintLimits(TabLimits caps)
        {
            TxtDayLimit.Text = CircesTab.Format(caps.DailySeconds, signed: false);
            TxtBacklogLimit.Text = CircesTab.Format(caps.BacklogSeconds, signed: false);
            TxtFactCap1.Text = TxtFactCap2.Text = CircesTab.Format(caps.DailySeconds, signed: false);
            var settings = CoreSettings.Current;
            PaintPending(TxtDayPending, settings.ChasterDayLimit);
            PaintPending(TxtBacklogPending, settings.ChasterBacklogLimit);
        }

        private static void PaintPending(TextBlock line, LimitSetting setting)
        {
            var s = LimitChange.Settle(setting, DateTime.UtcNow);
            if (!s.HasPending) { line.IsVisible = false; return; }
            line.Text = Loc.GetF("chaster_limit_pending", CircesTab.Format(s.PendingMinutes * 60, signed: false),
                s.PendingAtUtc!.Value.ToLocalTime().ToString("ddd HH:mm"));
            line.IsVisible = true;
        }

        private void LimitSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loading || !_menuReady) return;
            var settings = CoreSettings.Current;
            // The backlog never sits under the day: dragging the day past it carries it along.
            var wanted = TabLimits.FromMinutes((int)SliderDayLimit.Value, (int)SliderBacklogLimit.Value);
            // Down applies now; up waits a day (LimitChange). Each limit is asked on its own.
            var now = DateTime.UtcNow;
            settings.ChasterDayLimit = LimitChange.Request(settings.ChasterDayLimit, wanted.DailySeconds / 60, now);
            settings.ChasterBacklogLimit = LimitChange.Request(settings.ChasterBacklogLimit, wanted.BacklogSeconds / 60, now);
            if ((int)SliderBacklogLimit.Value != wanted.BacklogSeconds / 60)
            {
                _loading = true;
                try { SliderBacklogLimit.Value = wanted.BacklogSeconds / 60; }
                finally { _loading = false; }
            }
            PaintLimits(ChasterHead.Service?.Caps ?? wanted);
            RefreshNumbers();
            CoreSettings.Save();
        }

        private void ChkRelock_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading || !_menuReady) return;
            CoreSettings.Current.ChasterRelockPastEnd = ChkRelock.IsChecked == true;
            CoreSettings.Save();
        }
    }
}
