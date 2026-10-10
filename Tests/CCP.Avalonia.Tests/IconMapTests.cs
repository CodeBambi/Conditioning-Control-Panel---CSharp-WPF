using System.Linq;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;
using IconVariant = FluentIcons.Common.IconVariant;

namespace CCP.Avalonia.Tests;

/// <summary>Fluent icons L0a: the emoji -> icon table (oracle D1-D5), the id maps that keep Core
/// emoji edits from moving icons, and IconText.Split on the label shapes Core strings use.</summary>
public sealed class IconMapTests
{
    [Theory]
    [InlineData("🔒", IconKind.LockClosed, IconVariant.Filled, IconBrushes.Tier1)]
    [InlineData("🧪", IconKind.Beaker, IconVariant.Filled, IconBrushes.Tier2)]
    [InlineData("⚙️", IconKind.Settings, IconVariant.Regular, null)]    // VS16 form
    [InlineData("⚙", IconKind.Settings, IconVariant.Regular, null)]
    [InlineData("▶", IconKind.Play, IconVariant.Filled, null)]
    [InlineData("✕", IconKind.Dismiss, IconVariant.Regular, null)]
    [InlineData("⭐", IconKind.Star, IconVariant.Filled, IconBrushes.Gold)]
    [InlineData("☆", IconKind.Star, IconVariant.Regular, IconBrushes.Gold)]
    [InlineData("💖", IconKind.Heart, IconVariant.Filled, IconBrushes.Heart)]
    [InlineData("✅", IconKind.CheckmarkCircle, IconVariant.Filled, IconBrushes.Success)]
    [InlineData("⚠️", IconKind.Warning, IconVariant.Filled, IconBrushes.Warn)]
    [InlineData("👯‍♀️", IconKind.People, IconVariant.Regular, null)]  // ZWJ sequence
    [InlineData("📋", IconKind.Clipboard, IconVariant.Regular, null)]   // ⚑ fallback
    public void GlyphsMapToTheDesignedIcon(string glyph, IconKind kind, IconVariant variant, string? brush)
    {
        Assert.True(IconMap.TryGet(glyph, out var e), glyph);
        Assert.Equal(new IconEntry(kind, variant, brush), e);
    }

    [Fact]
    public void SpiralIsTheExtensionGlyphAndContentEmojiStayUnmapped()
    {
        Assert.True(IconMap.TryGet("🌀", out var spiral));
        Assert.Equal(IconBrand.Spiral, spiral.Brand);
        Assert.False(IconMap.TryGet("🎀", out _));   // per-site call
        Assert.False(IconMap.TryGet("🦄", out _));
        Assert.False(IconMap.TryGet("", out _));
    }

    [Fact]
    public void EveryBrushIsASemanticKeyAndEveryMdl2GlyphMaps()
    {
        Assert.All(IconMap.Entries.Values.Where(e => e.Brush != null), e => Assert.Contains(e.Brush, IconBrushes.All));
        Assert.True(IconMap.TryGetMdl2('\uE713', out var gear));
        Assert.Equal(IconKind.Settings, gear);
        Assert.True(IconMap.TryGetMdl2('\uE72E', out var chaster));
        Assert.Equal(IconKind.Key, chaster);
    }

    [Fact]
    public void EveryCoreFeatureSkillAndDoorHasAnIdMappedIcon()
    {
        Assert.All(FeatureDefinition.GetAllFeatures(), f => Assert.NotNull(IconMap.ForFeature(f.Id)));
        Assert.Equal(IconKind.Keyboard, IconMap.ForFeature("lock_cards")!.Value.Kind);   // not a premium padlock
        Assert.All(SkillDefinition.All, s => Assert.True(IconMap.ForSkill(s.Id) != null, s.Id));
        var doors = SettingsPaletteIndex.All.Where(e => e.Id.StartsWith("door.") || e.Id.StartsWith("tab.")).ToList();
        Assert.NotEmpty(doors);
        Assert.All(doors, d => Assert.True(IconMap.ForPalette(d.Id) != null, d.Id));
        Assert.Equal(IconKind.Key, IconMap.ForTab("chaster")!.Value.Kind);
        Assert.Equal(IconKind.Home, IconMap.ForPalette("tab.settings")!.Value.Kind);      // WPF E80F: the Dashboard
        Assert.Equal(IconKind.Settings, IconMap.ForPalette("door.settings")!.Value.Kind);
        Assert.Null(IconMap.ForPalette("set.spiral"));
    }

    [Fact]
    public void SplitKeepsClustersWholeRepeatsAndMirrors()
    {
        var s = IconText.Split("⚙️ System");
        Assert.Equal(new[] { "⚙️" }, s.Icons);
        Assert.Equal("System", s.Rest);
        Assert.False(s.Mirror);

        Assert.Equal(new[] { "👯‍♀️" }, IconText.Split("👯‍♀️ Friends").Icons);
        var hard = IconText.Split("⭐⭐ Hard");
        Assert.Equal(new[] { "⭐", "⭐" }, hard.Icons);
        Assert.Equal("Hard", hard.Rest);

        var spoil = IconText.Split("⚠ SPOILERS BELOW ⚠");
        Assert.True(spoil.Mirror);
        Assert.Equal("SPOILERS BELOW", spoil.Rest);

        Assert.Equal("+15 XP", IconText.Bare("+15 XP"));   // a leading symbol that is not an icon stays
        Assert.Equal("Plain", IconText.Bare("Plain"));
        Assert.Equal(new[] { "🦄" }, IconText.Split("🦄 Unicorn").Icons);   // unmapped icon still splits
        Assert.Equal("⚠", IconText.Split("⚠").Icons.Single());
        Assert.Equal("", IconText.Bare(null));
        Assert.Equal("System", HoverBubbleBar.StripLeadingGlyph("⚙ System"));
    }
}
