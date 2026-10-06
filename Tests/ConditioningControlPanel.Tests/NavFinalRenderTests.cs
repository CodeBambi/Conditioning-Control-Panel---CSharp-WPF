using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav rework integration (2026-10-06): the rail had only ever been checked by arithmetic. This
/// realizes the REAL NavSidebar markup out of MainWindow.xaml (with the window's own styles, the
/// app theme and the real foot chips; only event-handler attributes are stripped, since a loose
/// XAML parse has no code-behind) and measures it against the column the 901 px canvas gives it.
/// MainWindow itself is too heavy to realize here (see PageAdornerScopeTests).
///
/// <para>Set CCP_NAV_FINAL_DIR to a folder to also save final-rail.png, final-strip-&lt;section&gt;.png
/// and final-composite.png (rail + Play strip + page area on the 1585x901 canvas).</para>
/// </summary>
public class NavFinalRenderTests
{
    /// <summary>The canvas is 901 tall; row 0 (title bar, 36) is the only row above the rail.
    /// The XP bar and header rows are Auto and sit in column 1, so the rail gets 901 - 36.</summary>
    private const double RailColumnHeight = 901 - 36;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }

    private static readonly string[] HandlerAttributes =
    {
        "Click", "MouseEnter", "MouseLeave", "MouseLeftButtonDown", "MouseLeftButtonUp", "MouseRightButtonUp",
        "MouseDown", "MouseUp", "PreviewMouseLeftButtonDown", "PreviewMouseLeftButtonUp", "PreviewMouseDown",
        "Loaded", "Unloaded", "Checked", "Unchecked", "SizeChanged", "ToolTipOpening", "ContextMenuOpening",
        "KeyDown", "PreviewKeyDown", "ValueChanged", "SelectionChanged", "TextChanged", "GotFocus", "LostFocus",
        "IsVisibleChanged", "MouseWheel", "PreviewMouseWheel",
    };

    /// <summary>The NavSidebar Border, parsed loose with the window's resources around it.</summary>
    private static Grid BuildRail()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml"));

        var head = Regex.Match(xaml, @"<Window\b(.*?)>", RegexOptions.Singleline).Groups[1].Value;
        var ns = string.Join(" ", Regex.Matches(head, @"xmlns(:\w+)?=""[^""]*""").Select(m => m.Value))
            .Replace("clr-namespace:ConditioningControlPanel\"", "clr-namespace:ConditioningControlPanel;assembly=ConditioningControlPanel\"");
        ns = Regex.Replace(ns, @"clr-namespace:(ConditioningControlPanel[\w.]*)""", "clr-namespace:$1;assembly=ConditioningControlPanel\"");

        var resources = Regex.Match(xaml, @"<Window\.Resources>(.*?)</Window\.Resources>", RegexOptions.Singleline).Groups[1].Value;
        var start = xaml.IndexOf("<Border x:Name=\"NavSidebar\"", StringComparison.Ordinal);
        var stop = xaml.IndexOf("<!-- Header Bar", start, StringComparison.Ordinal);
        Assert.True(start > 0 && stop > start, "the NavSidebar block stopped parsing");
        var sidebar = xaml.Substring(start, stop - start);
        sidebar = sidebar.Substring(0, sidebar.LastIndexOf("</Border>", StringComparison.Ordinal) + "</Border>".Length);

        // Only the sidebar's own canvas placement goes; the rows inside it keep theirs.
        var firstTagEnd = sidebar.IndexOf('>');
        sidebar = Regex.Replace(sidebar.Substring(0, firstTagEnd), @"Grid\.(Row|RowSpan|Column|ColumnSpan)=""\d+""", string.Empty)
                  + sidebar.Substring(firstTagEnd);

        var loose = "<Grid " + ns + "><Grid.Resources>" + resources + "</Grid.Resources>" + sidebar + "</Grid>";
        foreach (var attr in HandlerAttributes)
            loose = Regex.Replace(loose, @"\s" + attr + @"=""[A-Za-z_][\w]*""", string.Empty);
        loose = Regex.Replace(loose, @"\sx:FieldModifier=""\w+""", string.Empty);

        return (Grid)XamlReader.Parse(loose);
    }

    private static void Layout(FrameworkElement e, double w, double h)
    {
        e.Measure(new Size(w, h));
        e.Arrange(new Rect(0, 0, w, h));
        e.UpdateLayout();
    }

    private static void Save(FrameworkElement e, int w, int h, string? dir, string name)
    {
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    private static string? OutDir() => Environment.GetEnvironmentVariable("CCP_NAV_FINAL_DIR") is { Length: > 0 } d ? d : null;

    [Fact]
    public void TheRealRailFitsTheCanvasColumnWithItsFootUnclipped()
    {
        var dir = OutDir();
        WpfRenderHarness.OnStaThread(() =>
        {
            var host = BuildRail();
            var sidebar = (Border)host.FindName("NavSidebar");
            var inner = (Grid)sidebar.Child;

            // Natural height: what the rail asks for with no ceiling.
            inner.Measure(new Size(96, double.PositiveInfinity));
            double natural = inner.DesiredSize.Height + inner.Margin.Top + inner.Margin.Bottom;

            Layout(host, 96, RailColumnHeight);
            var rows = (StackPanel)host.FindName("NavSectionRows");
            var gear = (FrameworkElement)host.FindName("DoorSettings");
            var gearBottom = gear.TranslatePoint(new Point(0, gear.ActualHeight), host).Y;
            var rowsBottom = rows.TranslatePoint(new Point(0, rows.ActualHeight), host).Y;
            var foot = (FrameworkElement)VisualTreeHelper.GetParent(gear);
            var footTop = foot.TranslatePoint(new Point(0, 0), host).Y;

            var report = $"natural={natural:F0} column={RailColumnHeight:F0} rowsBottom={rowsBottom:F0} footTop={footTop:F0} gearBottom={gearBottom:F0}";
            if (dir != null)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "final-rail.txt"), report);
            }
            Save(host, 96, (int)RailColumnHeight, dir, "final-rail.png");

            Assert.True(natural <= RailColumnHeight, "the rail needs more than its column: " + report);
            Assert.True(gearBottom <= RailColumnHeight, "the Settings gear is clipped: " + report);
            Assert.True(rowsBottom <= footTop, "the section rows run into the foot: " + report);
            Assert.Equal(8, ((Panel)rows).Children.Count + 1); // seven rows + the gear in the foot
        });
    }

    [Fact]
    public void EveryStripRendersAndAComposite()
    {
        var dir = OutDir();
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var section in NavSections.Order.Where(s => s.Key != NavSections.Home))
            {
                var strip = new SectionTabStrip { MotionOverride = MotionLevel.Off, Width = 1489 };
                var bg = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x10, 0x24)), Child = strip };
                strip.Show(section.Key, section.DefaultTab, section.Key == NavSections.Settings ? "Monitors" : null);
                Layout(bg, 1489, 90);
                Assert.True(strip.ActualHeight > 0, section.Key + " strip did not lay out");
                Save(bg, 1489, (int)Math.Ceiling(Math.Max(strip.ActualHeight, 30)), dir, $"final-strip-{section.Key}.png");
            }

            if (dir == null) return;

            // Composite: rail in column 0, the Play strip over a page area in column 1.
            var canvas = new Grid { Width = 1585, Height = 901, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x0B, 0x1A)) };
            canvas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            canvas.ColumnDefinitions.Add(new ColumnDefinition());
            canvas.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            canvas.RowDefinitions.Add(new RowDefinition());

            var rail = BuildRail();
            Grid.SetRow(rail, 1);
            canvas.Children.Add(rail);

            var page = new Grid { Margin = new Thickness(10, 5, 10, 10) };
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition());
            var playStrip = new SectionTabStrip { MotionOverride = MotionLevel.Off };
            playStrip.Show(NavSections.Play, "play");
            page.Children.Add(playStrip);
            FrameworkElement body;
            try { body = new Views.Tabs.PlayTabView(); }
            catch { body = new Border { Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(12) }; }
            Grid.SetRow(body, 1);
            page.Children.Add(body);
            Grid.SetRow(page, 1);
            Grid.SetColumn(page, 1);
            canvas.Children.Add(page);

            Layout(canvas, 1585, 901);
            Save(canvas, 1585, 901, dir, "final-composite.png");
        });
    }
}
