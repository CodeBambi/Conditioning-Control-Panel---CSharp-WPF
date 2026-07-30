using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// PR-0a — the ambient FX palette. Every FX colour in the app resolves through one chain
/// (fxPalette slot → theme.filterColor → theme.accentColor → #FF69B4) so that existing mods,
/// which define neither an fxPalette nor an InstalledPath, still get coherently tinted FX.
/// Also covers the manifest hex validation the four new slots ride on.
/// </summary>
public class ModFxPaletteTests
{
    [Fact]
    public void Slot_Wins_OverEverything()
        => Assert.Equal("#00FF41", ModService.ResolveFxSlotHex("#00FF41", "#112233", "#445566"));

    [Fact]
    public void FilterColor_Wins_WhenSlotUnset()
        => Assert.Equal("#112233", ModService.ResolveFxSlotHex(null, "#112233", "#445566"));

    [Fact]
    public void AccentColor_Wins_WhenSlotAndFilterUnset()
        => Assert.Equal("#445566", ModService.ResolveFxSlotHex(null, null, "#445566"));

    [Fact]
    public void HardDefault_WhenNothingIsSet()
        => Assert.Equal("#FF69B4", ModService.ResolveFxSlotHex(null, null, null));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankSlot_FallsThrough_LikeNull(string slot)
        => Assert.Equal("#112233", ModService.ResolveFxSlotHex(slot, "#112233", null));

    [Fact]
    public void BuiltInModShape_WithNoFxPalette_StillResolvesToItsAccent()
    {
        // The Dronification shape: accent only, no filterColor, no fxPalette, InstalledPath null.
        var theme = new ModTheme { AccentColor = "#00FF41" };
        Assert.Equal("#00FF41", ModService.ResolveFxSlotHex(null, theme.FilterColor, theme.AccentColor));
    }

    [Fact]
    public void ValidFxPalette_Passes()
    {
        var manifest = NewManifest();
        manifest.FxPalette = new ModFxPalette
        {
            MistColor = "#E81CA8",
            ParticleColor = "#FF69B4",
            GlowColor = "#00FF41",
            FlashTint = "#FFFFFF",
            MistOpacity = 0.6,
        };
        Assert.Null(ModService.SanitizeManifest(manifest));
    }

    [Fact]
    public void NoFxPalette_Passes()
        => Assert.Null(ModService.SanitizeManifest(NewManifest()));

    [Theory]
    [InlineData("mist")]
    [InlineData("particle")]
    [InlineData("glow")]
    [InlineData("flash")]
    public void BadHex_IsRejected_InEverySlot(string slot)
    {
        var manifest = NewManifest();
        var palette = new ModFxPalette();
        switch (slot)
        {
            case "mist": palette.MistColor = "not-a-color"; break;
            case "particle": palette.ParticleColor = "#GGGGGG"; break;
            case "glow": palette.GlowColor = "#FFF"; break;
            case "flash": palette.FlashTint = "FF69B4"; break;
        }
        manifest.FxPalette = palette;
        Assert.NotNull(ModService.SanitizeManifest(manifest));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void MistOpacity_OutOfRange_IsRejected(double value)
    {
        var manifest = NewManifest();
        manifest.FxPalette = new ModFxPalette { MistOpacity = value };
        Assert.NotNull(ModService.SanitizeManifest(manifest));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void MistOpacity_AtTheBounds_IsAccepted(double value)
    {
        var manifest = NewManifest();
        manifest.FxPalette = new ModFxPalette { MistOpacity = value };
        Assert.Null(ModService.SanitizeManifest(manifest));
    }

    private static ModManifest NewManifest() => new()
    {
        Id = "test.mod",
        Name = "Test Mod",
        Version = "1.0.0",
        Author = "Tests",
    };
}
