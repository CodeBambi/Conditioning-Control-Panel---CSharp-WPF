using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM gif-full reads a dealt GIF back off disk. The url comes from the host's own deal,
/// but the mapping is still fenced: only the two mapped origins, and never a path outside the root.
/// Plus the intensity setting's default and its round trip.
/// </summary>
public class BackRoomFxServicesTests
{
    private static readonly string Assets = Path.Combine(Path.GetTempPath(), "ccp-assets");
    private static readonly string Web = Path.Combine(Path.GetTempPath(), "ccp-web");

    [Fact]
    public void AssetsUrl_MapsIntoTheAssetsFolder_Unescaped()
        => Assert.Equal(Path.Combine(Assets, "images", "my gif.gif"),
            BackRoomFxServices.TryLocalPath("https://ccp.assets/images/my%20gif.gif", Assets, Web));

    [Fact]
    public void GameUrl_MapsIntoResourcesWeb()
        => Assert.Equal(Path.Combine(Web, "backroom", "stations", "slot", "fallback", "gif3.webp"),
            BackRoomFxServices.TryLocalPath("https://ccp.game/backroom/stations/slot/fallback/gif3.webp", Assets, Web));

    [Fact]
    public void AnimatedHintFragment_IsIgnored()
        => Assert.Equal(Path.Combine(Assets, "a.webp"),
            BackRoomFxServices.TryLocalPath("https://ccp.assets/a.webp#.gif", Assets, Web));

    [Theory]
    [InlineData("https://ccp.assets/..%2F..%2FWindows%2Fwin.ini")]
    [InlineData("https://ccp.assets/C:%5CWindows%5Cwin.ini")]
    [InlineData("https://evil.example/images/a.gif")]
    [InlineData("http://ccp.assets/images/a.gif")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("https://ccp.assets/")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_MapsToNothing(string? url)
        => Assert.Null(BackRoomFxServices.TryLocalPath(url, Assets, Web));

    [Fact]
    public void DotSegments_TheUriCollapses_StillLandInsideTheRoot()
    {
        var p = BackRoomFxServices.TryLocalPath("https://ccp.assets/%2E%2E/%2E%2E/secret.txt", Assets, Web);
        Assert.True(p == null || p.StartsWith(Assets + Path.DirectorySeparatorChar), p);
    }

    [Fact]
    public void Intensity_DefaultsToNormal_AndRoundTrips()
    {
        Assert.Equal(BackRoomFxIntensity.Normal, new AppSettings().BackRoomFxIntensity);
        Assert.Equal(BackRoomFxIntensity.Normal, JsonConvert.DeserializeObject<AppSettings>("{}")!.BackRoomFxIntensity);
        var json = JsonConvert.SerializeObject(new AppSettings { BackRoomFxIntensity = BackRoomFxIntensity.Calm });
        Assert.Equal(BackRoomFxIntensity.Calm, JsonConvert.DeserializeObject<AppSettings>(json)!.BackRoomFxIntensity);
    }

    [Fact]
    public void Intensity_OutOfRange_FallsBackToNormal()
        => Assert.Equal(BackRoomFxIntensity.Normal, new AppSettings { BackRoomFxIntensity = (BackRoomFxIntensity)9 }.BackRoomFxIntensity);
}
