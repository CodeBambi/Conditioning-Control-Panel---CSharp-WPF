using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM's own options (CONTRACT 10.13.C, 10.14): the picture source, the niche list and the
/// three audio levels, all of them travelling on the one <c>room-option</c> wire.
///
/// <para>Two properties this file exists to hold. First, the wire is shape-strict in both directions:
/// the page may press a button, never widen the protocol, so anything off the whitelist reads as null
/// rather than as a default. Second, the HOST owns the niche list. The page presses once per niche and
/// reads the answer off the next settings frame, so the two cannot disagree about what is selected -
/// which is why there is no list on the wire at all.</para>
/// </summary>
public class BackRoomRoomOptionTests
{
    private static BackRoomBridge.RoomOption? Read(string key, JToken value)
        => BackRoomBridge.ReadRoomOption(new JObject { ["type"] = "room-option", ["key"] = key, ["value"] = value });

    private static AppSettings Apply(AppSettings s, string key, JToken value)
    {
        var option = Read(key, value);
        Assert.NotNull(option);
        BackRoomHostService.ApplyRoomOption(s, option!);
        return s;
    }

    /* ------------------------------------------------------------------- the picture source */

    [Theory]
    [InlineData("auto")]
    [InlineData("local")]
    [InlineData("online")]
    [InlineData("mixed")]
    [InlineData("bundled")]
    public void MediaSource_TakesEveryWhitelistedValue(string value)
    {
        var s = Apply(new AppSettings(), "mediaSource", value);
        Assert.Equal(value, s.BackRoomMediaSource);
    }

    [Theory]
    [InlineData("scrolller")]     // the label, not the value
    [InlineData("LOCAL")]         // the whitelist is case-sensitive on purpose
    [InlineData("")]
    [InlineData("../../etc")]
    public void MediaSource_RefusesAnythingElse(string value)
        => Assert.Null(Read("mediaSource", value));

    [Fact]
    public void MediaSource_RefusesANumberAndABoolean()
    {
        Assert.Null(Read("mediaSource", 1));
        Assert.Null(Read("mediaSource", true));
    }

    /* --------------------------------------------------------------------- invert camera (2026-09-18) */

    [Fact]
    public void InvertLook_IsASwitchLikeTunnelAndMelt()
    {
        Assert.False(new AppSettings().BackRoomInvertLook);   // off by default: the drag moves the camera until the player says otherwise
        Assert.True(Apply(new AppSettings(), "invertLook", true).BackRoomInvertLook);
        Assert.False(Apply(new AppSettings { BackRoomInvertLook = true }, "invertLook", false).BackRoomInvertLook);
        Assert.Null(Read("invertLook", "true"));
        Assert.Null(Read("invertLook", 1));
    }

    /* --------------------------------------------------------------------- the three levels */

