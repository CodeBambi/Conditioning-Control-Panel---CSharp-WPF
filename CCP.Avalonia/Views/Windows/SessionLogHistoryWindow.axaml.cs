using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

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
    ///  - <see cref="HistoryRow"/> takes the fields it formats rather than a
    ///    <c>Models.SessionLog</c>: that model lives in the WPF head, not CCP.Core, and this port
    ///    may reference neither. The formatting it does — duration, media counts, status — is
    ///    ported verbatim, so restoring the <c>HistoryRow(SessionLog)</c> constructor when the model
    ///    reaches Core is a one-liner.
    /// </summary>
    public partial class SessionLogHistoryWindow : Window
    {
        private readonly TextBlock _txtEmpty;
        private readonly TextBlock _txtCount;
        private readonly ItemsControl _logList;

        public SessionLogHistoryWindow()
        {
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
            var rows = LoadRecentRows();

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

        /// <summary>
        /// WPF read <c>App.SessionLog.LoadRecentLogs()</c>.
        /// ponytail: needs SessionLogService, wired when it moves to Core. Until then this returns
        /// placeholder rows so the window renders the populated state rather than the empty one.
        /// </summary>
        private static List<HistoryRow> LoadRecentRows() => new()
        {
            new HistoryRow("🌀", "Deep Spiral", new DateTime(2026, 8, 30, 21, 14, 0),
                TimeSpan.FromMinutes(42) + TimeSpan.FromSeconds(18), videos: 12, images: 48, completed: true),
            new HistoryRow("💗", "Soft Start", new DateTime(2026, 8, 29, 19, 2, 0),
                TimeSpan.FromMinutes(11) + TimeSpan.FromSeconds(5), videos: 3, images: 20, completed: true),
            new HistoryRow("🔒", "Lockdown Hour", new DateTime(2026, 8, 27, 23, 40, 0),
                TimeSpan.FromHours(1) + TimeSpan.FromMinutes(6) + TimeSpan.FromSeconds(31), videos: 21, images: 0, completed: false),
        };

        private void LogRow_Click(object? sender, RoutedEventArgs e)
        {
            if (e.Source is not Control c) return;
            if (c.Tag is not HistoryRow) return;

            // WPF opened SessionCompleteWindow(row.Log, playSound: false) as a modal child.
            // ponytail: needs SessionCompleteWindow (not ported yet) and the SessionLog model,
            // wired when they move to Core.
        }
    }

    /// <summary>
    /// One row of the history list. Private nested class in the WPF original; public and top-level
    /// here because a compiled binding's <c>x:DataType</c> cannot name a nested type.
    /// </summary>
    public sealed class HistoryRow
    {
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

        public HistoryRow(string? icon, string? name, DateTime startedAt, TimeSpan duration,
                          int videos, int images, bool completed)
        {
            Icon = icon ?? "";
            Name = name ?? "";
            StartedText = startedAt.ToString("g");

            var d = duration;
            DurationText = d.TotalHours >= 1
                ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
                : $"{d.Minutes:D2}:{d.Seconds:D2}";

            MediaText = Loc.GetF("label_media_count_videos_images", videos, images);

            if (completed)
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
