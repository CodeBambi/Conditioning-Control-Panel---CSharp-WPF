using System;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The privacy layer: what leaves the machine, what the settings default to, and what the sanitisers
/// refuse (doc 02 §6, MASTER-SCOPE Train 2 "privacy inversion").
///
/// <para>These exist because this is the half of the feature that cannot be walked back after it
/// ships. A widened projection is not a bug report, it is a disclosure — so the exhaustive list in
/// <see cref="AwarenessProjection"/>'s doc comment is asserted here field by field, and the two claims
/// the UI will make in plain language ("titles stay on your PC", "only the cluster is sent for adult
/// sites") are tested as code rather than trusted as prose.</para>
/// </summary>
public class AwarenessPrivacyTests
{
    private const string SecretTitle = "CodeBambi's private wishlist — bank statement.pdf";

    private static ContextFrame Frame(
        string appId = "youtube",
        string? cluster = "site_video",
        string? title = null,
        MediaInfo? media = null)
        => new()
        {
            AppId = appId,
            AppCluster = cluster,
            Category = ActivityCategory.Media,
            ServiceName = "YouTube",
            PageTitleSanitized = title,
            NowPlaying = media,
            InputIdleSeconds = 12,
            Transition = TransitionKind.NewApp,
            DwellSeconds = 1500,
            VisitsToday = 4,
            MinutesToday = 47,
            MinutesThisWeek = 312,
            SinceLastVisit = TimeSpan.FromMinutes(22),
            DayStreak = 5,
            DayArcSummary = "morning: vscode 2h → afternoon: youtube 40m → now",
            TimeOfDay = TimeBucket.Afternoon,
            Weekday = DayOfWeek.Monday,
            UserLevel = 41,
            LoginStreakDays = 9,
            Tier = RarityTier.Rare,
            CutAt = new DateTime(2026, 8, 3, 14, 0, 0, DateTimeKind.Local)
        };

    // ===================== cloud projection =====================

    [Fact]
    public void CloudProjection_IsValidJsonAndCarriesTheNumbersTheJokeNeeds()
    {
        var json = AwarenessProjection.BuildCloudProjection(Frame());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("site_video", root.GetProperty("cluster").GetString());
        Assert.Equal("youtube", root.GetProperty("app_id").GetString());
        Assert.Equal("YouTube", root.GetProperty("app").GetString());
        Assert.Equal(4, root.GetProperty("visits_today").GetInt32());
        Assert.Equal(5, root.GetProperty("day_streak").GetInt32());
        Assert.Equal("15-30m", root.GetProperty("dwell").GetString());
        Assert.Equal("Rare", root.GetProperty("tier").GetString());
    }

    [Fact]
    public void CloudProjection_RoundsMinutesToFive()
    {
        using var doc = JsonDocument.Parse(AwarenessProjection.BuildCloudProjection(Frame()));

        Assert.Equal(45, doc.RootElement.GetProperty("minutes_today").GetInt32());     // 47 → 45
        Assert.Equal(310, doc.RootElement.GetProperty("minutes_week").GetInt32());     // 312 → 310
        Assert.Equal(20, doc.RootElement.GetProperty("since_last_visit_min").GetInt32());
    }

    [Fact]
    public void CloudProjection_NeverCarriesAPageTitleWhenTheAppIsNotAllowListed()
    {
        // The shipped allow list is empty, so PageTitleSanitized is null for everyone by default and
        // this is the state that actually goes over the wire in v6.8.
        var json = AwarenessProjection.BuildCloudProjection(Frame());

        Assert.DoesNotContain("title", json);
        Assert.DoesNotContain(SecretTitle, json);
    }

