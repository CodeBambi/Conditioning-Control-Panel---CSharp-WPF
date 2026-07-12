using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Central resolver for the active <see cref="PerformanceTier"/> and the per-tier rendering
/// knobs used to keep the app smooth when many heavy elements (flashes, bubbles, overlays,
/// video) are on screen at once.
///
/// Tier selection:
///   • <see cref="AppSettings.PerformanceMode"/> ON  → always <see cref="PerformanceTier.Performance"/>.
///   • else if <see cref="AppSettings.AutoPerformanceMode"/> ON → escalate by live heavy-element
///     count (flash windows + bubbles).
///   • else → <see cref="PerformanceTier.Quality"/>.
///
/// The Quality tier is intended to be visually indistinguishable from the pre-optimization app;
/// the cheaper tiers trade a little fidelity for headroom.
/// </summary>
public static class PerformanceProfile
{
    // Load thresholds for automatic escalation (number of active heavy elements).
    private const int BalancedThreshold = 8;
    private const int PerformanceThreshold = 16;

    /// <summary>
    /// Live count of heavy on-screen elements (flash windows + ambient bubbles). Cheap to read.
    /// </summary>
    public static int HeavyElementCount =>
        (App.Flash?.ActiveWindowCount ?? 0) + (App.Bubbles?.ActiveBubbles ?? 0);

    /// <summary>
    /// The rendering tier in effect right now. Read this at spawn/start time and cache it in
    /// per-frame loops — do not call it from inside a tick handler for every element.
    /// </summary>
    public static PerformanceTier CurrentTier
    {
        get
        {
            var settings = App.Settings?.Current;
            if (settings == null)
                return PerformanceTier.Quality;

            if (settings.PerformanceMode)
                return PerformanceTier.Performance;

            if (!settings.AutoPerformanceMode)
                return PerformanceTier.Quality;

            var count = HeavyElementCount;
            if (count >= PerformanceThreshold) return PerformanceTier.Performance;
            if (count >= BalancedThreshold) return PerformanceTier.Balanced;
            return PerformanceTier.Quality;
        }
    }

    /// <summary>Largest pixel dimension to decode flash images/GIF frames at, per tier.</summary>
    public static int MaxDecodeDimension(PerformanceTier tier) => tier switch
    {
        PerformanceTier.Performance => 512,
        PerformanceTier.Balanced => 768,
        _ => 1024,
    };

    /// <summary>Bitmap scaling quality for flash/bubble image controls, per tier.</summary>
    public static BitmapScalingMode ScalingMode(PerformanceTier tier) => tier switch
    {
        PerformanceTier.Performance => BitmapScalingMode.NearestNeighbor,
        _ => BitmapScalingMode.LowQuality,
    };

    /// <summary>
    /// Max tease bubbles running a REAL animated decode at once, per tier. The old flat cap of 2
    /// (ChaosTuning.TEASE_MAX_ANIMATED) left every extra tease bubble frozen on its first frame —
    /// visible on flash-heavy presets as "only the first gif animates" (#523). Frames are decoded
    /// once per path and shared (AnimatedWebp.DecodeCached), so additional same-gif bubbles only add
    /// a lightweight animation clock; scale the budget up on capable machines, keep it tight on weak
    /// ones. The per-file byte ceiling (TEASE_ANIMATED_MAX_BYTES) still guards huge gifs regardless.
    /// </summary>
    public static int MaxAnimatedTeaseBubbles(PerformanceTier tier) => tier switch
    {
        PerformanceTier.Performance => Chaos.ChaosTuning.TEASE_MAX_ANIMATED, // weak GPU: keep the safe floor
        PerformanceTier.Balanced => 6,
        _ => 10,
    };

    /// <summary>Whether decorative glow (DropShadow) is allowed at all, per tier.</summary>
    public static bool AllowGlow(PerformanceTier tier) => tier != PerformanceTier.Performance;

    /// <summary>Upper bound on glow blur radius, per tier (only used when glow is allowed).</summary>
    public static double MaxGlowBlurRadius(PerformanceTier tier) => tier switch
    {
        PerformanceTier.Balanced => 18,
        _ => 24,
    };

    /// <summary>Brain Drain screen-capture frames per second, per tier.</summary>
    public static int BrainDrainFps(PerformanceTier tier) => tier switch
    {
        PerformanceTier.Performance => 15,
        PerformanceTier.Balanced => 20,
        _ => 30,
    };

    /// <summary>
    /// Linear downscale divisor for the Brain Drain blur source bitmap, per tier
    /// (4 = capture/blur at 1/4 resolution, then upscale — the upscale is part of the blur).
    /// </summary>
    public static int BrainDrainDownscale(PerformanceTier tier) => tier switch
    {
        PerformanceTier.Performance => 8,
        _ => 4,
    };
}
