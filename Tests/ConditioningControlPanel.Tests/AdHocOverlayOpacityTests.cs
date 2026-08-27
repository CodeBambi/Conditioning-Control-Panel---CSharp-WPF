using Xunit;
using static ConditioningControlPanel.Services.OverlayService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1051: "in deeper player, spiral overlay effect only follows set effect opacity when
/// ramp effect is selected. If ramp isn't selected, opacity follows the setting of users engine."
///
/// The show path for spiral (ShowSpiralAdHoc -> StartSpiral) paints the user's saved SpiralOpacity
/// and the Deeper band only PARKED its own opacity in the ramp hold, so nothing ever applied it.
/// A ramp appeared to work only because each ramp tick reaches ApplySpiralOpacityDirect.
/// The rule the fix now runs on is pure and locked here: a FRESH ad-hoc overlay takes the effect's
/// opacity exactly, while one that is ALREADY on screen is only ever bumped UP (#573) so a timed
/// effect can never quietly dim a live band or the user's own tint.
/// </summary>
public class AdHocOverlayOpacityTests
{
    [Fact]
    public void FreshOverlay_TakesTheEffectsOwnOpacity_NotTheUsersSetting()
    {
        // The #1051 core: user's engine setting is 80%, the authored effect asks for 20%.
        var opacity = ResolveAdHocOverlayOpacity(alreadyShowing: false, rampHold: null,
            settingsFraction: 0.80, requested: 0.20);
        Assert.Equal(0.20, opacity, 6);
    }

    [Fact]
    public void FreshOverlay_IgnoresAStaleRampHold()
    {
        // Nothing is on screen, so no previous owner's value may leak into the new effect.
        var opacity = ResolveAdHocOverlayOpacity(alreadyShowing: false, rampHold: 0.95,
            settingsFraction: 0.80, requested: 0.10);
        Assert.Equal(0.10, opacity, 6);
    }

    [Fact]
    public void FreshOverlay_TakesAHigherEffectOpacityToo()
    {
        var opacity = ResolveAdHocOverlayOpacity(alreadyShowing: false, rampHold: null,
            settingsFraction: 0.10, requested: 0.90);
        Assert.Equal(0.90, opacity, 6);
    }

    [Fact]
    public void LiveOverlay_IsBumpedUpByAStrongerEffect()
    {
        // #573: a timed effect over a live tint boosts it.
        var opacity = ResolveAdHocOverlayOpacity(alreadyShowing: true, rampHold: null,
            settingsFraction: 0.30, requested: 0.75);
        Assert.Equal(0.75, opacity, 6);
    }

    [Fact]
    public void LiveOverlay_IsNeverDimmedByAWeakerEffect()
    {
        // #573's other half: the user's own tint (or a live band) must not be weakened.
        var opacity = ResolveAdHocOverlayOpacity(alreadyShowing: true, rampHold: null,
            settingsFraction: 0.80, requested: 0.20);
        Assert.Equal(0.80, opacity, 6);
    }

    [Fact]
    public void LiveOverlay_PrefersTheRampHoldOverTheUsersSetting()
    {
        // A Deeper band owns the opacity while its hold is parked; the saved setting is not the
        // current owner and must not be what a bump measures against.
        var opacity = ResolveAdHocOverlayOpacity(alreadyShowing: true, rampHold: 0.60,
            settingsFraction: 0.05, requested: 0.40);
        Assert.Equal(0.60, opacity, 6);
    }

    [Fact]
    public void RequestedOpacityIsClamped()
    {
        Assert.Equal(1.0, ResolveAdHocOverlayOpacity(false, null, 0.5, 4.2), 6);
        Assert.Equal(0.0, ResolveAdHocOverlayOpacity(false, null, 0.5, -1.0), 6);
    }

    [Fact]
    public void ZeroRequestOnAFreshOverlay_IsHonoured_NotSilentlyReplacedByTheSetting()
    {
        // An authored 0% effect is a legitimate instruction (a ramp's starting point). It must not
        // fall back to the user's setting the way the old code did.
        var opacity = ResolveAdHocOverlayOpacity(alreadyShowing: false, rampHold: null,
            settingsFraction: 0.70, requested: 0.0);
        Assert.Equal(0.0, opacity, 6);
    }
}
