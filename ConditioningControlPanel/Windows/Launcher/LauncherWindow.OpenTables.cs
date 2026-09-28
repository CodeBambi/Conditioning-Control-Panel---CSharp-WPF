using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// OPEN TABLES on the launcher (2026-09-23): an "N open" badge on the Goon Game tile, from the
/// same <see cref="GoonOpenTables"/> the friends drawer reads. Asked when the launcher shows and
/// every <see cref="OpenTablesPoll"/> while it stays visible, never while hidden. Hidden at zero
/// and on any error (an error reads as zero).
/// </summary>
public partial class LauncherWindow
{
    internal static readonly TimeSpan OpenTablesPoll = TimeSpan.FromSeconds(60);

    private const string GoonTileId = "goon";

    private DispatcherTimer? _openTablesTimer;
    private bool _openTablesHooked;
    private Border? _openTablesBadge;
    private TextBlock? _openTablesText;

    /// <summary>The badge for a tile, or null for every tile but a playable Goon Game.</summary>
    private FrameworkElement? OpenTablesBadgeFor(LauncherEntry entry, bool show)
    {
        if (!show || entry.Id != GoonTileId) return null;
        _openTablesText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("/Fonts/#Fredoka, Segoe UI"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x06, 0x18)),
        };
        _openTablesBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0xA2)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 2, 9, 3),
            Margin = new Thickness(10, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = _openTablesText,
            Tag = "launcher-open-tables",
        };
        PaintOpenTables(GoonOpenTables.Latest);
        return _openTablesBadge;
    }

    private void StartOpenTables()
    {
        try
        {
            if (!_openTablesHooked) { GoonOpenTables.Changed += OnOpenTablesChanged; _openTablesHooked = true; }
            _ = GoonOpenTables.RefreshAsync();
            _openTablesTimer ??= new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = OpenTablesPoll };
            _openTablesTimer.Tick -= OnOpenTablesTick;
            _openTablesTimer.Tick += OnOpenTablesTick;
            _openTablesTimer.Start();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] open tables start failed"); }
    }

    private void StopOpenTables()
    {
        _openTablesTimer?.Stop();
        if (_openTablesHooked) { GoonOpenTables.Changed -= OnOpenTablesChanged; _openTablesHooked = false; }
    }

    private void OnOpenTablesTick(object? sender, EventArgs e) => _ = GoonOpenTables.RefreshAsync();

    private void OnOpenTablesChanged(OpenTablesReply reply)
    {
        try { Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => PaintOpenTables(reply))); }
        catch { }
    }

    private void PaintOpenTables(OpenTablesReply reply)
    {
        if (_openTablesBadge == null || _openTablesText == null) return;
        int n = GoonOpenTables.OpenCount(reply);
        bool was = _openTablesBadge.Visibility == Visibility.Visible;
        _openTablesBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (n <= 0) return;
        _openTablesText.Text = Loc.GetF("launcher_goon_open", n);
        if (!was) MotionFx.StaggerIn(new FrameworkElement[] { _openTablesBadge });
    }
}
