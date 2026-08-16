using System;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE SPIRAL ROOM, actually built — the test a compile does not give you.
///
/// <para><b>Why this suite exists.</b> The FX pass (owner verdict 2026-08-16: "could use some FX, a
/// bolder text, maybe some little animation and flair") roughly doubled this view's markup: a hero
/// countdown made of two stacked TextBlocks bound to each other, an ember canvas, a splash with a
/// code-built spiral in it and a waiting panel that breathes. Every one of those is resolved when
/// the tree is BUILT, not when the XAML is compiled — a binding to a renamed element, an attached
/// Typography property that does not exist, an <c>x:Name</c> the code-behind reaches for and the
/// markup no longer declares all build perfectly clean and then throw the first time somebody opens
/// the tab. On the one night this feature runs, that is the whole feature.</para>
///
/// <para><b>Nothing here needs an <c>Application</c>, a dispatcher or a server.</b> The view wires
/// itself to the countdown service from <c>Loaded</c>, and a detached element that is measured and
/// arranged never raises Loaded — so this realizes the whole tree, resolves every binding, and
/// touches no service at all. That is deliberate: this suite is about the markup, and the state
/// machine over it is already a truth table in <see cref="SpiralRoomTests"/>.</para>
///
/// <para>It shares the app's one STA render thread for the reason
/// <see cref="CompanionWpfRenderCollection"/> documents at length: WPF caches resource dictionaries
/// per pack URI and hands the same instances to every consumer, and those take thread affinity from
/// whoever realizes them first.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class SpiralRoomRenderTests
{
    /// <summary>The fuse's gold. Restated here rather than imported so that a test asserting "the
    /// accent is untouchable" cannot be made to pass by something changing the constant.</summary>
    private static readonly Color FuseGold = Color.FromRgb(0xE0, 0xB0, 0x52);

    private static void Realize(Action<SpiralTabView> body, double width = 1200, double height = 620)
        => WpfRenderHarness.OnStaThread(() =>
        {
            var view = new SpiralTabView();
            view.Measure(new Size(width, height));
            view.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
            view.UpdateLayout();
            body(view);
        });

    /// <summary>
    /// THE WHOLE TREE BUILDS. If the markup and the code-behind have drifted apart in any of the
    /// ways a compiler cannot see, this is where it surfaces.
    /// </summary>
    [Fact]
    public void TheRoomRealizes()
        => Realize(view => Assert.True(view.ActualWidth > 0, "the spiral room arranged to nothing"));

    /// <summary>
    /// THE HERO IS THE COUNTDOWN. The blurred duplicate behind the digits carries the same string
    /// and the same size by binding — a glow that had to be retyped alongside the readout would go
    /// out of step within one tick of somebody forgetting, and a stale ghost behind live digits is
    /// the ugliest possible failure of this particular idea.
    /// </summary>
    [Fact]
    public void TheGlowFollowsTheDigits()
        => Realize(view =>
        {
            var digits = (System.Windows.Controls.TextBlock)view.FindName("FogDigits")!;
            var glow = (System.Windows.Controls.TextBlock)view.FindName("FogDigitsGlow")!;

            digits.Text = "2d 07:14:03";
            digits.FontSize = 56;
            view.UpdateLayout();

            Assert.Equal("2d 07:14:03", glow.Text);
            Assert.Equal(56, glow.FontSize);

            // ...and it follows the OTHER readout too, which is a different size on purpose: the
            // "any moment now" phrase is a sentence, and at digit size it wraps.
            digits.Text = "any moment now.";
            digits.FontSize = 30;
            view.UpdateLayout();

            Assert.Equal("any moment now.", glow.Text);
            Assert.Equal(30, glow.FontSize);
        });

    /// <summary>
    /// ACCENT IS UNTOUCHABLE (DescentFuseChrome). Every element the countdown is MADE of paints in
    /// the app's own gold, resolved here from the realized tree rather than from the file — a
    /// DynamicResource that happened to resolve to gold today would pass a source scrape and then
    /// turn pink the moment a mod loaded.
    /// </summary>
    [Fact]
    public void TheFuseSurfacesAreLiteralGold()
        => Realize(view =>
        {
            foreach (var name in new[] { "FogEyebrow", "FogDigits", "FogDigitsGlow", "SplashLine", "WaitingLine" })
            {
                var element = view.FindName(name);
                var brush = element switch
                {
                    System.Windows.Controls.TextBlock tb => tb.Foreground as SolidColorBrush,
                    _ => null,
                };
                Assert.True(brush != null, $"{name} is not a TextBlock with a solid brush");
                Assert.Equal(FuseGold, brush!.Color);
            }

            var hairline = (System.Windows.Shapes.Rectangle)view.FindName("FogHairline")!;
            Assert.Equal(FuseGold, ((SolidColorBrush)hairline.Fill).Color);
        });

    /// <summary>
    /// THE SPLASH HAS A SPIRAL IN IT. The glyph is built as geometry in the code-behind (the shape
    /// is arithmetic, and arithmetic is easier to re-tune than a path mini-language), so an empty
    /// Data is a splash that spins nothing — which builds, runs, and looks like a bug.
    /// </summary>
    [Fact]
    public void TheSplashGlyphIsDrawn()
        => Realize(view =>
        {
            var glyph = (System.Windows.Shapes.Path)view.FindName("SplashGlyph")!;
            Assert.NotNull(glyph.Data);
            Assert.False(glyph.Data!.IsEmpty(), "the splash's spiral has no geometry in it");
            Assert.True(glyph.Data.IsFrozen, "the spiral geometry should be frozen - it never changes");
        });

    /// <summary>
    /// THE ROOM STARTS WITH NOTHING SHOWING. Every one of the four surfaces is Collapsed until a
    /// state is applied, which is what lets the view be constructed on a fuse-dark install (every
    /// install on today's server) and measure as an empty rectangle.
    /// </summary>
    [Fact]
    public void EverySurfaceStartsCollapsed()
        => Realize(view =>
        {
            foreach (var name in new[] { "FogCopy", "EmbedHost", "SpiralSplash", "WaitingPanel", "EmberHost" })
            {
                var element = (UIElement)view.FindName(name)!;
                Assert.Equal(Visibility.Collapsed, element.Visibility);
            }
        });

    /// <summary>
    /// THE DRIFT FIELDS EXIST BUT ARE DARK. Both canvases are populated in the constructor (so
    /// there is no allocation on the frame the state changes) and every speck sits at zero opacity
    /// until its field is started.
    /// </summary>
    [Fact]
    public void TheDriftFieldsAreBuiltAndInvisible()
        => Realize(view =>
        {
            foreach (var name in new[] { "EmberHost", "WaitingMotes" })
            {
                var canvas = (System.Windows.Controls.Canvas)view.FindName(name)!;
                Assert.True(canvas.Children.Count > 0, $"{name} has no specks in it");
                foreach (UIElement speck in canvas.Children)
                    Assert.Equal(0, speck.Opacity);
            }
        });
}
