using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Nav polish waves 2/3/7/9 through the shell (twin of WPF SectionTabStripTests /
/// NavPolishPaintTests): the strip's tray, per-tab tints, raised plates with a bevelled rim, the
/// lit pill's ring and the sliding glowing hue fill, and the per-section page wash and ink.
/// </summary>
public sealed class SectionPaintShellTests
{
    private static Color Solid(IBrush? b) => Assert.IsAssignableFrom<ISolidColorBrush>(b).Color;

    [Fact]
    public async Task SectionPagesWearTheirHueOnTheStripTheWashAndTheInk()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            CoreSettings.Current.MotionLevel = MotionLevel.Off;   // restored by the isolation hook
            MainShellWindow? w = null;
            try
            {
                w = new MainShellWindow();
                w.Show();
                w.ShowTab("presets");
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var strip = w.Named<SectionTabStrip>("SectionStrip")!;
                var section = NavSections.SectionForTab("presets");
                var hue = NavStripRules.Accent(section);
                var all = NavStripTable.Pills(section).Select(t => t.Key).ToList();

                // The tray: deep ink under the hue, shaded along its top.
                Assert.Equal(NavStripRules.TrackFill(hue), Solid(strip.Track.Background));
                Assert.IsType<LinearGradientBrush>(strip.Track.BorderBrush);

                // Every pill wears its own near-hue, indexed as WPF builds them.
                var studio = strip.Part("studio")!;
                var ramp = strip.Part("ramp")!;
                Assert.Equal(NavStripRules.TabTint(section, all.IndexOf("studio"), all.Count), studio.Tint);
                Assert.Equal(NavStripRules.TabTint(section, all.IndexOf("ramp"), all.Count), ramp.Tint);
                Assert.NotEqual(studio.Tint, ramp.Tint);

                // A rest pill: a raised plate (lit top) inside a bevelled 1.5 px rim, hue-lifted label.
                var plate = Assert.IsType<LinearGradientBrush>(studio.Face.Background);
                Assert.Equal(NavStripRules.WithAlpha(studio.Tint, NavStripRules.RestFillAlpha + NavStripRules.PlateLift), plate.GradientStops[0].Color);
                var rim = Assert.IsType<LinearGradientBrush>(studio.Face.BorderBrush);
                Assert.Equal(4, rim.GradientStops.Count);
                Assert.Equal(1.5, studio.Face.BorderThickness.Top);
                Assert.Equal(NavStripRules.RestTextOn(hue, studio.Tint), Solid(studio.Label.Foreground));

                // The lit pill: the plate steps aside for the solid hue fill under it, a 2 px gloss
                // ring whose extra half pixel comes out of the padding, ink chosen for contrast.
                var presets = strip.Part("presets")!;
                Assert.Same(Brushes.Transparent, presets.Face.Background);
                Assert.Equal(2, presets.Face.BorderThickness.Top);
                Assert.Equal(NavStripRules.PillPadding - 0.5, presets.Face.Padding.Left);
                Assert.Equal(NavStripRules.ActiveTextOn(hue), Solid(presets.Label.Foreground));
                Assert.True(strip.ActiveFill.IsVisible);
                Assert.Equal(hue, Solid(strip.ActiveFill.Background));
                Assert.Equal(presets.Pill.Bounds.X, strip.ActiveFill.Margin.Left);
                Assert.Equal(presets.Pill.Bounds.Width, strip.ActiveFill.Width);
                Assert.Equal(0, strip.ActiveFill.BoxShadow.Count);   // Motion Off: no glow

                // A click moves the fill to the new pill (instantly at Motion Off).
                ramp.Pill.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                w.UpdateLayout();
                Assert.Equal(ramp.Pill.Bounds.X, strip.ActiveFill.Margin.Left);
                Assert.IsType<LinearGradientBrush>(presets.Face.Background);

                // The HUD band (WPF MainWindow.xaml:805): header + XP rows on 20% black, rows 1-3.
                var hud = w.Named<Border>("HudBand")!;
                Assert.Equal(Color.Parse("#33000000"), Solid(hud.Background));
                Assert.Equal((1, 3), (Grid.GetRow(hud), Grid.GetRowSpan(hud)));

                // The page wash and ink follow the section.
                var wash = w.Named<Border>("SectionWashFill")!;
                Assert.Equal(NavStripRules.WithAlpha(hue, 0x24 / 255.0), Solid(wash.Background));
                w.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                var play = NavStripRules.Accent(NavSections.SectionForTab("play"));
                Assert.NotEqual(hue, play);
                Assert.Equal(NavStripRules.WithAlpha(play, 0x24 / 255.0), Solid(wash.Background));
                Assert.Equal(NavStripRules.WithAlpha(play, 0x59 / 255.0), Solid(w.Named<Border>("SectionWashLine")!.Background));
                Assert.True(Application.Current!.TryFindResource("SectionInkBrush", out var ink));
                Assert.Equal(NavStripRules.Ink(NavSections.SectionForTab("play")), Solid((IBrush)ink!));
                // The window edge too (WPF SectionEdge.cs): the line at 0xE6, the glow band
                // brightness-balanced, the lift hidden at Motion Off.
                Assert.Equal(NavStripRules.WithAlpha(play, 0xE6 / 255.0), Solid(w.Named<Border>("GlassWindowEdge")!.BorderBrush));
                var band = w.Named<Panel>("SectionEdgeGlow")!.Children.OfType<Border>().ToList();
                Assert.Equal(4, band.Count);
                Assert.All(band, b => Assert.Equal(NavStripRules.WithAlpha(play, MainShellWindow.SectionEdgeGlowAlpha(play) / 255.0), Solid(b.Background)));
                Assert.False(w.Named<Border>("SectionEdgeLiftTop")!.IsVisible);

                // Motion on: the lit pill glows in the section hue.
                CoreSettings.Current.MotionLevel = MotionLevel.Full;
                w.ShowTab("presets");
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                Assert.Equal(NavStripRules.WithAlpha(hue, NavStripRules.ActiveGlowOpacity), strip.ActiveFill.BoxShadow[0].Color);
                Assert.True(w.Named<Border>("SectionEdgeLiftTop")!.IsVisible);
            }
            finally { w?.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void HomeInkIsTheAuthoredInkAndActiveTextReadsOnEveryHue()
    {
        // Colors.xaml authors Home's ink (the TestIsolation hook drops overrides back to it).
        Assert.Equal(Color.Parse("#FFCBB9FC"), NavStripRules.Ink(NavSections.Home));
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            Assert.True(NavStripRules.Contrast(NavStripRules.ActiveTextOn(hue), hue) >= 4.5, s.Key);
        }
    }
}
