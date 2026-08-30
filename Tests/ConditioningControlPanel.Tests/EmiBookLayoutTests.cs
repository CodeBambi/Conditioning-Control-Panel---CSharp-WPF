using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Where the book goes, at desk sizes nobody on this project owns.
///
/// <para>These exist because of a bug that could not be seen by reading the code and could not be
/// seen on the machine that wrote it. The book placed itself at her right, flipped left only if the
/// right overflowed, and then clamped into the work area - so when NEITHER side had room the clamp
/// dragged it back over her body. It needs a work area under about <c>bodyWidth + 750</c> to happen:
/// invisible at 1920, routine on a 1280 laptop with her scaled up. The geometry now lives in
/// <see cref="EmiBookLayout"/> precisely so a test can stand where those users stand.</para>
/// </summary>
public class EmiBookLayoutTests
{
    // A body box, as the desk window reports it: left and right edges in DIPs.
    private const double Gap = EmiBookLayout.BodyGap;
    private const double Full = EmiBookLayout.FullWidth;
    private const double Narrow = EmiBookLayout.NarrowWidth;

    // ---------------------------------------------------------------- the roomy desk

    /// <summary>With room on both sides she keeps her preference: the book opens on her right.</summary>
    [Fact]
    public void On_a_wide_desk_the_book_opens_on_her_right_at_full_width()
    {
        var p = EmiBookLayout.Place(0, 1920, 100, 300);

        Assert.False(p.OnHerLeft);
        Assert.False(p.Narrow);
        Assert.False(p.CoversHer);
        Assert.Equal(Full, p.Width);
        Assert.Equal(300 + Gap, p.Left);
    }

    /// <summary>Pinned to the right edge, the book flips rather than shrinking: her left is roomy.</summary>
    [Fact]
    public void Against_the_right_edge_it_flips_to_her_left_and_stays_full_width()
    {
        var p = EmiBookLayout.Place(0, 1920, 1700, 1900);

        Assert.True(p.OnHerLeft);
        Assert.False(p.Narrow);
        Assert.False(p.CoversHer);
        Assert.Equal(1700 - Gap - Full, p.Left);
    }

    /// <summary>A book on the second monitor is placed in THAT work area, not in the desktop.</summary>
    [Fact]
    public void A_second_monitor_work_area_is_respected_on_both_edges()
    {
        var p = EmiBookLayout.Place(1920, 1920, 3500, 3700);

        Assert.True(p.OnHerLeft);
        Assert.True(p.Left >= 1920);
        Assert.True(p.Left + p.Width <= 3840);
    }

    // ---------------------------------------------------------------- the narrow desk

    /// <summary>
    /// THE FIX. Nine hundred DIP of desk has no room for the full book on either side, and the old
    /// code answered that by putting the full book on top of her. It goes narrow and stays beside
    /// her instead: a smaller book that can be read beats a big one parked over the thing it is
    /// explaining.
    /// </summary>
    [Fact]
    public void When_the_full_book_fits_on_neither_side_it_goes_narrow_beside_her()
    {
        var p = EmiBookLayout.Place(0, 900, 350, 550);

        Assert.True(p.Narrow);
        Assert.Equal(Narrow, p.Width);
        Assert.False(p.CoversHer);
        Assert.Equal(550 + Gap, p.Left);
        Assert.True(p.Left + p.Width <= 900);
    }

    /// <summary>
    /// The narrow book flips too - the side rule does not stop applying at the small size.
    ///
    /// <para>The window here is tighter than it looks: her left has to hold the NARROW book and not
    /// the full one, or the book simply stays full width and flips, which is a different test. At
    /// 340 on an 800 desk her left has 328, which is over the narrow 270 and under the full 364.</para>
    /// </summary>
    [Fact]
    public void The_narrow_book_still_flips_to_the_roomier_side()
    {
        var p = EmiBookLayout.Place(0, 800, 340, 540);

        Assert.True(p.Narrow);
        Assert.True(p.OnHerLeft);
        Assert.False(p.CoversHer);
        Assert.Equal(340 - Gap - Narrow, p.Left);
    }

