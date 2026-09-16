using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Flashes v2 wave 2, rounded corners: the one resolver all three flash render paths share
/// (compositor round-rect clip, glow WPF card, no-glow WPF/solid host backing). The whole point
/// of the shared resolver is that a glow flash and a plain one cannot disagree about how round
/// they are, so these pin the four rules: ownership gates it, glow keeps its legacy 12, the
/// switch raises everything to 14, and nothing ever rounds past a quarter of the shorter side.
/// </summary>
public class FlashCornerRadiusTests
{
    // A comfortable flash: 768 x 432, so the 25% cap (108) is never the binding constraint.
    private const double BigSide = 432;

    [Fact]
    public void Off_AndUnowned_PlainFlashStaysSquare()
        => Assert.Equal(0.0, FlashCorners.Resolve(false, false, false, BigSide, 1.0));

    [Fact]
    public void Off_AndUnowned_GlowKeepsItsLegacyCard()
        => Assert.Equal(FlashCorners.GlowBaseDip, FlashCorners.Resolve(false, false, true, BigSide, 1.0));

    [Fact]
    public void SwitchOn_ButUnowned_ChangesNothing()
    {
        // A synced profile can carry the switch on from an account that owns the prize.
        Assert.Equal(0.0, FlashCorners.Resolve(true, false, false, BigSide, 1.0));
        Assert.Equal(FlashCorners.GlowBaseDip, FlashCorners.Resolve(true, false, true, BigSide, 1.0));
    }

    [Fact]
    public void Owned_ButSwitchOff_ChangesNothing()
    {
        Assert.Equal(0.0, FlashCorners.Resolve(false, true, false, BigSide, 1.0));
        Assert.Equal(FlashCorners.GlowBaseDip, FlashCorners.Resolve(false, true, true, BigSide, 1.0));
    }

    [Fact]
    public void OnAndOwned_RoundsPlainAndRaisesGlow()
    {
        Assert.Equal(FlashCorners.RoundedDip, FlashCorners.Resolve(true, true, false, BigSide, 1.0));
        Assert.Equal(FlashCorners.RoundedDip, FlashCorners.Resolve(true, true, true, BigSide, 1.0));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void CompositorScalesTheRadiusByDpi(double dpi)
    {
        // World px: the same physical roundness on a 150% screen as on a 100% one.
        var r = FlashCorners.Resolve(true, true, false, BigSide * dpi, dpi);
        Assert.Equal(FlashCorners.RoundedDip * dpi, r, 6);
    }

    [Fact]
    public void ADpiOfZeroFallsBackToOne()
        => Assert.Equal(FlashCorners.RoundedDip, FlashCorners.Resolve(true, true, false, BigSide, 0));

    [Fact]
    public void NeverMoreThanAQuarterOfTheShorterSide()
    {
        // A 40px-tall sliver would be a lozenge at 14: the cap pins it to 10.
        Assert.Equal(10.0, FlashCorners.Resolve(true, true, false, 40, 1.0));
        // The cap binds on the glow card too.
        Assert.Equal(8.0, FlashCorners.Resolve(false, false, true, 32, 1.0));
    }

    [Fact]
    public void TheCapAppliesAfterTheDpiScale()
    {
        // 40 DIP tall on a 200% screen is 80 world px, so the cap is 20 and 28 does not fit.
        Assert.Equal(20.0, FlashCorners.Resolve(true, true, false, 80, 2.0));
    }

    [Fact]
    public void ADegenerateSizeNeverGoesNegative()
    {
        Assert.Equal(0.0, FlashCorners.Resolve(true, true, true, 0, 1.0));
        Assert.Equal(0.0, FlashCorners.Resolve(true, true, true, -50, 1.0));
    }

    [Fact]
    public void RoundedCornersDefaultOff()
    {
        // Release-visible: a v2 owner upgrading must not find their flashes silently reshaped.
        Assert.False(new AppSettings().FlashRoundedCorners);
        var loaded = JsonConvert.DeserializeObject<AppSettings>("{}", new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Error = (_, args) => { args.ErrorContext.Handled = true; }
        })!;
        Assert.False(loaded.FlashRoundedCorners);
    }

    [Fact]
    public void RoundedCornersRoundTripsThroughSettingsJson()
    {
        var s = new AppSettings { FlashRoundedCorners = true };
        var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(s))!;
        Assert.True(back.FlashRoundedCorners);
    }
}