    [Fact]
    public void CloudProjection_CarriesTheTitleOnlyWhenOneWasAllowListedUpstream()
    {
        var json = AwarenessProjection.BuildCloudProjection(Frame(title: "Inbox"));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Inbox", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void CloudProjection_NeverCarriesNowPlaying()
    {
        // SMTC track names are machine-local. They may ride the Ollama projection; they may not leave.
        var media = new MediaInfo("Sleepy Bimbo Loop 4", "Bambi Sleep", "Playing", 5);
        var json = AwarenessProjection.BuildCloudProjection(Frame(media: media));

        Assert.DoesNotContain("Sleepy Bimbo Loop", json);
        Assert.DoesNotContain("Bambi Sleep", json);
        Assert.DoesNotContain("now_playing", json);
    }

    [Fact]
    public void CloudProjection_SendsOnlyTheClusterIdForAdultContent()
    {
        var frame = Frame(appId: "some_adult_site", cluster: AwarenessClusters.Adult, title: "a very specific page")
            with { ServiceName = "SomeAdultSite" };

        var json = AwarenessProjection.BuildCloudProjection(frame);

        Assert.Contains(AwarenessClusters.Adult, json);
        Assert.DoesNotContain("some_adult_site", json);
        Assert.DoesNotContain("SomeAdultSite", json);
        Assert.DoesNotContain("a very specific page", json);
        Assert.DoesNotContain("app_id", json);
    }

    [Fact]
    public void CloudProjection_WithholdsTheDayArcForAdultFramesToo()
    {
        // The arc is prose full of app ids. For an adult frame it is exactly the leak the cluster rule
        // exists to prevent.
        var frame = Frame(appId: "some_adult_site", cluster: AwarenessClusters.Adult);

        Assert.DoesNotContain("arc", AwarenessProjection.BuildCloudProjection(frame));
        Assert.Contains("arc", AwarenessProjection.BuildCloudProjection(Frame()));
    }

    [Fact]
    public void CloudProjection_SurvivesAnEmptyFrameAndANullOne()
    {
        Assert.Equal("{}", AwarenessProjection.BuildCloudProjection(null));

        var json = AwarenessProjection.BuildCloudProjection(new ContextFrame());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("unclustered", doc.RootElement.GetProperty("cluster").GetString());
    }

    [Fact]
    public void CloudProjection_KeepsTrendsAsLabelsNotObjects()
    {
        var frame = Frame() with
        {
            Trends = new[]
            {
                new TrendEvent(TrendKind.ReturnVisit, "youtube", "site_video", 4, 4, 47, 1500, TimeSpan.FromMinutes(22))
            }
        };

        using var doc = JsonDocument.Parse(AwarenessProjection.BuildCloudProjection(frame));
        var trends = doc.RootElement.GetProperty("trends");

        Assert.Equal(JsonValueKind.Array, trends.ValueKind);
        Assert.Equal("ReturnVisit(4)", trends[0].GetString());
    }

    // ===================== local projection =====================

    [Fact]
    public void LocalProjection_MayCarryTheThingsTheCloudMayNot()
    {
        var media = new MediaInfo("Sleepy Bimbo Loop 4", "Bambi Sleep", "Playing", 5);
        var frame = Frame(title: "Inbox — 4 unread", media: media);

        var json = AwarenessProjection.BuildLocalProjection(frame);

        Assert.Contains("Sleepy Bimbo Loop 4", json);
        Assert.Contains("Inbox", json);
        Assert.Contains("idle_seconds", json);
    }

    [Fact]
    public void LocalProjection_DoesNotCollapseTheAdultCluster()
    {
        var frame = Frame(appId: "some_adult_site", cluster: AwarenessClusters.Adult);
        Assert.Contains("some_adult_site", AwarenessProjection.BuildLocalProjection(frame));
    }

    // ===================== settings defaults =====================

    [Fact]
    public void FreshInstall_AwarenessV2IsOn()
        => Assert.True(new AppSettings().UseAwarenessV2);

    [Fact]
    public void UpgraderWithoutTheKeys_LandsOnTheShippedDefaults()
    {
        var settings = JsonConvert.DeserializeObject<AppSettings>("{}", new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Error = (_, args) => { args.ErrorContext.Handled = true; }
        })!;

        Assert.True(settings.UseAwarenessV2);
        Assert.Equal(AwarenessIntensity.Chatty, settings.AwarenessIntensity);
        Assert.Equal(30, settings.AwarenessRetentionDays);
        Assert.True(settings.AwarenessAdultReactionsEnabled);
        Assert.True(settings.AwarenessAdultRecordingEnabled);
    }

    [Fact]
    public void TheTitleAllowListShipsEmpty()
    {
        // This is the privacy inversion. Today every page title goes to the cloud; from v2 none does
        // until the user names an app. An accidental seed here would silently undo the whole change.
        Assert.Empty(new AppSettings().AwarenessTitleAllowList);
    }

    [Fact]
    public void TheDenyListShipsEmptyAndIsSeededVisiblyByThePrivacyPackage()
        => Assert.Empty(new AppSettings().AwarenessDenyList);

    [Fact]
    public void RetentionIsClampedToTheSliderRange()
    {
        var settings = new AppSettings { AwarenessRetentionDays = 3 };
        Assert.Equal(7, settings.AwarenessRetentionDays);

        settings.AwarenessRetentionDays = 4000;
        Assert.Equal(90, settings.AwarenessRetentionDays);
    }

    [Fact]
    public void ListEntriesAreSanitizedOnTheWayIntoSettings()
    {
        var settings = new AppSettings
        {
            AwarenessDenyList = new System.Collections.Generic.List<string>
            {
                "  KeePass  ", "*", "1password", "1PASSWORD", "", "a", new string('x', 200)
            }
        };

        Assert.Contains("keepass", settings.AwarenessDenyList);
        Assert.Contains("1password", settings.AwarenessDenyList);
        Assert.DoesNotContain("*", settings.AwarenessDenyList);
        Assert.DoesNotContain("a", settings.AwarenessDenyList);
        Assert.Equal(3, settings.AwarenessDenyList.Count);          // dedup + drops, cap applied
        Assert.All(settings.AwarenessDenyList, e => Assert.True(e.Length <= AwarenessText.MaxRuleLength));
    }
}
