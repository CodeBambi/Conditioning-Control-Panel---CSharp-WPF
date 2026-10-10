using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Bubbles v2, wave 2: the Brain Drain bubble. All of its decisions, none of its WPF.
///
/// <para>The bubble is a dashboard TRIGGER bubble (built here, not a row in
/// <see cref="ChaosBubbleVariants.All"/> - same arrangement as "glitch"), so it never leaks into
/// the Rabbit Hole's own pool or the chaos setup window. It joins the trigger roll as ONE more
/// equally-weighted id, which is what the other trigger variants are worth, and therefore still
/// sits behind the user's existing Trigger Bubbles switch and their BubbleTriggerChance dial.</para>
///
/// <para>It differs from the long-standing "braindrain" variant on purpose. That one is a live,
/// fused, indigo bubble whose payload is <c>ChaosFlashOverlay</c> - a faint full-screen wash of a
/// random flash image. It has never touched the Brain Drain blur. This one is benign, deep violet,
/// breathes, and on pop runs the real thing: ten seconds of the melting-glass Brain Drain overlay.</para>
/// </summary>
public static class BrainDrainBubble
{
    /// <summary>Spec/sprite id. Resolves the bundled molten-spiral bubble art.</summary>
    public const string VariantId = "braindrain_melt";

    /// <summary>How long the pop holds the drain, in ms.</summary>
    public const int OverlayMs = 10000;

    /// <summary>Melting glass: blur plus the Perlin displacement warp.</summary>
    public const string MeltKind = "braindrain_melt";

    /// <summary>Plain blur, no warp - the Reduced/Off motion fallback.</summary>
    public const string PlainKind = "braindrain";

    /// <summary>Deep violet, so it never reads as the indigo "braindrain" bubble or the
    /// bright-magenta "glitch" one. R,G,B kept as ints here to stay free of System.Windows.Media.</summary>
    public const byte TintR = 0x6A, TintG = 0x22, TintB = 0xA8;

    /// <summary>The bubble's slow breathe: opacity swings between <c>1 - Depth</c> and 1 at
    /// <c>RateRadPerSec</c>. Cheap on purpose - it rides the opacity the frame already computes,
    /// so neither render path gains an element.</summary>
    public const double PulseDepth = 0.18;
    public const double PulseRateRadPerSec = 1.9;

    /// <summary>Opacity multiplier for a bubble that has been alive <paramref name="secondsAlive"/>.</summary>
    public static double PulseAt(double secondsAlive) =>
        (1.0 - PulseDepth) + PulseDepth * (0.5 + 0.5 * Math.Sin(secondsAlive * PulseRateRadPerSec));

    /// <summary>
    /// Owned AND switched on. Ownership is any Bubbles v2 grant (Rain or Spiral In) and is read
    /// from PrizeGrants, never from settings - a synced profile cannot grant itself the bubble.
    /// </summary>
    public static bool IsEligible(bool v2Owned, bool settingOn) => v2Owned && settingOn;

    /// <summary>
    /// The id list the trigger roll draws from: the user's chosen variants plus, when eligible,
    /// the Brain Drain bubble as one more equally-weighted entry (so it lands about 1 spawn in
    /// <c>n+1</c> of those that already passed BubbleTriggerChance). Returns the input list
    /// untouched when it is not eligible, so an unowned account can never roll it.
    /// </summary>
    public static IReadOnlyList<string> RollPool(IReadOnlyList<string>? triggerIds, bool v2Owned, bool settingOn)
    {
        var baseIds = triggerIds ?? Array.Empty<string>();
        if (!IsEligible(v2Owned, settingOn)) return baseIds;
        var pool = new List<string>(baseIds.Count + 1);
        pool.AddRange(baseIds);
        if (!pool.Contains(VariantId)) pool.Add(VariantId);
        return pool;
    }

    /// <summary>
    /// Which timed overlay a pop plays at this motion level.
    ///
    /// <para>Full gets the melt. Reduced and Off both get the plain blur: the capture pump derives
    /// its warp amplitude from the SAME intensity number it derives the blur sigma from
    /// (BrainDrainLayer.SetIntensity), so there is no half-warp to ask for without inventing a
    /// second knob through StartBrainDrainBlur and the pump - and halving the intensity to halve
    /// the warp would quietly halve the blur too. Plain it is.</para>
    /// </summary>
    public static string OverlayKindFor(MotionLevel level) =>
        level == MotionLevel.Full ? MeltKind : PlainKind;

    /// <summary>
    /// Opacity handed to ShowOverlayTimed. That argument IS the drain's intensity - the overlay
    /// turns it straight back into a percent for StartBrainDrainBlur - so the user's own visual
    /// dial (BrainDrainBlurStrength) is the right scale. BrainDrainIntensity is deliberately NOT
    /// used: it is the audio half's per-minute trigger rate, as AppSettings says out loud.
    ///
    /// <para>Floor 0, not 1, since 2026-09-21: 0 is a real slider position now and it means "no
    /// picture", so clamping it up to 1 here would have left one path that still blurs the screen
    /// of someone who turned the blur off. The pop pays its XP either way - the payload checks the
    /// dial before it asks for an overlay at all.</para>
    /// </summary>
    public static double OverlayOpacity(int brainDrainBlurStrength) =>
        Math.Clamp(brainDrainBlurStrength, 0, 100) / 100.0;

    /// <summary>
    /// Whether the pop may raise the overlay at all. Two timed drains must never overlap (melt and
    /// plain blur are one overlay, and the second StartBrainDrainBlur would inherit the first's
    /// melt flag for its whole life), and a pop must never fight the user's own Brain Drain loop:
    /// if their drain is already up, the pop just pays its XP and the bubble is its own reward.
    /// </summary>
    public static bool ShouldPlayOverlay(bool timedDrainInFlight, bool userDrainUp) =>
        !timedDrainInFlight && !userDrainUp;
}
