using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The dashboard's off-tile treatment, the part of it that is pure: when a tile goes grey, when
/// a fade is allowed, and that the greyscale maths keeps alpha and lands every channel on the
/// same value.
/// </summary>
public class CardMuteRuleTests
{
    [Theory]
    // dim, active, locked, hovered, teased -> muted
    [InlineData(true, false, false, false, false, true)]   // off and left alone: grey
    [InlineData(true, true, false, false, false, false)]   // on: colour
    [InlineData(true, false, true, false, false, false)]   // locked: the veil, never the grey
    [InlineData(true, false, false, true, false, false)]   // hover hands the colour back
    [InlineData(true, false, false, false, true, false)]   // teased: keeps its blur
    [InlineData(false, false, false, false, false, false)] // destination tiles never opt in
    public void A_tile_is_grey_only_when_off_unlocked_unteased_and_not_hovered(
        bool dim, bool active, bool locked, bool hovered, bool teased, bool muted)
    {
        Assert.Equal(muted, CardMuteRule.ShouldMute(dim, active, locked, hovered, teased));
    }

    [Theory]
    [InlineData(false, false, true)]  // off half, mouse elsewhere: grey
    [InlineData(true, false, false)]  // on half: colour
    [InlineData(false, true, false)]  // off half the mouse committed to: colour (the reveal)
    [InlineData(true, true, false)]
    public void A_half_is_grey_only_when_off_and_not_the_committed_half(bool active, bool hovered, bool muted)
    {
        Assert.Equal(muted, CardMuteRule.ShouldMuteHalf(active, hovered));
    }

    [Theory]
    [InlineData(true, true, CardMuteRule.FadeMs)] // motion on, card on screen: fade
    [InlineData(false, true, 0)]                  // reduced motion: snap
    [InlineData(true, false, 0)]                  // first frame: snap, never animate in
    [InlineData(false, false, 0)]
    public void The_fade_runs_only_with_motion_allowed_and_the_card_loaded(bool motion, bool loaded, int ms)
    {
        Assert.Equal(ms, CardMuteRule.TransitionMs(motion, loaded));
    }

    [Fact]
    public void Greyscale_keeps_alpha_and_equalises_the_colour_channels()
    {
        // Two BGRA pixels: a saturated pink at 200 alpha, and a pure blue at 0 alpha.
        var px = new byte[] { 0xB4, 0x69, 0xFF, 200, 0xFF, 0x00, 0x00, 0 };

        ArtDesaturate.ToGrey(px);

        Assert.Equal(px[0], px[1]);
        Assert.Equal(px[1], px[2]);
        Assert.Equal(200, px[3]);
        // Rec.601 of (R 255, G 105, B 180): 0.299*255 + 0.587*105 + 0.114*180 = 158.4
        Assert.InRange(px[0], 157, 159);

        Assert.Equal(px[4], px[5]);
        Assert.Equal(px[5], px[6]);
        Assert.Equal(0, px[7]);
        // Pure blue is dark under luma weighting: 0.114 * 255 = 29.
        Assert.InRange(px[4], 28, 30);
    }

    [Fact]
    public void Greyscale_leaves_a_trailing_partial_pixel_alone()
    {
        var px = new byte[] { 10, 20, 30, 255, 99, 98 };
        ArtDesaturate.ToGrey(px);
        Assert.Equal(99, px[4]);
        Assert.Equal(98, px[5]);
    }
}
