using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The two facts about the token layer that need a live Avalonia application to state at all: what
/// the accent family RESOLVES to, and whether replacing a colour key repaints the brushes derived
/// from it.
///
/// <para><b>What these do not show.</b> No composited pixel. A headless application resolves
/// resources through the same lookup a real one does, so "which brush a control would paint with"
/// is a real answer — but "what the user sees" is not, and neither fact here is offered as
/// presentation evidence.</para>
/// </summary>
public class ThemeTokenHeadlessTests
{
    /// <summary>
    /// <b>Nothing in this product resolves an accent from the operating system any more.</b>
    ///
    /// <para>Before the token layer, every unstyled Fluent control took its accent from Fluent's
    /// <c>SystemAccentColor</c>, which Avalonia seeds from the platform — on Windows, from
    /// <c>HKCU\...\Explorer\Accent\AccentColorMenu</c>. Measured on a port capture that was
    /// #0078D4 at 3,500 exact-hex pixels and absent entirely from the same capture of the shipping
    /// product: the most-touched controls in a magenta application were Windows blue, and a
    /// DIFFERENT colour on the next machine.</para>
    ///
    /// <para>All seven keys are asserted, not just the base. Fluent derives hover, pressed and
    /// disabled states from the Light1..3 / Dark1..3 shades, and a base override that left the
    /// shades to the platform calculator would have fixed the resting state while leaving every
    /// interaction state on the machine's own blue — the harder defect to see and the one that
    /// only shows up under a pointer.</para>
    ///
    /// <para>The last assertion is the one that proves the override actually reaches Fluent rather
    /// than merely sitting in a dictionary: <c>SystemControlHighlightAccentBrush</c> is FLUENT'S
    /// OWN brush, built inside the theme over its own <c>SystemAccentColor</c>, and it comes back
    /// carrying the product's accent.</para>
    /// </summary>
    /// <remarks>
    /// The seven values are the CCP Default theme's, not the token file's: the base is the mod's
    /// own <c>AccentColor</c> and the six shades are upstream's <c>LightenColor</c> and
    /// <c>DarkenColor</c> at 0.15 / 0.30 / 0.45 (<c>CcpTheme.Tokens</c>). They are written out
    /// rather than recomputed here on purpose — a fact that recomputed the thing it is checking
    /// would pass whatever the arithmetic did.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("SystemAccentColor", "#FFE84393")]
    [InlineData("SystemAccentColorLight1", "#FFEB5FA3")]
    [InlineData("SystemAccentColorLight2", "#FFEE7BB3")]
    [InlineData("SystemAccentColorLight3", "#FFF297C3")]
    [InlineData("SystemAccentColorDark1", "#FFC5387C")]
    [InlineData("SystemAccentColorDark2", "#FFA22E66")]
    [InlineData("SystemAccentColorDark3", "#FF7F2450")]
    public void FluentsAccentFamilyResolvesToTheProductAccentAndNeverToThePlatforms(string key, string expected)
    {
        var app = Application.Current!;

        Assert.True(app.TryFindResource(key, ThemeVariant.Dark, out var value),
            $"'{key}' resolves to nothing — Fluent would fall back to the platform accent");
        Assert.Equal(Color.Parse(expected), Assert.IsType<Color>(value));

        // The Windows personalisation blue this replaced. Named rather than merely excluded by the
        // equality above, so a future edit that reintroduces it fails against the actual defect.
        Assert.NotEqual(Color.Parse("#FF0078D4"), (Color)value!);
    }

