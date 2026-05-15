using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services
{
    public enum NotificationType { Info, Success, Warning, Error }

    /// <summary>
    /// Non-blocking in-app toast/banner surface. Hosts toast Borders inside a
    /// StackPanel attached at the top-right of MainWindow's RootGrid. The host
    /// panel has no Background, so empty space is click-through — only the
    /// toast bodies themselves capture clicks. Each toast has a dismiss
    /// affordance; sticky toasts (ShowSticky) also persist their dismissed
    /// state to AppSettings.DismissedNotificationKeys so they don't re-appear
    /// on subsequent launches.
    /// </summary>
    public class NotificationService
    {
        private Panel? _host;
        private readonly List<(string message, NotificationType type, TimeSpan? duration, string? stickyKey, string? actionLabel, Action? action)> _pending = new();
        private readonly Dictionary<string, Border> _stickyByKey = new();

        public void AttachHost(Panel host)
        {
            _host = host;
            // Replay anything that fired before the host was attached.
            if (_pending.Count > 0)
            {
                var queue = _pending.ToArray();
                _pending.Clear();
                foreach (var (msg, type, dur, key, actionLabel, action) in queue)
                {
                    if (key != null) ShowStickyInternal(key, msg, type, actionLabel, action);
                    else ShowInternal(msg, type, dur ?? TimeSpan.FromSeconds(5), actionLabel, action);
                }
            }
        }

        public void Show(string message, NotificationType type = NotificationType.Info, TimeSpan? duration = null,
            string? actionLabel = null, Action? action = null)
        {
            if (_host == null) { _pending.Add((message, type, duration, null, actionLabel, action)); return; }
            ShowInternal(message, type, duration ?? TimeSpan.FromSeconds(5), actionLabel, action);
        }

        /// <summary>
        /// Persistent toast keyed by <paramref name="key"/>. No-op when the key
        /// is already in AppSettings.DismissedNotificationKeys (user dismissed
        /// it in a prior session) or already showing in this session.
        /// Optional action button fires <paramref name="action"/> and clears
        /// the sticky (but does NOT add the key to DismissedNotificationKeys —
        /// the action handler is responsible for resolving the underlying
        /// condition).
        /// </summary>
        public void ShowSticky(string key, string message, NotificationType type = NotificationType.Info,
            string? actionLabel = null, Action? action = null)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (App.Settings?.Current?.DismissedNotificationKeys?.Contains(key) == true) return;
            if (_host == null) { _pending.Add((message, type, null, key, actionLabel, action)); return; }
            ShowStickyInternal(key, message, type, actionLabel, action);
        }

        /// <summary>
        /// Programmatically clears a sticky toast (e.g. recalibrate-suggest
        /// after the user recalibrates). Does NOT mark the key as dismissed
        /// in AppSettings — the toast may re-show if the condition recurs.
        /// </summary>
        public void Dismiss(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (_stickyByKey.TryGetValue(key, out var border))
            {
                _stickyByKey.Remove(key);
                FadeOutAndRemove(border);
            }
            // Also remove from pending queue if it hasn't shown yet.
            _pending.RemoveAll(p => p.stickyKey == key);
        }

        private void ShowInternal(string message, NotificationType type, TimeSpan duration,
            string? actionLabel, Action? action)
        {
            var border = BuildToast(message, type, stickyKey: null, actionLabel, action);
            _host?.Children.Add(border);
            AnimateIn(border);

            var timer = new DispatcherTimer { Interval = duration };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                FadeOutAndRemove(border);
            };
            // Stashed on the border so manual-dismiss handlers in BuildToast
            // can stop the timer rather than leak it for the rest of the
            // duration window.
            border.Tag = timer;
            timer.Start();
        }

        private void ShowStickyInternal(string key, string message, NotificationType type,
            string? actionLabel, Action? action)
        {
            if (_stickyByKey.ContainsKey(key)) return; // already showing
            var border = BuildToast(message, type, stickyKey: key, actionLabel, action);
            _stickyByKey[key] = border;
            _host?.Children.Add(border);
            AnimateIn(border);
        }

        private Border BuildToast(string message, NotificationType type, string? stickyKey,
            string? actionLabel = null, Action? action = null)
        {
            var accent = type switch
            {
                NotificationType.Success => (SolidColorBrush)new BrushConverter().ConvertFromString("#4CAF50")!,
                NotificationType.Warning => (SolidColorBrush)new BrushConverter().ConvertFromString("#FFB347")!,
                NotificationType.Error   => (SolidColorBrush)new BrushConverter().ConvertFromString("#FF6B6B")!,
                _                        => (SolidColorBrush)new BrushConverter().ConvertFromString("#FF69B4")!,
            };

            var border = new Border
            {
                Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#252542")!,
                BorderBrush = accent,
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 10, 10),
                Margin = new Thickness(0, 0, 0, 10),
                MaxWidth = 360,
                Opacity = 0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 16,
                    ShadowDepth = 2,
                    Opacity = 0.4,
                },
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            var buttonStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Top,
            };

            // Optional action button. Firing the action also clears the
            // sticky from this session, but doesn't add the key to
            // DismissedNotificationKeys — the action handler is expected
            // to resolve the underlying condition (e.g. recalibration
            // populates DeviceName, so the toast simply won't re-trigger
            // next session).
            if (!string.IsNullOrWhiteSpace(actionLabel) && action != null)
            {
                var actionBtn = new Button
                {
                    Content = actionLabel,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = accent,
                    BorderThickness = new Thickness(0),
                    Foreground = Brushes.White,
                    Cursor = Cursors.Hand,
                };
                actionBtn.Click += (_, _) =>
                {
                    if (border.Tag is DispatcherTimer at) { try { at.Stop(); } catch { } }
                    try { action(); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "NotificationService: action button handler failed"); }
                    if (stickyKey != null) _stickyByKey.Remove(stickyKey);
                    FadeOutAndRemove(border);
                };
                buttonStack.Children.Add(actionBtn);
            }

            var dismissBtn = new Button
            {
                Content = "×",
                FontSize = 16,
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#B0B0C0")!,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
            };
            dismissBtn.Click += (_, _) =>
            {
                if (border.Tag is DispatcherTimer dt) { try { dt.Stop(); } catch { } }
                if (stickyKey != null)
                {
                    // Persistent dismissal: remember the key so we don't re-show next session.
                    var s = App.Settings?.Current;
                    if (s != null && !s.DismissedNotificationKeys.Contains(stickyKey))
                    {
                        s.DismissedNotificationKeys.Add(stickyKey);
                        App.Settings?.Save();
                    }
                    _stickyByKey.Remove(stickyKey);
                }
                FadeOutAndRemove(border);
            };
            buttonStack.Children.Add(dismissBtn);

            Grid.SetColumn(buttonStack, 1);
            grid.Children.Add(buttonStack);

            border.Child = grid;
            return border;
        }

        private static void AnimateIn(UIElement target)
        {
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            target.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void FadeOutAndRemove(Border border)
        {
            var anim = new DoubleAnimation
            {
                From = border.Opacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            anim.Completed += (_, _) =>
            {
                if (_host?.Children.Contains(border) == true)
                    _host.Children.Remove(border);
            };
            border.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }
}
