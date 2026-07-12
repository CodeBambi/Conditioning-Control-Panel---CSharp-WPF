namespace ConditioningControlPanel.Avalonia.Compositor;

/// <summary>
/// Authoritative z-layer constants for the unified compositor engine.
/// Lower values are rendered first (behind higher values).
/// These override any z-ordering found in the legacy Avalonia codebase.
/// </summary>
public static class CompositorLayers
{
    /// <summary>Video decoder frame (bottom-most content layer).</summary>
    public const int Video = 10;

    /// <summary>Mandatory video attention-check layer.</summary>
    public const int MandatoryVideo = 15;

    /// <summary>Browser fullscreen video mirror layer. Screen-captures the monitor the browser is
    /// fullscreen on and paints a stretched-to-fill copy on every OTHER monitor's compositor window
    /// (the source monitor is skipped to avoid a self-capture feedback freeze). Sits just above
    /// mandatory video so a co-active direct-URL LibVLC render is occluded by the live capture.</summary>
    public const int BrowserMirrorVideo = 16;

    /// <summary>Lock card overlay.</summary>
    public const int LockCard = 20;

    /// <summary>Flash image popups.</summary>
    public const int Flash = 30;

    /// <summary>Subliminal text flashes.</summary>
    public const int Subliminal = 40;

    /// <summary>Chaos / clickable bubbles.</summary>
    public const int Bubbles = 45;

    /// <summary>Bouncing text phrases.</summary>
    public const int BouncingText = 50;

    /// <summary>Brain drain blur overlay.</summary>
    public const int BrainDrain = 55;

    /// <summary>Spiral animation overlay.</summary>
    public const int Spiral = 60;

    /// <summary>Full-screen pink color tint (top-most session effect).</summary>
    public const int PinkTint = 70;

    // ---------------- Chaos band (100-199) ----------------
    // WPF contract evidence (Chaos/ChaosWindowZ.cs + Services/Chaos/ChaosModeService.cs):
    // chaos overlays live in the topmost band and are re-stacked to its TOP on every
    // show/arm (ChaosWindowZ.RaiseTopmost / RaiseAboveVideo, and
    // ChaosModeService.RaiseGameLayerAboveVideo lifts the whole game layer when a
    // mandatory video lands). A freshly-raised chaos overlay therefore sits above the
    // video AND above earlier-shown session-effect windows (spiral/pink tint) in WPF.
    // The compositor mirrors that with a dedicated band ABOVE PinkTint (70). Within the
    // band: ambient field FX lowest (100-119), cursor-attached telegraphs mid (120-139),
    // informational text (banners / pop text / announcer / wave timer) highest (140+).
    // Capture affinity: chaos visuals are capture-VISIBLE — no WPF chaos window calls
    // SetWindowDisplayAffinity (grep-verified 2026-07-04) — so chaos layers stay on the
    // MAIN surface (ExcludeFromCapture = false).

    /// <summary>Ambient field FX (Size Queen rings, snap-ripple casts, Aftermath residue,
    /// rabbit sparkle trails, The Bound tethers) — the floor of the chaos band: WPF
    /// RaiseGameLayerAboveVideo raises it FIRST ("bottom of the gameplay band: ambient FX
    /// that read fine UNDER the bubbles").</summary>
    public const int ChaosFieldFx = 100;

    /// <summary>Bouncing DVD-screensaver logos (Porn DVD toy / Intrusive Thoughts): field
    /// objects above the field-FX floor — WPF RaiseGameLayerAboveVideo raises the DVD logos
    /// after the ambient field FX, below the attention assets (gif cascade / flash wash).</summary>
    public const int ChaosDvd = 105;

    /// <summary>Falling GIF-cascade clips ("attention assets" in WPF: raised ABOVE the
    /// bubbles/chrome, second-highest of the passive set — under only the flash wash).</summary>
    public const int ChaosGifCascade = 110;

    /// <summary>Chaos "braindrain" full-screen image wash (top of the field-FX sub-band:
    /// WPF RaiseGameLayerAboveVideo raises the flash wash LAST of the passive set — above the
    /// gif cascade — as an "attention asset", so it caps the 100–119 field band).</summary>
    public const int ChaosFlashWash = 115;

    /// <summary>Chaos full-screen coloured-vignette "impact juice" pulses (migrated from the
    /// standalone ChaosFxWindow). Sits at the top of the ambient field band, above the flash
    /// wash, below the cursor-attached telegraphs and the info-text sub-band so the announcer /
    /// pop-text read clearly over the subtle edge tint (WPF raised the FX window fully topmost;
    /// keeping it under the info text is a deliberate readability improvement).</summary>
    public const int ChaosFx = 118;

    /// <summary>E-Stim lightning arc bolts (Electrified Rabbits free discharge): transient cyan
    /// bolts flashed between conducting bubble pairs on a pop burst. Ported from the WPF
    /// ChaosSkiaFxOverlay bolt path (the DEFAULT arc renderer when ChaosSkiaFxEnabled),
    /// replacing the standalone ChaosEStimOverlay window. In the cursor-telegraph sub-band,
    /// just below the cursor glow — a foreground gameplay FX above the ambient field band
    /// (100-119).</summary>
    public const int ChaosEStimArc = 125;

    /// <summary>Vibe-pop cursor trail (the vibe_popping toy's buzz): a warm buzzing glow + a short
    /// fading sparkle trail that follows the cursor while the buzz runs. Ported from the
    /// ChaosVibeTrailOverlay window. In the cursor-telegraph sub-band, just below the cool Rabbit
    /// Caller glow (they belong to different toys and do not normally coexist).</summary>
    public const int ChaosVibeTrail = 128;

    /// <summary>Rabbit Caller cursor-glow telegraph (first migrated chaos layer).</summary>
    public const int ChaosCursorGlow = 130;

    // Info-text sub-band (140+) planned order: effect-banner strip 140 (persistent ambient
    // label row, lowest), pop text 145 (positional floaters over the field), announcer 150
    // (the subtitle line — the most important messaging, and in WPF re-raised on every
    // ShowNext so a fresh announce sits above banner/pop-text windows raised earlier).

    /// <summary>Chaos effect-banner strip (active-bonus labels at the top of the primary work area).</summary>
    public const int ChaosEffectBanner = 140;

    /// <summary>Chaos floating combat text (score/effect words popped at a bubble).</summary>
    public const int ChaosPopText = 145;

    /// <summary>Chaos announcer subtitle line (pickups/beats + the Madam's narrator lines).</summary>
    public const int ChaosAnnouncer = 150;

    /// <summary>Chaos wave-timer pill (top-right of the primary monitor: wave, time-left, score).
    /// Top of the info-text sub-band; a persistent corner readout that never overlaps the
    /// centred announcer line, so its exact z vs the announcer is not visually load-bearing.</summary>
    public const int ChaosWaveTimer = 155;

    /// <summary>Attention-check gaze target (migrated from the standalone click-through Window
    /// hosting <c>AttentionCheckControl</c>). Topmost of the effect stack so the "look here"
    /// pulsing ring is never occluded (WPF hosted it in a Topmost window).</summary>
    public const int AttentionCheck = 160;
}
