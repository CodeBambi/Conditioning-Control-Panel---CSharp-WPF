using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 11, lane HUD. The owner: "the XP bar has the little white glow at the end a bit
/// offset compared to the tip of the XP bar". Cause: two white lights rode the tip. The meniscus
/// (12 px, MainWindow.HeroFx.cs) was centred ON the tip, but the tube's bead (8 px, a child of the
/// fill) was right-aligned INSIDE the fill with a 1 px margin and its brush's bright origin sat at
/// 0.4 of its width, so its brightest pixel landed about 6 px behind the tip and the pair read as
/// one glow smeared backwards. The bead now hangs half past the fill's edge so its centre IS the
/// tip, its brush is centred with only a slight top-left lamp highlight, and it hides on a fill
/// too short to carry it (never past the track start).
///
/// <para>The rendered test realises the REAL XPBarTrack block out of MainWindow.xaml (loose parse,
/// handlers stripped, the HeaderLevelChipRenderTests recipe), sets the fill and the meniscus the way
/// FillXpBarTo / AnimateXpMeniscus do, renders at 4x and measures on the tube's centre row: the
/// brightest pixel the glows add must sit within 1 px of the last fill pixel. Set CCP_HUD11_DIR to a
/// folder to also save the strips and the numbers.</para>
/// </summary>
public class HudPolish11Tests
{
    private const double TrackWidth = 600;
    private const double Scale = 4;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }

    private static string Xaml() =>
        File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml"))
            .Replace("\r\n", "\n");

    private static string TrackBlock(string xaml)
    {
        var start = xaml.IndexOf("<Border x:Name=\"XPBarTrack\"", StringComparison.Ordinal);
        Assert.True(start > 0, "XPBarTrack is gone");
        var txt = xaml.IndexOf("<TextBlock x:Name=\"TxtXP\"", start, StringComparison.Ordinal);
        var stop = xaml.LastIndexOf("</Border>", txt, StringComparison.Ordinal);
        Assert.True(stop > start, "lost the end of XPBarTrack");
        return xaml.Substring(start, stop + "</Border>".Length - start);
    }

    private static Border BuildTrack()
    {
        var xaml = Xaml();
        var head = Regex.Match(xaml, @"<Window\b(.*?)>", RegexOptions.Singleline).Groups[1].Value;
        var ns = string.Join(" ", Regex.Matches(head, @"xmlns(:\w+)?=""[^""]*""").Select(m => m.Value));
        ns = Regex.Replace(ns, @"clr-namespace:(ConditioningControlPanel[\w.]*)""", "clr-namespace:$1;assembly=ConditioningControlPanel\"");
        var block = TrackBlock(xaml);
        block = Regex.Replace(block, @"\s(ToolTipOpening|SizeChanged|Loaded)=""[A-Za-z_]\w*""", string.Empty);
        block = Regex.Replace(block, @"\sGrid\.Column=""\d+""", string.Empty);
        block = "<Border " + ns + block.Substring("<Border".Length);
        var track = (Border)XamlReader.Parse(block);
        track.HorizontalAlignment = HorizontalAlignment.Left;
        track.Margin = new Thickness(0);
        return track;
    }

    private sealed record Sample(double Fraction, double FillPx, double EdgePx, double GlowPx, double BeadPx, double MeniscusPx, double PeakPx);

    /// <summary>Lays the track out at <paramref name="fraction"/> and returns the centre row (BGRA)
    /// with the bead and meniscus shown as named.</summary>
    private static byte[] Row(Border track, double fraction, bool bead, bool meniscus, string? save = null)
    {
        var fill = (Border)track.FindName("XPBar");
        var dot = (Border)track.FindName("XPMeniscus");
        var slide = (TranslateTransform)track.FindName("XPMeniscusSlide");
        var beadEl = (Ellipse)track.FindName("XPTubeBead");
        var sheen = (Border)track.FindName("XPBarSheen");
        sheen.Opacity = 0;

        double w = fraction * TrackWidth;
        fill.Width = w;
        // FillXpBarTo / AnimateXpMeniscus at rest: X = max(0, w - dot / 2), rest opacity, hidden < 5 px.
        slide.X = Math.Max(0, w - dot.Width / 2);
        dot.Opacity = meniscus && w >= 5 ? 0.55 : 0;
        MainWindow.ApplyXpBeadRule(beadEl, w);
        if (!bead) beadEl.Visibility = Visibility.Hidden;

        var host = new Grid { Width = TrackWidth + 40, Height = 20, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x0B, 0x1A)) };
        host.Children.Add(track);
        track.Margin = new Thickness(20, 0, 0, 0);
        track.VerticalAlignment = VerticalAlignment.Center;
        host.Measure(new Size(host.Width, host.Height));
        host.Arrange(new Rect(0, 0, host.Width, host.Height));
        host.UpdateLayout();

        int pw = (int)(host.Width * Scale), ph = (int)(host.Height * Scale);
        var rtb = new RenderTargetBitmap(pw, ph, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        rtb.Render(host);
        host.Children.Clear();

        if (save != null)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(save);
            enc.Save(fs);
        }

        var row = new byte[pw * 4];
        rtb.CopyPixels(new Int32Rect(0, ph / 2, pw, 1), row, pw * 4, 0);
        return row;
    }

    private static double Lum(byte[] row, int x) => 0.114 * row[x * 4] + 0.587 * row[x * 4 + 1] + 0.299 * row[x * 4 + 2];

    private static double Diff(byte[] a, byte[] b, int x) =>
        Math.Abs(a[x * 4] - b[x * 4]) + Math.Abs(a[x * 4 + 1] - b[x * 4 + 1]) + Math.Abs(a[x * 4 + 2] - b[x * 4 + 2]);

    /// <summary>Track-local DIPs of the brightest pixel <paramref name="lit"/> adds over <paramref name="dark"/>, or NaN.</summary>
    private static double Brightest(byte[] lit, byte[] dark)
    {
        int best = -1; double bestGain = 2;
        for (int x = 0; x < lit.Length / 4; x++)
        {
            double gain = Lum(lit, x) - Lum(dark, x);
            if (gain > bestGain) { bestGain = gain; best = x; }
        }
        return best < 0 ? double.NaN : (best + 0.5) / Scale - 20;
    }

    /// <summary>Track-local DIPs of the brightest pixel within 8 px of <paramref name="tip"/> (absolute luminance).</summary>
    private static double Peak(byte[] row, double tip)
    {
        int lo = Math.Max(0, (int)((tip + 20 - 8) * Scale)), hi = Math.Min(row.Length / 4 - 1, (int)((tip + 20 + 8) * Scale));
        int best = lo;
        for (int x = lo; x <= hi; x++) if (Lum(row, x) > Lum(row, best)) best = x;
        return (best + 0.5) / Scale - 20;
    }

    private static List<Sample> Measure(string? dir)
    {
        var samples = new List<Sample>();
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var f in new[] { 0.0, 0.004, 0.05, 0.5, 0.83, 0.99, 1.0 })
            {
                var track = BuildTrack();
                var empty = Row(track, 0, false, false);
                var plain = Row(track, f, false, false);
                var beadOnly = Row(track, f, true, false);
                var menOnly = Row(track, f, false, true);
                var both = Row(track, f, true, true, dir == null ? null : System.IO.Path.Combine(dir, $"xp-tip-{f * 100:00}.png"));

                // the visual leading edge: the last pixel the fill changes on the centre row
                double edge = double.NaN;
                for (int x = plain.Length / 4 - 1; x >= 0; x--)
                    if (Diff(plain, empty, x) > 24) { edge = (x + 1.0) / Scale - 20; break; }

                samples.Add(new Sample(f, f * TrackWidth, edge, Brightest(both, plain),
                                       Brightest(beadOnly, plain), Brightest(menOnly, plain),
                                       f * TrackWidth >= MainWindow.XpBeadMinFillPx ? Peak(both, f * TrackWidth) : double.NaN));
            }
        });

        if (dir != null)
        {
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder("fraction fillPx edgePx glowPx beadPx meniscusPx peakPx (track-local DIPs; edge = right side of the last fill pixel)\n");
            foreach (var s in samples)
                sb.AppendLine($"{s.Fraction:0.000} {s.FillPx:0.00} {s.EdgePx:0.00} {s.GlowPx:0.00} {s.BeadPx:0.00} {s.MeniscusPx:0.00} {s.PeakPx:0.00}");
            File.AppendAllText(System.IO.Path.Combine(dir, "xp-tip.txt"), sb.ToString());
        }
        return samples;
    }

    [Fact]
    public void TheGlowsBrightestPixelSitsOnTheTipAtEveryFill()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_HUD11_DIR") is { Length: > 0 } d ? d : null;
        var samples = Measure(dir);

        foreach (var s in samples.Where(s => s.FillPx >= MainWindow.XpBeadMinFillPx))
        {
            Assert.False(double.IsNaN(s.EdgePx), $"no fill edge at {s.Fraction:P0}");
            // the last fill pixel is [edge - 1, edge); the glow's brightest pixel lands on it, within 1 px
            Assert.True(Math.Abs(s.GlowPx - (s.EdgePx - 0.5)) <= 1.0,
                $"at {s.Fraction:P0} the glow peaks at {s.GlowPx:F2} but the tip is at {s.EdgePx:F2}");
            Assert.True(Math.Abs(s.PeakPx - (s.EdgePx - 0.5)) <= 1.0,
                $"at {s.Fraction:P0} the brightest pixel is at {s.PeakPx:F2} but the tip is at {s.EdgePx:F2}");
            Assert.True(Math.Abs(s.BeadPx - (s.EdgePx - 0.5)) <= 1.0,
                $"at {s.Fraction:P0} the bead peaks at {s.BeadPx:F2} but the tip is at {s.EdgePx:F2}");
        }

        // nothing lights an empty or near-empty track, so nothing sits past the track start
        foreach (var s in samples.Where(s => s.FillPx < MainWindow.XpBeadMinFillPx))
            Assert.True(double.IsNaN(s.GlowPx), $"at {s.Fraction:P1} a glow shows on an empty track at {s.GlowPx:F2}");
    }

    [Fact]
    public void TheBeadHidesBelowItsMinimumFillAndShowsAboveIt()
    {
        Assert.False(MainWindow.XpBeadVisible(0));
        Assert.False(MainWindow.XpBeadVisible(MainWindow.XpBeadMinFillPx - 0.01));
        Assert.True(MainWindow.XpBeadVisible(MainWindow.XpBeadMinFillPx));
        Assert.True(MainWindow.XpBeadVisible(TrackWidth));
        Assert.False(MainWindow.XpBeadVisible(double.NaN));
    }

    [Fact]
    public void TheBeadHangsHalfPastTheFillAndItsBrushIsCentredWithALampHighlight()
    {
        var xaml = Xaml();
        var bead = Regex.Match(xaml, "<Ellipse x:Name=\"XPTubeBead\"[^>]*/>", RegexOptions.Singleline).Value;
        Assert.Contains("Width=\"8\" Height=\"8\"", bead);
        Assert.Contains("HorizontalAlignment=\"Right\"", bead);
        Assert.Contains("Margin=\"0,0,-4,0\"", bead);                  // centre = the fill's right edge
        Assert.Contains("Visibility=\"Hidden\"", bead);                 // until a fill can carry it
        Assert.Matches(new Regex("<Border x:Name=\"XPBar\"[^>]*SizeChanged=\"XPBar_SizeChanged\""), xaml);

        // The meniscus fits the groove: taller than the 10 px track, layout clipped it into a flat block.
        var track = Regex.Match(xaml, "<Border x:Name=\"XPBarTrack\"[^>]*>").Value;
        var height = Regex.Match(track, "Height=\"(\\d+)\"").Groups[1].Value;
        Assert.Contains("<Border x:Name=\"XPMeniscus\" Width=\"" + height + "\" Height=\"" + height + "\"", xaml);

        var depth = File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "Theme", "Depth.xaml"));
        var brush = Regex.Match(depth, "<RadialGradientBrush x:Key=\"DepthTubeBead\"[^>]*>").Value;
        Assert.Contains("Center=\"0.5,0.5\"", brush);
        Assert.Contains("RadiusX=\"0.5\" RadiusY=\"0.5\"", brush);       // fades out at the bead's own edge
        var origin = Regex.Match(brush, "GradientOrigin=\"([0-9.]+),([0-9.]+)\"");
        Assert.True(origin.Success);
        double ox = double.Parse(origin.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        double oy = double.Parse(origin.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        // the lamp is top-left, but only just: 8 px * (0.5 - ox) stays under 1 px
        Assert.InRange(ox, 0.40, 0.5);
        Assert.InRange(oy, 0.30, 0.5);
    }
}
