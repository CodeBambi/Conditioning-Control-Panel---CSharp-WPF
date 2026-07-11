using ConditioningControlPanel.Core.Services.AvatarTube;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the Circe emote layout fold for the 2026-07-12 "emote transform parity" fix
/// (Engram obs #6, topic avatartube-rootcause-2026-07-11). The fold mirrors WPF
/// <c>AvatarTubeWindow.CirceEmotes.cs:388-393</c> and is what makes the Avalonia emote
/// frames land at the SAME scale/offset as the neutral pose (the prior port applied a
/// separate <c>baseScale=1.0</c> transform to the emote images and ignored the mod avatar
/// scale, producing the owner-visible "avatar enlarged + shifted up during Circe emotes").
/// The Y SIGN is the load-bearing detail: it must SUBTRACT so an emote <c>offsetY:-30</c>
/// ("up 30px") survives the AvatarBorder "+bottom = up" margin math in the same on-screen
/// direction WPF produces.
/// </summary>
public class AvatarTubeLayoutFoldTests
{
    // ---- Inactive layout: pure pose base, zero delta ----

    [Fact]
    public void Scale_Inactive_ReturnsModScaleUnchanged()
    {
        Assert.Equal(0.864, AvatarTubeLayoutFold.Scale(0.864, active: false, emoteScaleMul: 0.855));
    }

    [Fact]
    public void OffsetX_Inactive_ReturnsModOffsetUnchanged()
    {
        Assert.Equal(7, AvatarTubeLayoutFold.OffsetX(7, active: false, emoteOffX: 10));
    }

    [Fact]
    public void OffsetY_Inactive_ReturnsModOffsetUnchanged()
    {
        Assert.Equal(7, AvatarTubeLayoutFold.OffsetY(7, active: false, emoteOffY: -30));
    }

    // ---- Active layout: scale multiplies, X adds ----

    [Fact]
    public void Scale_Active_MultipliesModByEmoteScaleMul()
    {
        // BambiSleep-style: mod 0.864 * layout 0.855 => 0.7387 (emote SMALLER than pose, not larger).
        Assert.Equal(0.864 * 0.855, AvatarTubeLayoutFold.Scale(0.864, active: true, emoteScaleMul: 0.855), 4);
    }

    [Fact]
    public void Scale_Active_UnitModScale_AdoptsEmoteScaleMul()
    {
        // The bug case: mod 1.0 * 0.855 = 0.855 (NOT the old divergent 1.0*0.855 that ignored mod,
        // but here mod is genuinely 1.0). Confirms the multiplier path.
        Assert.Equal(0.855, AvatarTubeLayoutFold.Scale(1.0, active: true, emoteScaleMul: 0.855), 6);
    }

    [Fact]
    public void OffsetX_Active_AddsEmoteDelta()
    {
        Assert.Equal(17, AvatarTubeLayoutFold.OffsetX(7, active: true, emoteOffX: 10));
    }

    // ---- Y SIGN (the load-bearing parity detail) ----

    [Fact]
    public void OffsetY_Active_SubtractsEmoteDelta_WpfSign()
    {
        // WPF CirceEmotes.cs:393: modOffY - emoteOffY. With emoteOffY=5 (layout "down 5px"),
        // the effective Y DECREASES by 5 (margin math: 210+dy, smaller dy = lower bottom margin =
        // figure moves DOWN 5px). X adds; Y subtracts.
        Assert.Equal(2, AvatarTubeLayoutFold.OffsetY(7, active: true, emoteOffY: 5));
    }

    [Fact]
    public void OffsetY_BambisleepLayoutNeg30_IncreasesMargin_UpOnScreen()
    {
        // The bambisleep emotes.json layout: offsetY:-30 (the layout stores Y as "+= down", so
        // -30 = "up 30px"). Folded per WPF: modOffY - (-30) = modOffY + 30. Applied to AvatarBorder
        // bottom margin (210 + dy) in Avalonia (+Y = down screen coords), a +30 bottom margin pulls
        // the figure UP 30px on screen — the SAME on-screen direction WPF produces and the SAME
        // direction the old TranslateTransform(0, -30) produced. The sign is what keeps the parity.
        int dy = AvatarTubeLayoutFold.OffsetY(modOffY: 0, active: true, emoteOffY: -30);
        Assert.Equal(30, dy);                 // modOffY - (-30) = +30
        Assert.True(dy > 0, "bottom margin must increase so the figure moves UP on screen");
    }

    [Fact]
    public void OffsetY_DetachedBambisleep_MatchesAttachedSign()
    {
        // bambisleep detachedY:-30 folds the same way as the attached Y (WPF detached uses the
        // same fold). Detached bottom margin is 228 + dy; +30 still moves the figure up.
        int dyDet = AvatarTubeLayoutFold.OffsetY(modOffY: 0, active: true, emoteOffY: -30);
        Assert.Equal(30, dyDet);
    }

    // ---- BambiSleep layout snapshot (the concrete repro values) ----

    [Fact]
    public void BambisleepLayout_FullFold_MatchesWpfContract()
    {
        // emotes.json layout { scale:0.855, offsetX:10, offsetY:-30, detachedX:10, detachedY:-30 }
        // against a representative mod global layout (modScale 0.864, modOffX 0, modOffY 0).
        double scale = AvatarTubeLayoutFold.Scale(0.864, active: true, emoteScaleMul: 0.855);
        int offX = AvatarTubeLayoutFold.OffsetX(0, active: true, emoteOffX: 10);
        int offY = AvatarTubeLayoutFold.OffsetY(0, active: true, emoteOffY: -30);

        Assert.Equal(0.864 * 0.855, scale, 4);   // 0.7387 — emote smaller than the 0.864 pose
        Assert.Equal(10, offX);                   // X adds: 0 + 10
        Assert.Equal(30, offY);                   // Y subtracts the -30: 0 - (-30) = +30
    }

    [Fact]
    public void Fold_NeverProducesEnlargeVsInactive()
    {
        // The defining property of the fix: an ACTIVE emote layout must NEVER make the effective
        // scale LARGER than the inactive (pose) scale when the emote scale multiplier is < 1.
        // (The old baseScale=1.0 path produced 1.0*0.855 = 0.855 vs a pose at modScale < 0.855,
        // i.e. the emote was larger than the pose.) With the fold, active scale = mod*mul which is
        // <= mod whenever mul <= 1.
        double mod = 0.864;
        double mul = 0.855;
        double inactive = AvatarTubeLayoutFold.Scale(mod, false, mul);
        double active = AvatarTubeLayoutFold.Scale(mod, true, mul);
        Assert.True(active <= inactive + 1e-9, "active emote scale must not exceed the pose scale");
    }
}
