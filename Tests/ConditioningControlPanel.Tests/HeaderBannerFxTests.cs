using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 6 (owner, 2026-10-06): the header banner is 20% bigger with brighter text, pops
/// and flashes on every beat change, breathes its glow, and runs sparkles over the "consider
/// supporting the project" beat. These pin the sizes, the motion gate (Off moves nothing, the
/// sparkles belong to the support beat only), that every decorative layer lets clicks through to
/// the hyperlinks, and render the REAL header out of MainWindow.xaml (the loose-parse recipe of
/// <see cref="HeaderLevelChipRenderTests"/>).
///
/// <para>Set CCP_NAV_PNG_DIR to a folder to also write banner-support.png (with a sparkle run
/// caught mid-flight) and banner-welcome.png.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class HeaderBannerFxTests
{
    private const double BandWidth = 1469;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

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
        var xaml = Source("MainWindow", "MainWindow.xaml").Replace("\r\n", "\n");
        var head = Regex.Match(xaml, @"<Window\b(.*?)>", RegexOptions.Singleline).Groups[1].Value;
        var ns = string.Join(" ", Regex.Matches(head, @"xmlns(:\w+)?=""[^""]*""").Select(m => m.Value));
        ns = Regex.Replace(ns, @"clr-namespace:(ConditioningControlPanel[\w.]*)""", "clr-namespace:$1;assembly=ConditioningControlPanel\"");
        var resources = Regex.Match(xaml, @"<Window\.Resources>(.*?)</Window\.Resources>", RegexOptions.Singleline).Groups[1].Value;

        const string marker = "<Border Grid.Row=\"1\" Grid.Column=\"1\" Background=\"Transparent\" Padding=\"15,8\">";
        var start = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start > 0, "lost the header bar");
        var stop = xaml.IndexOf("\n        </Border>", start, StringComparison.Ordinal);
        var header = xaml.Substring(start, stop + "\n        </Border>".Length - start);
        header = "<Border x:Name=\"HeaderBarBlock\" Background=\"Transparent\" Padding=\"15,8\">"
                 + header.Substring(marker.Length);

        var loose = "<Grid " + ns + " Background=\"#120B1A\"><Grid.Resources>" + resources + "</Grid.Resources>"
                    + header + "</Grid>";
        foreach (var attr in HandlerAttributes)
            loose = Regex.Replace(loose, @"\s" + attr + @"=""[A-Za-z_][\w]*""", string.Empty);
        loose = Regex.Replace(loose, @"\sx:FieldModifier=""\w+""", string.Empty);
        var root = (Grid)XamlReader.Parse(loose);
        return (Border)root.FindName("HeaderBarBlock");
    }

    // =====================================================================================
    //  sizes
    // =====================================================================================

    [Fact]
    public void TheBannerIsTwentyPercentBiggerWithBrighterGlowingText()
    {
        Assert.Equal(31, BannerFxRules.HostHeight);       // 26 * 1.2
        Assert.Equal(744, BannerFxRules.HostMaxWidth);    // 620 * 1.2
        Assert.Equal(14, BannerFxRules.BeatFontSize);

        var xaml = Source("MainWindow", "MainWindow.xaml");
        var host = Regex.Match(xaml, "<Border Grid.Column=\"3\" x:Name=\"HeaderBannerHost\".*?>", RegexOptions.Singleline).Value;
        Assert.Contains("Height=\"31\"", host);
        Assert.Contains("MaxWidth=\"744\"", host);

        var body = Regex.Match(xaml, "x:Name=\"HeaderBannerHost\".*?x:Name=\"BannerSparkleLayer\"", RegexOptions.Singleline).Value;
        Assert.True(body.Length > 0, "the sparkle layer left the banner host");
        Assert.Contains("x:Name=\"BannerGlow\"", body);
        Assert.Contains("x:Name=\"BannerFlashRing\"", body);
        foreach (var beat in new[] { "TxtBannerPrimary", "TxtBannerSecondary", "TxtBannerWeb", "TxtBannerPool" })
        {
            var tag = Regex.Match(body, "<TextBlock x:Name=\"" + beat + "\"[^>]*>").Value;
            Assert.Contains("FontSize=\"14\"", tag);
            Assert.Contains("PinkButtonHoveredBrush", tag);
            // Each beat carries the soft pink glow TxtPlayerTitle wears.
            var from = body.IndexOf(tag, StringComparison.Ordinal);
            var next = body.IndexOf("</TextBlock>", from, StringComparison.Ordinal);
            Assert.Contains("<DropShadowEffect", body.Substring(from, next - from));
        }
        // The support line's bold run stands upright and white.
        Assert.Contains("label_consider_supporting_it}\" FontWeight=\"Bold\" FontStyle=\"Normal\" Foreground=\"White\"", body);
    }

    // =====================================================================================
    //  the motion gate
    // =====================================================================================

    [Fact]
    public void OffMovesNothingAndAHiddenWindowNeither()
    {
        foreach (var support in new[] { true, false })
        {
            Assert.Equal(default, BannerFxRules.OnBeatChange(MotionLevel.Off, windowActive: true, support));
            Assert.Equal(default, BannerFxRules.OnBeatChange(MotionLevel.Full, windowActive: false, support));
        }
        // Off never allows the ambient half either (ChromeAmbientAllowed is false there).
        Assert.False(BannerFxRules.Breathe(ambientAllowed: false));
        Assert.False(BannerFxRules.RepeatSparkles(ambientAllowed: false, supportOnScreen: true));
    }

    [Fact]
    public void TheSparkleRunBelongsToTheSupportBeatOnly()
    {
        foreach (var level in new[] { MotionLevel.Full, MotionLevel.Reduced })
        {
            var support = BannerFxRules.OnBeatChange(level, true, isSupportBeat: true);
            Assert.True(support.Pop && support.Flash && support.Sparkles);

            var other = BannerFxRules.OnBeatChange(level, true, isSupportBeat: false);
            Assert.True(other.Pop && other.Flash);
            Assert.False(other.Sparkles);
        }
        // The 12 s repeat is ambient and only while the support line is up.
        Assert.True(BannerFxRules.RepeatSparkles(true, true));
        Assert.False(BannerFxRules.RepeatSparkles(true, false));
        Assert.True(BannerFxRules.Breathe(true));
        Assert.Equal(12, BannerFxRules.SparkleRepeatSeconds);
    }

    [Fact]
    public void ARunCrossesTheTextLeftToRightInAStagger()
    {
        var run = BannerFxRules.PlanRun(textLeft: 40, textWidth: 500, layerHeight: 31, BannerFxRules.SparkleCount, seed: 7);
        Assert.Equal(BannerFxRules.SparkleCount, run.Count);
        Assert.InRange(run.Count, 5, 8);
        for (int i = 0; i < run.Count; i++)
        {
            var f = run[i];
            Assert.True(f.ToX - f.FromX > 300, $"star {i} barely moves: {f.FromX:F0} -> {f.ToX:F0}");
            Assert.InRange(f.FromX, 30, 110);
            Assert.InRange(f.ToX, 440, 560);
            Assert.InRange(f.Y, 31 / 2.0 - 8, 31 / 2.0 + 8);
            if (i > 0) Assert.True(f.DelaySeconds > run[i - 1].DelaySeconds);
        }
        Assert.Empty(BannerFxRules.PlanRun(0, 0, 31, 7, 1));
    }

    [Fact]
    public void TheRotationAndTheChromeLoopsDriveTheFx()
    {
        var marquee = Source("MainWindow", "MainWindow.Marquee.cs");
        var tick = Regex.Match(marquee, @"void BannerRotationTimer_Tick\(.*?\n        \}", RegexOptions.Singleline).Value;
        Assert.Contains("OnBannerBeatChanged(fadeInTarget);", tick);
        // Off swaps the text with no fade.
        Assert.Contains("MotionFx.AllowTransitions", tick);

        var chrome = Source("MainWindow", "MainWindow.ChromeFx.cs");
        var loops = Regex.Match(chrome, @"void ApplyChromeFxLoops\(\).*?\n        \}", RegexOptions.Singleline).Value;
        Assert.Contains("ApplyBannerFxLoops();", loops);

        var fx = Source("MainWindow", "MainWindow.BannerFx.cs");
        var apply = Regex.Match(fx, @"void ApplyBannerFxLoops\(\).*?\n        \}", RegexOptions.Singleline).Value;
        Assert.Contains("ChromeAmbientAllowed", apply);
        Assert.Contains("AmbientFrameRate", apply);
        Assert.Contains("RepeatBehavior.Forever", apply);
    }

    // =====================================================================================
    //  realised: clicks pass through, and the two beats render
    // =====================================================================================

    [Fact]
    public void TheBannerRendersAndItsFxLayersLetTheHyperlinksThrough()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_NAV_PNG_DIR") is { Length: > 0 } d ? d : null;
        WpfRenderHarness.OnStaThread(() =>
        {
            var header = BuildHeader();
            ((TextBlock)header.FindName("TxtPlayerTitle")).Text = "Basic Subject";
            var host = (Border)header.FindName("HeaderBannerHost");
            var primary = (TextBlock)header.FindName("TxtBannerPrimary");
            var secondary = (TextBlock)header.FindName("TxtBannerSecondary");
            var layer = (Canvas)header.FindName("BannerSparkleLayer");

            foreach (var name in new[] { "BannerSparkleLayer", "BannerFlashRing", "BannerSheenHost" })
                Assert.False(((UIElement)header.FindName(name)).IsHitTestVisible, name + " would swallow the hyperlink clicks");

            Layout(header);
            Assert.Equal(31, host.ActualHeight, 1);
            Assert.True(host.ActualWidth > 400, $"the banner collapsed to {host.ActualWidth:F0} px at {BandWidth} px");
            Assert.Equal(14, primary.FontSize);

            // A sparkle run caught mid-flight (sprites are hit-test invisible too).
            var origin = primary.TranslatePoint(new Point(0, 0), layer);
            var glow = (Color)header.FindResource("PinkColor");
            foreach (var (f, i) in BannerFxRules.PlanRun(origin.X, primary.ActualWidth, layer.ActualHeight, BannerFxRules.SparkleCount, 3)
                                               .Select((f, i) => (f, i)))
            {
                var sprite = BannerFxRules.CreateSparkleSprite(f.Size, glow);
                Assert.False(sprite.IsHitTestVisible);
                double t = 0.25 + i * 0.07;
                Canvas.SetLeft(sprite, f.FromX + (f.ToX - f.FromX) * t - f.Size / 2);
                Canvas.SetTop(sprite, f.Y - f.Size / 2);
                sprite.RenderTransform = new RotateTransform(f.Spin * t);
                layer.Children.Add(sprite);
            }
            Layout(header);

            // A click on the support line (where the link and the stars sit) lands on the text.
            var mid = primary.TranslatePoint(new Point(primary.ActualWidth - 30, primary.ActualHeight / 2), host);
            var hit = VisualTreeHelper.HitTest(host, mid)?.VisualHit;
            Assert.NotNull(hit);
            Assert.True(IsWithin(hit!, primary), $"a click on the support line landed on {hit!.GetType().Name}");

            if (dir != null) Save(header, host, Path.Combine(dir, "banner-support.png"));

            layer.Children.Clear();
            primary.Opacity = 0;
            secondary.Opacity = 1;
            secondary.Text = "Welcome back, CodeBambi";
            Layout(header);
            if (dir != null)
            {
                Save(header, host, Path.Combine(dir, "banner-welcome.png"));
                Save(header, header, Path.Combine(dir, "banner-header.png"), scale: 1);
            }
        });
    }

    private static bool IsWithin(DependencyObject hit, DependencyObject target)
    {
        for (var o = hit; o != null;
             o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o))
            if (ReferenceEquals(o, target)) return true;
        return false;
    }

    private static void Layout(FrameworkElement header)
    {
        header.Measure(new Size(BandWidth, double.PositiveInfinity));
        header.Arrange(new Rect(0, 0, BandWidth, header.DesiredSize.Height));
        header.UpdateLayout();
    }

    /// <summary>Renders the whole header at <paramref name="scale"/>x and crops to
    /// <paramref name="el"/> (plus a margin for its glow). The header is the parse root, so it
    /// sits at 0,0 and the crop rect is simply el's bounds in header space.</summary>
    private static void Save(FrameworkElement header, FrameworkElement el, string path, double scale = 2)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        int fullW = (int)Math.Ceiling(header.ActualWidth * scale), fullH = (int)Math.Ceiling(header.ActualHeight * scale);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x12, 0x0B, 0x1A)), null,
                             new Rect(0, 0, header.ActualWidth, header.ActualHeight));
        }
        var rtb = new RenderTargetBitmap(fullW, fullH, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Render(header);

        const double pad = 14;
        var box = el.TransformToAncestor(header).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
        box.Inflate(pad, pad);
        box.Intersect(new Rect(0, 0, header.ActualWidth, header.ActualHeight));
        var crop = new Int32Rect((int)(box.X * scale), (int)(box.Y * scale),
                                 Math.Min(fullW - (int)(box.X * scale), (int)(box.Width * scale)),
                                 Math.Min(fullH - (int)(box.Y * scale), (int)(box.Height * scale)));
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(new CroppedBitmap(rtb, crop)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
