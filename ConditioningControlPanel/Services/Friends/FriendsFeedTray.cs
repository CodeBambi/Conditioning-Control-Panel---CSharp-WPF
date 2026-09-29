using System;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>
/// The tray tooltip's "N new" line. <see cref="TrayIconService"/> hands its icon over once; from
/// then on the tooltip keeps the app's own line and gains "N new from friends" under it while the
/// feed has unread lines. Refreshed on every feed change and again whenever the pointer moves over
/// the icon (which is always before Windows shows the tooltip), so a feed built after the tray, or
/// an account switch, still reads right.
/// </summary>
public static class FriendsFeedTray
{
    /// <summary>The feed the tooltip counts. The app's own; swappable for the suite.</summary>
    internal static Func<FriendsFeed?> Source { get; set; } = () => App.FriendsFeed;

    public static void Attach(System.Windows.Forms.NotifyIcon? icon)
    {
        if (icon == null) return;
        try
        {
            var baseText = icon.Text ?? "";
            FriendsFeed? hooked = null;
            void Refresh()
            {
                try
                {
                    var feed = Source();
                    if (!ReferenceEquals(feed, hooked))
                    {
                        if (hooked != null) hooked.Changed -= Refresh;
                        if (feed != null) feed.Changed += Refresh;
                        hooked = feed;
                    }
                    var n = feed?.Unread ?? 0;
                    var text = FriendsFeedRules.TrayText(baseText, n, n > 0 ? Loc.GetF("friends_feed_tray", n) : "");
                    if (icon.Text != text) icon.Text = text;
                }
                catch (Exception ex) { App.Logger?.Debug("[Friends] tray tooltip failed: {E}", ex.Message); }
            }
            icon.MouseMove += (_, _) => Refresh();
            Refresh();
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] tray attach failed: {E}", ex.Message); }
    }
}
