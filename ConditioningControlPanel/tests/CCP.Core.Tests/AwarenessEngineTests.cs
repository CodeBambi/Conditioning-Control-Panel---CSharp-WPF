using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Awareness;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the AI-1 window-awareness engine contract ported from WPF
/// Services/UI/WindowAwarenessService.cs: strict classification priority order (:563-641),
/// page-name/browser-tab extraction (:645-720), idle transition (:489-497), the
/// category-or-name debounce (:507), and the StillOnActivity milestone sequence
/// {1,5,10}min (:408-475). All timer-driven paths are exercised via the internal
/// PollTick/StillOnMilestoneTick seams with injected times - no real timers run.
/// </summary>
public class AwarenessEngineTests
{
    private class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool WasSettingsFileMissing => true;
        public List<string> PendingPresetReinstalls { get; } = new();
        public void Save() { }
        public void Save(bool suppressCloudBackup = false) { }
        public void SaveImmediate(bool suppressCloudBackup = false) { }
        public void RestoreFrom(AppSettings settings) { }
        public void Reset() { }
    }

    private class FakeTitleProvider : IForegroundWindowTitleProvider
    {
        public string? Title { get; set; }
        public string? GetForegroundWindowTitle() => Title;
    }

    private static readonly DateTime T0 = new(2026, 7, 12, 12, 0, 0);

    private static (AwarenessService Engine, FakeTitleProvider Provider) CreateEngine()
    {
        var settings = new FakeSettingsService();
        settings.Current.AwarenessModeEnabled = true;
        settings.Current.AwarenessConsentGiven = true;
        var provider = new FakeTitleProvider();
        return (new AwarenessService(settings, provider), provider);
    }

    // --- Classification priority order (WPF :563-641, first match wins per dict, strict order) ---

    [Theory]
    // Gaming beats Learning ("minecraft" + "github" both present)
    [InlineData("Minecraft speedrun guide - GitHub", ActivityCategory.Gaming, "Minecraft")]
    // Learning beats Shopping ("github" + "amazon" both present)
    [InlineData("aws-sdk issues - GitHub - amazon cloud", ActivityCategory.Learning, "aws-sdk issues on GitHub")]
    // Shopping beats Social ("amazon" + "discord" both present)
    [InlineData("gift ideas from discord - Amazon.com", ActivityCategory.Shopping, "gift ideas from discord on Amazon")]
    // Social beats Media ("discord" + "youtube" both present)
    [InlineData("#youtube-links - Discord", ActivityCategory.Social, "#youtube-links on Discord")]
    // Media beats Working ("youtube" + "vs code" both present)
    [InlineData("vs code tips - YouTube", ActivityCategory.Media, "vs code tips on YouTube")]
    // Working matched when no higher category hits
    [InlineData("main.cs - MyProject - Visual Studio Code", ActivityCategory.Working, "main.cs on VS Code")]
    // Unknown fallback
    [InlineData("qzxv wprk", ActivityCategory.Unknown, "something")]
    public void Categorize_ResolvesPriorityOrder(string title, ActivityCategory expectedCategory, string expectedName)
    {
        var (category, detectedName, _, _) = AwarenessClassifier.Categorize(title);
        Assert.Equal(expectedCategory, category);
        Assert.Equal(expectedName, detectedName);
    }

    [Fact]
    public void Categorize_BrowserFallback_ExtractsTabName()
    {
        // No dictionary hit; generic browser detection (WPF :630-637)
        var (category, detectedName, serviceName, pageTitle) = AwarenessClassifier.Categorize(
            "Quantum knitting forum - Google Chrome");
        Assert.Equal(ActivityCategory.Browsing, category);
        Assert.Equal("Quantum knitting forum", detectedName);
        Assert.Equal("browser", serviceName);
        Assert.Equal("Quantum knitting forum", pageTitle);
    }

    [Fact]
    public void Categorize_EmptyTitle_IsUnknownSomething()
    {
        var (category, detectedName, serviceName, pageTitle) = AwarenessClassifier.Categorize("   ");
        Assert.Equal(ActivityCategory.Unknown, category);
        Assert.Equal("something", detectedName);
        Assert.Equal("", serviceName);
        Assert.Equal("", pageTitle);
    }

    [Fact]
    public void Categorize_GamingHasNoPageTitle()
    {
        // Gaming returns (name, name, "") - no page extraction (WPF :572-577)
        var (category, detectedName, serviceName, pageTitle) = AwarenessClassifier.Categorize("VALORANT");
        Assert.Equal(ActivityCategory.Gaming, category);
        Assert.Equal("Valorant", detectedName);
        Assert.Equal("Valorant", serviceName);
        Assert.Equal("", pageTitle);
    }

    // --- Page-name extraction (WPF :695-720) ---

    [Fact]
    public void ExtractPageNameWithService_SplitsOnDashAndBuildsDisplayName()
    {
        var (displayName, pageTitle) = AwarenessClassifier.ExtractPageNameWithService(
            "CodeBambi's wishlist - Throne", "Throne");
        Assert.Equal("CodeBambi's wishlist on Throne", displayName);
        Assert.Equal("CodeBambi's wishlist", pageTitle);
    }

    [Theory]
    [InlineData("Deep dive — Wikipedia")]   // em-dash separator
    [InlineData("Deep dive | Wikipedia")]   // pipe separator
    public void ExtractPageNameWithService_SupportsAllSeparators(string title)
    {
        var (displayName, pageTitle) = AwarenessClassifier.ExtractPageNameWithService(title, "Wikipedia");
        Assert.Equal("Deep dive on Wikipedia", displayName);
        Assert.Equal("Deep dive", pageTitle);
    }

    [Fact]
    public void ExtractPageNameWithService_NoSeparator_FallsBackToServiceName()
    {
        var (displayName, pageTitle) = AwarenessClassifier.ExtractPageNameWithService("Wikipedia", "Wikipedia");
        Assert.Equal("Wikipedia", displayName);
        Assert.Equal("", pageTitle);
    }

    [Fact]
    public void ExtractPageNameWithService_TruncatesLongFirstPartAt40()
    {
        var longPart = new string('a', 45);
        var (displayName, pageTitle) = AwarenessClassifier.ExtractPageNameWithService(
            $"{longPart} - GitHub", "GitHub");
        // Display truncates to 37 + "..." (WPF :712-713); raw page title stays full.
        Assert.Equal($"{new string('a', 37)}... on GitHub", displayName);
        Assert.Equal(longPart, pageTitle);
    }

    [Fact]
    public void ExtractBrowserTabName_StripsSuffixAndTrimsAt50()
    {
        Assert.Equal("Cat pictures", AwarenessClassifier.ExtractBrowserTabName("Cat pictures - Google Chrome"));

        var longTab = new string('b', 60);
        var trimmed = AwarenessClassifier.ExtractBrowserTabName($"{longTab} - Mozilla Firefox");
        Assert.Equal(new string('b', 47) + "...", trimmed); // 47 + "..." = 50 (WPF :685-686)

        Assert.Equal("a webpage", AwarenessClassifier.ExtractBrowserTabName(" - Google Chrome"));
    }

    // --- Idle transition (WPF :489-497: title unchanged AND >= 5min -> Idle "being idle") ---

    [Fact]
    public void PollTick_SameTitleForFiveMinutes_TransitionsToIdle()
    {
        var (engine, provider) = CreateEngine();
        var events = new List<ActivityChangedEventArgs>();
        engine.ActivityChanged += (_, e) => events.Add(e);

        provider.Title = "VALORANT";
        engine.PollTick(T0);
        Assert.Single(events);
        Assert.Equal(ActivityCategory.Gaming, events[0].Category);

        // Same title, under the threshold: no idle yet.
        engine.PollTick(T0.AddMinutes(4));
        Assert.Single(events);
        Assert.Equal(ActivityCategory.Gaming, engine.CurrentActivity);

        // Same title, at the 5min threshold: Idle "being idle".
        engine.PollTick(T0.AddMinutes(5));
        Assert.Equal(2, events.Count);
        Assert.Equal(ActivityCategory.Idle, events[1].Category);
        Assert.Equal("being idle", events[1].DetectedName);
        Assert.Equal(ActivityCategory.Idle, engine.CurrentActivity);

        // Still the same title: Idle does not re-fire (WPF :493 guard).
        engine.PollTick(T0.AddMinutes(11));
        Assert.Equal(2, events.Count);
    }

    // --- Debounce (WPF :507: fire only when category OR detectedName changed) ---

    [Fact]
    public void PollTick_SameCategoryAndName_FiresSingleActivityChanged()
    {
        var (engine, provider) = CreateEngine();
        var events = new List<ActivityChangedEventArgs>();
        engine.ActivityChanged += (_, e) => events.Add(e);

        provider.Title = "VALORANT - lobby";
        engine.PollTick(T0);
        // Different title, same category + detected name ("Valorant"): debounced.
        provider.Title = "VALORANT - ranked match";
        engine.PollTick(T0.AddSeconds(2));

        Assert.Single(events);
        Assert.Equal("Valorant", events[0].DetectedName);
    }

    [Fact]
    public void PollTick_UnchangedTitle_DoesNotRefire()
    {
        var (engine, provider) = CreateEngine();
        var events = new List<ActivityChangedEventArgs>();
        engine.ActivityChanged += (_, e) => events.Add(e);

        provider.Title = "Discord";
        engine.PollTick(T0);
        engine.PollTick(T0.AddSeconds(2));
        engine.PollTick(T0.AddSeconds(4));

        Assert.Single(events);
        Assert.Equal(ActivityCategory.Social, events[0].Category);
    }

    [Fact]
    public void PollTick_CategoryChange_FiresWithPreviousContext()
    {
        var (engine, provider) = CreateEngine();
        var events = new List<ActivityChangedEventArgs>();
        engine.ActivityChanged += (_, e) => events.Add(e);

        provider.Title = "VALORANT";
        engine.PollTick(T0);
        provider.Title = "wishlist - Throne";
        engine.PollTick(T0.AddSeconds(2));

        Assert.Equal(2, events.Count);
        Assert.Equal(ActivityCategory.Shopping, events[1].Category);
        Assert.Equal(ActivityCategory.Gaming, events[1].PreviousCategory);
        Assert.Equal("Valorant", events[1].PreviousServiceName);
        Assert.True(events[1].IsNewService);
    }

    [Fact]
    public void PollTick_PopulatesAppClusterAndAppId()
    {
        var (engine, provider) = CreateEngine();
        var events = new List<ActivityChangedEventArgs>();
        engine.ActivityChanged += (_, e) => events.Add(e);

        provider.Title = "VALORANT";
        engine.PollTick(T0);
        Assert.Equal("game_competitive", events[0].AppCluster);
        Assert.Equal("", events[0].AppId);

        provider.Title = "Discord";
        engine.PollTick(T0.AddSeconds(2));
        Assert.Equal("discord", events[1].AppId);
    }

    // --- StillOnActivity milestones {1,5,10}min (WPF :408-475) ---

    [Fact]
    public void StillOn_FiresMilestoneSequence_1_5_10_ThenStops()
    {
        var (engine, provider) = CreateEngine();
        var stillOn = new List<ActivityChangedEventArgs>();
        engine.StillOnActivity += (_, e) => stillOn.Add(e);

        provider.Title = "VALORANT";
        engine.PollTick(T0);

        engine.StillOnMilestoneTick(T0.AddMinutes(1));
        Assert.Single(stillOn);
        engine.StillOnMilestoneTick(T0.AddMinutes(5));
        Assert.Equal(2, stillOn.Count);
        engine.StillOnMilestoneTick(T0.AddMinutes(10));
        Assert.Equal(3, stillOn.Count);

        // No fourth milestone exists: further ticks never fire (WPF :429-430).
        engine.StillOnMilestoneTick(T0.AddMinutes(15));
        Assert.Equal(3, stillOn.Count);

        Assert.All(stillOn, e => Assert.Equal(ActivityCategory.Gaming, e.Category));
        Assert.All(stillOn, e => Assert.Equal("Valorant", e.DetectedName));
    }

    [Fact]
    public void StillOn_ActivityChange_ResetsMilestones()
    {
        var (engine, provider) = CreateEngine();
        var stillOn = new List<ActivityChangedEventArgs>();
        engine.StillOnActivity += (_, e) => stillOn.Add(e);

        provider.Title = "VALORANT";
        engine.PollTick(T0);
        engine.StillOnMilestoneTick(T0.AddMinutes(1));
        Assert.Single(stillOn);

        // Switch activity: milestone index resets to the 1min milestone (WPF :414-417).
        var t1 = T0.AddMinutes(2);
        provider.Title = "Discord";
        engine.PollTick(t1);

        engine.StillOnMilestoneTick(t1.AddMinutes(1));
        Assert.Equal(2, stillOn.Count);
        Assert.Equal(ActivityCategory.Social, stillOn[1].Category);
        Assert.Equal("Discord", stillOn[1].DetectedName);
    }

    [Fact]
    public void StillOn_NotArmedForUnknownOrIdle()
    {
        var (engine, provider) = CreateEngine();
        var stillOn = new List<ActivityChangedEventArgs>();
        engine.StillOnActivity += (_, e) => stillOn.Add(e);

        // Unknown activity never arms milestones (WPF :419-421).
        provider.Title = "qzxv wprk";
        engine.PollTick(T0);
        engine.StillOnMilestoneTick(T0.AddMinutes(1));
        Assert.Empty(stillOn);

        // Idle transition disarms a running milestone chain.
        provider.Title = "VALORANT";
        engine.PollTick(T0.AddMinutes(2));
        engine.PollTick(T0.AddMinutes(7)); // same title >= 5min -> Idle
        Assert.Equal(ActivityCategory.Idle, engine.CurrentActivity);
        engine.StillOnMilestoneTick(T0.AddMinutes(8));
        Assert.Empty(stillOn);
    }

    // --- Lifecycle guards ---

    [Fact]
    public void Start_WithoutTitleProvider_NoOpsGracefully()
    {
        var settings = new FakeSettingsService();
        settings.Current.AwarenessModeEnabled = true;
        settings.Current.AwarenessConsentGiven = true;
        using var engine = new AwarenessService(settings, titleProvider: null);

        engine.Start();
        Assert.False(engine.IsRunning);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Start_RequiresBothEnableAndConsent(bool enabled, bool consent)
    {
        // WPF :336-342: Start() no-ops unless AwarenessModeEnabled && AwarenessConsentGiven.
        var settings = new FakeSettingsService();
        settings.Current.AwarenessModeEnabled = enabled;
        settings.Current.AwarenessConsentGiven = consent;
        using var engine = new AwarenessService(settings, new FakeTitleProvider());

        engine.Start();
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void StartAndStop_TogglesIsRunning_AndStopClearsActivity()
    {
        var (engine, provider) = CreateEngine();
        using (engine)
        {
            // Establish an activity via the deterministic seam BEFORE starting the real
            // 1.5s poll timer, so no thread-pool callback can race the assertions.
            provider.Title = "VALORANT";
            engine.PollTick(T0);
            Assert.Equal(ActivityCategory.Gaming, engine.CurrentActivity);

            engine.Start();
            Assert.True(engine.IsRunning);

            // WPF :358-373: Stop resets category -> Unknown, name -> "".
            engine.Stop();
            Assert.False(engine.IsRunning);
            Assert.Equal(ActivityCategory.Unknown, engine.CurrentActivity);
            Assert.Equal("", engine.CurrentDetectedName);
        }
    }
}
