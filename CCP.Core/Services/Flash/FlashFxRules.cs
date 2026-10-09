using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>One flash's glow, or <see cref="None"/>. Colour is 0xRRGGBB; radius in DIPs.</summary>
public readonly record struct FlashGlowLook(uint Rgb, double BlurRadius, double Opacity, bool LuckyPulse, bool NatashaBlink)
{
    public static readonly FlashGlowLook None = new(0, 0, 0, false, false);
    public bool HasGlow => BlurRadius > 0;
}

/// <summary>
/// The per-flash decisions WPF 7.1.5 makes inline in <c>FlashService.SpawnFlashWindow</c> and
/// <c>OnFlashClicked</c>, lifted out as pure rules so the Avalonia head plays them the same way:
/// the lucky roll (skill lucky_bimbo, 5%, x10 XP), the glow (lucky gold, sparkle-boost pink,
/// Natasha's red halo; glow toggle + performance tier), the per-flash XP and the hydra multiply.
/// </summary>
public static class FlashFxRules
{
    public const string LuckySkillId = "lucky_bimbo";
    public const double LuckyChance = 0.05;
    public const int LuckyMultiplier = 10;
    public const uint Gold = 0xFFD700, HotPink = 0xFF69B4;
    public const int HydraHardLimit = 20;

    /// <summary>WPF SkillTreeService.RollLuckyFlash: 1, or 10 on a 5% proc with lucky_bimbo.
    /// A hydra child or a remix mirror never rolls.</summary>
    public static int RollLucky(bool ownsLuckySkill, int hydraGeneration, bool remixMirror, Random rng)
    {
        if (hydraGeneration > 0 || remixMirror || !ownsLuckySkill) return 1;
        return rng.NextDouble() < LuckyChance ? LuckyMultiplier : 1;
    }

    /// <summary>WPF PerformanceProfile.AllowGlow.</summary>
    public static bool AllowGlow(PerformanceTier tier) => tier != PerformanceTier.Performance;

    /// <summary>WPF PerformanceProfile.MaxGlowBlurRadius.</summary>
    public static double MaxGlowBlurRadius(PerformanceTier tier) => tier == PerformanceTier.Balanced ? 18 : 24;

    /// <summary>
    /// WPF SpawnFlashWindow glow block: glow only for a lucky flash or sparkle-boost tier &gt; 0
    /// (behind FlashGlowEnabled and the perf tier), or Natasha's favourite (perf tier only).
    /// Lucky = gold 60 @ 0.9 pulsing; sparkle = pink 25/35/45 @ 0.5/0.6/0.7; Natasha alone = a
    /// thin red halo. The blur is capped per tier.
    /// </summary>
    public static FlashGlowLook ResolveGlow(bool glowSetting, PerformanceTier tier, bool isLucky, int sparkleTier, bool natasha)
    {
        bool glowEnabled = glowSetting && AllowGlow(tier);
        bool natashaGlow = natasha && AllowGlow(tier);
        if (!((glowEnabled && (isLucky || sparkleTier > 0)) || natashaGlow)) return FlashGlowLook.None;

        uint color = isLucky ? Gold
            : natashaGlow ? ((uint)NatashasFavourite.R << 16 | (uint)NatashasFavourite.G << 8 | NatashasFavourite.B)
            : HotPink;
        double blur, opacity;
        if (isLucky) { blur = 60; opacity = 0.9; }
        else
        {
            blur = sparkleTier switch { 1 => 25, 2 => 35, _ => 45 };
            opacity = sparkleTier switch { 1 => 0.5, 2 => 0.6, _ => 0.7 };
        }
        if (natashaGlow && !isLucky && (sparkleTier == 0 || !glowEnabled))
        {
            blur = NatashasFavourite.HaloBlurDip;
            opacity = NatashasFavourite.HaloOpacity;
        }
        blur = Math.Min(blur, MaxGlowBlurRadius(tier));
        return new FlashGlowLook(color, blur, opacity, isLucky, natashaGlow && !isLucky);
    }

    /// <summary>The lucky pulse (WPF: blur r..1.6r and opacity 0.7..1.0, 400 ms autoreverse), at
    /// <paramref name="t"/> seconds. Returns (blur, opacity).</summary>
    public static (double Blur, double Opacity) LuckyPulseAt(double baseBlur, double t)
    {
        var phase = (t % 0.8) / 0.4;
        var k = phase <= 1 ? phase : 2 - phase;
        return (baseBlur * (1 + 0.6 * k), 0.7 + 0.3 * k);
    }

    /// <summary>XP one flash pays (WPF SpawnFlashWindow): 8 with its sound, else 4; with
    /// independent hydra timing a child pays 75% (gen 1) or 1 (gen 2+). Times the lucky multiplier.</summary>
    public static int Xp(bool soundPlaying, bool hydraLinkedTiming, int hydraGeneration, int multiplier)
    {
        int xp = soundPlaying ? 8 : 4;
        if (!hydraLinkedTiming && hydraGeneration > 0)
            xp = hydraGeneration >= 2 ? 1 : (int)Math.Max(1, Math.Round(xp * 0.75));
        return xp * Math.Max(1, multiplier);
    }

    /// <summary>
    /// WPF OnFlashClicked + TriggerMultiplication: how many hydra children a pop spawns (0..2).
    /// CorruptionMode only, never off a remix, a gaze pop multiplies only an original flash
    /// (#784), and only while active + 1 stays under min(HydraLimit, 20). <paramref name="activeAfterPop"/>
    /// is the on-screen count with the popped flash already removed.
    /// </summary>
    public static int HydraSpawnCount(bool corruptionMode, int hydraLimit, bool isRemix, bool fromGaze,
        int hydraGeneration, int activeAfterPop)
    {
        if (!corruptionMode || isRemix) return 0;
        if (fromGaze && hydraGeneration != 0) return 0;
        var max = Math.Min(hydraLimit, HydraHardLimit);
        if (activeAfterPop + 1 >= max) return 0;
        return Math.Max(0, Math.Min(2, max - activeAfterPop));
    }

    /// <summary>Hydra child lifetime: linked = whatever the parent had left (at least 1 s, as
    /// WPF measures it), independent = the parent's full original lifetime.</summary>
    public static int HydraChildLifetimeMs(bool linked, int parentLifetimeMs, double parentRemainingMs) =>
        linked ? Math.Max(1000, (int)parentRemainingMs) : parentLifetimeMs;
}
