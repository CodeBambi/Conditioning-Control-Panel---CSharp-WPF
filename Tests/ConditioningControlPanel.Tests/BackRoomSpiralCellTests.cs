using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.BackRoom.Overlays;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM's fullscreen Loom spiral, one field per screen (CONTRACT 10.14 item 2: "centred on that monitor").
/// A weave whose shape differs from its monitor's is cropped from the CENTRE of each cell, never from its top-left
/// corner. The bug this guards: an Image with its own Width/Height at UniformToFill renders the scaled-up picture
/// pinned to the cell's top-left, so a square Loom weave on a 16:9 screen, or a 16:9 weave on a 16:10, 21:9 or
/// portrait screen, put the spiral's eye off centre (or off screen).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class BackRoomSpiralCellTests
{
    private static readonly Color Red = Color.FromRgb(255, 0, 0), Blue = Color.FromRgb(0, 0, 255);

    /// <summary>A 100x100 picture split in two halves: top/bottom (<paramref name="horizontalSplit"/>) or left/right.</summary>
    private static BitmapSource Halves(bool horizontalSplit)
    {
        const int n = 100;
        var px = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool first = horizontalSplit ? y < n / 2 : x < n / 2;
                var c = first ? Red : Blue;
                int i = (y * n + x) * 4;
                px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255;
            }
        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, px, n * 4);
        bmp.Freeze();
        return bmp;
    }

    private static Color[,] Render(PxRect cellRect, BitmapSource source)
    {
        var (cell, img) = BackRoomLoomSpiralOverlay.BuildCell(cellRect);
        img.Source = source;
        cell.Measure(new Size(cellRect.W, cellRect.H));
        cell.Arrange(new Rect(0, 0, cellRect.W, cellRect.H));
        cell.UpdateLayout();
        int w = (int)cellRect.W, h = (int)cellRect.H;
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(cell);
        var raw = new byte[w * h * 4];
        rtb.CopyPixels(raw, w * 4, 0);
        var colors = new Color[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                colors[x, y] = Color.FromArgb(raw[i + 3], raw[i + 2], raw[i + 1], raw[i]);
            }
        return colors;
    }

    private static bool IsRed(Color c) => c.R > 200 && c.B < 60 && c.A > 200;
    private static bool IsBlue(Color c) => c.B > 200 && c.R < 60 && c.A > 200;

    [Fact]
    public void AWideCell_CropsASquareWeave_FromItsMiddle()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            // 200x100 cell, 100x100 weave: scaled to 200x200, the middle 100 rows kept (source rows 25..75).
            var c = Render(new PxRect(0, 0, 200, 100), Halves(horizontalSplit: true));
            foreach (int x in new[] { 5, 100, 195 })
            {
                Assert.True(IsRed(c[x, 5]) && IsRed(c[x, 45]), $"column {x}: the top half of the cell shows the weave's top half");
                Assert.True(IsBlue(c[x, 55]) && IsBlue(c[x, 95]), $"column {x}: the bottom half of the cell shows the weave's bottom half");
            }
        });
    }

    [Fact]
    public void ATallCell_CropsASquareWeave_FromItsMiddle()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            // 100x200 (a portrait monitor), 100x100 weave split left/right: the middle 100 columns kept.
            var c = Render(new PxRect(0, 0, 100, 200), Halves(horizontalSplit: false));
            foreach (int y in new[] { 5, 100, 195 })
            {
                Assert.True(IsRed(c[5, y]) && IsRed(c[45, y]), $"row {y}: the left half of the cell shows the weave's left half");
                Assert.True(IsBlue(c[55, y]) && IsBlue(c[95, y]), $"row {y}: the right half of the cell shows the weave's right half");
            }
        });
    }

    [Fact]
    public void TheCell_IsClippedAndPlacedAtItsMonitor()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var (cell, img) = BackRoomLoomSpiralOverlay.BuildCell(new PxRect(-1920, 40, 1920, 1200));
            Assert.True(cell.ClipToBounds);
            Assert.Equal((1920.0, 1200.0), (cell.Width, cell.Height));
            Assert.Equal((-1920.0, 40.0), (System.Windows.Controls.Canvas.GetLeft(cell), System.Windows.Controls.Canvas.GetTop(cell)));
            Assert.True(double.IsNaN(img.Width) && double.IsNaN(img.Height), "the image carries no size of its own");
            Assert.Equal((HorizontalAlignment.Center, VerticalAlignment.Center), (img.HorizontalAlignment, img.VerticalAlignment));
        });
    }
}
