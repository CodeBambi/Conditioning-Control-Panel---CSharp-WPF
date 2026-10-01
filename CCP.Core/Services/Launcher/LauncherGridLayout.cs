using System;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// The arithmetic behind the launcher's games column: how many columns the tiles take at a given
/// width, how tall a card is at that width, how tall the grid is, and whether the column has to
/// scroll because the cards no longer fit.
///
/// <para>A card is sized by its WIDTH, not by the room it is given. The art plate is a 16:9
/// landscape - the shape every piece of tile art is drawn at - so the picture is shown whole
/// instead of being cropped to whatever rectangle was left above the text; the text block under
/// it is a fixed height, so a one-line blurb and a two-line blurb put their titles on the same
/// line. Everything below follows from those two facts.</para>
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

    /// <summary>The art plate's shape. Every tile picture is drawn 16:9 (1376x768, 1024x572), so
    /// a plate of any other shape is a crop.</summary>
    public const double ArtAspect = 16.0 / 9.0;

    /// <summary>The art plate never drops under this: below it the picture is a stripe.</summary>
    public const double MinArtHeight = 96;

    // The text block, top to bottom. Fixed rather than measured so every card's title sits on
    // the same line whatever its blurb wraps to.
    public const double TextPadTop = 8;
    public const double TitleHeight = 26;
    public const double BlurbGap = 4;
    public const double BlurbHeight = 38;   // two lines at 14 px
    public const double PlayGap = 8;
    public const double PlayHeight = 40;
    public const double TextPadBottom = 12;

    /// <summary>The whole text block under the art, margins included.</summary>
    public const double TextHeight =
        TextPadTop + TitleHeight + BlurbGap + BlurbHeight + PlayGap + PlayHeight + TextPadBottom;

    /// <summary>The gap a tile keeps on each side, and the rim it draws inside it.</summary>
    public const double TileMargin = 9;
    public const double TileBorder = 1.5;

    /// <summary>The shortest a card can be: the art floor plus the text block.</summary>
    public const double MinTileHeight = MinArtHeight + TextHeight;

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

    /// <summary>The art plate's width inside one cell: the cell less the tile's margins and rim.</summary>
    public static double TileWidth(double columnWidth, int columns)
    {
        if (columnWidth <= 0 || columns <= 0) return 0;
        return Math.Max(0, columnWidth / columns - 2 * TileMargin - 2 * TileBorder);
    }

    /// <summary>The art plate at that width: 16:9, floored so a narrow column still shows a picture.</summary>
    public static double ArtHeight(double tileWidth)
        => tileWidth <= 0 ? MinArtHeight : Math.Max(MinArtHeight, tileWidth / ArtAspect);

    /// <summary>A whole card at that width: the landscape plate plus the fixed text block.</summary>
    public static double TileHeight(double tileWidth) => ArtHeight(tileWidth) + TextHeight;

    /// <summary>The grid's height: every row is exactly one card tall, margins included.</summary>
    public static double GridHeight(double columnWidth, int columns, int rows)
    {
        if (rows <= 0) return 0;
        return rows * (TileHeight(TileWidth(columnWidth, columns)) + 2 * TileMargin);
    }

    /// <summary>True when the cards are taller than the viewport and the column has to scroll.
    /// When they are not, the block is centred in the column instead of hanging from the top.</summary>
    public static bool NeedsScroll(double available, double gridHeight)
        => available > 0 && gridHeight > available + 0.5;
}
