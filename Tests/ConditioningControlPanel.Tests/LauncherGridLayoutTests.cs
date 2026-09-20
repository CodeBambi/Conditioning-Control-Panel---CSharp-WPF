using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The games column arithmetic behind "cards do not fit". Six tiles at the window's MinHeight
/// used to leave two rows on screen and one below the fold; every row here is a size the
/// launcher can be dragged to and the answer the grid has to give.
/// </summary>
public class LauncherGridLayoutTests
{
    // The games column at the launcher's default (1280 wide) and minimum (1000 wide) sizes:
    // window width minus the 28 px side margins, the 440 px panel card and the 28 px gutter.
    private const double ColumnAtDefault = 1280 - 56 - 440 - 28;
    private const double ColumnAtMin = 1000 - 56 - 440 - 28;

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
    public void Six_tiles_fit_the_default_window_without_scrolling()
    {
        // 1280 x 800: the scroller sees about 658 px (title row 48, footer 52, body margins 22,
        // eyebrow 20). Two rows of three share it at 329 each, over the card floor, and the grid
        // is exactly the viewport. The old fixed 156 px plate needed three rows of ~264 here.
        int columns = LauncherGridLayout.Columns(ColumnAtDefault, 6);
        int rows = LauncherGridLayout.Rows(6, columns);
        Assert.Equal(2, rows);
        Assert.Equal(329, LauncherGridLayout.TileHeight(658, rows));
        Assert.False(LauncherGridLayout.NeedsScroll(658, rows));
        Assert.Equal(658, LauncherGridLayout.GridHeight(658, rows));
    }

    [Fact]
    public void Six_tiles_at_the_minimum_window_fall_back_to_scrolling_at_the_floor()
    {
        // 1000 x 680: two columns, three rows in about 538 px is 179 a row, under the floor.
        // The grid grows to three rows at the floor and the scroller scrolls the rest.
        int columns = LauncherGridLayout.Columns(ColumnAtMin, 6);
        int rows = LauncherGridLayout.Rows(6, columns);
        Assert.Equal(3, rows);
        Assert.True(LauncherGridLayout.NeedsScroll(538, rows));
        Assert.Equal(3 * LauncherGridLayout.MinTileHeight, LauncherGridLayout.GridHeight(538, rows));
    }

    [Fact]
    public void Art_takes_the_room_above_the_text_and_never_less_than_the_floor()
    {
        Assert.Equal(200, LauncherGridLayout.ArtHeight(350, 150));
        Assert.Equal(LauncherGridLayout.MinArtHeight, LauncherGridLayout.ArtHeight(200, 150));
        Assert.Equal(LauncherGridLayout.MinArtHeight, LauncherGridLayout.ArtHeight(0, 150));
    }

    [Fact]
    public void Scrolling_is_the_fallback_when_a_row_would_drop_under_the_card_floor()
    {
        // Three rows in 540 px is 180 a row: under the 252 floor, so the grid grows to three
        // rows at the floor and the scroller has the rest to scroll.
        Assert.True(LauncherGridLayout.NeedsScroll(540, 3));
        Assert.Equal(3 * LauncherGridLayout.MinTileHeight, LauncherGridLayout.GridHeight(540, 3));
        // Two rows in 504 px is exactly the floor: no scroll, grid is the viewport.
        Assert.False(LauncherGridLayout.NeedsScroll(504, 2));
        Assert.Equal(504, LauncherGridLayout.GridHeight(504, 2));
    }

    [Fact]
    public void Nothing_to_lay_out_gives_zero_not_a_division_by_zero()
    {
        Assert.Equal(0, LauncherGridLayout.TileHeight(540, 0));
        Assert.Equal(0, LauncherGridLayout.GridHeight(540, 0));
        Assert.False(LauncherGridLayout.NeedsScroll(540, 0));
        Assert.Equal(0, LauncherGridLayout.TileHeight(0, 3));
    }
}
