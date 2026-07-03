using System;

namespace ConditioningControlPanel.Core.Services.Overlays;

/// <summary>
/// Cross-platform seam for the screen-overlay subsystem (pink filter, spiral, brain drain).
/// The legacy WPF implementation lives in <c>Services/Notifications/OverlayService.cs</c>;
/// Avalonia heads provide a stub or platform-native replacement.
/// </summary>
public interface IOverlayService
{
    /// <summary>Whether the overlay subsystem is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>When true, overlay level checks are bypassed (e.g. remote control commands).</summary>
    bool BypassLevelCheck { get; set; }

    /// <summary>Start the overlay subsystem. Safe to call repeatedly.</summary>
    void Start();

    /// <summary>Stop the overlay subsystem and tear down all overlay windows.</summary>
    void Stop();

    /// <summary>Recreate or update overlays to match current settings.</summary>
    void RefreshOverlays();

    /// <summary>Briefly intensify active overlays, then restore.</summary>
    void PulseOverlays();

    /// <summary>Restart overlays when the monitor configuration changes.</summary>
    void RefreshForMultiMonitorChange();

    /// <summary>Show an overlay ad-hoc for a fixed duration, then hide it.</summary>
    void ShowOverlayTimed(string kind, int durationMs, double opacity);

    /// <summary>Show an overlay ad-hoc until <see cref="HideOverlaySustained"/> is called.</summary>
    void ShowOverlaySustained(string kind, double opacity);

    /// <summary>Hide an overlay shown via <see cref="ShowOverlaySustained"/>.</summary>
    void HideOverlaySustained(string kind);

    /// <summary>Update the opacity of an overlay shown via <see cref="ShowOverlaySustained"/>.</summary>
    void SetSustainedOverlayOpacity(string kind, double opacity);

    /// <summary>
    /// Releases the pink/spiral opacity ramp holds set by <see cref="SetSustainedOverlayOpacity"/>
    /// WITHOUT tearing the overlays down: the settings-driven refresh re-takes ownership on its
    /// next tick and re-applies the user's saved opacity. Called when a session ends but the
    /// overlays may legitimately stay up (the user had them enabled before the session).
    /// WPF parity: OverlayService.ReleaseOpacityRampHolds.
    /// </summary>
    void ReleaseOpacityRampHolds();

    /// <summary>Pre-decode the configured spiral GIF off the UI thread to avoid hitches.</summary>
    void WarmSpiralCache();

    /// <summary>Called when a topmost window (e.g. mandatory video) opens, so overlays can defer expensive re-pins.</summary>
    void NotifyTopWindowOpened();

    /// <summary>Called when a topmost window (e.g. mandatory video) closes, so overlays can re-assert their z-order.</summary>
    void NotifyTopWindowClosed();
}
