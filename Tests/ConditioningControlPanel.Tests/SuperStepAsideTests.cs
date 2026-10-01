using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Super only: the pink tint steps aside for Creep, the spiral for Vortex, live and with a fade.</summary>
public class SuperStepAsideTests
{
    [Fact]
    public void Hides_only_when_super_replaces_the_base_and_nobody_else_holds_it()
    {
        Assert.True(SuperStepAside.Hides(replacesBase: true, otherOwnerHolds: false));
        Assert.False(SuperStepAside.Hides(replacesBase: true, otherOwnerHolds: true));   // a pop, a voice command, a Deeper band
        Assert.False(SuperStepAside.Hides(replacesBase: false, otherOwnerHolds: false));
        Assert.False(SuperStepAside.Hides(replacesBase: false, otherOwnerHolds: true));
    }

    [Fact]
    public void Base_off_or_a_weekly_try_never_hides_the_base()
    {
        // ReplacesBase feeds HidesBase with IsOn, which is false the moment the base toggle is off.
        Assert.False(SuperModeRule.HidesBase(SuperEffect.Creep, SuperMode.SuperOnly, superIsOn: false, trying: false));
        Assert.False(SuperModeRule.HidesBase(SuperEffect.Vortex, SuperMode.SuperOnly, superIsOn: true, trying: true));
        Assert.True(SuperModeRule.HidesBase(SuperEffect.Creep, SuperMode.SuperOnly, superIsOn: true, trying: false));
        Assert.True(SuperModeRule.HidesBase(SuperEffect.Vortex, SuperMode.SuperOnly, superIsOn: true, trying: false));
        Assert.False(SuperModeRule.HidesBase(SuperEffect.Vortex, SuperMode.Both, superIsOn: true, trying: false));
    }

    [Fact]
    public void Fade_follows_motion_level()
    {
        Assert.Equal(SuperStepAside.FadeSeconds, SuperStepAside.FadeSecondsFor(MotionLevel.Full));
        Assert.Equal(SuperStepAside.FadeSeconds * 2, SuperStepAside.FadeSecondsFor(MotionLevel.Reduced));   // half speed
        Assert.Equal(0.12, SuperStepAside.FadeSecondsFor(MotionLevel.Off));                                  // house 120 ms fade
    }

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public void Veil_fades_out_and_back_in_over_the_fade_time_never_in_one_frame(MotionLevel level)
    {
        double fade = SuperStepAside.FadeSecondsFor(level);
        const double dt = 1 / 60.0;
        double veil = 1;
        veil = SuperStepAside.Step(veil, hidden: true, dt, fade);
        Assert.InRange(veil, 0.0001, 0.9999);   // moved, but not gone in one frame
        int frames = 1;
        while (veil > 0 && frames < 1000) { veil = SuperStepAside.Step(veil, true, dt, fade); frames++; }
        Assert.Equal(0, veil);
        Assert.InRange(frames * dt, fade - dt, fade + 2 * dt);
        for (int i = 0; i < 1000 && veil < 1; i++) veil = SuperStepAside.Step(veil, false, dt, fade);
        Assert.Equal(1, veil);
    }

    [Fact]
    public void Step_clamps_and_holds_at_the_target()
    {
        Assert.Equal(0, SuperStepAside.Step(0, true, 0.5, 0.45));
        Assert.Equal(1, SuperStepAside.Step(1, false, 0.5, 0.45));
        Assert.Equal(1, SuperStepAside.Step(7, false, 0, 0.45));
        Assert.Equal(0, SuperStepAside.Step(-3, true, 0, 0.45));
        Assert.Equal(1, SuperStepAside.Step(0, false, 0.1, 0));   // zero fade = instant, never a divide by zero
    }

