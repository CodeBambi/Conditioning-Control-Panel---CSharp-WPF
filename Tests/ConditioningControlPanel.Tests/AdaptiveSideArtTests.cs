using ConditioningControlPanel.Features;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1321: Awareness and Listening flickered between the wide and the compact layout in a
/// loop. The switch changes the page's height, which shows or hides the ScrollViewer's scrollbar,
/// which moves the Grid's width back across a single threshold. The decision now holds the collapse
/// until the page is a clear margin wider than the threshold.
/// </summary>
public class AdaptiveSideArtTests
{
    private const double T = 1000;

    [Theory]
    [InlineData(999, false, true)]    // wide page dips under the threshold: collapse
    [InlineData(1000, false, false)]  // at the threshold: stay wide
    [InlineData(1200, true, false)]   // well past it: the art comes back
    [InlineData(1010, true, true)]    // collapsed, and only a scrollbar wider: hold
    [InlineData(999, true, true)]     // collapsed and still narrow: hold
    public void CollapsesBelowAndWidensOnlyPastTheMargin(double width, bool collapsedNow, bool expected)
        => Assert.Equal(expected, AdaptiveSideArt.ShouldCollapse(width, T, collapsedNow));

    [Fact]
    public void TheMarginIsWiderThanAScrollbar()
        => Assert.True(AdaptiveSideArt.WidenMargin > 17 * 2);

    [Theory]
    [InlineData(17)]
    [InlineData(12)]
    [InlineData(8)]
    public void AScrollbarAppearingOrGoingCannotFlipItBack(double scrollbar)
    {
        // Start anywhere around the threshold, in either state, and let the scrollbar follow the
        // layout: compact is short (no scrollbar), wide is tall (scrollbar). The state must settle
        // after at most one change and never cycle.
        for (double viewport = T - 60; viewport <= T + 80; viewport += 1)
        {
            foreach (var start in new[] { false, true })
            {
                bool collapsed = start;
                int changes = 0;
                for (int pass = 0; pass < 10; pass++)
                {
                    double width = collapsed ? viewport : viewport - scrollbar;
                    bool next = AdaptiveSideArt.ShouldCollapse(width, T, collapsed);
                    if (next != collapsed) changes++;
                    collapsed = next;
                }
                Assert.True(changes <= 1, $"viewport {viewport}, scrollbar {scrollbar}: {changes} changes");
            }
        }
    }
}
