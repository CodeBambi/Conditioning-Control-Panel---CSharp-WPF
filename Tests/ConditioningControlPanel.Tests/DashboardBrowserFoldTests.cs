using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Home dashboard's browser fold (owner ask 2026-09-12). The fold itself is WPF layout and
/// lives in MainWindow.DashboardFold.cs; what is testable, and what actually matters on release
/// day, is the default: the card is OPEN until somebody clicks the chevron, so no upgrader's
/// dashboard changes shape on its own.
///
/// The serializer settings mirror SettingsService.Load, so this exercises the real load path's
/// semantics rather than a friendlier default one.
/// </summary>
public class DashboardBrowserFoldTests
{
    private static readonly JsonSerializerSettings LoaderSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Error = (_, args) => { args.ErrorContext.Handled = true; }
    };

    private static AppSettings Load(string json)
        => JsonConvert.DeserializeObject<AppSettings>(json, LoaderSettings)!;

    [Fact]
    public void FreshInstall_BrowserIsNotFolded()
        => Assert.False(new AppSettings().DashboardBrowserCollapsed);

    [Fact]
    public void EmptyDocument_BrowserIsNotFolded()
        => Assert.False(Load("{}").DashboardBrowserCollapsed);

    [Fact]
    public void UpgraderWithoutTheKey_BrowserIsNotFolded()
    {
        // The shape of a settings.json written before the fold existed: plenty of unrelated
        // members, no DashboardBrowserCollapsed anywhere.
        const string json = """
        {
          "Welcomed": true,
          "LastSeenVersion": "6.9.3",
          "DashboardToggleHintUses": 2,
          "rail_favorites": [ "tab.deeper" ]
        }
        """;

        var settings = Load(json);

        Assert.False(settings.DashboardBrowserCollapsed);
        // Sanity: the rest of the document still won, so this is not passing because the whole
        // load fell back to a default-constructed object.
        Assert.Equal("6.9.3", settings.LastSeenVersion);
        Assert.Equal(2, settings.DashboardToggleHintUses);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSavedChoiceRoundTrips(bool folded)
    {
        var json = JsonConvert.SerializeObject(new AppSettings { DashboardBrowserCollapsed = folded });
        Assert.Equal(folded, Load(json).DashboardBrowserCollapsed);
    }
}
