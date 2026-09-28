using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    public enum NotificationType { Info, Success, Warning, Error }

    /// <summary>
    /// In-app corner toasts, ported from ConditioningControlPanel/Services/Notifications/
    /// NotificationService.cs: same colours, sizes, 5 s default, action button and × dismiss,
    /// newest stacked below in MainShellWindow's NotificationHost. Toasts fired before the host
    /// attaches are replayed on attach, as WPF does.
    /// ponytail: WPF's ShowSticky/Dismiss(key) are not ported - nothing on this head calls them yet.
    /// </summary>
    public sealed class NotificationService
    {
        private Panel? _host;
        private readonly List<(string message, NotificationType type, TimeSpan? duration, string? actionLabel, Action? action)> _pending = new();

        public void AttachHost(Panel host)
        {
            _host = host;
            var queue = _pending.ToArray();
            _pending.Clear();
            foreach (var (msg, type, dur, label, action) in queue) Show(msg, type, dur, label, action);
        }

        public void Show(string message, NotificationType type = NotificationType.Info, TimeSpan? duration = null,
            string? actionLabel = null, Action? action = null)
        {
            if (_host == null) { _pending.Add((message, type, duration, actionLabel, action)); return; }

            var border = BuildToast(message, type, actionLabel, action);
            _host.Children.Add(border);
            border.Opacity = 1; // fades in through the Opacity transition (WPF AnimateIn, 180 ms)

            var timer = new DispatcherTimer { Interval = duration ?? TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => { timer.Stop(); FadeOutAndRemove(border); };
            border.Tag = timer;
            timer.Start();
        }

        private Border BuildToast(string message, NotificationType type, string? actionLabel, Action? action)
        {
            var accent = new SolidColorBrush(Color.Parse(type switch
            {
                NotificationType.Success => "#4CAF50",
                NotificationType.Warning => "#FFB347",
                NotificationType.Error => "#FF6B6B",
                _ => "#FF69B4",
            }));

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#252542")),
                BorderBrush = accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 10, 10),
                Margin = new Thickness(0, 0, 0, 10),
                MaxWidth = 360,
                Opacity = 0,
                BoxShadow = BoxShadows.Parse("0 2 16 0 #66000000"),
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new QuadraticEaseOut() },
                },
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);

            if (!string.IsNullOrWhiteSpace(actionLabel) && action != null)
            {
                // TextBlock content: Avalonia's Button would read '_' in a label as an access key.
                var actionBtn = new Button
                {
                    Name = "ToastAction",
                    Content = new TextBlock { Text = actionLabel, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
                    Padding = new Thickness(10, 4),
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = accent,
                    BorderThickness = new Thickness(0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                actionBtn.Click += (_, _) =>
                {
                    try { action(); }
                    catch (Exception ex) { Log.Warning(ex, "NotificationService: action button handler failed"); }
                    FadeOutAndRemove(border);
                };
                buttons.Children.Add(actionBtn);
            }

            var dismissBtn = new Button
            {
                Name = "ToastDismiss",
                Content = new TextBlock { Text = "×", FontSize = 16, Foreground = new SolidColorBrush(Color.Parse("#B0B0C0")) },
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Top,
            };
            dismissBtn.Click += (_, _) => FadeOutAndRemove(border);
            buttons.Children.Add(dismissBtn);

            border.Child = grid;
            return border;
        }

        private void FadeOutAndRemove(Border border)
        {
            (border.Tag as DispatcherTimer)?.Stop();
            border.IsHitTestVisible = false;
            // WPF FadeOutAndRemove: 220 ms ease-in (the fade-in transition is 180 ms ease-out).
            border.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new QuadraticEaseIn() },
            };
            border.Opacity = 0;
            DispatcherTimer.RunOnce(() => _host?.Children.Remove(border), TimeSpan.FromMilliseconds(220));
        }
    }
}
