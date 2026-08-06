using System.Collections.Generic;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ProfileCosmetics.Sanitize is the only funnel between untrusted cosmetics data and the Trainer
/// Card renderer. Three sources feed it — a hand-editable settings.json, a server echo that may
/// predate the field, and ANOTHER USER's loadout out of /user/lookup — and the spec's rule for all
/// three is the same: unknown or invalid ids degrade to "none", never a crash and never a blocked
/// card. These tests pin that contract, plus the two clamps (4 pins, 2 charms) the server also
/// enforces, and the deliberate asymmetry that the unlock filter only applies where unlocks are
/// actually knowable (your own card).
/// </summary>
public class ProfileCosmeticsSanitizeTests
{
    private static readonly HashSet<string> Banners = new() { "gradient_velvet", "program_kept" };
    private static readonly HashSet<string> Achievements = new() { "a1", "a2", "a3", "a4", "a5", "a6" };
    private static readonly HashSet<string> Unlocked = new() { "a1", "a2", "a3", "a4", "a5" };
    // Typed like the registry (and like the server): a decoration is worn over the avatar, a charm
    // sits in a card corner, and the two sets are validated separately.
    private static readonly HashSet<string> Decos = new() { "bambi_silk_bow", "circe_visor" };
    private static readonly HashSet<string> Charms = new() { "bambi_lollipop", "sissy_heel" };

    private static ProfileCosmetics Clean(ProfileCosmetics? raw, HashSet<string>? unlocked = null)
        => ProfileCosmetics.Sanitize(raw, Banners, Achievements, unlocked, Decos, Charms);

    [Fact]
    public void NullInputIsAnEmptyLoadout()
    {
        var clean = Clean(null);
        Assert.True(clean.IsEmpty);
        Assert.Empty(clean.PinnedAchievements);
        Assert.Empty(clean.Charms);
    }

    [Fact]
    public void UnknownBannerDegradesToNone()
        => Assert.Null(Clean(new ProfileCosmetics { BannerId = "banner_from_a_newer_build" }).BannerId);

    [Fact]
    public void KnownBannerSurvives()
        => Assert.Equal("program_kept", Clean(new ProfileCosmetics { BannerId = "program_kept" }).BannerId);

    [Fact]
    public void BannerIsNotValidatedWhenNoPoolIsSupplied()
    {
        // A caller that cannot enumerate the art pool (tests, headless paths) must not have its
        // ids stripped — the renderer degrades on its own when the asset is missing.
        var clean = ProfileCosmetics.Sanitize(new ProfileCosmetics { BannerId = "anything" });
        Assert.Equal("anything", clean.BannerId);
    }

    [Theory]
    [InlineData("#FF69B4")]
    [InlineData("#b478ff")]   // case is normalized, not rejected
    [InlineData("FFD700")]    // a missing '#' is repaired
    public void CuratedAccentsAreAccepted(string input)
        => Assert.NotNull(Clean(new ProfileCosmetics { Accent = input }).Accent);

    [Theory]
    [InlineData("#123456")]   // off-palette
    [InlineData("red")]       // named colour
    [InlineData("#FF69B")]    // malformed
    [InlineData("   ")]
    public void OffPaletteAccentsAreRejected(string input)
        => Assert.Null(Clean(new ProfileCosmetics { Accent = input }).Accent);

    [Fact]
    public void AccentIsNormalizedToUpperHash()
        => Assert.Equal("#B478FF", Clean(new ProfileCosmetics { Accent = "b478ff" }).Accent);

    [Fact]
    public void LockedTitleIsDroppedOnYourOwnCard()
        => Assert.Null(Clean(new ProfileCosmetics { TitleId = "a6" }, Unlocked).TitleId);

    [Fact]
    public void UnlockedTitleSurvivesOnYourOwnCard()
        => Assert.Equal("a1", Clean(new ProfileCosmetics { TitleId = "a1" }, Unlocked).TitleId);

    [Fact]
    public void ViewedProfileKeepsTitlesWeCannotVerify()
    {
        // /user/lookup never says which achievements THEY unlocked. Applying our own unlock set
        // there would blank out most of the community's titles, so the filter is skipped.
        var clean = Clean(new ProfileCosmetics { TitleId = "a6" }, unlocked: null);
        Assert.Equal("a6", clean.TitleId);
    }

    [Fact]
    public void TitleUnknownToThisBuildIsDroppedEitherWay()
    {
        Assert.Null(Clean(new ProfileCosmetics { TitleId = "retired_achievement" }, Unlocked).TitleId);
        Assert.Null(Clean(new ProfileCosmetics { TitleId = "retired_achievement" }, null).TitleId);
    }

