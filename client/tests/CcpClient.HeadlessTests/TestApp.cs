using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(CcpClient.HeadlessTests.TestAppBuilder))]

namespace CcpClient.HeadlessTests;

/// <summary>
/// Minimal headless application: Fluent theme, the product's token dictionary, and the one
/// application-wide style that goes with it. The real <c>App</c> is NOT reused — it has no
/// parameterless constructor by design (AVLN3001, composition-root construction) and carries
/// lifetime wiring a test app must not have. Default headless drawing (fake backend, no pixels):
/// the spike asserts tree/layout/style/binding facts (draw-level), never rendered frames or
/// presentation facts.
///
/// <para><b>Why the token dictionary is here and not only in <c>App.axaml</c>.</b> Every surface
/// in this suite resolves its colour through <c>DynamicResource</c> now. A test application that
/// carried only <c>FluentTheme</c> would resolve none of those keys, hand every brush a null, and
/// red the whole suite for a reason that has nothing to do with any product defect — so the two
/// applications have to agree about the token layer or the suite stops measuring the product.
/// This mirrors <c>App.axaml</c> deliberately, and <c>ThemeTokenTests</c> holds the two in step by
/// reading both files rather than by anyone remembering to.</para>
/// </summary>
public class TestApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());

        Resources.MergedDictionaries.Add(new ResourceInclude((Uri?)null)
        {
            Source = new Uri("avares://CcpClient.Desktop/Themes/Ccp.axaml"),
        });

        // DynamicResourceExtension, not a direct lookup: ResourceDictionary's indexer reads only
        // its OWN entries and answers null for anything reached through a merged dictionary
        // (measured, 12.1.1), which would have handed the setter a null and left every window on
        // the platform default while every assertion about it still read plausibly.
        Styles.Add(new Style(x => x.OfType<Avalonia.Controls.Window>())
        {
            Setters =
            {
                new Setter(
                    Avalonia.Controls.Window.FontFamilyProperty,
                    new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("CcpFontFamily")),
            },
        });
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