    /// <summary>
    /// <b>Fluent's own derived brush carries the product accent</b>, which is what makes the seven
    /// colour keys above a fix rather than seven unused dictionary entries.
    /// </summary>
    [AvaloniaFact]
    public void FluentsOwnAccentBrushCarriesTheProductAccent()
    {
        var app = Application.Current!;

        Assert.True(
            app.TryFindResource("SystemControlHighlightAccentBrush", ThemeVariant.Dark, out var brush),
            "Fluent 12.1.1 no longer publishes SystemControlHighlightAccentBrush — the accent "
            + "override needs re-pointing at whatever replaced it");
        Assert.Equal(Color.Parse("#FFE84393"), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
    }

    /// <summary>
    /// <b>Replacing a colour key repaints the brushes derived from it, live, on the brush instance
    /// the tree already holds.</b>
    ///
    /// <para>This is the structural question the token layer exists to answer, and it is answered
    /// by measurement rather than by reading the shape. The shipping product re-themes at runtime:
    /// <c>RefreshThemeAwareElements</c> assigns into <c>Application.Current.Resources</c> by key
    /// and everything downstream repaints (WPF MainWindow/MainWindow.xaml.cs:1618-1682). This
    /// fact shows the port's dictionary behaves the same way — so mod-driven re-theming, which is
    /// deliberately NOT built here, is a small feature over this file rather than a second sweep
    /// of 225 sites.</para>
    ///
    /// <para>The restore at the end is not politeness. The headless application is shared across
    /// the assembly, so a fact that left a token green would recolour every later fact's tree.</para>
    /// </summary>
    [AvaloniaFact]
    public void ReplacingAColourKeyRepaintsEveryBrushDerivedFromIt()
    {
        var app = Application.Current!;

        // The CCP Default theme's AccentDarkColor, which is what the token really holds at runtime
        // now — TestApp applies the theme over the seed exactly as App does. The seed's #FFFF1493
        // is the value in Themes/Ccp.axaml and is NOT what a lookup returns.
        var original = Color.Parse("#FFB83078");

        Assert.True(app.TryFindResource("ShellAccentBrush", ThemeVariant.Dark, out var found));
        var brush = Assert.IsType<SolidColorBrush>(found);
        Assert.Equal(original, brush.Color);

        try
        {
            app.Resources["ShellAccent"] = Color.Parse("#FF00FF00");

            // The SAME instance, not a fresh lookup: a tree that already holds this brush repaints
            // with it. A re-theme that only served new colours to new lookups would leave every
            // window that was already open on the old palette.
            Assert.Equal(Color.Parse("#FF00FF00"), brush.Color);
        }
        finally
        {
            app.Resources["ShellAccent"] = original;
        }

        Assert.Equal(original, brush.Color);
    }

    /// <summary>
    /// <b>What a LOOKUP returns is the theme's value and not the token file's</b>, which is the one
    /// thing about this mechanism that no source-level fact can state.
    ///
    /// <para><c>Themes/Ccp.axaml</c> is a design-time seed — every ground in it is the shipping
    /// product's own <c>Resources/Theme/Colors.xaml</c> line, and that file is a design-time seed
    /// too. The skin over it is the built-in mod, applied in <c>App.Initialize</c> before any
    /// window exists. So a surface that binds <c>PanelBgBrush</c> resolves the MOD's panel colour,
    /// and the seed's value is not reachable from a running application at all. Both are asserted:
    /// the seed is named explicitly, because "the theme was applied" and "the theme happens to
    /// agree with the seed" are different facts and only one of them is this one.</para>
    ///
    /// <para><b>What it does not show.</b> Nothing composited. A resolved brush is what a control
    /// WOULD paint with; the headed <c>studio-dial/live</c> capture is what says it did.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData("DarkerBg", "#FF08080C", "#FF121220")]
    [InlineData("SurfaceBg", "#FF0C0C13", "#FF181830")]
    [InlineData("PanelBg", "#FF11111A", "#FF1C1C35")]
    [InlineData("PanelAccent", "#FF34343C", "#FF2E2E4A")]
    [InlineData("PanelAccentHover", "#FF4C4C53", "#FF3A3A5C")]
    [InlineData("PinkColor", "#FFE84393", "#FFFF69B4")]
    [InlineData("ShellAccent", "#FFB83078", "#FFFF1493")]
    [InlineData("ShellAccentBright", "#FFFF6FB5", "#FFFF8FAF")]
    public void AThemedKeyResolvesToTheSkinAndNotToTheSeedItWasPaintedOver(
        string key, string skin, string seed)
    {
        var app = Application.Current!;

        Assert.True(app.TryFindResource(key, ThemeVariant.Dark, out var value), $"'{key}' resolves to nothing");
        Assert.Equal(Color.Parse(skin), Assert.IsType<Color>(value));
        Assert.NotEqual(Color.Parse(seed), (Color)value!);
    }

    /// <summary>
    /// <b>And the keys a mod theme does not supply keep their seed value.</b> The asymmetry is
    /// upstream's, and it is the half that is easy to get wrong by being thorough: a port that
    /// re-derived every colour from the accent would look plausible and would stop matching the
    /// product it was ported from. Measured on the shipping product, <c>ElevatedSurface</c>'s seed
    /// value <c>#222240</c> is 5.13% of its window.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("ElevatedSurface", "#FF222240")]
    [InlineData("SeatBg", "#FF1F1F3A")]
    [InlineData("TextLight", "#FFF0F0F5")]
    [InlineData("TextMuted", "#FFA0A0BC")]
    [InlineData("TextDim", "#FF7A7A94")]
    public void AKeyNoModThemeSuppliesStillResolvesToItsSeed(string key, string seed)
    {
        var app = Application.Current!;

        Assert.True(app.TryFindResource(key, ThemeVariant.Dark, out var value), $"'{key}' resolves to nothing");
        Assert.Equal(Color.Parse(seed), Assert.IsType<Color>(value));
    }
}
