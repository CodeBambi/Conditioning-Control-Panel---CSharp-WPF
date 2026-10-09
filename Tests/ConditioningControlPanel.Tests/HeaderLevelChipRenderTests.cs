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
/// Nav polish wave 4 (2026-10-06): the owner found the level twice (a "Lvl N" pill in the header
/// AND "LVL N" beside the XP bar) and the version twice (the header and the title bar). The header
/// lost its pill and its version; the XP row's chip became a filled pill and is the one level
/// readout. This realizes the REAL header bar and XP row out of MainWindow.xaml (loose parse with the
/// window's resources and the app theme, handler attributes stripped, the same recipe as
/// <see cref="NavFinalRenderTests"/>) at the 1469 px the canvas's column 1 gets on a 1585 px window,
/// and checks the chip is the first level text anyone reads and the XP row did not grow.
///
/// <para>Set CCP_NAV_FINAL_DIR to a folder to also save header-xp-band.png.</para>
/// </summary>
public class HeaderLevelChipRenderTests
{
    private const double BandWidth = 1469;

    /// <summary>The XP row before the pill: the 22 px quest stamps and ~23 px stat pills set it.
    /// The brief allows 4 px of growth at most.</summary>
    private const double MaxXpRowContentHeight = 23 + 4;

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

    /// <summary>One canvas block, from its marker comment to the closing tag at canvas indent.</summary>
    private static string Block(string xaml, string startMarker, string closing)
    {
        var start = xaml.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start > 0, "lost the block starting " + startMarker);
        var stop = xaml.IndexOf("\n" + closing, start, StringComparison.Ordinal);
        Assert.True(stop > start, "lost the end of the block starting " + startMarker);
        var block = xaml.Substring(start, stop + closing.Length + 1 - start);
        var firstTagEnd = block.IndexOf('>');
        return Regex.Replace(block.Substring(0, firstTagEnd), @"Grid\.(Row|RowSpan|Column|ColumnSpan)=""\d+""", string.Empty)
               + block.Substring(firstTagEnd);
    }

    private static Grid BuildBand()
    {
        var xaml = Xaml().Replace("\r\n", "\n");
        var head = Regex.Match(xaml, @"<Window\b(.*?)>", RegexOptions.Singleline).Groups[1].Value;
        var ns = string.Join(" ", Regex.Matches(head, @"xmlns(:\w+)?=""[^""]*""").Select(m => m.Value));
        ns = Regex.Replace(ns, @"clr-namespace:(ConditioningControlPanel[\w.]*)""", "clr-namespace:$1;assembly=ConditioningControlPanel\"");
        var resources = Regex.Match(xaml, @"<Window\.Resources>(.*?)</Window\.Resources>", RegexOptions.Singleline).Groups[1].Value;

        var header = Block(xaml, "<Border Grid.Row=\"1\" Grid.Column=\"1\" Background=\"Transparent\" Padding=\"15,8\">", "        </Border>");
        var xp = Block(xaml, "<Grid Grid.Row=\"3\" Grid.Column=\"1\" Margin=\"15,10,15,5\">", "        </Grid>");
        header = "<Border x:Name=\"HeaderBarBlock\"" + header.Substring("<Border".Length);
        xp = "<Grid x:Name=\"XpRowBlock\" Grid.Row=\"1\"" + xp.Substring("<Grid".Length);

        var loose = "<Grid " + ns + " Background=\"#120B1A\"><Grid.Resources>" + resources + "</Grid.Resources>"
                    + "<Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>"
                    + "<Border Grid.RowSpan=\"2\" Background=\"#33000000\"/>"
                    + header + xp + "</Grid>";
        foreach (var attr in HandlerAttributes)
            loose = Regex.Replace(loose, @"\s" + attr + @"=""[A-Za-z_][\w]*""", string.Empty);
        loose = Regex.Replace(loose, @"\sx:FieldModifier=""\w+""", string.Empty);
        return (Grid)XamlReader.Parse(loose);
    }

    [Fact]
    public void TheHeaderNoLongerCarriesALevelPillOrAVersion()
    {
        var xaml = Xaml();
        Assert.DoesNotContain("x:Name=\"TxtLevel\"", xaml);
        Assert.DoesNotContain("x:Name=\"TxtHeaderVersion\"", xaml);
        Assert.DoesNotContain("label_lvl_1}", xaml);

        // ...and no code-behind reaches for either name any more.
        var stragglers = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "ConditioningControlPanel"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bTxtLevel\b|\bTxtHeaderVersion\b"))
            .ToArray();
        Assert.True(stragglers.Length == 0, "still referenced in: " + string.Join(", ", stragglers));
    }

    [Fact]
    public void TheLevelChipIsAFilledPillThatKeepsItsPopRig()
    {
        var xaml = Xaml();
        var chip = Regex.Match(xaml, "<Border x:Name=\"LevelChip\".*?</Border>\\s*\n", RegexOptions.Singleline);
        Assert.True(chip.Success, "LevelChip is gone");
        Assert.Contains("Background=\"{DynamicResource PinkBrush}\"", chip.Value);
        Assert.Contains("<DropShadowEffect", chip.Value);
        Assert.Contains("x:Name=\"LevelChipScale\"", chip.Value);
        Assert.Contains("x:Name=\"LevelChipRotate\"", chip.Value);
        Assert.Contains("x:Name=\"TxtLevelLabel\"", chip.Value);
        Assert.Contains("FontWeight=\"ExtraBold\"", chip.Value);
        Assert.Contains("Foreground=\"White\"", chip.Value);
    }

    [Fact]
    public void TheBandRendersWithTheChipAsTheOnlyLevelAndTheXpRowBarelyGrows()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_NAV_FINAL_DIR") is { Length: > 0 } d ? d : null;
        WpfRenderHarness.OnStaThread(() =>
        {
            var band = BuildBand();
            ((TextBlock)band.FindName("TxtLevelLabel")).Text = "LVL 44";
            ((TextBlock)band.FindName("TxtPlayerTitle")).Text = "Basic Subject";

            band.Measure(new Size(BandWidth, double.PositiveInfinity));
            band.Arrange(new Rect(0, 0, BandWidth, band.DesiredSize.Height));
            band.UpdateLayout();

            var chip = (FrameworkElement)band.FindName("LevelChip");
            var xpRow = (Grid)band.FindName("XpRowBlock");
            Assert.True(chip.ActualWidth > 40 && chip.ActualHeight >= 20, $"chip did not lay out: {chip.ActualWidth}x{chip.ActualHeight}");

            // The XP row's content height (its margin aside) stays within 4 px of the stat pills.
            double content = xpRow.ActualHeight;
            Assert.True(content <= MaxXpRowContentHeight, $"the XP row grew to {content:F1} px");

            if (dir != null)
            {
                Directory.CreateDirectory(dir);
                int h = (int)Math.Ceiling(band.ActualHeight);
                var rtb = new RenderTargetBitmap((int)BandWidth, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(band);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(Path.Combine(dir, "header-xp-band.png"))) enc.Save(fs);
                File.WriteAllText(Path.Combine(dir, "header-xp-band.txt"),
                    $"band {BandWidth}x{band.ActualHeight:F1}; xp row {content:F1}; chip {chip.ActualWidth:F1}x{chip.ActualHeight:F1}");
            }
        });
    }
}
