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
    [AvaloniaTheory]
    [InlineData("SystemAccentColor", "#FFFF69B4")]
    [InlineData("SystemAccentColorLight1", "#FFFF85C2")]
    [InlineData("SystemAccentColorLight2", "#FFFFA1D0")]
    [InlineData("SystemAccentColorLight3", "#FFFFBDDE")]
    [InlineData("SystemAccentColorDark1", "#FFD9599A")]
    [InlineData("SystemAccentColorDark2", "#FFB34A80")]
    [InlineData("SystemAccentColorDark3", "#FF8C3A66")]
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
        Assert.Equal(Color.Parse("#FFFF69B4"), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
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
        var original = Color.Parse("#FFFF1493");

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
}
