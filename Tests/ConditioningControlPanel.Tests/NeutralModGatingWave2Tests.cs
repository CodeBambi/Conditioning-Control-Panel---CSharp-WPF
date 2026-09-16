using System.Collections.Generic;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Quiz;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The second pass over Wave 1's ungated sites. #1053 closed four; a review of the release branch
/// found three more places where "nothing changes for anyone who already has a mod active" was
/// false, and all three are pinned here:
///
/// <list type="number">
///   <item>the weekly Intake niche, where the BambiSleep branch went missing and took the mod's
///         pass card and prompt bank with it;</item>
///   <item><c>Identity.PetName</c>, declared but set by no themed built-in, which made the Deeper
///         speak prompt praise a Bambi user with the vanilla word;</item>
///   <item>the marquee banner migration, which rewrote a themed mod's default banner once and
///         permanently on the first launch of 6.9.4.</item>
/// </list>
///
/// Everything here reads the built-in manifests or a pure resolver, so no App statics are needed.
/// </summary>
public class NeutralModGatingWave2Tests
{
    // ---- Intake niche ------------------------------------------------------

    /// <summary>
    /// The regression that started this pass: Wave 1 moved the fallback from "bambi" to "default"
    /// without adding the branch the other three themed built-ins have, so "bambi" became
    /// unreachable and every Bambi user silently changed pass card and prompt bank.
    /// </summary>
    [Theory]
    [InlineData(BuiltInMods.BambiSleepId, "bambi")]
    [InlineData(BuiltInMods.SissyHypnoId, "sissy")]
    [InlineData(BuiltInMods.DronificationId, "drone")]
    [InlineData(BuiltInMods.LockedId, "circe")]
    public void Every_themed_builtin_names_its_own_intake_niche(string modId, string expected)
        => Assert.Equal(expected, IntakeNiche.Resolve(modId, tags: null, sissyContentMode: false));

    [Fact]
    public void CcpDefault_and_no_mod_stay_on_the_neutral_niche()
    {
        Assert.Equal(IntakeNiche.Fallback, IntakeNiche.Resolve(BuiltInMods.CCPDefaultId, null, false));
        Assert.Equal(IntakeNiche.Fallback, IntakeNiche.Resolve(null, null, false));
    }

    /// <summary>A third-party .ccpmod asks through its manifest tags instead of an id.</summary>
    [Theory]
    [InlineData("bambi", "bambi")]
    [InlineData("Sissy", "sissy")]
    [InlineData("DRONE", "drone")]
    [InlineData("chastity", "circe")]
    public void A_third_party_mod_asks_for_a_niche_by_tag(string tag, string expected)
        => Assert.Equal(expected, IntakeNiche.Resolve("some-creator-mod", new List<string> { tag }, false));

    /// <summary>
    /// The legacy enum has exactly one positive reading. Its other value means "no sissy mod", not
    /// "bambi", so it must not resurrect the pre-6.9.4 themed fallback.
    /// </summary>
    [Fact]
    public void The_legacy_content_mode_only_speaks_for_sissy()
    {
        Assert.Equal("sissy", IntakeNiche.Resolve(null, null, sissyContentMode: true));
        Assert.Equal(IntakeNiche.Fallback, IntakeNiche.Resolve(null, null, sissyContentMode: false));
    }

    /// <summary>Every niche the resolver can return has to be one this build ships art for.</summary>
    [Fact]
    public void Every_resolvable_niche_is_a_shipped_niche()
    {
        foreach (var modId in new[]
                 {
                     BuiltInMods.BambiSleepId, BuiltInMods.SissyHypnoId,
                     BuiltInMods.DronificationId, BuiltInMods.LockedId, BuiltInMods.CCPDefaultId
                 })
        {
            Assert.Contains(IntakeNiche.Resolve(modId, null, false), IntakeNiche.All);
        }
    }

    // ---- Pet name ----------------------------------------------------------

    /// <summary>
    /// #1053 pointed the Deeper seeded praise at <c>VocabTokens.PetName</c>, but no themed built-in
    /// set one, so the swap was a no-op and every themed mod read the vanilla word.
    /// </summary>
    [Theory]
    [InlineData(nameof(BuiltInMods.BambiSleep))]
    [InlineData(nameof(BuiltInMods.SissyHypno))]
    [InlineData(nameof(BuiltInMods.Dronification))]
    [InlineData(nameof(BuiltInMods.Locked))]
    public void Every_themed_builtin_names_its_own_pet_name(string mod)
    {
        var identity = Manifest(mod).Identity!;

        Assert.False(string.IsNullOrWhiteSpace(identity.PetName));
        Assert.False(string.IsNullOrWhiteSpace(identity.Collective));
        Assert.NotEqual(VocabTokens.VanillaPetName, identity.PetName);
    }

    /// <summary>
    /// The one that has to read exactly right: in 6.9.3 a Bambi user's speak prompt praised them
    /// with "good girl", and the seed is now "that's it, {petname}".
    /// </summary>
    [Fact]
    public void BambiSleep_speak_prompt_praise_still_says_good_girl()
    {
        Assert.Equal("good girl", BuiltInMods.BambiSleep.Identity!.PetName);
        Assert.Equal("that's it, good girl", $"that's it, {BuiltInMods.BambiSleep.Identity!.PetName}");
    }

    /// <summary>
    /// CCP Default must NOT name one. VocabTokens owns the owner-locked vanilla pair, and a second
    /// copy here would drift the first time one of the two is edited.
    /// </summary>
    [Fact]
    public void CcpDefault_leaves_the_vanilla_pet_name_to_VocabTokens()
    {
        Assert.Null(BuiltInMods.CCPDefault.Identity!.PetName);
        Assert.Null(BuiltInMods.CCPDefault.Identity!.Collective);
        Assert.Equal("sweetie", VocabTokens.VanillaPetName);
    }

