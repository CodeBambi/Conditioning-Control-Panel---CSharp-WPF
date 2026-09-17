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

    /// <summary>
    /// A dealt url is no longer guaranteed to be a picture: since the wall deal may carry Scrolller
    /// CLIPS (played by the page's own room/clip-source.js inside WebView2, which is Chromium), it can
    /// name a .webm or an .mp4. Every caller of the private LocalFile hands its path to WPF imaging,
    /// which cannot decode a video container at all, so an unguarded clip url is a visibly broken
    /// overlay rather than a still frame.
    ///
    /// <para>Today nothing can reach it: an fx message always names a station and the bridge keys its
    /// deals by station, so the room's deal - the only deal that carries clips - is the one deal no
    /// host effect ever draws from. This is guarded anyway, because that safety is a coincidence of two
    /// other files agreeing, and it is one line to stop depending on it.</para>
    /// </summary>
    [Theory]
    [InlineData("https://ccp.assets/.temp/ccp_temp_remote_abc.webm", false)]
    [InlineData("https://ccp.assets/.temp/ccp_temp_remote_abc.mp4", false)]
    [InlineData("https://ccp.assets/clip.m4v", false)]
    [InlineData("https://ccp.assets/loop.gif", true)]
    [InlineData("https://ccp.assets/.temp/ccp_temp_remote_abc.webp", true)]
    [InlineData("https://ccp.assets/still.PNG", true)]
    [InlineData("https://ccp.game/backroom/stations/slot/fallback/gif0.webp", true)]
    [InlineData("https://ccp.assets/no-extension", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAPictureIsHandedToAWpfOverlay(string? url, bool drawable)
        => Assert.Equal(drawable, BackRoomFxServices.IsDrawablePicture(url));

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