    // ---------------------------------------------------------------- the desk with no room at all

    /// <summary>
    /// Below the narrow book's own needs there is no honest answer, and the book overlaps her. Two
    /// things still have to hold: it is fully ON the screen, and it SAYS it covered her rather than
    /// leaving that to be discovered.
    /// </summary>
    [Fact]
    public void With_no_room_at_all_it_overlaps_her_but_stays_on_the_screen_and_admits_it()
    {
        var p = EmiBookLayout.Place(0, 600, 200, 400);

        Assert.True(p.CoversHer);
        Assert.Equal(Narrow, p.Width);
        Assert.True(p.Left >= 0);
        Assert.True(p.Left + p.Width <= 600);
    }

    /// <summary>Her dragged half off the left edge gives a negative room reading; it must not throw
    /// the side choice or put the book off screen.</summary>
    [Fact]
    public void A_body_hanging_off_the_left_edge_still_places_on_screen()
    {
        var p = EmiBookLayout.Place(0, 1280, -80, 120);

        Assert.False(p.OnHerLeft);
        Assert.True(p.Left >= 0);
        Assert.True(p.Left + p.Width <= 1280);
    }

    // ---------------------------------------------------------------- the sweep

    /// <summary>
    /// THE REGRESSION, swept rather than sampled: walk her across a desk a DIP at a time and the
    /// book must never sit on her body unless it has declared that it had to. This is the assertion
    /// the old code failed, and only at the far end of the walk on the narrower desks.
    /// </summary>
    [Theory]
    [InlineData(1920.0, 200.0)]
    [InlineData(1600.0, 200.0)]
    [InlineData(1366.0, 240.0)]
    [InlineData(1280.0, 300.0)]
    [InlineData(1024.0, 260.0)]
    public void The_book_never_lands_on_her_body_without_saying_so(double workW, double bodyW)
    {
        for (double bodyL = 0; bodyL + bodyW <= workW; bodyL += 1)
        {
            double bodyR = bodyL + bodyW;
            var p = EmiBookLayout.Place(0, workW, bodyL, bodyR);

            Assert.True(p.Left >= 0, $"book off the left edge at bodyL {bodyL}");
            Assert.True(p.Left + p.Width <= workW, $"book off the right edge at bodyL {bodyL}");

            bool overlaps = p.Left < bodyR && p.Left + p.Width > bodyL;
            if (overlaps)
                Assert.True(p.CoversHer, $"book covered her at bodyL {bodyL} without declaring it");
        }
    }

    /// <summary>The width is one of two. A continuous one would give every desk a different text
    /// column, and the cards are written to a measured one.</summary>
    [Theory]
    [InlineData(1920.0)]
    [InlineData(1366.0)]
    [InlineData(1024.0)]
    [InlineData(800.0)]
    [InlineData(480.0)]
    public void The_book_only_ever_takes_one_of_its_two_widths(double workW)
    {
        for (double bodyL = 0; bodyL + 200 <= workW; bodyL += 7)
        {
            var p = EmiBookLayout.Place(0, workW, bodyL, bodyL + 200);
            Assert.True(p.Width == Full || p.Width == Narrow, $"odd width {p.Width}");
            Assert.Equal(p.Width == Narrow, p.Narrow);
        }
    }

    /// <summary>When the book did fit beside her, it sits exactly one gap off her edge - not
    /// approximately, and not wherever a clamp left it.</summary>
    [Theory]
    [InlineData(1920.0)]
    [InlineData(1366.0)]
    [InlineData(1024.0)]
    public void A_book_that_fits_sits_exactly_one_gap_off_her_edge(double workW)
    {
        for (double bodyL = 0; bodyL + 200 <= workW; bodyL += 7)
        {
            double bodyR = bodyL + 200;
            var p = EmiBookLayout.Place(0, workW, bodyL, bodyR);
            if (p.CoversHer) continue;

            double near = p.OnHerLeft ? bodyL - (p.Left + p.Width) : p.Left - bodyR;
            Assert.Equal(Gap, near, 6);
        }
    }
}
