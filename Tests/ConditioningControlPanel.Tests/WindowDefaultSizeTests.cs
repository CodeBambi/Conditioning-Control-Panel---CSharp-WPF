using System;
using System.IO;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 7 (owner, 2026-10-06): "make the default size of the window 25% bigger since we
/// now have more space". The panel opens at 1954 x 1179 DIP (was 1563 x 943) and a work area that
/// cannot take it shrinks the window on BOTH axes by one factor (<see cref="WindowFitRule"/>),
/// because the whole UI is a Viewbox Stretch="Fill" over a fixed design canvas: a per-axis clamp
/// would squash the panel on every 1080p desk.
///
/// <para>The XAML half is a source test: MainWindow cannot be constructed off a real app (same
/// idiom as <c>Phase8RedirectContractTests</c>).</para>
/// </summary>
public class WindowDefaultSizeTests
{
    private const double OldWidth = 1563, OldHeight = 943;
    private const double CanvasWidth = 1585, CanvasHeight = 901;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string MainWindowXaml() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml"));

    /// <summary>The attributes of the root &lt;Window&gt; element only (up to its first '&gt;').</summary>
    private static string WindowElement()
    {
        var xaml = MainWindowXaml();
        var start = xaml.IndexOf("<Window ", StringComparison.Ordinal);
        Assert.True(start >= 0, "MainWindow.xaml has no <Window element");
        var end = xaml.IndexOf('>', start);
        return xaml.Substring(start, end - start);
    }

