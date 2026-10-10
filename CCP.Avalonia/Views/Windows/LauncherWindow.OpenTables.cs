using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.GoonGame;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// OPEN TABLES on the launcher (WPF Windows/Launcher/LauncherWindow.OpenTables.cs): an "N open" badge
    /// on the Goon Game tile, from the same <see cref="GoonOpenTables"/> the friends drawer reads. Asked
    /// when the launcher shows and every <see cref="OpenTablesPoll"/> while it stays visible, never while
    /// hidden. Hidden at zero and on any error (an error reads as zero).
    /// WPF's StaggerIn on the badge's first appearance is StaggerInOpenTablesBadge below.
    /// </summary>
    public partial class LauncherWindow
    {
        internal static readonly TimeSpan OpenTablesPoll = TimeSpan.FromSeconds(60);

        private DispatcherTimer? _openTablesTimer;
        private bool _openTablesHooked;
        private Border? _openTablesBadge;
        private TextBlock? _openTablesText;

        /// <summary>The badge for a tile, or null for every tile but a playable Goon Game.</summary>
        private Control? OpenTablesBadgeFor(string id, bool show)
        {
            if (!show || !string.Equals(id, "goon", StringComparison.OrdinalIgnoreCase)) return null;
            _openTablesText = new TextBlock
            {
                FontSize = 12, FontWeight = FontWeight.Bold, FontFamily = new FontFamily("Fredoka, Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x06, 0x18)),
            };
            _openTablesBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0xA2)),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(10, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false, Child = _openTablesText, Tag = "launcher-open-tables",
            };
            PaintOpenTables(GoonOpenTables.Latest);
            return _openTablesBadge;
        }

        /// <summary>Called once from the constructor: poll while visible, stop when hidden or closed.</summary>
        private void HookOpenTables()
        {
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                if (IsVisible) StartOpenTables(); else StopOpenTables();
            };
            Closed += (_, _) => StopOpenTables();
        }

        private void StartOpenTables()
        {
            try
            {
                if (!_openTablesHooked) { GoonOpenTables.Changed += OnOpenTablesChanged; _openTablesHooked = true; }
                _ = GoonOpenTables.RefreshAsync();
                if (_openTablesTimer == null)
                {
                    _openTablesTimer = new DispatcherTimer { Interval = OpenTablesPoll };
                    _openTablesTimer.Tick += (_, _) => _ = GoonOpenTables.RefreshAsync();
                }
                _openTablesTimer.Start();
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] open tables start failed"); }
        }

        private void StopOpenTables()
        {
            _openTablesTimer?.Stop();
            if (_openTablesHooked) { GoonOpenTables.Changed -= OnOpenTablesChanged; _openTablesHooked = false; }
        }

        private void OnOpenTablesChanged(OpenTablesReply reply)
        {
            try { Dispatcher.UIThread.Post(() => PaintOpenTables(reply)); }
            catch { }
        }

        internal void PaintOpenTables(OpenTablesReply reply)
        {
            if (_openTablesBadge == null || _openTablesText == null) return;
            int n = GoonOpenTables.OpenCount(reply);
            bool was = _openTablesBadge.IsVisible;
            _openTablesBadge.IsVisible = n > 0;
            if (n > 0) _openTablesText.Text = Loc.GetF("launcher_goon_open", n);
            if (n > 0 && !was) StaggerInOpenTablesBadge();
            else if (n <= 0) { _openTablesIn?.Stop(); _openTablesIn = null; }
        }

        private DispatcherTimer? _openTablesIn;
        private readonly TranslateTransform _openTablesRise = new();

        /// <summary>Tests: the badge is on its way in.</summary>
        internal bool OpenTablesBadgeEntering => _openTablesIn?.IsEnabled == true;

        /// <summary>WPF MotionFx.StaggerIn on the badge's first appearance: it fades in over 220 ms
        /// from a 10 px rise that settles in 260 ms, both quadratic ease out; with transitions off,
        /// or while the launcher is not on screen, it just shows.</summary>
        private void StaggerInOpenTablesBadge()
        {
            var badge = _openTablesBadge;
            if (badge == null) return;
            _openTablesIn?.Stop();
            _openTablesIn = null;
            if (badge.RenderTransform == null) badge.RenderTransform = _openTablesRise;
            var rise = badge.RenderTransform as TranslateTransform;
            if (!global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowTransitions || !IsVisible)
            {
                badge.Opacity = 1;
                if (rise != null) rise.Y = 0;
                return;
            }
            badge.Opacity = 0;
            if (rise != null) rise.Y = global::ConditioningControlPanel.Motion.MotionTimings.StaggerRisePx;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            DispatcherTimer? timer = null;
            timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
            {
                double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                double f = Math.Clamp(ms / global::ConditioningControlPanel.Motion.MotionTimings.StaggerFadeMs, 0, 1);
                double r = Math.Clamp(ms / global::ConditioningControlPanel.Motion.MotionTimings.StaggerRiseMs, 0, 1);
                badge.Opacity = Math.Clamp(1 - (1 - f) * (1 - f), 0, 1);
                if (rise != null) rise.Y = global::ConditioningControlPanel.Motion.MotionTimings.StaggerRisePx * (1 - r) * (1 - r);
                if (r < 1) return;
                timer!.Stop();
                if (ReferenceEquals(_openTablesIn, timer)) _openTablesIn = null;
            });
            _openTablesIn = timer;
            timer.Start();
        }
    }
}
