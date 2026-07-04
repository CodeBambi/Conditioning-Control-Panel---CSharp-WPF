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

    /// <summary>Bouncing DVD-screensaver logos (Porn DVD toy / Intrusive Thoughts): field
    /// objects above the field-FX floor — WPF RaiseGameLayerAboveVideo raises the DVD logos
    /// after the ambient field FX, below the attention assets (gif cascade / flash wash).</summary>
    public const int ChaosDvd = 105;

    /// <summary>Chaos "braindrain" full-screen image wash (top of the field-FX sub-band:
    /// WPF RaiseGameLayerAboveVideo raises the flash wash LAST of the passive set — above the
    /// gif cascade — as an "attention asset", so it caps the 100–119 field band).</summary>
    public const int ChaosFlashWash = 115;

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
}
