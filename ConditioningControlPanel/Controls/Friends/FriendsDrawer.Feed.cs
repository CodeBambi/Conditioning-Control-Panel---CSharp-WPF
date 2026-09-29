namespace ConditioningControlPanel.Controls.Friends;

/// <summary>The drawer's feed seam. RenderList calls <see cref="AddFeed"/> right after the leash
/// section; the feed lane implements it in this file (what happened with friends, newest first,
/// unread marked, read when shown).</summary>
public sealed partial class FriendsDrawer
{
    partial void AddFeed();
}
