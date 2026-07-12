using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Core.Services.AIService;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Tests for <see cref="SystemPromptBuilder"/> base-persona branch precedence (community > preset >
/// default) and the portable <see cref="KnownVideoLinks"/> URL->name resolution. These exercise the
/// AI-11 port of WPF <c>BambiSprite.GetSystemPrompt</c> branches 1-3 (BambiSprite.cs:522-543) and the
/// hypnotube reverse-lookup (BambiSprite.cs:651-669).
/// </summary>
public sealed class SystemPromptBuilderTests
{
    // ---- Branch precedence: community > preset > default ----

    [Fact]
    public void CommunityBranch_WinsOverPreset_AndUsesCompanionPrompt()
    {
        // A user preset is active, but a community/custom prompt is configured -> community wins
        // (WPF BambiSprite.cs:522-535). The community branch builds from settings.CompanionPrompt.
        var settings = NewSettings();
        settings.ActivePersonalityPresetId = "user-preset-1";
        settings.UserPersonalityPresets.Add(new PersonalityPreset
        {
            Id = "user-preset-1",
            Name = "User",
            PromptSettings = new CompanionPromptSettings { Personality = "PRESET-SHOULD-NOT-APPEAR" }
        });
        settings.CompanionPrompt.UseCustomPrompt = true;
        settings.ActiveCommunityPromptId = "community-1";
        settings.CompanionPrompt.Personality = "COMMUNITY-MARKER-X";

        var output = new SystemPromptBuilder(new FakeSettingsService(settings)).GetSystemPrompt();

        Assert.Contains("COMMUNITY-MARKER-X", output);
        Assert.DoesNotContain("PRESET-SHOULD-NOT-APPEAR", output);
    }

    [Fact]
    public void PresetBranch_UsesActivePresetPromptSettings()
    {
        // No community prompt -> the resolved active preset's PromptSettings is the base persona
        // (WPF BambiSprite.cs:536-543 + PersonalityService.GetActivePreset). A user preset is the
        // active one here so its personality shows up, proving the preset branch (not the default
        // fallback and not settings.CompanionPrompt) supplied the base persona.
        var settings = NewSettings();
        settings.ActivePersonalityPresetId = "user-preset-1";
        settings.UserPersonalityPresets.Add(new PersonalityPreset
        {
            Id = "user-preset-1",
            Name = "User",
            PromptSettings = new CompanionPromptSettings { Personality = "PRESET-MARKER-Y" }
        });

        var output = new SystemPromptBuilder(new FakeSettingsService(settings)).GetSystemPrompt();

        Assert.Contains("PRESET-MARKER-Y", output);
    }

    [Fact]
    public void DefaultFallback_WhenPresetHasNoPromptSettings()
    {
        // When the resolved preset has null PromptSettings, the GetDefaultBambiSpritePrompt
        // fallback runs (WPF BambiSprite.cs:542 + :796-904). Defensively unreachable in normal WPF
        // flow but ported faithfully; forced here via a user preset with null PromptSettings.
        var settings = NewSettings();
        settings.ActivePersonalityPresetId = "empty-preset";
        settings.UserPersonalityPresets.Add(new PersonalityPreset
        {
            Id = "empty-preset",
            Name = "Empty",
            PromptSettings = null
        });

        var output = new SystemPromptBuilder(new FakeSettingsService(settings)).GetSystemPrompt();

        Assert.Contains("Bad Influence Bestie", output);
    }

    // ---- KnownVideoLinks URL->name resolution (hypnotube block) ----

    [Fact]
    public void Hypnotube_KnownUrl_ResolvesToTableFriendlyName()
    {
        // WPF BambiSprite.cs:651-669: a URL present in KnownVideoLinks resolves to its friendly
        // display name (not the slug-derived form). The default active preset (BambiSprite) drives
        // the 12-step assembly; mods=null so the mod ships no pool and the hypnotube block runs.
        var settings = NewSettings();
        settings.HypnotubeLinksSissyHypno =
            "https://hypnotube.com/video/bambi-tiktok-in-beat-longer-version-56194.html";

        var output = new SystemPromptBuilder(new FakeSettingsService(settings)).GetSystemPrompt();

        // Table name (preserves "TikTok" casing and " - " separators) must appear.
        Assert.Contains("- \"Bambi TikTok - In Beat - Longer Version\"", output);
        // The slug-fallback form must NOT appear (it would title-case to "Tiktok" with no dashes).
        Assert.DoesNotContain("Bambi Tiktok In Beat Longer Version", output);
    }

    [Fact]
    public void Hypnotube_UnknownUrl_FallsBackToSlugDerivedName()
    {
        // A URL not in the table falls back to the slug-derived readable name
        // (WPF BambiSprite.cs:661-668 / SystemPromptBuilder.SlugToName).
        var settings = NewSettings();
        settings.HypnotubeLinksSissyHypno =
            "https://hypnotube.com/video/brand-new-unlisted-clip-99999.html";

        var output = new SystemPromptBuilder(new FakeSettingsService(settings)).GetSystemPrompt();

        Assert.Contains("- \"Brand New Unlisted Clip\"", output);
    }

    // ---- KnownVideoLinks table unit tests ----

    [Fact]
    public void KnownVideoLinks_TryGetName_Hit()
    {
        var ok = KnownVideoLinks.TryGetName(
            "https://hypnotube.com/video/naughty-bambi-109749.html", out var name);

        Assert.True(ok);
        Assert.Equal("Naughty Bambi", name);
    }

    [Fact]
    public void KnownVideoLinks_TryGetName_IsCaseInsensitiveOnUrl()
    {
        // Mirrors the WPF OrdinalIgnoreCase reverse-map (BambiSprite.cs:652-653).
        var ok = KnownVideoLinks.TryGetName(
            "HTTPS://HYPNOTUBE.COM/video/naughty-bambi-109749.html", out var name);

        Assert.True(ok);
        Assert.Equal("Naughty Bambi", name);
    }

    [Fact]
    public void KnownVideoLinks_TryGetName_Miss()
    {
        var ok = KnownVideoLinks.TryGetName(
            "https://hypnotube.com/video/does-not-exist-99999.html", out var name);

        Assert.False(ok);
        Assert.Null(name);
    }

    [Fact]
    public void KnownVideoLinks_HypnotubeVideoNames_ExcludesBambiCloudPlaylists()
    {
        // BambiCloud entries are audio playlists; only HypnoTube videos belong in the video-name set.
        var names = KnownVideoLinks.HypnotubeVideoNames.ToList();

        Assert.Contains("Naughty Bambi", names);
        Assert.Contains("Ultimate Sissy Mindfuck", names);
        Assert.DoesNotContain("IQ Programming", names);
    }

    // ---- helpers ----

    private static AppSettings NewSettings() => new();

    private sealed class FakeSettingsService : ISettingsService
    {
        public FakeSettingsService(AppSettings current) { Current = current; }
        public AppSettings Current { get; }
        public bool WasSettingsFileMissing => false;
        public List<string> PendingPresetReinstalls { get; } = new();
        public void Save() { }
        public void Save(bool suppressCloudBackup = false) { }
        public void SaveImmediate(bool suppressCloudBackup = false) { }
        public void RestoreFrom(AppSettings settings) { }
        public void Reset() { }
    }
}
