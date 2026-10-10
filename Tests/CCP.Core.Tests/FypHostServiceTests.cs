using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Lane k9 (HA1): the For You feed's host rules as WPF 7.1.5 FypHostService has them. The
/// page is never trusted: remote content needs consent AND a non-library source, settings frames
/// are whitelisted and clamped, the file menu reaches library media only.</summary>
[Collection(SessionStatics.Name)]
public sealed class FypHostServiceTests
{
    private static AppSettings Fresh() => new();

    [Fact]
    public void RemoteNeedsConsentAndANonLibrarySource()
    {
        var s = Fresh();
        Assert.Equal("library", FypHostService.EffectiveFeedSource(s));
        Assert.False(FypHostService.RemoteAllowed(s));
        Assert.False(FypHostService.RemoteAllowed(null));

        // The app-wide source carries over only with consent.
        s.MediaSource = "online";
        Assert.Equal("library", FypHostService.EffectiveFeedSource(s));
        Assert.False(FypHostService.RemoteAllowed(s));

        // A page cannot talk its way past the consent card.
        Assert.Equal(FypHostService.SettingEffect.None, FypHostService.ApplySetting(s, "source", "online"));
        Assert.Equal("library", s.FypSource);
        Assert.False(FypHostService.RemoteAllowed(s));

        // Consent is one-way and only on a literal true.
        FypHostService.ApplySetting(s, "onlineConsented", false);
        Assert.False(s.HasRemoteMediaConsent);
        FypHostService.ApplySetting(s, "onlineConsented", true);
        Assert.True(s.HasRemoteMediaConsent);
        FypHostService.ApplySetting(s, "onlineConsented", false);
        Assert.True(s.HasRemoteMediaConsent);

        Assert.Equal("online", FypHostService.EffectiveFeedSource(s));   // the app-wide source, now consented
        Assert.True(FypHostService.RemoteAllowed(s));

        // The feed's own picker wins once it has left "library", ratio included.
        s.RemoteMediaRatio = 70;
        Assert.Equal(70, FypHostService.EffectiveOnlineRatio(s));
        FypHostService.ApplySetting(s, "source", "mixed");
        FypHostService.ApplySetting(s, "onlineRatio", 20);
        Assert.Equal("mixed", FypHostService.EffectiveFeedSource(s));
        Assert.Equal(20, FypHostService.EffectiveOnlineRatio(s));

        // Local app-wide + library feed = nothing remote, consent or not.
        s.MediaSource = "local";
        FypHostService.ApplySetting(s, "source", "library");
        Assert.False(FypHostService.RemoteAllowed(s));
    }

    [Fact]
    public void SettingsFramesAreWhitelistedAndSanitized()
    {
        var s = Fresh();
        FypHostService.ApplySetting(s, "layout", "trio");
        FypHostService.ApplySetting(s, "layout", "sideways");   // not a layout: the property keeps its default
        FypHostService.ApplySetting(s, "muted", true);
        FypHostService.ApplySetting(s, "volume", 40);
        Assert.Equal("duo", s.FypLayout);
        Assert.True(s.FypMuted);
        Assert.Equal(40, s.FypVolume);

        // Unknown keys, null keys and null values change nothing and never throw.
        Assert.Equal(FypHostService.SettingEffect.None, FypHostService.ApplySetting(s, "AuthToken", "x"));
        Assert.Equal(FypHostService.SettingEffect.None, FypHostService.ApplySetting(s, null, "x"));
        Assert.Equal(FypHostService.SettingEffect.None, FypHostService.ApplySetting(s, "layout", null));
        Assert.Equal(FypHostService.SettingEffect.None, FypHostService.ApplySetting(s, "volume", "loud"));
        Assert.Equal(40, s.FypVolume);

        // Niches: catalog ids only, no duplicates.
        var real = FypOnlineCoordinator.Catalog[0].Id;
        FypHostService.ApplySetting(s, "onlineNiches", new JArray(real, "not-a-niche", real));
        Assert.Equal(new[] { real }, s.FypOnlineNiches);

        // Custom subs: sanitized, de-duplicated, capped at 20; each kept in the library.
        var many = new JArray(Enumerable.Range(0, 30).Select(i => (object)("sub" + i)).ToArray()) { "sub0", "bad name!", "" };
        FypHostService.ApplySetting(s, "onlineCustomSubs", many);
        Assert.Equal(20, s.FypOnlineCustomSubs.Count);
        Assert.All(s.FypOnlineCustomSubs, name => Assert.NotNull(FypOnlineCoordinator.SanitizeSub(name)));
        Assert.Contains(s.BuildRemoteSubLibraryView(), r => r.Name == "sub0" && r.Selected);
    }

