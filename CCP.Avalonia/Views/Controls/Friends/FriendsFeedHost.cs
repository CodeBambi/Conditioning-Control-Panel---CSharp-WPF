// PORTED from ConditioningControlPanel/App.xaml.cs (App.FriendsFeed): the head's one friends feed,
// listening to the friends service from the moment it is built, so a poke that lands while the
// drawer is folded is still a line (and a badge on the rail chip) when it opens.
using System;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Friends;

internal static class FriendsFeedHost
{
    private static FriendsFeed? _feed;

    /// <summary>The app's feed, or null before the friends service exists.</summary>
    internal static FriendsFeed? Feed => _feed;

    /// <summary>Raised when the feed is created or swapped, so a chip built earlier can listen.</summary>
    internal static event Action? FeedChanged;

    /// <summary>App startup, once the friends service is built (WPF FriendsFeed.CreateForApp).</summary>
    internal static void Attach(IFriendsService? service)
    {
        try
        {
            if (_feed == null) _feed = FriendsFeed.CreateForApp(service, () => Platform.FriendsHead.Identity()?.UnifiedId);
            else _feed.Attach(service);
            FeedChanged?.Invoke();
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] feed not built: {E}", ex.Message); }
    }
}
