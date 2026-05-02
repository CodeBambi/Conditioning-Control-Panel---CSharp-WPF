using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    public partial class SessionLogHistoryWindow : Window
    {
        public SessionLogHistoryWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadLogs();
        }

        private void LoadLogs()
        {
            var logs = App.SessionLog?.LoadRecentLogs() ?? new List<SessionLog>();
            var rows = logs.Select(l => new HistoryRow(l)).ToList();

            if (rows.Count == 0)
            {
                TxtEmpty.Visibility = Visibility.Visible;
                LogList.Visibility = Visibility.Collapsed;
                TxtCount.Text = "";
            }
            else
            {
                TxtEmpty.Visibility = Visibility.Collapsed;
                LogList.Visibility = Visibility.Visible;
                LogList.ItemsSource = rows;
                TxtCount.Text = Loc.GetF("label_session_count", rows.Count);
            }
        }

        private void LogRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.Tag is not HistoryRow row) return;

            try
            {
                var dialog = new SessionCompleteWindow(row.Log, playSound: false)
                {
                    Owner = this,
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open historical session log");
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private class HistoryRow
        {
            public SessionLog Log { get; }
            public string Icon { get; }
            public string Name { get; }
            public string StartedText { get; }
            public string DurationText { get; }
            public string MediaText { get; }
            public string StatusText { get; }
            public Brush StatusBrush { get; }

            public HistoryRow(SessionLog log)
            {
                Log = log;
                Icon = log.SessionIcon ?? "";
                Name = log.SessionName ?? "";
                StartedText = log.StartedAt.ToString("g");

                var d = log.Duration;
                DurationText = d.TotalHours >= 1
                    ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
                    : $"{d.Minutes:D2}:{d.Seconds:D2}";

                int videos = log.Media?.Count(m => m.Type == MediaType.Video) ?? 0;
                int images = (log.Media?.Count ?? 0) - videos;
                MediaText = Loc.GetF("label_media_count_videos_images", videos, images);

                if (log.Completed)
                {
                    StatusText = Loc.Get("label_completed");
                    StatusBrush = new SolidColorBrush(Color.FromRgb(144, 238, 144));
                }
                else
                {
                    StatusText = Loc.Get("label_aborted");
                    StatusBrush = new SolidColorBrush(Color.FromRgb(255, 165, 0));
                }
            }
        }
    }
}
