using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(CcpClient.HeadlessTests.TestAppBuilder))]

namespace CcpClient.HeadlessTests;

/// <summary>
/// Minimal headless application (SP-008): Fluent theme only. The real <c>App</c> is NOT
/// reused — it has no parameterless constructor by design (AVLN3001, composition-root
/// construction) and carries lifetime wiring a test app must not have. Default headless
/// drawing (fake backend, no pixels): the spike asserts tree/layout/style/binding facts
/// (draw-level), never rendered frames or presentation facts.
/// </summary>
public class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
