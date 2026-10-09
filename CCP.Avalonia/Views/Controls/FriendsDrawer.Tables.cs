// PORTED from ConditioningControlPanel/Controls/Friends/FriendsDrawer.Tables.cs (7.1.5): OPEN TABLES in
// the drawer. A friend with a listed Goon table floats to the top with a pink "hosting a table" row and
// a Join button. Every signed-in account joins (only HOSTING is a patron perk); a signed-out drawer
// shows a lock whose press only says to sign in, and never launches the game. The list comes from
// GoonOpenTables (Core), asked on open and every TablesPoll while the drawer is open, never folded.
// ponytail: GoonHostService.Launch(joinCode) is WPF-only; the port opens the Goon window (its own
// lobby) and logs the code. Swap JoinTable when the Goon host learns a join code.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    internal static readonly TimeSpan TablesPoll = TimeSpan.FromSeconds(30);
    private DispatcherTimer? _tablesTimer;
    private bool _tablesHooked;

    /// <summary>Test seam: the open tables the drawer draws from.</summary>
    internal Func<OpenTablesReply> Tables { get; set; } = () => GoonOpenTables.Latest;
    /// <summary>Test seam: "may this account sit down" (signed in). Fails closed.</summary>
    internal Func<bool> CanJoinTables { get; set; } = () => { try { return CoreAccount.IsLoggedIn; } catch { return false; } };
    /// <summary>Test seam: what a Join press does once the bar is cleared.</summary>
    internal Action<string> JoinTable { get; set; } = code =>
    {
        Serilog.Log.Information("[Friends] join table {Code}: the Goon window opens on its own lobby (no join code on this head yet)", code);
        Games.GameWindow.Launch("goon");
    };
    /// <summary>Test seam: ask the server for tables (off in the suite).</summary>
    internal Action RefreshTables { get; set; } = () => _ = GoonOpenTables.RefreshAsync();

    private void StartTables()
    {
        if (!_tablesHooked) { GoonOpenTables.Changed += OnTablesChanged; _tablesHooked = true; }
        try { RefreshTables(); } catch { }
        _tablesTimer ??= new DispatcherTimer(TablesPoll, DispatcherPriority.Normal, OnTablesTick);
        _tablesTimer.Start();
    }

    private void StopTables()
    {
        _tablesTimer?.Stop();
        if (_tablesHooked) { GoonOpenTables.Changed -= OnTablesChanged; _tablesHooked = false; }
    }

    private void OnTablesTick(object? sender, EventArgs e) { try { RefreshTables(); } catch { } }

    private void OnTablesChanged(OpenTablesReply _) =>
        Dispatcher.UIThread.Post(() => { try { Render(); } catch (Exception ex) { Serilog.Log.Debug("[Friends] tables repaint: {E}", ex.Message); } });

    /// <summary>The table this friend is hosting, or null.</summary>
    internal OpenTable? TableFor(Friend f)
    {
        try { return GoonOpenTables.ForFriend(Tables(), f.Id, f.Name); }
        catch { return null; }
    }

    /// <summary>The pink wash a hosting row wears (the mockup's <c>.fr.hosting</c>).</summary>
    private static void DressHostingRow(Border row)
    {
        row.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.3, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.7, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0x33, 0xFF, 0x5F, 0xA2), 0), new GradientStop(Color.FromArgb(0x0A, 0xFF, 0x5F, 0xA2), 1) },
        };
        row.BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0x5F, 0xA2));
        row.BorderThickness = new Thickness(1);
    }

    /// <summary>"hosting a table" in pink, bold: replaces the activity line on a hosting row.</summary>
    private static TextBlock HostingLine() => Tagged(Label(Loc.Get("friends_table_hosting"), 11.5, Pink, Display, FontWeight.Bold), "friends-table-hosting");

    /// <summary>Join (signed in) or a lock (signed out). Both run through the same click: the bar is
    /// asked at press time, so a sign-in that lands mid-session works without a repaint.</summary>
    private Button TableJoinButton(Friend f, OpenTable table)
    {
        bool can = CanJoinTables();
        var b = can
            ? Pill(Loc.Get("friends_table_join"), Pink, new SolidColorBrush(Color.FromRgb(0x2A, 0x06, 0x18)), "friends-table-join", Pink)
            : Pill("\U0001F512", Raised, Text, "friends-table-locked", Line2);
        b.Padding = can ? new Thickness(12, 4, 12, 4) : new Thickness(10, 4, 10, 4);
        b.FontSize = 13;
        if (b.Content is TextBlock tb) tb.FontSize = 13;
        b.Margin = new Thickness(6, 0, 0, 0);
        b.Cursor = Hand();
        ToolTip.SetTip(b, Loc.Get(can ? "friends_table_join_tip" : "friends_table_locked_tip"));
        var code = table.Code;
        b.Click += (_, e) => { e.Handled = true; PressJoin(f, code); };
        return b;
    }

    internal void PressJoin(Friend f, string code)
    {
        try
        {
            if (!CanJoinTables())
            {
                // Joining needs an account and nothing else; the lock's tooltip says so.
                Serilog.Log.Information("[Friends] table join pressed while signed out");
                return;
            }
            Serilog.Log.Information("[Friends] joining {Name}'s open table", f.Name);
            JoinTable(code);
            CloseRequested?.Invoke();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[Friends] table join failed"); }
    }
}
