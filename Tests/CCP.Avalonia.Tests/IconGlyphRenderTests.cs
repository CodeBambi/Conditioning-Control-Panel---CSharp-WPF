using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using IconKind = FluentIcons.Common.Symbol;
using IconVariant = FluentIcons.Common.IconVariant;

namespace CCP.Avalonia.Tests;

/// <summary>Fluent icons L0a: FluentIcons.Avalonia 2.1.343 really draws through fx:IconGlyph on
/// Avalonia 12.1.2 headless Skia (not blank, not the .notdef box), Foreground inherits, Regular
/// differs from Filled; the semantic brushes keep >= 3:1 on the app surfaces; a converted gate's
/// IconGlyph padlock is still found by PremiumGateFx.</summary>
public sealed class IconGlyphRenderTests
{
    private static void Platform()   // P45/P48: own Skia headless setup
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    /// <summary>Renders <paramref name="content"/> on black, returns BGRA pixels of the 48x48 box.</summary>
    private static byte[] Render(Control content, IBrush? inherited = null)
    {
        var host = new Border { Width = 48, Height = 48, Background = Brushes.Black, Child = content };
        if (inherited != null) host.SetValue(TextElement.ForegroundProperty, inherited);
        var w = new Window { Width = 48, Height = 48, Content = host, Background = Brushes.Black };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            using var frame = w.CaptureRenderedFrame()!;
            var buf = new byte[48 * 48 * 4];
            var mem = System.Runtime.InteropServices.Marshal.AllocHGlobal(buf.Length);
            try
            {
                frame.CopyPixels(new PixelRect(0, 0, 48, 48), mem, buf.Length, 48 * 4);
                System.Runtime.InteropServices.Marshal.Copy(mem, buf, 0, buf.Length);
                if (frame.Format == global::Avalonia.Platform.PixelFormats.Rgba8888)   // normalise to BGRA
                    for (int i = 0; i < buf.Length; i += 4) (buf[i], buf[i + 2]) = (buf[i + 2], buf[i]);
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(mem); }
            return buf;
        }
        finally { w.Close(); }
    }

    private static int Lit(byte[] px) => Enumerable.Range(0, px.Length / 4).Count(i => px[i * 4] + px[i * 4 + 1] + px[i * 4 + 2] > 120);
    private static int Diff(byte[] a, byte[] b) => Enumerable.Range(0, a.Length / 4).Count(i => Math.Abs(a[i * 4 + 1] - b[i * 4 + 1]) > 60);

    [Fact]
    public void FluentGlyphsDrawRealDistinctShapesInheritForegroundAndVary() => AvaloniaTestDispatcher.Run(() =>
    {
        Platform();
        var white = Brushes.White;
        var lockF = Render(new IconGlyph { Kind = IconKind.LockClosed, Variant = IconVariant.Filled, Size = 40 }, white);
        var lockR = Render(new IconGlyph { Kind = IconKind.LockClosed, Size = 40 }, white);
        var dismiss = Render(new IconGlyph { Kind = IconKind.Dismiss, Size = 40 }, white);
        var notdef = Render(new TextBlock
        {
            FontFamily = new FontFamily("avares://FluentIcons.Resources.Avalonia/Assets#Seagull Fluent Icons"),   // the package's font
            FontSize = 40, Text = "\uFFFF", Foreground = white,
        });

        Assert.True(Lit(lockF) > 150, $"filled lock lit {Lit(lockF)}");           // not blank
        Assert.True(Lit(lockR) > 60, $"regular lock lit {Lit(lockR)}");
        Assert.True(Lit(lockF) > Lit(lockR) + 60, $"Filled {Lit(lockF)} vs Regular {Lit(lockR)}");
        Assert.True(Diff(lockR, dismiss) > 60, "two kinds drew the same shape (tofu?)");
        Assert.True(Diff(lockF, notdef) > 60 && Diff(lockR, notdef) > 60, "icon equals the .notdef render");

        // Foreground inherits from an ancestor: red ancestor -> red pixels, no blue/green.
        var red = Render(new IconGlyph { Kind = IconKind.LockClosed, Variant = IconVariant.Filled, Size = 40 }, Brushes.Red);
        int reds = Enumerable.Range(0, red.Length / 4).Count(i => red[i * 4 + 2] > 200 && red[i * 4 + 1] < 60 && red[i * 4] < 60);
        Assert.True(reds > 150, $"red pixels {reds}");
    });

    [Fact]
    public void EmojiResolvesThroughTheMapSpiralDrawsAndUnmappedFallsBackToText() => AvaloniaTestDispatcher.Run(() =>
    {
        Platform();
        var g = new IconGlyph { Emoji = "🔒" };
        Assert.Equal(IconKind.LockClosed, g.Kind);
        Assert.Equal(IconVariant.Filled, g.Variant);
        Assert.False(g.IsFallback);
        Assert.Equal(global::Avalonia.Automation.AccessibilityView.Raw,
            global::Avalonia.Automation.AutomationProperties.GetAccessibilityView(g));

        var spiral = Render(new IconGlyph { Emoji = "🌀", Size = 40 }, Brushes.White);
        Assert.True(Lit(spiral) > 60, $"spiral lit {Lit(spiral)}");

        var unicorn = new IconGlyph { Emoji = "🦄" };
        Assert.True(unicorn.IsFallback);
        Assert.Equal("🦄", unicorn.GetVisualChildren().OfType<TextBlock>().Single().Text);
    });

    [Fact]
    public void IconLabelDrawsLeadingAndMirrorIconsAndStrBareStripsThem() => AvaloniaTestDispatcher.Run(() =>
    {
        Platform();
        var label = new IconLabel { Text = "⭐⭐ Hard ⭐" };
        var stars = label.Children.OfType<IconGlyph>().ToList();
        Assert.Equal(3, stars.Count);                                  // two leading + the mirror
        Assert.All(stars, g => Assert.Equal(IconKind.Star, g.Kind));
        Assert.Equal("Hard", label.Label.Text);

        var sys = IconLabel.ForKey("section_system");                  // en: "⚙ System"
        Assert.Equal(IconKind.Settings, sys.Children.OfType<IconGlyph>().Single().Kind);
        Assert.Equal(IconText.Bare(ConditioningControlPanel.Localization.Loc.Get("section_system")), sys.Label.Text);
        var tb = new TextBlock();
        tb.Bind(TextBlock.TextProperty, (global::Avalonia.Data.Binding)new ConditioningControlPanel.Avalonia.Localization.StrBareExtension("section_system").ProvideValue(null!));
        Assert.Equal(sys.Label.Text, tb.Text);
        Assert.DoesNotContain("⚙", tb.Text);
    });

    [Fact]
    public void PremiumGateFindsAnIconGlyphPadlock() => AvaloniaTestDispatcher.Run(() =>
    {
        Platform();
        Assert.True(PremiumGateFx.IsPadlock(new IconGlyph { Kind = IconKind.LockClosed }));
        Assert.True(PremiumGateFx.IsPadlock(new IconGlyph { Emoji = "🔒" }));
        Assert.False(PremiumGateFx.IsPadlock(new IconGlyph { Kind = IconKind.LockOpen }));
        Assert.True(PremiumGateFx.IsPadlock(new TextBlock { Text = "🔒" }));
    });

    private static double Lum(Color c)
    {
        static double Ch(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    [Fact]
    public void SemanticIconBrushesKeepThreeToOneOnTheAppSurfaces() => AvaloniaTestDispatcher.Run(() =>
    {
        Platform();
        Color Res(string key)
        {
            Assert.True(Application.Current!.TryGetResource(key, null, out var v), key);
            return v is Color c ? c : ((ISolidColorBrush)v!).Color;
        }
        foreach (var bg in new[] { "SurfaceBg", "PanelBg", "DarkerBg" })
            foreach (var key in IconBrushes.All)
            {
                double a = Lum(Res(key)), b = Lum(Res(bg));
                double ratio = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
                Assert.True(ratio >= 3.0, $"{key} on {bg}: {ratio:F2}:1 (WCAG 1.4.11 needs 3:1)");
            }
    });
}