    [Theory]
    [InlineData("subVolume")]
    [InlineData("sfxVolume")]
    [InlineData("musicVolume")]
    public void Levels_Take0To100(string key)
    {
        foreach (var level in new[] { 0, 1, 50, 99, 100 })
            Assert.Equal(level, Read(key, level)!.Level);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public void Levels_RefuseAnythingOutsideTheRange(int level)
        => Assert.Null(Read("subVolume", level));

    [Fact]
    public void Levels_RefuseAStringAndAFraction()
    {
        // "50" is the classic bug: a page that stringifies its slider must be told, not quietly obeyed.
        Assert.Null(Read("sfxVolume", "50"));
        Assert.Null(Read("sfxVolume", 50.5));
    }

    [Fact]
    public void Levels_LandOnTheRoomsOwnSettings_NotTheApps()
    {
        var s = new AppSettings { MasterVolume = 80, SubAudioVolume = 70 };
        Apply(s, "subVolume", 25);
        Apply(s, "sfxVolume", 60);
        Apply(s, "musicVolume", 5);
        Assert.Equal((25, 60, 5), (s.BackRoomSubVolume, s.BackRoomSfxVolume, s.BackRoomMusicVolume));
        // The app's own volumes are untouched: the room is not a second writer on them.
        Assert.Equal((80, 70), (s.MasterVolume, s.SubAudioVolume));
    }

    /* ----------------------------------------------------------------------- the niche list */

    [Theory]
    [InlineData("EroticHypnosis")]
    [InlineData("bambi_sleep")]
    [InlineData("ab")]
    public void Niche_AcceptsWhatRedditAllows(string name)
        => Assert.Equal(name, Read("mediaSubAdd", name)!.Text);

    [Theory]
    [InlineData("a")]                       // too short
    [InlineData("has space")]
    [InlineData("r/EroticHypnosis")]        // the room strips the prefix before it sends
    [InlineData("../../../secrets")]
    [InlineData("naughty-dash")]
    [InlineData("")]
    public void Niche_RefusesAnythingElse(string name)
    {
        Assert.Null(Read("mediaSubAdd", name));
        Assert.Null(Read("mediaSubRemove", name));
        Assert.Null(Read("mediaSubToggle", name));
    }

    [Fact]
    public void Niche_RefusesANameLongerThanFortyCharacters()
        => Assert.Null(Read("mediaSubAdd", new string('a', 41)));

    [Fact]
    public void Niche_AddsUpToTheCapAndThenStops()
    {
        var s = new AppSettings();
        for (var i = 0; i < AppSettings.BackRoomMediaSubCap + 3; i++) Apply(s, "mediaSubAdd", "niche_" + i);
        Assert.Equal(AppSettings.BackRoomMediaSubCap, s.BackRoomMediaSubs.Count);
        Assert.Equal("niche_0", s.BackRoomMediaSubs[0]);
    }

    [Fact]
    public void Niche_IsOneCommunityWhateverItsCase()
    {
        var s = new AppSettings();
        Apply(s, "mediaSubAdd", "EroticHypnosis");
        Apply(s, "mediaSubAdd", "erotichypnosis");
        Assert.Single(s.BackRoomMediaSubs);
        Apply(s, "mediaSubRemove", "EROTICHYPNOSIS");
        Assert.Empty(s.BackRoomMediaSubs);
    }

    [Fact]
    public void Niche_SwitchedOffKeepsItsPlaceInTheList()
    {
        // The owner's ruling: a saved disabled niche stays visible so it can be switched back on.
        var s = new AppSettings();
        Apply(s, "mediaSubAdd", "hypno");
        Apply(s, "mediaSubAdd", "bimbo");
        Apply(s, "mediaSubToggle", "hypno");
        Assert.Equal(new[] { "hypno", "bimbo" }, s.BackRoomMediaSubs);
        Assert.Equal(new[] { "hypno" }, s.BackRoomMediaSubsOff);

        Apply(s, "mediaSubToggle", "hypno");
        Assert.Empty(s.BackRoomMediaSubsOff);
    }

    [Fact]
    public void Niche_RemovedLosesItsDisabledFlagToo()
    {
        var s = new AppSettings();
        Apply(s, "mediaSubAdd", "hypno");
        Apply(s, "mediaSubToggle", "hypno");
        Apply(s, "mediaSubRemove", "hypno");
        Assert.Empty(s.BackRoomMediaSubs);
        // Otherwise the name sits in the disabled list forever, shown by nothing and cleared by nothing.
        Assert.Empty(s.BackRoomMediaSubsOff);
    }

    [Fact]
    public void Niche_ToggleIgnoresANameThatIsNotInTheList()
    {
        var s = new AppSettings();
        Apply(s, "mediaSubToggle", "neverAdded");
        Assert.Empty(s.BackRoomMediaSubsOff);
    }

    /* ------------------------------------------------------------- what the page is told back */

    [Fact]
    public void MediaWire_ResolvesAutoAgainstTheApp_AndRefusesRemoteWithoutConsent()
    {
        var s = new AppSettings { BackRoomMediaSource = "auto", MediaSource = "online" };
        Assert.False(s.HasRemoteMediaConsent);
        Assert.Equal("local", BackRoomHostService.EffectiveMediaSource(s));

        // Asking for it outright does not get past consent either: one place decides, not the deal.
        s.BackRoomMediaSource = "online";
        Assert.Equal("local", BackRoomHostService.EffectiveMediaSource(s));

        s.RemoteMediaConsented = true;
        Assert.Equal("online", BackRoomHostService.EffectiveMediaSource(s));

        s.BackRoomMediaSource = "auto";
        s.MediaSource = "mixed";
        Assert.Equal("mixed", BackRoomHostService.EffectiveMediaSource(s));
    }

    [Fact]
    public void MediaWire_CarriesTheListTheCapAndTheConsentSoThePickerCanPaintItself()
    {
        var s = new AppSettings { BackRoomMediaSource = "bundled", RemoteMediaConsented = true };
        BackRoomHostService.ApplyRoomOption(s, Read("mediaSubAdd", "hypno")!);
        BackRoomHostService.ApplyRoomOption(s, Read("mediaSubToggle", "hypno")!);

        var wire = JObject.FromObject(BackRoomHostService.MediaWire(s));
        Assert.Equal("bundled", (string?)wire["source"]);
        Assert.Equal("bundled", (string?)wire["effective"]);
        Assert.Equal(new[] { "hypno" }, wire["subs"]!.ToObject<string[]>());
        Assert.Equal(new[] { "hypno" }, wire["off"]!.ToObject<string[]>());
        Assert.Equal(AppSettings.BackRoomMediaSubCap, (int)wire["cap"]!);
        Assert.True((bool)wire["consented"]!);
    }

    [Fact]
    public void AudioWire_IsThreeFractions_NotThreePercentages()
    {
        // The page hands these straight to the kit's bus setters and the music element, which take 0..1.
        var wire = JObject.FromObject(BackRoomHostService.AudioWire(
            new AppSettings { BackRoomSubVolume = 25, BackRoomSfxVolume = 100, BackRoomMusicVolume = 0 }));
        Assert.Equal(0.25, (double)wire["sub"]!, 6);
        Assert.Equal(1.0, (double)wire["sfx"]!, 6);
        Assert.Equal(0.0, (double)wire["music"]!, 6);
    }

    [Fact]
    public void AudioWire_WithNoSettingsAtAll_IsTheRoomsDefaults()
    {
        var wire = JObject.FromObject(BackRoomHostService.AudioWire(null));
        Assert.Equal(1.0, (double)wire["sub"]!, 6);
        Assert.Equal(1.0, (double)wire["sfx"]!, 6);
        Assert.Equal(0.15, (double)wire["music"]!, 6);
    }

    [Fact]
    public void EveryNewOption_PushesASettingsFrame()
    {
        // A press that the room cannot see the result of is a press the player will make twice.
        foreach (var name in new[]
                 {
                     nameof(AppSettings.BackRoomMediaSource), nameof(AppSettings.BackRoomMediaSubs),
                     nameof(AppSettings.BackRoomMediaSubsOff), nameof(AppSettings.BackRoomSubVolume),
                     nameof(AppSettings.BackRoomSfxVolume), nameof(AppSettings.BackRoomMusicVolume),
                     nameof(AppSettings.MediaSource), nameof(AppSettings.RemoteMediaRatio),
                 })
            Assert.Contains(name, BackRoomHostService.SettingsFrameProperties);
    }

    [Fact]
    public void AnUnknownKey_IsStillNull()
    {
        Assert.Null(Read("mediaSubs", new JArray("hypno")));
        Assert.Null(Read("volume", 50));
        Assert.Null(Read("", "x"));
    }
}
