using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>The Avalonia <see cref="IBubbleCountHost"/>: the game and answer windows, and WPF
    /// BubbleCountService.ShowFullscreenMessage (:597) for the strict retry / mercy card.</summary>
    internal sealed class BubbleCountHost : IBubbleCountHost
    {
        public static BubbleCountHost Instance { get; } = new();
        public BubbleCountScheduler Scheduler { get; internal set; }   // tests swap in a fixed library

        private readonly List<Window> _messages = new();

        private BubbleCountHost() => Scheduler = new BubbleCountScheduler(this);

        internal IReadOnlyList<Window> Messages => _messages;
        public double LastVideoDurationSeconds => BubbleCountWindow.LastVideoDurationSeconds;

        // Core's timers may tick off the UI thread (unseeded CoreDispatch), so every door hops onto it.
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) => Dispatcher.UIThread.Invoke(() =>
            BubbleCountWindow.ShowOnAllMonitors(path, (BubbleCountScheduler.Difficulty)difficulty, strict, onComplete));

        /// <summary>WPF: magenta 64 pt bold text on black, maximised on every screen, then <paramref name="then"/>.</summary>
        public void ShowMessage(string text, int ms, Action then) => Dispatcher.UIThread.Invoke(() =>
        {
            var host = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var screens = host is null ? Array.Empty<Screen>() : Features.ScreenList.Enumerate(host);
            var batch = new List<Window>();
            foreach (var screen in screens.Count > 0 ? screens : new Screen?[] { null })
            {
                var w = new Window
                {
                    WindowDecorations = WindowDecorations.None,
                    Background = Brushes.Black,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Content = new TextBlock
                    {
                        Text = text,
                        Foreground = Brushes.Magenta,
                        FontSize = 64,
                        FontWeight = FontWeight.Bold,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                if (screen != null)
                {
                    w.WindowStartupLocation = WindowStartupLocation.Manual;
                    w.Position = new PixelPoint(screen.Bounds.X + 100, screen.Bounds.Y + 100);
                }
                w.Show();
                w.WindowState = WindowState.Maximized;
                batch.Add(w);
            }
            _messages.AddRange(batch);
            DispatcherTimer.RunOnce(() =>
            {
                if (!_messages.Remove(batch[0])) return;   // panic closed it: no retry
                foreach (var w in batch) { _messages.Remove(w); w.Close(); }
                then();
            }, TimeSpan.FromMilliseconds(ms));
        });

        public void CloseAll() => Dispatcher.UIThread.Invoke(() =>
        {
            foreach (var w in _messages.ToArray()) try { w.Close(); } catch { }
            _messages.Clear();
            BubbleCountWindow.ForceCloseAll();
            BubbleCountResultWindow.ForceCloseAll();
        });
    }
}
