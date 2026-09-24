using System.Collections.Generic;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Open tables in the drawer: the hosting friend floats up with a pink row, Join for
/// anyone signed in, a lock when signed out, and the lock never launches the game.</summary>
public partial class FriendsDrawerTests
{
    private static OpenTablesReply KitHosting() => GoonOpenTablesApi.Parse(
        """{"ok":true,"tables":[{"code":"ROBN42","name":"Robin","friend":true,"friendId":"robin","cardSec":60}]}""");

    [Fact]
    public void A_hosting_friend_floats_to_the_top_with_join()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            var d = NewDrawer(new FakeFriends(Sample()));
            var joined = new List<string>();
            d.Tables = KitHosting;
            d.CanJoinTables = () => true;
            d.JoinTable = code => joined.Add(code);
            d.Render();

            // Robin is offline in the sample, but the listing says they are at a table.
            Assert.Equal(new[] { "robin", "kit", "sam", "noor", "in:dee", "out:ash" }, d.RowIds);
            var row = d.RowFor("robin")!;
            Assert.NotNull(Find(row, "friends-table-hosting"));
            Assert.NotNull(Find(row, "friends-table-join"));
            Assert.Null(Find(row, "friends-activity"));
            Assert.Null(Find(d.RowFor("kit")!, "friends-table-join"));

            d.PressJoin(Sample().Friends[3], "ROBN42");
            Assert.Equal(new[] { "ROBN42" }, joined);
            Layout(d);
        });
    }

    [Fact]
    public void A_signed_out_drawer_sees_a_lock_that_never_launches()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var d = NewDrawer(new FakeFriends(Sample()));
            var joined = new List<string>();
            d.Tables = KitHosting;
            d.CanJoinTables = () => false;
            d.JoinTable = code => joined.Add(code);
            d.Render();

            var row = d.RowFor("robin")!;
            Assert.NotNull(Find(row, "friends-table-locked"));
            Assert.Null(Find(row, "friends-table-join"));
            Assert.Empty(joined);
        });
    }

    [Fact]
    public void No_tables_means_no_pink_rows()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var d = NewDrawer(new FakeFriends(Sample()));
            d.Tables = () => OpenTablesReply.Empty;
            d.Render();
            Assert.Equal(new[] { "kit", "sam", "robin", "noor", "in:dee", "out:ash" }, d.RowIds);
            foreach (var id in d.RowIds) Assert.Null(Find(d.RowFor(id)!, "friends-table-hosting"));
        });
    }
}
