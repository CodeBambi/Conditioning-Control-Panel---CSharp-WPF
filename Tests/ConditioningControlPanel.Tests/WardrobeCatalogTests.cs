using System;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The wardrobe's whole failure story is "a missing PNG or an unknown id renders the plain avatar"
/// (DESIGN.md Phase 3). These tests pin the degradation paths that hold whether or not the 60 art
/// files are present in the test host's output folder, so they stay honest on a machine where the
/// art stage has not run.
/// </summary>
public class WardrobeCatalogTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no_such_item")]
    [InlineData("bambi/../../secrets")]
    public void UnknownIdsHaveNoArtAndNoItem(string? id)
    {
        Assert.Null(WardrobeCatalog.GetImage(id));
        Assert.Null(WardrobeCatalog.Find(id));
        Assert.False(WardrobeCatalog.HasArt(id));
    }

    [Fact]
    public void CollectionsAreNeverNull()
    {
        Assert.NotNull(WardrobeCatalog.Items);
        Assert.NotNull(WardrobeCatalog.Mods);
        Assert.NotNull(WardrobeCatalog.ItemsFor("bambi", decorations: true));
        Assert.Empty(WardrobeCatalog.ItemsFor("no_such_mod", decorations: true));
        Assert.Empty(WardrobeCatalog.ItemsFor("no_such_mod", decorations: false));
    }

    /// <summary>
    /// Null and "empty set" mean different things to Sanitize: null is "cannot validate, pass the
    /// ids through", an empty set would be "nothing is valid, strip the loadout". The catalog must
    /// never produce the second one by accident.
    /// </summary>
    [Fact]
    public void KnownIdsIsNullOrNonEmptyNeverAnEmptySet()
    {
        var ids = WardrobeCatalog.KnownIds();
        if (ids != null) Assert.NotEmpty(ids);
    }

    [Fact]
    public void KnownIdsMatchesTheLoadedItems()
    {
        var ids = WardrobeCatalog.KnownIds();
        if (ids == null)
        {
            Assert.Empty(WardrobeCatalog.Items);
            return;
        }

        Assert.Equal(WardrobeCatalog.Items.Count, ids.Count);
        Assert.All(WardrobeCatalog.Items, item => Assert.Contains(item.Id, ids));
    }

    /// <summary>
    /// The two slot sets partition the registry and never overlap. ProfileCosmetics.Sanitize
    /// validates avatar_deco against one and charms against the other — exactly as the server does
    /// (ccp-server proxy/cosmetics.js) — so a union here would re-open the mix-up those sets exist
    /// to close: an id accepted into the wrong slot renders locally and is stripped on upload.
    /// </summary>
    [Fact]
    public void DecoAndCharmIdsPartitionTheRegistry()
    {
        var decos = WardrobeCatalog.DecoIds();
        var charms = WardrobeCatalog.CharmIds();

        if (WardrobeCatalog.KnownIds() == null)
        {
            // No registry on disk: both must be null so Sanitize reads "cannot validate".
            Assert.Null(decos);
            Assert.Null(charms);
            return;
        }

        Assert.NotNull(decos);
        Assert.NotNull(charms);
        Assert.Equal(WardrobeCatalog.Items.Count, decos!.Count + charms!.Count);
        Assert.Empty(decos.Intersect(charms));
        Assert.All(WardrobeCatalog.Items, item =>
            Assert.Contains(item.Id, item.IsDecoration ? decos : charms));
    }

    /// <summary>
    /// Every item is one slot or the other; the renderer has no third branch. When the registry is
    /// on disk this also guards the mod/type split the picker's tabs are built from.
    /// </summary>
    [Fact]
    public void EveryItemIsExactlyOneSlotType()
    {
        Assert.All(WardrobeCatalog.Items, item =>
        {
            Assert.NotEqual(item.IsDecoration, item.IsCharm);
            Assert.False(string.IsNullOrWhiteSpace(item.Id));
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
            Assert.False(string.IsNullOrWhiteSpace(item.Mod));
        });
    }

    [Fact]
    public void ItemsForReturnsOnlyThatModAndSlot()
    {
        foreach (var mod in WardrobeCatalog.Mods)
        {
            Assert.All(WardrobeCatalog.ItemsFor(mod, decorations: true), i =>
            {
                Assert.True(i.IsDecoration);
                Assert.Equal(mod, i.Mod, ignoreCase: true);
            });
            Assert.All(WardrobeCatalog.ItemsFor(mod, decorations: false), i =>
            {
                Assert.True(i.IsCharm);
                Assert.Equal(mod, i.Mod, ignoreCase: true);
            });
        }
    }

    /// <summary>
    /// The single piece of wardrobe geometry in the app: art is authored with the avatar circle at
    /// 70% of the canvas, so a 104px avatar draws its decoration at ~148.6px. If this constant ever
    /// drifts, every hat in the game slides off someone's head.
    /// </summary>
    [Fact]
    public void AvatarCircleRatioIsTheAuthoringRatio()
    {
        Assert.Equal(0.70, WardrobeCatalog.AvatarCircleRatio, 3);
        Assert.Equal(148.571, 104d / WardrobeCatalog.AvatarCircleRatio, 3);
    }

    [Fact]
    public void InvalidateIsSafeAndReloads()
    {
        var before = WardrobeCatalog.Items.Count;
        WardrobeCatalog.Invalidate();
        Assert.Equal(before, WardrobeCatalog.Items.Count);
    }
}
