using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The launcher's mod dropdown lists mods in the panel's order (stock first, canonical order,
/// then user mods A-Z) and the asset preset switch writes exactly what the panel's combo writes.
/// </summary>
public class LauncherModMenuTests
{
    [Fact]
    public void Order_puts_stock_mods_first_in_canonical_order_then_user_mods_alphabetically()
    {
        var rows = new List<LauncherModRow>
        {
            new("zeta-pack", "Zeta", false),
            new(BuiltInMods.LockedId, "Locked", true),
            new("alpha-pack", "alpha", false),
            new(BuiltInMods.CCPDefaultId, "CCP Default", true),
            new(BuiltInMods.BambiSleepId, "Bambi Sleep", true),
        };

        var ordered = LauncherModMenu.Order(rows).Select(r => r.Id).ToList();

        Assert.Equal(new[]
        {
            BuiltInMods.CCPDefaultId, BuiltInMods.BambiSleepId, BuiltInMods.LockedId, "alpha-pack", "zeta-pack"
        }, ordered);
    }

    [Fact]
    public void Order_keeps_an_unknown_built_in_and_survives_null()
    {
        Assert.Empty(LauncherModMenu.Order(null));

        var rows = new List<LauncherModRow> { new("builtin-future", "Future", true), new("u", "User", false) };
        var ordered = LauncherModMenu.Order(rows).Select(r => r.Id).ToList();
        Assert.Equal(new[] { "u", "builtin-future" }, ordered);
    }

    [Theory]
    [InlineData("Mod: {0}", "Bambi Sleep", "Mod: Bambi Sleep")]
    [InlineData("Mod: {0}", "  ", "Mod: -")]
    [InlineData("Mod: {0}", null, "Mod: -")]
    [InlineData("broken {", "X", "X")]
    public void Label_formats_the_name_and_falls_back(string format, string? name, string expected)
        => Assert.Equal(expected, LauncherModMenu.Label(format, name, "-"));

    // ---- asset presets ----

    private static AppSettings WithPresets()
    {
        var s = new AppSettings { DisabledAssetPaths = new HashSet<string> { "old/one.jpg" } };
        s.AssetPresets = new List<AssetPreset>
        {
            AssetPreset.CreateDefault(),
            new() { Id = "p1", Name = "Pink", DisabledAssetPaths = new HashSet<string> { "a.jpg", "b.mp4" } },
        };
        return s;
    }

    [Fact]
    public void Apply_replaces_the_disabled_set_and_remembers_the_id()
    {
        var s = WithPresets();

        var applied = AssetPresetService.Apply(s, "p1");

        Assert.NotNull(applied);
        Assert.Equal("p1", s.CurrentAssetPresetId);
        Assert.Equal(new[] { "a.jpg", "b.mp4" }, s.DisabledAssetPaths.OrderBy(x => x));
        // A copy, not the preset's own set: editing the app's selection must not edit the preset.
        s.DisabledAssetPaths.Add("c.jpg");
        Assert.Equal(2, applied!.DisabledAssetPaths.Count);
    }

    [Fact]
    public void Apply_with_an_unknown_id_writes_nothing()
    {
        var s = WithPresets();
        s.CurrentAssetPresetId = "p1";

        Assert.Null(AssetPresetService.Apply(s, "nope"));
        Assert.Null(AssetPresetService.Apply(s, null));

        Assert.Equal("p1", s.CurrentAssetPresetId);
        Assert.Equal(new[] { "old/one.jpg" }, s.DisabledAssetPaths);
    }

    [Fact]
    public void Active_is_the_current_id_else_the_default_row()
    {
        var s = WithPresets();
        Assert.True(AssetPresetService.Active(s)!.IsDefault);

        s.CurrentAssetPresetId = "p1";
        Assert.Equal("p1", AssetPresetService.Active(s)!.Id);

        s.CurrentAssetPresetId = "deleted";
        Assert.True(AssetPresetService.Active(s)!.IsDefault);
    }
}