    [Fact]
    public void PinsAreClampedToFour()
    {
        var clean = Clean(new ProfileCosmetics
        {
            PinnedAchievements = new List<string> { "a1", "a2", "a3", "a4", "a5" }
        }, Unlocked);

        Assert.Equal(ProfileCosmetics.MaxPinnedAchievements, clean.PinnedAchievements.Count);
        Assert.Equal(new[] { "a1", "a2", "a3", "a4" }, clean.PinnedAchievements);
    }

    [Fact]
    public void PinsDropDuplicatesAndLockedIdsBeforeTheClamp()
    {
        // "a6" is locked and "junk" is unknown: both must be discarded WITHOUT consuming a slot,
        // otherwise a garbage payload could squeeze out real pins.
        var clean = Clean(new ProfileCosmetics
        {
            PinnedAchievements = new List<string> { "a1", "a1", "a6", "junk", "a2", "a3", "a4" }
        }, Unlocked);

        Assert.Equal(new[] { "a1", "a2", "a3", "a4" }, clean.PinnedAchievements);
    }

    [Fact]
    public void CharmsAreClampedToTwoAndValidated()
    {
        var clean = Clean(new ProfileCosmetics
        {
            Charms = new List<string> { "sissy_heel", "not_in_registry", "bambi_lollipop", "sissy_heel" }
        });

        Assert.Equal(new[] { "sissy_heel", "bambi_lollipop" }, clean.Charms);
    }

    [Fact]
    public void SlotsAreValidatedByTypeNotByTheUnionOfTheRegistry()
    {
        // The server checks avatar_deco against its deco ids and charms against its charm ids
        // (ccp-server proxy/cosmetics.js). A union check here would let a charm equip into the
        // decoration slot, render on the wearer's own card, and be silently stripped on upload —
        // so nobody else would ever see it and the wearer would never know.
        var clean = Clean(new ProfileCosmetics
        {
            AvatarDeco = "bambi_lollipop",                       // a charm in the deco slot
            Charms = new List<string> { "circe_visor" }          // a deco in a charm slot
        });

        Assert.Null(clean.AvatarDeco);
        Assert.Empty(clean.Charms);
    }

    [Fact]
    public void WardrobePayloadRoundTripsWhenTheRegistryIsUnavailable()
    {
        // A Phase 2 client with no registry on disk must not silently strip a Phase 3 loadout.
        var clean = ProfileCosmetics.Sanitize(
            new ProfileCosmetics
            {
                AvatarDeco = "sissy_pearl_choker",
                Charms = new List<string> { "bambi_lollipop" }
            },
            Banners, Achievements, Unlocked, knownDecoIds: null, knownCharmIds: null);

        Assert.Equal("sissy_pearl_choker", clean.AvatarDeco);
        Assert.Equal(new[] { "bambi_lollipop" }, clean.Charms);
    }

    [Fact]
    public void BlankStringsBecomeNull()
    {
        var clean = Clean(new ProfileCosmetics
        {
            BannerId = "  ",
            Accent = "",
            TitleId = "\t",
            PinnedAchievements = new List<string> { "", "  ", null! }
        });

        Assert.True(clean.IsEmpty);
    }

    [Fact]
    public void CloneIsDetached()
    {
        var original = new ProfileCosmetics
        {
            BannerId = "program_kept",
            PinnedAchievements = new List<string> { "a1" }
        };

        var copy = original.Clone();
        copy.BannerId = "gradient_velvet";
        copy.PinnedAchievements.Add("a2");

        Assert.Equal("program_kept", original.BannerId);
        Assert.Single(original.PinnedAchievements);
    }

    [Fact]
    public void SerializesWithTheSnakeCaseContractTheServerExpects()
    {
        var json = JsonConvert.SerializeObject(new ProfileCosmetics
        {
            BannerId = "program_kept",
            Accent = "#FFD700",
            TitleId = "a1",
            PinnedAchievements = new List<string> { "a1" },
            AvatarDeco = "bambi_silk_bow",
            Charms = new List<string> { "circe_visor" }
        });

        Assert.Contains("\"banner_id\":\"program_kept\"", json);
        Assert.Contains("\"accent\":\"#FFD700\"", json);
        Assert.Contains("\"title_id\":\"a1\"", json);
        Assert.Contains("\"pinned_achievements\":[\"a1\"]", json);
        Assert.Contains("\"avatar_deco\":\"bambi_silk_bow\"", json);
        Assert.Contains("\"charms\":[\"circe_visor\"]", json);
    }

    [Fact]
    public void DeserializesAServerPayloadIncludingUnknownFields()
    {
        // Forward compatibility: a newer server adding a field must not break an older client.
        const string payload = """
        {
          "banner_id": "program_kept",
          "accent": "#FF69B4",
          "title_id": "a2",
          "pinned_achievements": ["a2", "a3"],
          "avatar_deco": null,
          "charms": [],
          "frame_id": "something_from_the_future"
        }
        """;

        var parsed = JsonConvert.DeserializeObject<ProfileCosmetics>(payload);
        var clean = Clean(parsed, Unlocked);

        Assert.Equal("program_kept", clean.BannerId);
        Assert.Equal("#FF69B4", clean.Accent);
        Assert.Equal("a2", clean.TitleId);
        Assert.Equal(new[] { "a2", "a3" }, clean.PinnedAchievements);
        Assert.Null(clean.AvatarDeco);
    }

