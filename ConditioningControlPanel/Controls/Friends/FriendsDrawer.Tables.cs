using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// OPEN TABLES in the drawer (2026-09-23). A friend with a listed Goon Game table floats to the
/// top with a pink "hosting a table" row and a Join button. Every signed-in account joins
/// (owner call 2026-09-24, only HOSTING is a patron perk); a signed-out drawer shows a lock whose
/// press only says to sign in, and never launches the game.
///
/// <para>The list comes from <see cref="GoonOpenTables"/>, asked on open and every
/// <see cref="TablesPoll"/> while the drawer is open, never while it is folded. A server without
/// the route answers nothing and the drawer simply has no pink rows.</para>
/// </summary>
public sealed partial class FriendsDrawer
{
    internal static readonly TimeSpan TablesPoll = TimeSpan.FromSeconds(30);

    private DispatcherTimer? _tablesTimer;
    private bool _tablesHooked;

    /// <summary>Test seam: the open tables the drawer draws from.</summary>
    internal Func<OpenTablesReply> Tables { get; set; } = () => GoonOpenTables.Latest;

    /// <summary>Test seam: "may this account sit down" (signed in). Fails closed.</summary>
    internal Func<bool> CanJoinTables { get; set; } = () =>
    {
        try { return GoonHostService.JoiningAllowed(); } catch { return false; }
    };

    /// <summary>Test seam: what a Join press does once the bar is cleared.</summary>
    internal Action<string> JoinTable { get; set; } = code =>
        GoonHostService.Launch(duckMainWindow: true, joinCode: code);

    private void StartTables()
    {
        if (!_tablesHooked) { GoonOpenTables.Changed += OnTablesChanged; _tablesHooked = true; }
        _ = GoonOpenTables.RefreshAsync();
        _tablesTimer ??= new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TablesPoll };
        _tablesTimer.Tick -= OnTablesTick;
        _tablesTimer.Tick += OnTablesTick;
        _tablesTimer.Start();
    }

    private void StopTables()
    {
        _tablesTimer?.Stop();
        if (_tablesHooked) { GoonOpenTables.Changed -= OnTablesChanged; _tablesHooked = false; }
    }

    private void OnTablesTick(object? sender, EventArgs e) => _ = GoonOpenTables.RefreshAsync();

    private void OnTablesChanged(OpenTablesReply _)
    {
        try { Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Render)); }
        catch (Exception ex) { App.Logger?.Debug("[Friends] tables repaint: {E}", ex.Message); }
    }

    /// <summary>The table this friend is hosting, or null.</summary>
    internal OpenTable? TableFor(Friend f)
    {
        try { return GoonOpenTables.ForFriend(Tables(), f.Id, f.Name); }
        catch { return null; }
    }

    /// <summary>The pink wash a hosting row wears (the mockup's <c>.fr.hosting</c>).</summary>
    private static void DressHostingRow(Border row)
    {
        var wash = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.3),
            EndPoint = new Point(1, 0.7),
        };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x33, 0xFF, 0x5F, 0xA2), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x0A, 0xFF, 0x5F, 0xA2), 1));
        wash.Freeze();
        row.Background = wash;
        row.BorderBrush = FriendsLook.Frozen(Color.FromArgb(0x59, 0xFF, 0x5F, 0xA2));
        row.BorderThickness = new Thickness(1);
    }

    /// <summary>"hosting a table" in pink, bold: replaces the activity line on a hosting row.</summary>
    private static TextBlock HostingLine()
    {
        var t = FriendsLook.Label(Loc.Get("friends_table_hosting"), 11.5, FriendsLook.PinkBrush, FriendsLook.Display, FontWeights.Bold);
        t.Tag = "friends-table-hosting";
        return t;
    }

    /// <summary>Join (signed in) or a lock (signed out). Both run through the same click: the bar
    /// is asked at press time, so a sign-in that lands mid-session works without a repaint.</summary>
    private Button TableJoinButton(Friend f, OpenTable table)
    {
        bool can = CanJoinTables();
        object content = can
            ? Loc.Get("friends_table_join")
            : "\U0001F512";
        var b = can
            ? FriendsLook.Pill(content, FriendsLook.PinkBrush, FriendsLook.Frozen(Color.FromRgb(0x2A, 0x06, 0x18)),
                FriendsLook.PinkBrush, 8, new Thickness(12, 4, 12, 4), FriendsLook.PinkBrush)
            : FriendsLook.Pill(content, FriendsLook.RaisedBrush, FriendsLook.TextBrush,
                FriendsLook.Line2Brush, 8, new Thickness(10, 4, 10, 4));
        b.FontSize = 13;
        b.VerticalAlignment = VerticalAlignment.Center;
        b.Margin = new Thickness(6, 0, 0, 0);
        b.Tag = can ? "friends-table-join" : "friends-table-locked";
        b.ToolTip = can ? Loc.Get("friends_table_join_tip") : Loc.Get("friends_table_locked_tip");
        var code = table.Code;
        b.Click += (_, e) =>
        {
            e.Handled = true;
            PressJoin(f, code);
        };
        return b;
    }

    internal void PressJoin(Friend f, string code)
    {
        try
        {
            if (!CanJoinTables())
            {
                // Joining needs an account and nothing else; the lock's tooltip says so.
                App.Logger?.Information("[Friends] table join pressed while signed out");
                return;
            }
            App.Logger?.Information("[Friends] joining {Name}'s open table", f.Name);
            JoinTable(code);
            CloseRequested?.Invoke();
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "[Friends] table join failed"); }
    }
}
