using System;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// The arithmetic behind the launcher's games column: how many columns the tiles take at a given
/// width, how many rows that makes, how tall each tile can be, and whether the column has to fall
/// back to scrolling because the tiles would shrink past what still reads as a card.
///
/// <para>Pure on purpose. The window feeds it the column's ActualWidth and the viewport's
/// ActualHeight on SizeChanged and applies the answers; nothing here touches WPF, so the tester's
/// "cards do not fit" is a row in a test and not a desk run.</para>
/// </summary>
public static class LauncherGridLayout
{
    /// <summary>A games column at least this wide takes three columns of tiles. The default
    /// 1280 px window leaves the column 756 px, and it must land on three: six tiles in two rows
    /// of three fit its height, three rows of two never did.</summary>
    public const double ThreeColumnWidth = 740;

    /// <summary>The art plate never drops under this: below it the picture is a stripe.</summary>
    public const double MinArtHeight = 96;

    /// <summary>
    /// The smallest tile that still reads as a card: the art floor (96) plus the text block with
    /// a two-line blurb (8 + 25 + 4 + 38 + 8 + 40 + 12 = 135) plus the tile's margin and rim (21).
    /// Under this the column scrolls instead of squeezing.
    /// </summary>
    public const double MinTileHeight = 252;

    /// <summary>Two columns by default, three once the column is wide enough, never more than
    /// there are tiles and never fewer than one.</summary>
    public static int Columns(double width, int count)
    {
        if (count <= 1) return 1;
        int want = width >= ThreeColumnWidth ? 3 : 2;
        return Math.Min(want, count);
    }

    /// <summary>Rows needed to seat <paramref name="count"/> tiles in <paramref name="columns"/>.</summary>
    public static int Rows(int count, int columns)
    {
        if (count <= 0 || columns <= 0) return 0;
        return (count + columns - 1) / columns;
    }

    /// <summary>The height each row gets when the whole column is shared out evenly.</summary>
    public static double TileHeight(double available, int rows)
    {
        if (rows <= 0 || available <= 0) return 0;
        return available / rows;
    }

    /// <summary>
    /// The art plate's share of a tile: whatever is left above the text block, floored at
    /// <see cref="MinArtHeight"/>.
    /// </summary>
    public static double ArtHeight(double tileHeight, double textHeight)
        => Math.Max(MinArtHeight, tileHeight - textHeight);

    /// <summary>True when sharing the column out evenly would make a tile shorter than
    /// <see cref="MinTileHeight"/>: keep the tiles at the floor and let the column scroll.</summary>
    public static bool NeedsScroll(double available, int rows)
        => rows > 0 && TileHeight(available, rows) < MinTileHeight;

    /// <summary>
    /// The height to give the grid inside its scroller: the viewport when every tile fits,
    /// otherwise rows at the floor so the scroller has something to scroll.
    /// </summary>
    public static double GridHeight(double available, int rows)
    {
        if (rows <= 0) return 0;
        return NeedsScroll(available, rows) ? rows * MinTileHeight : available;
    }
}