    [Fact]
    public void AnEmptyLoadoutIsReportedEmpty()
    {
        Assert.True(new ProfileCosmetics().IsEmpty);
        Assert.False(new ProfileCosmetics { Accent = "#FFD700" }.IsEmpty);
        Assert.False(new ProfileCosmetics { PinnedAchievements = new List<string> { "a1" } }.IsEmpty);
    }

    // ---- wardrobe editor transforms ----

    [Fact]
    public void TransformValuesAreClampedToTheirEnvelopes()
    {
        var clean = Clean(new ProfileCosmetics
        {
            AvatarDeco = "bambi_silk_bow",
            DecoTransform = new CosmeticTransform { X = 9, Y = -9, Scale = 50, Rotation = 720 },
            Charms = new List<string> { "bambi_lollipop" },
            CharmTransforms = new Dictionary<string, CosmeticTransform>
            {
                ["bambi_lollipop"] = new() { X = -3, Y = 4, Scale = 0.0001, Rotation = -999 }
            }
        });

        Assert.NotNull(clean.DecoTransform);
        Assert.Equal(0.75, clean.DecoTransform!.X);        // deco offsets clamp to [-0.75, 0.75]
        Assert.Equal(-0.75, clean.DecoTransform.Y);
        Assert.Equal(3.0, clean.DecoTransform.Scale);      // scale clamps to [0.3, 3]
        Assert.Equal(180, clean.DecoTransform.Rotation);   // rotation clamps to [-180, 180]

        var charm = clean.CharmTransforms!["bambi_lollipop"];
        Assert.Equal(0, charm.X);                          // charm centres clamp to the card, [0, 1]
        Assert.Equal(1, charm.Y);
        Assert.Equal(0.3, charm.Scale);
        Assert.Equal(-180, charm.Rotation);
    }

    [Fact]
    public void TransformsForUnwornItemsAreDropped()
    {
        var clean = Clean(new ProfileCosmetics
        {
            // No deco equipped, and a charm transform pointing at an id that is not worn.
            DecoTransform = new CosmeticTransform { X = 0.2 },
            Charms = new List<string> { "bambi_lollipop" },
            CharmTransforms = new Dictionary<string, CosmeticTransform>
            {
                ["sissy_heel"] = new() { X = 0.5, Y = 0.5 }
            }
        });

        Assert.Null(clean.DecoTransform);
        Assert.Null(clean.CharmTransforms);
    }

    [Fact]
    public void AnIdentityDecoTransformIsNotCarried()
    {
        // Payload hygiene: a deco transform the editor put back to defaults must serialize away.
        var clean = Clean(new ProfileCosmetics
        {
            AvatarDeco = "bambi_silk_bow",
            DecoTransform = new CosmeticTransform()
        });
        Assert.Null(clean.DecoTransform);
    }

    [Fact]
    public void TransformsRoundTripThroughJson()
    {
        var json = JsonConvert.SerializeObject(new ProfileCosmetics
        {
            AvatarDeco = "bambi_silk_bow",
            DecoTransform = new CosmeticTransform { X = 0.1, Y = -0.2, Scale = 1.5, Rotation = 30, Flip = true },
            Charms = new List<string> { "bambi_lollipop" },
            CharmTransforms = new Dictionary<string, CosmeticTransform>
            {
                ["bambi_lollipop"] = new() { X = 0.9, Y = 0.8, Scale = 0.8 }
            }
        });

        Assert.Contains("\"deco_transform\"", json);
        Assert.Contains("\"charm_transforms\"", json);

        var parsed = JsonConvert.DeserializeObject<ProfileCosmetics>(json)!;
        var clean = Clean(parsed, Unlocked);
        Assert.Equal(0.1, clean.DecoTransform!.X);
        Assert.True(clean.DecoTransform.Flip);
        Assert.Equal(0.9, clean.CharmTransforms!["bambi_lollipop"].X);
    }

    [Fact]
    public void PreEditorPayloadsCarryNoTransformFields()
    {
        // Backward compatibility: a loadout that was never arranged serializes byte-identical to
        // what a pre-editor client sent, so the server sees no phantom field churn.
        var json = JsonConvert.SerializeObject(new ProfileCosmetics { AvatarDeco = "bambi_silk_bow" });
        Assert.DoesNotContain("deco_transform", json);
        Assert.DoesNotContain("charm_transforms", json);
    }
}