    [Fact]
    public void GhostAndEyeFramesReportTheirEffect_NeverStoreGhost()
    {
        var s = Fresh();
        Assert.Equal(FypHostService.SettingEffect.GhostOn, FypHostService.ApplySetting(s, "clickThrough", true));
        Assert.Equal(FypHostService.SettingEffect.GhostOff, FypHostService.ApplySetting(s, "clickThrough", false));
        Assert.Equal(FypHostService.SettingEffect.GhostOn, FypHostService.ApplySetting(null, "clickThrough", true));   // settings-free
        Assert.Equal(FypHostService.SettingEffect.EyeControlOn, FypHostService.ApplySetting(s, "eyeControl", true));
        Assert.True(s.FypEyeControl);
        Assert.Equal(FypHostService.SettingEffect.EyeControlOff, FypHostService.ApplySetting(s, "eyeControl", false));
        Assert.Equal(FypHostService.SettingEffect.EyeGazeChanged, FypHostService.ApplySetting(s, "eyeGaze", true));
        Assert.Equal(FypHostService.SettingEffect.OpacityChanged, FypHostService.ApplySetting(s, "windowOpacity", 0.4));
        Assert.Equal(0.4, s.FypWindowOpacity, 3);
    }

    [Fact]
    public void InitCarriesSettingsCatalogAndLibrary_AndNeverAnEyeToggleTheHeadCannotDrive()
    {
        var s = Fresh();
        s.FypEyeControl = true;
        s.FypEyeGaze = true;
        s.TryAddLibrarySub("hypno");
        var prev = FypHostService.StatsFilePathOverride;
        FypHostService.StatsFilePathOverride = Path.Combine(Path.GetTempPath(), "k9-nostats-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var init = FypHostService.BuildInit(s, Array.Empty<FypAssetManifest.Entry>(), eyeControl: false);
            Assert.Equal("init", (string?)init["type"]);
            Assert.Empty((JArray)init["assets"]!);
            Assert.False((bool)init["settings"]!["eyeControl"]!);
            Assert.False((bool)init["settings"]!["eyeGaze"]!);
            Assert.Equal("library", (string?)init["settings"]!["source"]);
            Assert.False((bool)init["settings"]!["onlineConsented"]!);
            Assert.Equal(FypOnlineCoordinator.Catalog.Length, ((JArray)init["online"]!["niches"]!).Count);
            Assert.Contains((JArray)init["online"]!["library"]!, r => (string?)r["name"] == "hypno");
            Assert.Equal(JTokenType.Null, init["stats"]!.Type);

            var on = FypHostService.BuildInit(s, Array.Empty<FypAssetManifest.Entry>(), eyeControl: true);
            Assert.True((bool)on["settings"]!["eyeControl"]!);
        }
        finally { FypHostService.StatsFilePathOverride = prev; }
    }

    [Fact]
    public void StatsRoundTripVerbatim()
    {
        var file = Path.Combine(Path.GetTempPath(), "k9-stats-" + Guid.NewGuid().ToString("N") + ".json");
        var prev = FypHostService.StatsFilePathOverride;
        FypHostService.StatsFilePathOverride = file;
        try
        {
            Assert.Null(FypHostService.LoadStats());
            FypHostService.SaveStats(JObject.Parse("{\"clips\":12,\"nested\":{\"a\":[1,2]}}"));
            Assert.Equal(12, (int)FypHostService.LoadStats()!["clips"]!);
            File.WriteAllText(file, "{broken");
            Assert.Null(FypHostService.LoadStats());   // a bad file never throws
        }
        finally
        {
            FypHostService.StatsFilePathOverride = prev;
            try { File.Delete(file); } catch { }
        }
    }