    private static double Attr(string element, string name)
    {
        var m = Regex.Match(element, @"\s" + name + "=\"([0-9.]+)\"");
        Assert.True(m.Success, $"<Window> has no {name}");
        return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void The_window_opens_about_6_percent_bigger()
    {
        var w = WindowElement();
        Assert.Equal(1661, Attr(w, "Width"));
        Assert.Equal(1002, Attr(w, "Height"));
        Assert.Equal(WindowFitRule.DefaultWidthDip, Attr(w, "Width"));
        Assert.Equal(WindowFitRule.DefaultHeightDip, Attr(w, "Height"));

        // Owner: 25% bigger was too big, so 15% off that: 1563 x 943 x 1.25 x 0.85, to the pixel.
        Assert.Equal(Math.Round(OldWidth * 1.25 * 0.85), Attr(w, "Width"));
        Assert.Equal(Math.Round(OldHeight * 1.25 * 0.85), Attr(w, "Height"));
    }

    [Fact]
    public void The_bigger_default_keeps_the_aspect_the_canvas_was_tuned_at()
    {
        var w = WindowElement();
        var aspect = Attr(w, "Width") / Attr(w, "Height");
        Assert.InRange(aspect, OldWidth / OldHeight - 0.002, OldWidth / OldHeight + 0.002);
        // The design canvas is unchanged, so every DIP of it now draws about 1.05x bigger.
        Assert.Contains($"x:Name=\"DesignCanvas\" Width=\"{CanvasWidth}\" Height=\"{CanvasHeight}\"", MainWindowXaml());
        Assert.InRange(Attr(w, "Width") / CanvasWidth, 1.03, 1.07);
    }

    [Fact]
    public void The_floors_did_not_move()
    {
        var w = WindowElement();
        Assert.Equal(1131, Attr(w, "MinWidth"));
        Assert.Equal(620, Attr(w, "MinHeight"));
    }

    [Fact]
    public void Both_clamp_paths_use_the_uniform_rule()
    {
        var root = Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow");
        var center = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"));
        var fit = File.ReadAllText(Path.Combine(root, "MainWindow.WorkAreaFit.cs"));
        Assert.Contains("WindowFitRule.Fit(Width, Height, screenWidth, screenHeight)", center);
        Assert.DoesNotContain("Width = Math.Min(Width, screenWidth)", center);
        Assert.Contains("WindowFitRule.FitPx(w, h, wa.Width, wa.Height)", fit);
        Assert.DoesNotContain("Math.Min(w, wa.Width)", fit);
    }

    // ---- the pure arithmetic --------------------------------------------------------------

    [Fact]
    public void A_window_that_fits_is_never_touched_and_never_grown()
    {
        Assert.Equal((1954.0, 1179.0), WindowFitRule.Fit(1954, 1179, 2560, 1392)); // owner's 1440p desk
        Assert.Equal((1000.0, 600.0), WindowFitRule.Fit(1000, 600, 2560, 1392));
        Assert.Equal((1954.0, 1179.0), WindowFitRule.Fit(1954, 1179, 1954, 1179));
    }

    [Fact]
    public void A_1080p_desk_shrinks_both_axes_by_one_factor()
    {
        // 1920x1080 at 100%, 48 px taskbar: only the HEIGHT overflows.
        var (w, h) = WindowFitRule.Fit(1954, 1179, 1920, 1032);
        Assert.Equal(1032, h, 6);
        Assert.Equal(1954 * 1032.0 / 1179, w, 6);
        Assert.Equal(1954.0 / 1179, w / h, 6);
        Assert.True(w < 1920);
    }

    [Fact]
    public void A_width_bound_area_lands_the_width_and_scales_the_height()
    {
        var (w, h) = WindowFitRule.Fit(1954, 1179, 1500, 1400);
        Assert.Equal(1500, w, 6);
        Assert.Equal(1179 * 1500.0 / 1954, h, 6);
    }

    [Theory]
    [InlineData(1920, 1032)]   // 1080p, 100%
    [InlineData(1536, 826)]    // 1080p, 125%
    [InlineData(1280, 688)]    // 1080p, 150%
    [InlineData(1707, 928)]    // 1440p, 150%
    [InlineData(1280, 672)]    // 4K TV, 300%
    public void Every_common_desk_keeps_the_aspect_and_fits(double areaW, double areaH)
    {
        var (w, h) = WindowFitRule.Fit(1954, 1179, areaW, areaH);
        Assert.True(w <= areaW + 1e-9 && h <= areaH + 1e-9);
        Assert.Equal(1954.0 / 1179, w / h, 6);
        // One axis is binding: no wasted room on both sides.
        Assert.True(Math.Abs(w - areaW) < 1e-6 || Math.Abs(h - areaH) < 1e-6);
    }

    [Fact]
    public void Broken_measurements_never_collapse_the_window()
    {
        Assert.Equal((1954.0, 1179.0), WindowFitRule.Fit(1954, 1179, 0, 1032));
        Assert.Equal((1954.0, 1179.0), WindowFitRule.Fit(1954, 1179, double.NaN, 1032));
        Assert.Equal((1954.0, 1179.0), WindowFitRule.Fit(1954, 1179, 1920, double.PositiveInfinity));
        Assert.Equal((double.NaN, 1179.0), WindowFitRule.Fit(double.NaN, 1179, 1920, 1032));
    }

    [Fact]
    public void The_pixel_path_rounds_and_never_passes_the_area()
    {
        // 1954x1179 DIP at 125% = 2443x1474 px; a 1080p work area is 1920x1032 px.
        var (w, h) = WindowFitRule.FitPx(2443, 1474, 1920, 1032);
        Assert.Equal(1032, h);
        Assert.Equal((int)Math.Round(2443 * 1032.0 / 1474), w);
        Assert.True(w <= 1920);

        Assert.Equal((1954, 1179), WindowFitRule.FitPx(1954, 1179, 2560, 1392));
        Assert.Equal((1954, 1179), WindowFitRule.FitPx(1954, 1179, 0, 1392));
    }

    [Fact]
    public void The_fitted_size_is_stable_on_a_second_pass()
    {
        // The fit runs again on DPI changes and after load: a fitted window must be a no-op.
        var first = WindowFitRule.FitPx(2443, 1474, 1920, 1032);
        Assert.Equal(first, WindowFitRule.FitPx(first.Width, first.Height, 1920, 1032));
    }
}
