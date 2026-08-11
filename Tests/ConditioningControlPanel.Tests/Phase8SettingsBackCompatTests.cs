using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// UX restructure Phase 8 — settings written by a PRE-demolition build must still load.
///
/// <para>Phase 8 deleted the three ghost containers (<c>ProgressionTabView</c>,
/// <c>PatreonTabView</c>, <c>LegacyDashboardHost</c>) and every <c>LoadSettings</c>/<c>SaveSettings</c>
/// line that read or seeded them. The demolition audit's ruling was that <b>no AppSettings property
/// may be removed</b> — every one of the 71 properties those containers round-tripped has a live
/// editor elsewhere, so the model is not edited at all and old files deserialize byte-identically.
/// This suite is that ruling made executable.</para>
///
/// <para><b>Why "it didn't throw" is not enough.</b> The real loader
/// (<c>Services/Settings/SettingsService.Load</c>) installs an <c>Error</c> handler that sets
/// <c>Handled = true</c> on every deserialization fault, so a property that lost its setter, its
/// <c>[JsonProperty]</c> name or its type would be swallowed in silence and the user would simply
/// find that setting reset. Every assertion below therefore checks the VALUE landed — and the blob
/// is deliberately authored with non-default values so "reset to default" cannot pass.</para>
///
/// <para>The serializer settings mirror the real load path, not a friendlier one.</para>
/// </summary>
public class Phase8SettingsBackCompatTests
{
    private static readonly JsonSerializerSettings LoaderSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Error = (_, args) => { args.ErrorContext.Handled = true; }
    };

    /// <summary>
    /// A settings.json in the shape a 6.7.4 install writes: every property the three demolished
    /// containers used to seed or save, at a NON-DEFAULT value, plus the one-shots the first-run
    /// rework reads and the preserved-forever <c>NewYearNoteReactionSeen</c> latch.
    /// </summary>
    private const string PreDemolitionSettingsJson = """
    {
      "Welcomed": true,
      "LastSeenVersion": "6.7.4",
      "ModPickerShown": true,
      "ModPickerOfflineOffers": 2,
      "FirstRunAssetsPromptShown": true,
      "NewYearNoteReactionSeen": true,

      "FlashEnabled": true,
      "FlashClickable": true,
      "CorruptionMode": true,
      "HydraLinkedTiming": true,
      "FlashGlowEnabled": true,
      "FlashFrequency": 42,
      "SimultaneousImages": 4,
      "HydraLimit": 7,
      "ImageScale": 63,
      "FlashOpacity": 71,
      "FadeDuration": 9,
      "FlashDuration": 11,
      "FlashAudioEnabled": true,

      "MandatoryVideosEnabled": true,
      "VideosPerHour": 5,
      "StrictLockEnabled": true,
      "AttentionChecksEnabled": true,
      "AttentionDensity": 6,
      "RandomizeAttentionTargets": true,
      "AttentionLifespan": 8,
      "AttentionSize": 44,

      "SubliminalEnabled": true,
      "SubliminalFrequency": 21,
      "SubliminalDuration": 7,
      "SubliminalOpacity": 55,
      "SubAudioEnabled": true,
      "SubAudioVolume": 66,

      "DualMonitorEnabled": true,
      "PerformanceMode": true,
      "AutoPerformanceMode": false,
      "VideoForceHardwareDecoding": true,
      "UnifiedOverlayHost": false,
      "MotionLevel": 1,

      "SpiralEnabled": true,
      "SpiralOpacity": 37,
      "PinkFilterEnabled": true,
      "PinkFilterOpacity": 29,
      "BubblesEnabled": true,
      "BubblesFrequency": 13,
      "BubblesVolume": 81,
      "LockCardEnabled": true,
      "LockCardFrequency": 8,
      "LockCardRepeats": 3,
      "LockCardStrict": true,
      "BubbleCountEnabled": true,
      "BubbleCountStrictLock": true,
      "BubbleCountFrequency": 6,
      "BubbleCountDifficulty": 2,
      "BouncingTextEnabled": true,
      "BouncingTextSpeed": 8,
      "BouncingTextSize": 120,
      "BouncingTextAlwaysOnTop": true,
      "MindWipeEnabled": true,
      "MindWipeFrequency": 27,
      "MindWipeVolume": 73,
      "MindWipeLoop": true,
      "BrainDrainEnabled": true,
      "BrainDrainIntensity": 88,
      "BrainDrainHighRefresh": true,

      "SchedulerEnabled": true,
      "SchedulerStartTime": "21:30",
      "SchedulerEndTime": "23:45",
      "SchedulerMonday": true,
      "SchedulerTuesday": false,
      "SchedulerWednesday": true,
      "SchedulerThursday": false,
      "SchedulerFriday": true,
      "SchedulerSaturday": false,
      "SchedulerSunday": true,
      "IntensityRampEnabled": true,
      "RampDurationMinutes": 45,
      "SchedulerMultiplier": 2.5,
      "EndSessionOnRampComplete": true,
      "RampLinkFlashOpacity": false,
      "RampLinkSpiralOpacity": false,
      "RampLinkPinkFilterOpacity": false,
      "RampLinkMasterAudio": false,
      "RampLinkSubliminalAudio": false,

      "KeywordTriggersEnabled": true,
      "KeywordBufferTimeoutMs": 4500,
      "KeywordGlobalCooldownSeconds": 90,
      "KeywordSessionMultiplier": 1.75,
      "KeywordHighlightEnabled": true,
      "KeywordHighlightDurationMs": 2500,
      "ScreenOcrEnabled": true,
      "ScreenOcrIntervalMs": 7000,
      "OcrHighlightAll": true,
      "OcrConfirmationScans": 3,
      "OcrHighlightVisibleInCapture": true,

      "AttentionCheckEnabled": true,
      "AttentionCheckMinPerSession": 2,
      "AttentionCheckMaxPerSession": 9,
      "AttentionCheckGraceMs": 6500,
      "AttentionCheckFailMode": 1,
      "AttentionCheckScope": 1
    }
    """;

    private static AppSettings Load(string json)
        => JsonConvert.DeserializeObject<AppSettings>(json, LoaderSettings)!;

    // =====================================================================================
    //  1. the blob is honest — every key in it is a real, settable property
    // =====================================================================================

    [Fact]
    public void EveryKeyInTheBlobIsStillAPropertyOnAppSettings()
    {
        // Without this, the whole suite could pass vacuously: Newtonsoft ignores unknown members,
        // so a property RENAMED (or a typo here) would look exactly like a property preserved.
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in typeof(AppSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            names.Add(p.Name);
            var attr = p.GetCustomAttribute<JsonPropertyAttribute>();
            if (!string.IsNullOrEmpty(attr?.PropertyName)) names.Add(attr!.PropertyName!);
        }

        var missing = JObject.Parse(PreDemolitionSettingsJson).Properties()
                             .Select(p => p.Name)
                             .Where(n => !names.Contains(n))
                             .ToList();

        Assert.True(missing.Count == 0,
            "Phase 8 removed or renamed AppSettings properties an old settings.json still carries — "
            + "the audit's ruling was that none may be touched: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoAppSettingsPropertyLostItsSetter()
    {
        // A property demoted to get-only would deserialize as a silent no-op under the loader's
        // swallow-everything Error handler.
        var readOnly = JObject.Parse(PreDemolitionSettingsJson).Properties()
            .Select(p => typeof(AppSettings).GetProperty(p.Name))
            .Where(p => p != null && p!.SetMethod == null)
            .Select(p => p!.Name)
            .ToList();

        Assert.True(readOnly.Count == 0, "these became get-only and can no longer round-trip: "
                                         + string.Join(", ", readOnly));
    }

    // =====================================================================================
    //  2. it loads, and every value survives
    // =====================================================================================

    [Fact]
    public void APreDemolitionSettingsFileDeserializesWithoutThrowing()
    {
        var ex = Record.Exception(() => Load(PreDemolitionSettingsJson));
        Assert.Null(ex);
        Assert.NotNull(Load(PreDemolitionSettingsJson));
    }

    [Fact]
    public void TheDocumentGenuinelyWon_NotADefaultConstructedFallback()
    {
        var s = Load(PreDemolitionSettingsJson);
        Assert.True(s.Welcomed);
        Assert.Equal("6.7.4", s.LastSeenVersion);
        Assert.True(s.ModPickerShown);
        Assert.Equal(2, s.ModPickerOfflineOffers);
        Assert.True(s.FirstRunAssetsPromptShown);
    }

    /// <summary>The 28 LegacyDashboardHost-owned properties; live editors are the Studio rack panels.</summary>
    [Fact]
    public void LegacyDashboardHostProperties_StillRoundTrip()
    {
        var s = Load(PreDemolitionSettingsJson);

        // Flash (FlashFeatureControl)
        Assert.True(s.FlashEnabled);
        Assert.True(s.FlashClickable);
        Assert.True(s.CorruptionMode);
        Assert.True(s.HydraLinkedTiming);
        Assert.True(s.FlashGlowEnabled);
        Assert.Equal(42, s.FlashFrequency);
        Assert.Equal(4, s.SimultaneousImages);
        Assert.Equal(7, s.HydraLimit);

        // Visuals (VisualsFeatureControl)
        Assert.Equal(63, s.ImageScale);
        Assert.Equal(71, s.FlashOpacity);
        Assert.Equal(9, s.FadeDuration);
        Assert.Equal(11, s.FlashDuration);
        Assert.True(s.FlashAudioEnabled);

        // Video + attention targets (VideoFeatureControl)
        Assert.True(s.MandatoryVideosEnabled);
        Assert.Equal(5, s.VideosPerHour);
        Assert.True(s.StrictLockEnabled);
        Assert.True(s.AttentionChecksEnabled);
        Assert.Equal(6, s.AttentionDensity);
        Assert.True(s.RandomizeAttentionTargets);
        Assert.Equal(8, s.AttentionLifespan);
        Assert.Equal(44, s.AttentionSize);

        // Subliminal (SubliminalFeatureControl)
        Assert.True(s.SubliminalEnabled);
        Assert.Equal(21, s.SubliminalFrequency);
        Assert.Equal(7, s.SubliminalDuration);
        Assert.Equal(55, s.SubliminalOpacity);
        Assert.True(s.SubAudioEnabled);
        Assert.Equal(66, s.SubAudioVolume);

        // System + performance (SystemFeatureControl / Settings · Performance)
        Assert.True(s.DualMonitorEnabled);
        Assert.True(s.PerformanceMode);
        Assert.False(s.AutoPerformanceMode);
        Assert.True(s.VideoForceHardwareDecoding);
        Assert.False(s.UnifiedOverlayHost);
        Assert.Equal(MotionLevel.Reduced, s.MotionLevel);
    }

    /// <summary>The 34 ProgressionTabView-owned properties; live editors are the Studio rack panels.</summary>
    [Fact]
    public void ProgressionTabProperties_StillRoundTrip()
    {
        var s = Load(PreDemolitionSettingsJson);

        Assert.True(s.SpiralEnabled);
        Assert.Equal(37, s.SpiralOpacity);
        Assert.True(s.PinkFilterEnabled);
        Assert.Equal(29, s.PinkFilterOpacity);

        Assert.True(s.BubblesEnabled);
        Assert.Equal(13, s.BubblesFrequency);
        Assert.Equal(81, s.BubblesVolume);

        Assert.True(s.LockCardEnabled);
        Assert.Equal(8, s.LockCardFrequency);
        Assert.Equal(3, s.LockCardRepeats);
        Assert.True(s.LockCardStrict);

        Assert.True(s.BubbleCountEnabled);
        Assert.True(s.BubbleCountStrictLock);
        Assert.Equal(6, s.BubbleCountFrequency);
        Assert.Equal(2, s.BubbleCountDifficulty);

        Assert.True(s.BouncingTextEnabled);
        Assert.Equal(8, s.BouncingTextSpeed);
        Assert.Equal(120, s.BouncingTextSize);
        Assert.True(s.BouncingTextAlwaysOnTop);

        Assert.True(s.MindWipeEnabled);
        Assert.Equal(27, s.MindWipeFrequency);
        Assert.Equal(73, s.MindWipeVolume);
        Assert.True(s.MindWipeLoop);

        // Brain Drain — the G2 rescue: its ONLY editor before Phase 4 was the dead tab.
        Assert.True(s.BrainDrainEnabled);
        Assert.Equal(88, s.BrainDrainIntensity);
        Assert.True(s.BrainDrainHighRefresh);
    }

    [Fact]
    public void SchedulerAndRampProperties_StillRoundTrip()
    {
        var s = Load(PreDemolitionSettingsJson);

        Assert.True(s.SchedulerEnabled);
        Assert.Equal("21:30", s.SchedulerStartTime);
        Assert.Equal("23:45", s.SchedulerEndTime);
        Assert.True(s.SchedulerMonday);
        Assert.False(s.SchedulerTuesday);
        Assert.True(s.SchedulerWednesday);
        Assert.False(s.SchedulerThursday);
        Assert.True(s.SchedulerFriday);
        Assert.False(s.SchedulerSaturday);
        Assert.True(s.SchedulerSunday);

        Assert.True(s.IntensityRampEnabled);
        Assert.Equal(45, s.RampDurationMinutes);
        Assert.Equal(2.5, s.SchedulerMultiplier);
        Assert.True(s.EndSessionOnRampComplete);

        // All five ramp links default TRUE, so false-in-the-file is the only value that proves
        // the read happened.
        Assert.False(s.RampLinkFlashOpacity);
        Assert.False(s.RampLinkSpiralOpacity);
        Assert.False(s.RampLinkPinkFilterOpacity);
        Assert.False(s.RampLinkMasterAudio);
        Assert.False(s.RampLinkSubliminalAudio);
    }

    /// <summary>The PatreonTabView-owned keyword/OCR properties; live editor is the Awareness door.</summary>
    [Fact]
    public void KeywordAndOcrProperties_StillRoundTrip()
    {
        var s = Load(PreDemolitionSettingsJson);

        // KeywordTriggersEnabled is [JsonIgnore] BY DESIGN — a Patreon-gated listener that must be
        // re-armed each session, never silently resurrected from a file. Phase 8 moved its editor
        // from the dead PatreonTab to AwarenessTab.ChkAwarenessMaster; the non-persistence is
        // unchanged and is asserted here so a future "fix" to persist it is a deliberate act.
        Assert.NotNull(typeof(AppSettings).GetProperty(nameof(AppSettings.KeywordTriggersEnabled))!
                                          .GetCustomAttribute<JsonIgnoreAttribute>());
        Assert.False(s.KeywordTriggersEnabled);

        Assert.Equal(4500, s.KeywordBufferTimeoutMs);
        Assert.Equal(90, s.KeywordGlobalCooldownSeconds);
        Assert.Equal(1.75, s.KeywordSessionMultiplier);
        Assert.True(s.KeywordHighlightEnabled);
        Assert.Equal(2500, s.KeywordHighlightDurationMs);

        Assert.True(s.ScreenOcrEnabled);
        Assert.Equal(7000, s.ScreenOcrIntervalMs);
        Assert.True(s.OcrHighlightAll);
        Assert.Equal(3, s.OcrConfirmationScans);
        Assert.True(s.OcrHighlightVisibleInCapture);
    }

    // =====================================================================================
    //  3. the two things Phase 8 was explicitly forbidden to disturb
    // =====================================================================================

    [Fact]
    public void TheScrappedAttentionCheckPropertiesSurvivedTheirDeletedUi()
    {
        // Phase 8 deleted AttentionCheckSettingsDialog + AttentionCheckFeatureControl (zero
        // constructors anywhere), but AttentionCheckService still READS these six, and they were
        // persisted historically — so they keep round-tripping. Removing them would also be the
        // easiest way to accidentally take the LIVE video attention-target settings with them,
        // which are a different feature entirely (asserted above).
        var s = Load(PreDemolitionSettingsJson);

        Assert.True(s.AttentionCheckEnabled);
        Assert.Equal(2, s.AttentionCheckMinPerSession);
        Assert.Equal(9, s.AttentionCheckMaxPerSession);
        Assert.Equal(6500, s.AttentionCheckGraceMs);
        Assert.Equal(AppSettings.AttentionCheckFailModeKind.XpPenalty, s.AttentionCheckFailMode);
        Assert.Equal(AppSettings.AttentionCheckScopeKind.DuringSessionsOnly, s.AttentionCheckScope);
    }

    [Fact]
    public void NewYearNoteReactionSeenIsPreservedExactly()
    {
        // Hard rule 7. Once true it must STAY true across the demolition: resetting it re-arms a
        // once-ever moment on an install that already had it.
        Assert.True(Load(PreDemolitionSettingsJson).NewYearNoteReactionSeen);
        Assert.False(Load("{}").NewYearNoteReactionSeen);
    }

    // =====================================================================================
    //  4. the shapes either end of the upgrade
    // =====================================================================================

    [Fact]
    public void AFreshInstallStillReadsAsNotYetWelcomed()
    {
        // The first-run gate is `if (settings.Welcomed) return false`, so this default is what
        // decides whether a fresh box gets the wizard at all.
        Assert.False(Load("{}").Welcomed);
        Assert.False(Load("{}").FirstRunAssetsPromptShown);
    }

    [Fact]
    public void AnUpgraderIsNeverOfferedTheFirstRunWizard()
    {
        // Same file the gate reads on an upgrade install: Welcomed already true means
        // ShouldRunAndClaim short-circuits and MainWindow takes the untouched What's New branch.
        Assert.True(Load(PreDemolitionSettingsJson).Welcomed);
    }

    [Fact]
    public void AnUnknownLegacyKeyIsIgnoredRatherThanFatal()
    {
        // Settings files from builds that had properties we have since dropped must not poison the
        // load. (Nothing was dropped in Phase 8 — this guards the next phase that tries.)
        var s = Load("""{ "SomeKeyThatNoLongerExists": 12, "FlashFrequency": 15 }""");
        Assert.Equal(15, s.FlashFrequency);
    }
}
