using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Sparkle wallet's croupier EMI is two bitmap layers (body + waving arm) referenced by
/// short-form pack URIs in SparkleWallet.xaml. A sprite missing from the csproj's Resource list
/// does not fail the build: it throws inside InitializeComponent the first time the dashboard
/// loads. This suite realizes the control on the shared STA thread and reads the pixels back,
/// so that failure lands here instead.
/// </summary>
public class SparkleWalletRenderTests
{
    [Fact]
    public void Wallet_realizes_and_paints_the_croupier_emi()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            var wallet = new SparkleWallet();
            var host = new Grid { Background = Brushes.Transparent };
            host.Children.Add(wallet);
            host.Measure(new Size(196, 48));
            host.Arrange(new Rect(0, 0, 196, 48));
            host.UpdateLayout();

            var body = wallet.FindName("WaveArm") as Image;
            Assert.NotNull(body);
            Assert.NotNull(body!.Source);
            Assert.IsType<RotateTransform>(body.RenderTransform);

            var rtb = new RenderTargetBitmap(196, 48, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host);
            var px = new byte[196 * 48 * 4];
            rtb.CopyPixels(px, 196 * 4, 0);

            // The mascot column is the first 50px. Count opaque pixels whose hue is gold-ish
            // (red and green well above blue), which only the sprite's casing can supply.
            int gold = 0;
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 50; x++)
                {
                    int i = (y * 196 + x) * 4;
                    byte b = px[i], g = px[i + 1], r = px[i + 2], a = px[i + 3];
                    if (a > 200 && r > 120 && g > 90 && r > b + 40 && g > b + 20) gold++;
                }
            Assert.True(gold > 150, $"expected the gold croupier casing in the mascot column, found {gold} gold pixels");
        });
    }
}
