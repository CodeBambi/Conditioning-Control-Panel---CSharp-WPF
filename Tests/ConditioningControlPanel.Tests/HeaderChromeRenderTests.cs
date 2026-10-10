using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 6 (owner, 2026-10-06): the Sparkle Points pill reads 20% smaller than the
/// 0.75-scaled 48 px wallet it was (36 px tall in the header), with its "?" ON the pill; and the
/// account tag ("CodeBambi") left the mod chip to sit immediately left of the profile bubble.
/// This realizes the REAL header bar out of MainWindow.xaml (loose parse with the window's
/// resources, handler attributes stripped, the recipe of <see cref="HeaderLevelChipRenderTests"/>)
/// at the 1469 px column 1 gets on a 1585 px window and measures both.
///
/// <para>Set CCP_NAV_PNG_DIR to a folder to also save header-chrome.png and header-wallet.png.</para>
/// </summary>
public class HeaderChromeRenderTests
{
    private const double BandWidth = 1469;

    /// <summary>The wallet's header height before wave 6: 48 px at a 0.75 LayoutTransform.</summary>
    private const double OldWalletHeight = 48 * 0.75;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }

    private static string Xaml() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml"));

    private static readonly string[] HandlerAttributes =
    {
        "Click", "MouseEnter", "MouseLeave", "MouseLeftButtonDown", "MouseLeftButtonUp", "MouseRightButtonUp",
        "MouseDown", "MouseUp", "PreviewMouseLeftButtonDown", "PreviewMouseLeftButtonUp", "PreviewMouseDown",
        "Loaded", "Unloaded", "Checked", "Unchecked", "SizeChanged", "ToolTipOpening", "ContextMenuOpening",
        "KeyDown", "PreviewKeyDown", "ValueChanged", "SelectionChanged", "TextChanged", "GotFocus", "LostFocus",
        "IsVisibleChanged", "MouseWheel", "PreviewMouseWheel", "DropDownOpened", "DropDownClosed", "PreviewMouseRightButtonUp",
        "MouseRightButtonDown", "PreviewMouseRightButtonDown", "RequestNavigate",
    };

    private static Border BuildHeader()
    {
        var xaml = Xaml().Replace("\r\n", "\n");
        var head = Regex.Match(xaml, @"<Window\b(.*?)>", RegexOptions.Singleline).Groups[1].Value;
        var ns = string.Join(" ", Regex.Matches(head, @"xmlns(:\w+)?=""[^""]*""").Select(m => m.Value));
        ns = Regex.Replace(ns, @"clr-namespace:(ConditioningControlPanel[\w.]*)""", "clr-namespace:$1;assembly=ConditioningControlPanel\"");
        var resources = Regex.Match(xaml, @"<Window\.Resources>(.*?)</Window\.Resources>", RegexOptions.Singleline).Groups[1].Value;

        const string marker = "<Border Grid.Row=\"1\" Grid.Column=\"1\" Background=\"Transparent\" Padding=\"15,8\">";
        var start = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start > 0, "lost the header bar");
        var stop = xaml.IndexOf("\n        </Border>", start, StringComparison.Ordinal);
        Assert.True(stop > start, "lost the end of the header bar");
        var header = xaml.Substring(start, stop + "\n        </Border>".Length - start);
        header = "<Border x:Name=\"HeaderBarBlock\" Background=\"#120B1A\" Padding=\"15,8\">" + header.Substring(marker.Length);

        var loose = "<Grid " + ns + "><Grid.Resources>" + resources + "</Grid.Resources>" + header + "</Grid>";
        foreach (var attr in HandlerAttributes)
            loose = Regex.Replace(loose, @"\s" + attr + @"=""[A-Za-z_][\w]*""", string.Empty);
        loose = Regex.Replace(loose, @"\sx:FieldModifier=""\w+""", string.Empty);
        var root = (Grid)XamlReader.Parse(loose);
        return (Border)root.FindName("HeaderBarBlock");
    }

    private static Rect BoundsIn(FrameworkElement e, Visual root) =>
        e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    [Fact]
    public void TheAccountChipSitsBesideTheBubbleAndTheWalletShrankWithItsHelpOnThePill()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        var dir = Environment.GetEnvironmentVariable("CCP_NAV_PNG_DIR") is { Length: > 0 } d ? d : null;
        WpfRenderHarness.OnStaThread(() =>
        {
            var header = BuildHeader();
            var root = (Grid)header.Parent;
            ((TextBlock)header.FindName("TxtPlayerTitle")).Text = "Basic Subject";
            ((TextBlock)header.FindName("TxtAccountChipName")).Text = "CodeBambi";
            ((TextBlock)header.FindName("ProfileBubbleInitials")).Text = "CB";
            var wallet = (ConditioningControlPanel.Controls.SparkleWallet)header.FindName("HeaderSparkleWallet");
            wallet.SetBalance(1234);
            wallet.RefreshText(); // Loaded never fires offscreen; the label comes from here.

            root.Measure(new Size(BandWidth, double.PositiveInfinity));
            root.Arrange(new Rect(0, 0, BandWidth, root.DesiredSize.Height));
            root.UpdateLayout();

            // 1. The chip is the bubble's left-hand neighbour in the right-hand chrome, 6 px apart.
            var chip = (FrameworkElement)header.FindName("BtnAccountChip");
            var bubble = (FrameworkElement)header.FindName("BtnProfileBubble");
            var panel = Assert.IsType<StackPanel>(chip.Parent);
            Assert.Same(panel, bubble.Parent);
            var kids = panel.Children.Cast<UIElement>().ToList();
            Assert.Equal(kids.IndexOf(chip) + 1, kids.IndexOf(bubble));
            var chipBox = BoundsIn(chip, root);
            var bubbleBox = BoundsIn(bubble, root);
            Assert.InRange(bubbleBox.Left - chipBox.Right, 5.5, 6.5);
            Assert.InRange(chip.ActualHeight, 23.5, 24.5);
            Assert.True(chipBox.Top >= bubbleBox.Top - 0.5 && chipBox.Bottom <= bubbleBox.Bottom + 0.5,
                $"the chip should sit inside the bubble's height band: chip {chipBox}, bubble {bubbleBox}");

            // ...and the mod chip's StackPanel no longer carries it.
            var mod = (FrameworkElement)header.FindName("ModSelectorCombo");
            Assert.NotSame(mod.Parent, chip.Parent);

            // 2. The wallet reads 20% smaller than the 36 px it was, unscaled (crisp text).
            var walletBox = BoundsIn(wallet, root);
            Assert.InRange(walletBox.Height, OldWalletHeight * 0.8 - 1, OldWalletHeight * 0.8 + 1);
            Assert.True(wallet.LayoutTransform == null || wallet.LayoutTransform.Value.IsIdentity,
                "the header should no longer scale the wallet");

            // 3. The "?" lies inside the pill's outline, at its right end.
            var walletButton = (Button)wallet.FindName("WalletButton");
            var outline = (FrameworkElement)walletButton.Template.FindName("Outline", walletButton);
            var help = (FrameworkElement)wallet.FindName("HelpButton");
            var outlineBox = BoundsIn(outline, root);
            var helpBox = BoundsIn(help, root);
            Assert.True(outlineBox.Contains(helpBox), $"the ? {helpBox} should sit inside the pill {outlineBox}");
            Assert.True(helpBox.Left > outlineBox.Left + outlineBox.Width * 0.7, "the ? should ride the pill's right end");
            var balance = (FrameworkElement)wallet.FindName("BalanceText");
            Assert.True(BoundsIn(balance, root).Right <= helpBox.Left, "the balance must stay clear of the ?");

            if (dir != null)
            {
                Directory.CreateDirectory(dir);
                int h = (int)Math.Ceiling(root.ActualHeight);
                var rtb = new RenderTargetBitmap((int)BandWidth, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(Path.Combine(dir, "header-chrome.png"))) enc.Save(fs);

                // A 3x close-up of the wallet so its text can be judged.
                const int k = 3;
                var crop = new Rect(walletBox.Left - 4, walletBox.Top - 10, walletBox.Width + 8, walletBox.Height + 14);
                var big = new RenderTargetBitmap((int)(crop.Width * k), (int)(crop.Height * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new VisualBrush(root) { Viewbox = crop, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill },
                        null, new Rect(0, 0, crop.Width, crop.Height));
                }
                big.Render(dv);
                var enc2 = new PngBitmapEncoder();
                enc2.Frames.Add(BitmapFrame.Create(big));
                using (var fs = File.Create(Path.Combine(dir, "header-wallet.png"))) enc2.Save(fs);
                File.WriteAllText(Path.Combine(dir, "header-chrome.txt"),
                    $"wallet {walletBox}; outline {outlineBox}; help {helpBox}; chip {chipBox}; bubble {bubbleBox}");
            }
        });
    }
}
