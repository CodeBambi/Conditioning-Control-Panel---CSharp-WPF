using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Social &gt; Friends (nav rework 2026-10-06): the page hosts the SAME drawer class as the
/// rail chip's popup, in page mode. Lives beside FriendsDrawerTests to share its fake service.
/// </summary>
public partial class FriendsDrawerTests
{
    private static FriendsTabView NewPage(FakeFriends svc)
    {
        PresenceAsk.Asked = () => true;
        PresenceAsk.MarkAsked = () => { };
        Outside.Clear();
        FriendsDrawer.Outside = (text, _) => Outside.Add(text);
        ResetDrawerExtras();
        var page = new FriendsTabView(svc);
        page.Drawer.MeName = () => "cb";
        page.Drawer.MeTier = () => 0;
        page.Drawer.Render();
        return page;
    }

    private static FriendsSnapshot NoFriends() => new(
        Array.Empty<Friend>(), Array.Empty<FriendRequest>(), Array.Empty<FriendRequest>(), "CCP-7K2Q9", FriendPresence.None);

    [Fact]
    public void Friends_page_hosts_the_drawer_in_page_mode()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = NewPage(new FakeFriends(Sample()));
            Assert.True(page.Drawer.AsPage);
            Assert.True(double.IsNaN(page.Drawer.Width));
            Assert.Null(page.Drawer.Effect);
            Assert.Equal(FriendsDrawer.PageMaxWidth, page.Drawer.MaxWidth);
            // The same rows the popup draws.
            Assert.NotNull(page.Drawer.RowFor("sam"));
            Assert.NotNull(page.Drawer.RowFor("in:dee"));
            SocialShots.Shot(page, "friends", 900, 620);
        });
    }

    [Fact]
    public void Friends_page_never_claims_escape_so_the_panic_key_gets_it()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = NewPage(new FakeFriends(Sample()));
            Assert.False(EscapeClaim.InASurface(page.Drawer));
            // The popup drawer still claims it (it has something to fold).
            var popup = NewDrawer(new FakeFriends(Sample()));
            Assert.True(EscapeClaim.InASurface(popup));
        });
    }

    [Fact]
    public void Friends_page_leaves_the_leash_to_the_leash_page()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = NewPage(new FakeFriends(Sample()));
            Assert.Null(Find(page.Drawer, "leash-section"));
            var popup = NewDrawer(new FakeFriends(Sample()));
            Assert.NotNull(Find(popup, "leash-section"));
        });
    }

    [Fact]
    public void Friends_page_empty_state_is_one_line_and_an_add_button()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = NewPage(new FakeFriends(NoFriends()));
            Assert.NotNull(Find(page.Drawer, "friends-page-empty"));
            var add = Find(page.Drawer, "friends-page-empty-add") as Button;
            Assert.NotNull(add);
            Assert.False(page.Drawer.AddBoxOpen);
            add!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(page.Drawer.AddBoxOpen);
            SocialShots.Shot(page, "friends-empty", 900, 520);

            // The popup keeps its own one-line empty state.
            var popup = NewDrawer(new FakeFriends(NoFriends()));
            Assert.Null(Find(popup, "friends-page-empty"));
            Assert.NotNull(Find(popup, "friends-empty"));
        });
    }
}