    [Fact]
    public void ClipXpIsCappedPerRollingMinute()
    {
        FypHostService.ResetClipXpForTests();
        try
        {
            var t = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < FypHostService.MaxClipXpPerMinute; i++) Assert.True(FypHostService.AllowClipXp(t.AddSeconds(i)));
            Assert.False(FypHostService.AllowClipXp(t.AddSeconds(30)));
            Assert.True(FypHostService.AllowClipXp(t.AddSeconds(61)));    // the oldest left the window
        }
        finally { FypHostService.ResetClipXpForTests(); }
    }

    [Fact]
    public void BatchFramesAppendThenStatus_AndAnEmptyBatchStillSaysSo()
    {
        var entry = new FypAssetManifest.Entry { Id = "scrolller/a/1", Url = "https://cdn.example/1.mp4" };
        var frames = FypHostService.BatchFrames(new FypOnlineCoordinator.FeedBatch(new() { entry }, null, false, 120)).ToList();
        Assert.Equal(new[] { "assets-append", "online-status" }, frames.Select(f => (string?)f["type"]));
        Assert.Equal("scrolller/a/1", (string?)frames[0]["assets"]![0]!["id"]);
        Assert.True((bool)frames[1]["ok"]!);
        Assert.Equal(1, (int)frames[1]["fresh"]!);
        Assert.Equal(120, (int)frames[1]["poolTotal"]!);

        var dry = FypHostService.BatchFrames(new FypOnlineCoordinator.FeedBatch(new(), "offline", true, 0)).ToList();
        Assert.Single(dry);
        Assert.False((bool)dry[0]["ok"]!);
        Assert.Equal("offline", (string?)dry[0]["error"]);
        Assert.True((bool)dry[0]["dry"]!);
    }

    [Fact]
    public void AProbeIsRememberedOnlyWhenItAnswered_AndAFoundSubJoinsTheFeed()
    {
        var s = Fresh();
        var failed = FypHostService.CommitProbe(s, "hypno", new SubProbe { Ok = false, Error = "network" });
        Assert.Equal("network", (string?)failed["error"]);
        Assert.Empty(s.FypOnlineSubVerdicts);            // a transport failure taught us nothing

        var missing = FypHostService.CommitProbe(s, "nosuchsub", new SubProbe { Ok = false });
        Assert.False((bool)missing["ok"]!);
        Assert.False(s.FypOnlineSubVerdicts["nosuchsub"].Ok);
        Assert.Empty(s.FypOnlineCustomSubs);

        var found = FypHostService.CommitProbe(s, "hypno", new SubProbe { Ok = true, VideoCount = 42 });
        Assert.Equal("sub-probe", (string?)found["type"]);
        Assert.Equal(42, (int)found["videoCount"]!);
        Assert.Contains("hypno", s.FypOnlineCustomSubs);
        FypHostService.CommitProbe(s, "hypno", new SubProbe { Ok = true, VideoCount = 42 });
        Assert.Single(s.FypOnlineCustomSubs);            // never twice

        Assert.Equal("invalid", (string?)FypHostService.InvalidProbeFrame("bad name!")["error"]);
        Assert.Equal("library", (string?)FypHostService.LibraryFrame(s)["type"]);
    }

    [Fact]
    public void TheFileMenuReachesLibraryMediaOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "k9-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sep = Path.DirectorySeparatorChar;
            Assert.Equal(Path.Combine(root, "videos", "a.mp4"), FypFileMenu.ResolveLocal(root, "videos/a.mp4"));
            Assert.Equal(Path.Combine(root, "videos", "sub", "b.webm"), FypFileMenu.ResolveLocal(root, "videos/sub/b.webm"));
            Assert.Equal(Path.Combine(root, "images", "c.gif"), FypFileMenu.ResolveLocal(root, "images/c.gif"));
            Assert.EndsWith(sep + "a.mp4", FypFileMenu.ResolveLocal(root, "videos\\a.mp4"));

            foreach (var bad in new[]
            {
                null, "", "scrolller/x/1", "videos/a.exe", "videos/a.lnk", "images/a.png", "images/a.mp4",
                "videos/../../secret.mp4", "videos/.temp/a.mp4", ".temp/videos/a.mp4", "other/a.mp4", "a.mp4",
                "videos//a.mp4", "videos/C:/a.mp4", "videos/\"a.mp4",
            })
                Assert.Null(FypFileMenu.ResolveLocal(root, bad));
            Assert.Null(FypFileMenu.ResolveLocal(null, "videos/a.mp4"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