    // ---- Marquee banner ----------------------------------------------------

    /// <summary>An unmodded install still gets moved off the gendered default exactly once.</summary>
    [Fact]
    public void The_marquee_migration_still_runs_for_an_unmodded_install()
    {
        var settings = new AppSettings { MarqueeMessage = AppSettings.LegacyGenderedMarqueeMessage };

        settings.MigrateMarqueeMessage(BuiltInMods.CCPDefaultId);

        Assert.Equal(AppSettings.DefaultMarqueeMessage, settings.MarqueeMessage);
        Assert.True(settings.MarqueeNeutralDefaultMigrated);
    }

    /// <summary>
    /// The regression: a themed mod's banner is that mod's voice, and rewriting it is a visible,
    /// one-way change to a user who was promised none. The flag still latches, so a later switch to
    /// CCP Default does not spring the same rewrite on them months afterwards.
    /// </summary>
    [Theory]
    [InlineData(BuiltInMods.BambiSleepId)]
    [InlineData(BuiltInMods.SissyHypnoId)]
    [InlineData(BuiltInMods.DronificationId)]
    [InlineData(BuiltInMods.LockedId)]
    [InlineData("some-creator-mod")]
    public void A_themed_mod_keeps_its_banner_and_is_never_asked_again(string modId)
    {
        var settings = new AppSettings { MarqueeMessage = AppSettings.LegacyGenderedMarqueeMessage };

        settings.MigrateMarqueeMessage(modId);

        Assert.Equal(AppSettings.LegacyGenderedMarqueeMessage, settings.MarqueeMessage);
        Assert.True(settings.MarqueeNeutralDefaultMigrated);
    }

    /// <summary>
    /// No mod service yet means no answer yet. The migration defers rather than guessing, because
    /// guessing wrong here is permanent.
    /// </summary>
    [Fact]
    public void An_unknown_mod_defers_the_migration_instead_of_guessing()
    {
        var settings = new AppSettings { MarqueeMessage = AppSettings.LegacyGenderedMarqueeMessage };

        settings.MigrateMarqueeMessage(null);

        Assert.Equal(AppSettings.LegacyGenderedMarqueeMessage, settings.MarqueeMessage);
        Assert.False(settings.MarqueeNeutralDefaultMigrated);
    }

    /// <summary>A banner the user wrote is never touched, in any mod.</summary>
    [Fact]
    public void A_hand_written_banner_survives_the_migration()
    {
        var settings = new AppSettings { MarqueeMessage = "MY OWN WORDS" };

        settings.MigrateMarqueeMessage(BuiltInMods.CCPDefaultId);

        Assert.Equal("MY OWN WORDS", settings.MarqueeMessage);
    }

    /// <summary>It is a one-shot: a user who types the old text back in keeps it.</summary>
    [Fact]
    public void The_migration_never_runs_twice()
    {
        var settings = new AppSettings
        {
            MarqueeMessage = AppSettings.LegacyGenderedMarqueeMessage,
            MarqueeNeutralDefaultMigrated = true
        };

        settings.MigrateMarqueeMessage(BuiltInMods.CCPDefaultId);

        Assert.Equal(AppSettings.LegacyGenderedMarqueeMessage, settings.MarqueeMessage);
    }

    /// <summary>
    /// The blank-banner fallback resolves through the mod, so every built-in has to name one, and
    /// CCP Default's has to be the neutral const the settings default already uses.
    /// </summary>
    [Fact]
    public void CcpDefault_banner_is_the_neutral_settings_default()
        => Assert.Equal(AppSettings.DefaultMarqueeMessage, BuiltInMods.CCPDefault.Messages!.MarqueeBanner);

    [Fact]
    public void BambiSleep_and_SissyHypno_keep_the_6_9_3_banner()
    {
        Assert.Equal(AppSettings.LegacyGenderedMarqueeMessage, BuiltInMods.BambiSleep.Messages!.MarqueeBanner);
        Assert.Equal(AppSettings.LegacyGenderedMarqueeMessage, BuiltInMods.SissyHypno.Messages!.MarqueeBanner);
    }

    [Theory]
    [InlineData(nameof(BuiltInMods.Dronification))]
    [InlineData(nameof(BuiltInMods.Locked))]
    public void The_two_mods_the_6_9_3_banner_never_fitted_speak_for_themselves(string mod)
    {
        var banner = Manifest(mod).Messages!.MarqueeBanner;

        Assert.False(string.IsNullOrWhiteSpace(banner));
        Assert.NotEqual(AppSettings.LegacyGenderedMarqueeMessage, banner);
        Assert.NotEqual(AppSettings.DefaultMarqueeMessage, banner);
    }

    /// <summary>Author-supplied in a downloaded .ccpmod, so capped like every other manifest string.</summary>
    [Fact]
    public void An_over_long_mod_banner_is_truncated_not_rejected()
    {
        var manifest = new ModManifest
        {
            Id = "test-banner-cap",
            Name = "Banner cap",
            Messages = new ModMessages { MarqueeBanner = new string('x', 900) }
        };

        var error = ModService.SanitizeManifest(manifest);

        Assert.Null(error);
        Assert.Equal(120, manifest.Messages.MarqueeBanner!.Length);
    }

    private static ModManifest Manifest(string name) => name switch
    {
        nameof(BuiltInMods.BambiSleep) => BuiltInMods.BambiSleep,
        nameof(BuiltInMods.SissyHypno) => BuiltInMods.SissyHypno,
        nameof(BuiltInMods.Dronification) => BuiltInMods.Dronification,
        nameof(BuiltInMods.Locked) => BuiltInMods.Locked,
        _ => BuiltInMods.CCPDefault
    };
}
