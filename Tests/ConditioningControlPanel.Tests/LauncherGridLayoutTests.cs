using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The games column arithmetic. A card is sized by its WIDTH now - a 16:9 art plate plus a fixed
/// text block - so these rows are the sizes the launcher can be dragged to and the card that
/// comes out of each one.
/// </summary>
public class LauncherGridLayoutTests
{
    // The games column at the launcher's default (1280 wide) and minimum (1000 wide) sizes:
    // window width minus the 28 px side margins, the 440 px panel card and the 28 px gutter.
    private const double ColumnAtDefault = 1280 - 56 - 440 - 28;   // 756
    private const double ColumnAtMin = 1000 - 56 - 440 - 28;       // 476

    [Theory]
    [InlineData(ColumnAtMin, 6, 2)]
    [InlineData(ColumnAtDefault, 6, 3)]
    [InlineData(739, 6, 2)]
    [InlineData(740, 6, 3)]
    [InlineData(1396, 6, 3)]
    public void Columns_switch_to_three_at_740(double width, int count, int expected)
    {
        Assert.Equal(expected, LauncherGridLayout.Columns(width, count));
    }

    [Theory]
    [InlineData(1200, 1, 1)]
    [InlineData(1200, 2, 2)]
    [InlineData(400, 1, 1)]
    [InlineData(400, 0, 1)]
    public void Columns_never_exceed_the_tile_count_and_never_drop_under_one(double width, int count, int expected)
    {
        Assert.Equal(expected, LauncherGridLayout.Columns(width, count));
    }

    [Theory]
    [InlineData(6, 2, 3)]
    [InlineData(6, 3, 2)]
    [InlineData(5, 2, 3)]
    [InlineData(5, 3, 2)]
    [InlineData(7, 3, 3)]
    [InlineData(1, 2, 1)]
    [InlineData(0, 2, 0)]
    public void Rows_round_up(int count, int columns, int expected)
    {
        Assert.Equal(expected, LauncherGridLayout.Rows(count, columns));
    }

    [Fact]
    public void The_text_block_is_the_sum_of_its_own_rows()
    {
        // The one number LauncherWindow.Tiles.cs builds the card's lower half against.
        Assert.Equal(136, LauncherGridLayout.TextHeight);
        Assert.Equal(LauncherGridLayout.MinArtHeight + LauncherGridLayout.TextHeight,
                     LauncherGridLayout.MinTileHeight);
    }

    [Fact]
    public void A_cell_pays_for_its_margins_and_its_rim_before_the_art_gets_a_width()
    {
        // 756 / 3 = 252 a cell, less 9 px of margin and 1.5 px of rim on each side.
        Assert.Equal(231, LauncherGridLayout.TileWidth(ColumnAtDefault, 3));
        Assert.Equal(217, LauncherGridLayout.TileWidth(ColumnAtMin, 2));
        Assert.Equal(0, LauncherGridLayout.TileWidth(0, 3));
        Assert.Equal(0, LauncherGridLayout.TileWidth(756, 0));
    }

    [Fact]
    public void The_art_plate_is_a_landscape_and_never_a_stripe()
    {
        Assert.Equal(180, LauncherGridLayout.ArtHeight(320));
        // 16:9 of 160 is 90, under the floor, so the floor wins.
        Assert.Equal(LauncherGridLayout.MinArtHeight, LauncherGridLayout.ArtHeight(160));
        Assert.Equal(LauncherGridLayout.MinArtHeight, LauncherGridLayout.ArtHeight(0));
        // Every piece of tile art is drawn 16:9, so the plate is too and nothing is cropped.
        Assert.Equal(1376d / 768d, LauncherGridLayout.ArtAspect, 1);
    }

    [Fact]
    public void A_card_is_its_landscape_plate_plus_the_text_block()
    {
        Assert.Equal(180 + LauncherGridLayout.TextHeight, LauncherGridLayout.TileHeight(320));
        Assert.Equal(LauncherGridLayout.MinTileHeight, LauncherGridLayout.TileHeight(0));
    }

    [Fact]
    public void Six_cards_fit_the_default_window_and_sit_in_the_middle_of_the_column()
    {
        // 1280 x 800: the scroller sees about 658 px. Three columns, two rows, each row a
        // 231 x 130 plate plus the 136 text block plus 18 of margin = 284, so 568 in 658.
        int columns = LauncherGridLayout.Columns(ColumnAtDefault, 6);
        int rows = LauncherGridLayout.Rows(6, columns);
        Assert.Equal(3, columns);
        Assert.Equal(2, rows);
        double height = LauncherGridLayout.GridHeight(ColumnAtDefault, columns, rows);
        Assert.Equal(567.875, height, 3);
        Assert.False(LauncherGridLayout.NeedsScroll(658, height));
    }

    [Fact]
    public void Six_cards_at_the_minimum_window_overflow_and_the_column_scrolls()
    {
        // 1000 x 680: two columns, three rows of a 217 x 122 plate. 828 will not fit 538.
        int columns = LauncherGridLayout.Columns(ColumnAtMin, 6);
        int rows = LauncherGridLayout.Rows(6, columns);
        Assert.Equal(2, columns);
        Assert.Equal(3, rows);
        double height = LauncherGridLayout.GridHeight(ColumnAtMin, columns, rows);
        Assert.Equal(828.1875, height, 3);
        Assert.True(LauncherGridLayout.NeedsScroll(538, height));
    }

    [Fact]
    public void A_block_that_is_exactly_the_viewport_does_not_scroll()
    {
        Assert.False(LauncherGridLayout.NeedsScroll(600, 600));
        Assert.True(LauncherGridLayout.NeedsScroll(600, 601));
        // Nothing measured yet is not a reason to scroll.
        Assert.False(LauncherGridLayout.NeedsScroll(0, 900));
    }

    [Fact]
    public void Nothing_to_lay_out_gives_zero_not_a_division_by_zero()
    {
        Assert.Equal(0, LauncherGridLayout.GridHeight(756, 3, 0));
        Assert.Equal(0, LauncherGridLayout.GridHeight(0, 0, 0));
    }
}
