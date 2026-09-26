using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Features;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The split tile's active ring must hug the card edge (owner screenshot, 2026-09-26): a ring centred
/// 2px in with a 3px stroke left a half-pixel sliver of dark card body outside the pink line, and its
/// square polygon corner was cut away by the rounded content clip, baring the body at the corner.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class SplitFeatureCardRingTests
{
    private const int Size = 200;
    // RootBorder margin 6 + border 1: ContentRoot starts here.
    private const int Edge = 7;

    private static Color[,] RenderActive()
    {
        var card = new SplitFeatureCard { IsActiveA = true, IsActiveB = true };
        card.Resources["ElevatedSurfaceBrush"] = new SolidColorBrush(Color.FromRgb(16, 16, 24));
        card.Resources["GlassBorderBrush"] = new SolidColorBrush(Color.FromRgb(16, 16, 24));
        card.Resources["FxGlowBrush"] = new SolidColorBrush(Color.FromRgb(255, 0, 255));
        card.Resources["FxGlowColor"] = Color.FromRgb(255, 0, 255);
        card.Resources["HelpButtonStyle"] = new Style(typeof(Button));
        var host = new Grid { Width = Size, Height = Size, Background = Brushes.Black };
        host.Children.Add(card);
        host.Measure(new Size(Size, Size));
        host.Arrange(new Rect(0, 0, Size, Size));
        host.UpdateLayout();

        var rtb = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host);
        var raw = new byte[Size * Size * 4];
        rtb.CopyPixels(raw, Size * 4, 0);
        var c = new Color[Size, Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int i = (y * Size + x) * 4;
                c[x, y] = Color.FromArgb(raw[i + 3], raw[i + 2], raw[i + 1], raw[i]);
            }
        return c;
    }

    private static bool IsPink(Color c) => c.R > 100 && c.B > 100 && c.G < 60;

    [Fact]
    public void TheRingStartsOnTheFirstContentPixel_AlongEveryEdge()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var c = RenderActive();
            int far = Size - 1 - Edge;
            // Half A owns the top and left edges near the top-left, half B the bottom and right.
            Assert.True(IsPink(c[Edge, 50]), $"left edge (A) is {c[Edge, 50]}");
            Assert.True(IsPink(c[50, Edge]), $"top edge (A) is {c[50, Edge]}");
            Assert.True(IsPink(c[far, 150]), $"right edge (B) is {c[far, 150]}");
            Assert.True(IsPink(c[150, far]), $"bottom edge (B) is {c[150, far]}");
        });
    }

    [Fact]
    public void TheRingFollowsTheRoundedCorner()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var c = RenderActive();
            // The clip's 11px arc meets the 45-degree diagonal about 3.2px in from each edge; the
            // first pixels inside it must be pink, not the dark body.
            Assert.True(IsPink(c[Edge + 4, Edge + 4]), $"top-left corner is {c[Edge + 4, Edge + 4]}");
            int far = Size - 1 - Edge - 4;
            Assert.True(IsPink(c[far, far]), $"bottom-right corner is {c[far, far]}");
        });
    }
}
