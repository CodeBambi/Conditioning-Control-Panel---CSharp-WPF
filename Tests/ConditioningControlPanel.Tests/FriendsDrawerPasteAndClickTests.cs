using System.Windows.Controls;
using System.Windows.Documents;
using ConditioningControlPanel.Controls.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1368: a pasted Hypnotube link gives its video number, not "" (the box used to cut a
/// paste to 8 characters) and not the digits scattered through the slug.
/// ccp-bugs #1369: a click on a word in a friend's card lands on a Run, and walking up from it
/// must not throw.
/// </summary>
public class FriendsDrawerPasteAndClickTests
{
    [Theory]
    [InlineData("https://hypnotube.com/video/ultimate-sissy-mindfuck-106170.html", "106170")]
    [InlineData("https://hypnotube.com/video/part-2-of-3-91559.html", "91559")]
    [InlineData("hypnotube.com/video/up-and-down-95541.HTML?t=30", "95541")]
    [InlineData("  106170 ", "106170")]
    [InlineData("12a34", "1234")]
    [InlineData("123456789", "12345678")]
    [InlineData("https://hypnotube.com/", "")]
    [InlineData("", "")]
    public void A_pasted_link_or_a_typed_number_gives_the_video_number(string typed, string expected)
    {
        Assert.Equal(expected, FriendsDrawerRules.NormaliseHtId(typed));
    }

    [Fact]
    public void Walking_up_from_a_word_in_a_card_does_not_throw()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var run = new Run("Sam");
            var text = new TextBlock(run);
            var card = new Border { Child = text, Tag = "friends-card" };

            Assert.Same(text, FriendsDrawer.ParentOf(run));
            Assert.Same(card, FriendsDrawer.ParentOf(text));
        });
    }
}
