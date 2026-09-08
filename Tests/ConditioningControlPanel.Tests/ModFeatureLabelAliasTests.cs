using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Mod feature-label renames: the singular/plural alias fill
/// (<c>ModService.FillFeatureLabelAliases</c>, run from <c>SanitizeManifest</c>) and the Infection
/// Control built-in stub.
///
/// <para><b>The lost rename (Infection Control port ticket).</b> The English literal in the UI IS
/// the lookup key - <c>MainWindow.ModAwareLabel</c> probes <c>MakeModAware("Mandatory Video")</c>
/// for the dashboard card and <c>"Mandatory Videos"</c> for the preset detail row. Infection
/// Control's mod.json renames only the plural, so "Goon Fuel" appeared on one surface and the
/// stock name stayed on the other. The pack is a downloaded artifact and cannot be patched from
/// here, so the missing twin is filled in on load for every mod.</para>
/// </summary>
public class ModFeatureLabelAliasTests
{
    [Fact]
    public void A_plural_only_rename_reaches_the_singular_probe()
    {
        var map = new Dictionary<string, string> { ["Mandatory Videos"] = "Goon Fuel" };

        ModService.FillFeatureLabelAliases(map);

        Assert.Equal("Goon Fuel", ModService.ApplyTextReplacements(map, "Mandatory Video"));
        Assert.Equal("Goon Fuel", ModService.ApplyTextReplacements(map, "Mandatory Videos"));
    }

    [Fact]
    public void A_singular_only_rename_reaches_the_plural_probe()
    {
        var map = new Dictionary<string, string> { ["Lock Card"] = "Affirmations" };

        ModService.FillFeatureLabelAliases(map);

        Assert.Equal("Affirmations", ModService.ApplyTextReplacements(map, "Lock Card"));
        Assert.Equal("Affirmations", ModService.ApplyTextReplacements(map, "Lock Cards"));
    }

    /// <summary>
    /// An author who spelled both forms out meant both forms. The fill only ever ADDS a key.
    /// </summary>
    [Fact]
    public void An_author_supplied_twin_is_never_overwritten()
    {
        var map = new Dictionary<string, string>
        {
            ["Mandatory Videos"] = "Mandatory Playback",
            ["Mandatory Video"] = "A Single Playback",
        };

        ModService.FillFeatureLabelAliases(map);

        Assert.Equal("A Single Playback", map["Mandatory Video"]);
        Assert.Equal("Mandatory Playback", map["Mandatory Videos"]);
    }

    [Fact]
    public void A_manifest_that_renames_neither_form_gains_nothing()
    {
        var map = new Dictionary<string, string> { ["Bambi"] = "Unit" };

        ModService.FillFeatureLabelAliases(map);

        Assert.Single(map);
    }

    /// <summary>
    /// The longest-key-first rule still decides, so filling the shorter twin cannot start
    /// half-rewriting the longer phrase that contains it.
    /// </summary>
    [Fact]
    public void The_longer_key_still_wins_over_the_alias()
    {
        var map = new Dictionary<string, string>
        {
            ["Mandatory Videos"] = "Goon Fuel",
            ["Mandatory Video Attention Check"] = "Vitals Check",
        };

        ModService.FillFeatureLabelAliases(map);

        Assert.Equal("Vitals Check", ModService.ApplyTextReplacements(map, "Mandatory Video Attention Check"));
    }

    /// <summary>
    /// The built-in stub is what the user sees until the 200 MB pack finishes extracting (and
    /// forever, if it never downloads). It has to carry the feature renames and the tube fit the
    /// pack ships, or the mod activates wearing half of someone else's name.
    /// </summary>
    [Fact]
    public void The_infection_control_stub_carries_the_pack_feature_renames()
    {
        var manifest = BuiltInMods.InfectionControl;
        var map = manifest.TextReplacements;

        Assert.NotNull(map);
        Assert.Equal("Goon Fuel", ModService.ApplyTextReplacements(map, "Mandatory Video"));
        Assert.Equal("Goon Fuel", ModService.ApplyTextReplacements(map, "Mandatory Videos"));
        Assert.Equal("Affirmations", ModService.ApplyTextReplacements(map, "Lock Card"));
        Assert.Equal("Affirmations", ModService.ApplyTextReplacements(map, "Lock Cards"));
        Assert.Equal("Quarantine", ModService.ApplyTextReplacements(map, "Lockdown"));
    }

    [Fact]
    public void The_infection_control_stub_carries_the_pack_tube_fit()
    {
        var layout = BuiltInMods.InfectionControl.TubeLayout;

        Assert.NotNull(layout);
        Assert.Equal(0.9, layout!.AvatarScale ?? 0);
        Assert.Equal(40, layout.AvatarOffsetY);
        Assert.Equal(40, layout.AvatarDetachedOffsetY);
    }
}
