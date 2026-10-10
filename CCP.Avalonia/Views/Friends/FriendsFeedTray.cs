// PORTED from ConditioningControlPanel/Services/Friends/FriendsFeedTray.cs (7.1.5): the tray tooltip's
// "N new" line. The tooltip keeps the app's own line and gains "N new from friends" under it while the
// feed has unread lines (FriendsFeedRules.TrayText, Core). WPF refreshed on every feed change and on the
// pointer moving over the icon; Avalonia's TrayIcon has no pointer event, so the landing's 3 s tick and
// the feed's Changed event refresh it instead (a feed built after the tray, or an account switch, still
// reads right within a tick).
using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Friends.Feed;

namespace ConditioningControlPanel.Avalonia.Views.Friends;

internal static class FriendsFeedTray
{
    /// <summary>The feed the tooltip counts. The app's own; swappable for the suite.</summary>
    internal static Func<FriendsFeed?> Source { get; set; } = () => FriendsFeedHost.Feed;

    private static FriendsFeed? _hooked;
    private static TrayIcon? _icon;
    private static string _baseText = "";

    /// <summary>Called from the landing tick with the shell's tray icon (null without a tray).</summary>
    internal static void Refresh(TrayIcon? icon)
    {
        if (icon == null) return;
        try
        {
            if (!ReferenceEquals(icon, _icon)) { _icon = icon; _baseText = icon.ToolTipText ?? ""; }
            var feed = Source();
            if (!ReferenceEquals(feed, _hooked))
            {
                if (_hooked != null) _hooked.Changed -= OnFeedChanged;
                if (feed != null) feed.Changed += OnFeedChanged;
                _hooked = feed;
            }
            var text = Text(_baseText, feed?.Unread ?? 0);
            if (icon.ToolTipText != text) icon.ToolTipText = text;
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] tray tooltip failed: {E}", ex.Message); }
    }

    /// <summary>The tooltip for <paramref name="unread"/> new feed lines over the app's own line.</summary>
    internal static string Text(string baseText, int unread) =>
        FriendsFeedRules.TrayText(baseText, unread, unread > 0 ? Loc.GetF("friends_feed_tray", unread) : "");

    private static void OnFeedChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnFeedChanged); return; }
        Refresh(_icon);
    }
}
