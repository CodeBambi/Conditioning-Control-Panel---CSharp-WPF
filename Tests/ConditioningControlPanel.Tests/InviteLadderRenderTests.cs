using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Invites;
using ConditioningControlPanel.Services.Invites;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The "Your rewards" row draws picture tiles, one per rung, each carrying its badge art.
/// Set CCP_INVITE_LADDER_PNG to a file path to keep the rendered row for a look.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class InviteLadderRenderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(10)]
    public void EveryRungIsAPictureTile(int converted)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var next = InviteRewards.Next(converted);
            var row = new UniformGrid { Columns = InviteRewards.Ladder.Count, Width = 600 };
            foreach (var rung in InviteRewards.Ladder)
                row.Children.Add(InvitePanel.RungTile(rung, converted, ReferenceEquals(rung, next)));
            var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)), Padding = new Thickness(16), Child = row };

            host.Measure(new Size(632, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            host.UpdateLayout();

            foreach (Border tile in row.Children)
            {
                var badge = FindImage(tile);
                Assert.NotNull(badge);
                Assert.NotNull(badge!.Source);
            }

            var w = (int)Math.Ceiling(host.ActualWidth);
            var h = (int)Math.Ceiling(host.ActualHeight);
            Assert.True(h > 120, $"row too short: {h}");
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host);

            var outPath = Environment.GetEnvironmentVariable("CCP_INVITE_LADDER_PNG");
            if (!string.IsNullOrEmpty(outPath))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(outPath.Replace(".png", $"_{converted}.png"));
                enc.Save(fs);
            }
        });
    }

    private static Image? FindImage(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Image img) return img;
            var deeper = FindImage(child);
            if (deeper != null) return deeper;
        }
        return null;
    }
}
