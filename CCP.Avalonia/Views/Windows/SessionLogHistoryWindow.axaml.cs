using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Windows/SessionLogHistoryWindow.xaml.cs. Deviations:
    ///  - WPF's <c>DialogResult</c> becomes <c>Close(true)</c>, because Avalonia carries the result
    ///    through <c>ShowDialog&lt;bool?&gt;</c>.
    ///  - <c>Visibility</c> -> <c>IsVisible</c>.
    ///  - The row Click handler moves out of the DataTemplate onto the ItemsControl: template
    ///    content has no name scope to bind a markup handler through. The Tag still carries the
    ///    row, exactly as the WPF original read it.
    ///  - Reads Core's <see cref="SessionLogService"/> directly instead of <c>App.SessionLog</c>;
    ///    the service holds no state for a read.
    /// </summary>
    public partial class SessionLogHistoryWindow : Window
    {
        private readonly TextBlock _txtEmpty;
        private readonly TextBlock _txtCount;
        private readonly ItemsControl _logList;

        private readonly IReadOnlyList<SessionLog> _logs;

        /// <summary>WPF's constructor: the user's persisted logs, newest first.</summary>
        public SessionLogHistoryWindow() : this(new SessionLogService().LoadRecentLogs()) { }

        internal SessionLogHistoryWindow(IReadOnlyList<SessionLog> logs)
        {
            _logs = logs;
            AvaloniaXamlLoader.Load(this);

            _txtEmpty = this.FindControl<TextBlock>("TxtEmpty")!;
            _txtCount = this.FindControl<TextBlock>("TxtCount")!;
            _logList = this.FindControl<ItemsControl>("LogList")!;

            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close(true);

            // One handler on the list instead of one inside the DataTemplate; Click bubbles.
            _logList.AddHandler(Button.ClickEvent, LogRow_Click);

            Loaded += (_, _) => LoadLogs();
        }

        private void LoadLogs()
        {
            var rows = _logs.Select(l => new HistoryRow(l)).ToList();

            if (rows.Count == 0)
            {
                _txtEmpty.IsVisible = true;
                _logList.IsVisible = false;
                _txtCount.Text = "";
            }
            else
            {
                _txtEmpty.IsVisible = false;
                _logList.IsVisible = true;
                _logList.ItemsSource = rows;
                _txtCount.Text = Loc.GetF("label_session_count", rows.Count);
            }
        }

        private void LogRow_Click(object? sender, RoutedEventArgs e)
        {
            if (e.Source is not Control c) return;
            if (c.Tag is not HistoryRow row) return;

            try
            {
                _ = new SessionCompleteWindow(row.Log, playSound: false).ShowDialog(this);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open historical session log");
            }
        }
    }

    /// <summary>
    /// One row of the history list. Private nested class in the WPF original; public and top-level
    /// here because a compiled binding's <c>x:DataType</c> cannot name a nested type.
    /// </summary>
    public sealed class HistoryRow
    {
        public SessionLog Log { get; }
        public string Icon { get; }
        public string Name { get; }
        public string StartedText { get; }
        public string DurationText { get; }
        public string MediaText { get; }
        public string StatusText { get; }
        public IBrush StatusBrush { get; }

        /// <summary>Icon + name, one bound string. WPF drew this as three Runs in one TextBlock.</summary>
        public string Headline => $"{Icon} {Name}";

        /// <summary>The WPF footer line's five Runs, joined with the same separator.</summary>
        public string Meta => $"{StartedText}  ·  {DurationText}  ·  {MediaText}";

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
