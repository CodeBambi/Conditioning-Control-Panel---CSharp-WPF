using System;

namespace ConditioningControlPanel.Core.Services.Compositor;

/// <summary>
/// Minimal portable contract for a compositor layer. Avalonia-specific layers implement
/// <c>IAvaloniaLayer</c> in <c>CCP.Avalonia</c> which adds the strongly-typed render method.
/// </summary>
public interface ILayer
{
    /// <summary>The z-index of this layer. Lower values are rendered first (behind).</summary>
    int ZIndex { get; }

    /// <summary>Whether the layer currently has visible content to render.</summary>
    bool IsActive { get; }

    /// <summary>Called when the layer is first registered with the compositor.</summary>
    void OnActivated();

    /// <summary>Called when the layer is unregistered from the compositor.</summary>
    void OnDeactivated();

    /// <summary>
    /// AMBIENT CLICK-THROUGH AFFINITY (per-region click-through, team review 2026-07-09).
    /// When true, this layer is 'tinted glass' the user works THROUGH: its painted region is
    /// EXCLUDED from the compositor's capture mask, so clicks there pass to the app behind.
    /// ONLY three layers set this true: the theme color filter (<c>PinkTintLayer</c>), the
    /// spiral (<c>SpiralLayer</c>), and the Blink Trainer (its own click-through windows).
    /// Every OTHER active layer captures pointer input over the region it paints (returns
    /// false here and contributes to the mask via <see cref="CollectCaptureRegions"/>).
    /// Default is false (capture-by-default is the post-2026-07-09 polarity).
    /// </summary>
    bool IsAmbientClickThrough => false;

    /// <summary>
    /// Append this layer's painted capture region(s) (PHYSICAL virtual-desktop px — the same
    /// space as <c>ScreenInfo.Bounds</c> and the WH_MOUSE_LL <c>HookPoint</c>) to the builder.
    /// Called once per engine tick for every ACTIVE, NON-AMBIENT layer to build the per-frame
    /// capture mask; the hook swallows clicks inside the union and passes the rest.
    /// <para>
    /// COVERAGE by layer type:
    /// <list type="bullet">
    ///   <item>Full-bounds layers (video, mandatory video, brain drain, opaque-bg subliminal):
    ///         add every effect screen's bounds.</item>
    ///   <item>Per-item layers (bubbles, flash discs, bouncing text, transparent-bg subliminal):
    ///         add each live item's painted rect.</item>
    /// </list>
    /// The default implementation contributes nothing; a new non-ambient layer MUST override
    /// this (and the engine explicitly skips ambient layers) or its clicks leak to the app
    /// behind. <c>screens</c> is the compositor's current effect-screen list (one per monitor,
    /// or primary-only when <c>DualMonitorEnabled</c> is off); a layer may read it for the
    /// full-bounds default. The builder ignores degenerate (zero/negative) rects.
    /// </para>
    /// </summary>
    void CollectCaptureRegions(CaptureMaskBuilder builder, System.Collections.Generic.IReadOnlyList<ConditioningControlPanel.Core.Platform.ScreenInfo> screens)
    {
        // Default: contribute nothing. Non-ambient layers override to expose their painted
        // region(s); ambient layers (IsAmbientClickThrough == true) are skipped by the engine.
    }
}
