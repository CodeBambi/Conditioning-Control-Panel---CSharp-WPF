using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Defaults that a settings.json without the key must land on. These are release-visible: the
/// browser video engine shipped OFF after v6.6.3 and was never released, so 6.7 flips the default
/// ON and EVERY install — fresh or upgrading from 6.6.3 — has to deserialize to true. A regression
/// here is silent (the app just quietly keeps using LibVLC), which is exactly what a test is for.
///
/// The serializer settings mirror SettingsService.Load so this exercises the real load path's
/// semantics, not a friendlier default one.
/// </summary>
public class AppSettingsDefaultsTests
{
    private static readonly JsonSerializerSettings LoaderSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Error = (_, args) => { args.ErrorContext.Handled = true; }
    };

    private static AppSettings Load(string json)
        => JsonConvert.DeserializeObject<AppSettings>(json, LoaderSettings)!;

    [Fact]
    public void FreshInstall_BrowserVideoEngineOn()
        => Assert.True(new AppSettings().BrowserVideoEngineEnabled);

    [Fact]
    public void EmptyDocument_BrowserVideoEngineOn()
        => Assert.True(Load("{}").BrowserVideoEngineEnabled);

    [Fact]
    public void UpgraderWithoutTheKey_BrowserVideoEngineOn()
    {
        // Shape of a real 6.6.3 settings.json: plenty of unrelated members, no
        // BrowserVideoEngineEnabled anywhere (the key was added after that release).
        const string json = """
        {
          "Welcomed": true,
          "LastSeenVersion": "6.6.3",
          "DualMonitorEnabled": true,
          "VideoBlurredBackgroundEnabled": false,
          "ModPickerShown": true
        }
        """;

        var settings = Load(json);

        Assert.True(settings.BrowserVideoEngineEnabled);
        // Sanity: the rest of the document still won, so this isn't passing because the
        // whole load fell back to a default-constructed object.
        Assert.Equal("6.6.3", settings.LastSeenVersion);
        Assert.False(settings.VideoBlurredBackgroundEnabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitValue_AlwaysWinsOverTheDefault(bool persisted)
    {
        var json = $"{{ \"BrowserVideoEngineEnabled\": {(persisted ? "true" : "false")} }}";
        Assert.Equal(persisted, Load(json).BrowserVideoEngineEnabled);
    }

    [Fact]
    public void ModPickerShown_DefaultsFalse_SoUpgradersStillGetTheOffer()
        => Assert.False(Load("{}").ModPickerShown);
}