    [Fact]
    public void Ease_is_smooth_at_both_ends()
    {
        Assert.Equal(0, SuperStepAside.Ease(0));
        Assert.Equal(1, SuperStepAside.Ease(1));
        Assert.Equal(0.5, SuperStepAside.Ease(0.5), 6);
        Assert.True(SuperStepAside.Ease(0.05) < 0.05);   // eases out of zero
        Assert.True(SuperStepAside.Ease(0.95) > 0.95);   // eases into one
    }

    [Fact]
    public void The_pink_tint_and_the_spiral_draw_through_the_veil()
    {
        foreach (var name in new[] { "PinkTintLayer.cs", "SpiralLayer.cs" })
        {
            var src = Read("ConditioningControlPanel", "Services", "Compositor", name);
            var render = src.Substring(src.IndexOf("public override void Render", StringComparison.Ordinal));
            Assert.Contains("SuperStepAside.Ease(_veil)", render);
            Assert.Contains("public void SetStepAside(bool on)", src);
            Assert.Contains("SuperStepAside.FadeSecondsFor(MotionFx.Level)", src);
        }
    }

    [Fact]
    public void Overlay_service_asks_the_right_effect_for_each_layer_and_keeps_asking()
    {
        var src = Read("ConditioningControlPanel", "Services", "Notifications", "OverlayService.cs");
        var push = src.Substring(src.IndexOf("private void PushStepAside()", StringComparison.Ordinal));
        push = push.Substring(0, push.IndexOf("private void SyncVortex()", StringComparison.Ordinal));
        int pink = push.IndexOf("_pinkLayer.SetStepAside", StringComparison.Ordinal);
        int spiral = push.IndexOf("_spiralLayer.SetStepAside", StringComparison.Ordinal);
        Assert.True(pink > 0 && spiral > pink);
        Assert.Contains("ReplacesBase(Super.SuperEffect.Creep)", push.Substring(pink, spiral - pink));
        Assert.Contains("ReplacesBase(Super.SuperEffect.Vortex)", push.Substring(spiral));
        Assert.Contains("_timedPinkHolds > 0 || _sustainedPinkHeld || _rampPinkOpacity.HasValue", push);
        Assert.Contains("_timedSpiralHolds > 0 || _sustainedSpiralHeld || _rampSpiralOpacity.HasValue", push);

        // Live: the pick change event, the 500 ms reconciler and every direct opacity push re-ask.
        var onChanged = src.Substring(src.IndexOf("private void OnSuperChanged", StringComparison.Ordinal), 400);
        Assert.Contains("Super.SuperEffect.Creep", onChanged);
        Assert.Contains("PushStepAside", onChanged);
        var tick = src.Substring(src.IndexOf("private void UpdateOverlays", StringComparison.Ordinal));
        tick = tick.Substring(0, tick.IndexOf("bool needed = ReassertZOrder()", StringComparison.Ordinal));
        Assert.Contains("PushStepAside();", tick);
    }

    [Fact]
    public void Panic_and_the_base_toggle_still_take_everything_down()
    {
        var overlay = Read("ConditioningControlPanel", "Services", "Notifications", "OverlayService.cs");
        var stopPink = overlay.Substring(overlay.IndexOf("internal void StopPinkFilter()", StringComparison.Ordinal), 300);
        Assert.Contains("_pinkLayer?.Hide()", stopPink);
        var stopSpiral = overlay.Substring(overlay.IndexOf("internal void StopSpiral()", StringComparison.Ordinal), 300);
        Assert.Contains("_spiralLayer?.Hide()", stopSpiral);
        // Creep follows its base toggle: IsOn asks the pink filter's own switch.
        var creep = Read("ConditioningControlPanel", "Services", "Super", "CreepController.cs");
        Assert.Contains("SuperAccess.IsOn(SuperEffect.Creep)", creep);
        Assert.Equal(nameof(AppSettings.PinkFilterEnabled), SuperBase.SettingFor(SuperEffect.Creep));
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services")))
            dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("repo root");
        return File.ReadAllText(Path.Combine(dir.FullName, Path.Combine(parts)));
    }
}
