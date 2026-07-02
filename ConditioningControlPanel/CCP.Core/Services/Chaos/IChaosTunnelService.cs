namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Opt-in 3D "rabbit hole" tunnel background for Chaos runs. The Windows head implements it via an
/// embedded WebView2 page (three.js); other heads have no implementation (resolved as null from DI).
/// The host owns the overlay window lifecycle; the chaos run drives it through these calls.
/// </summary>
public interface IChaosTunnelService
{
    /// <summary>Build the window + page and start loading WITHOUT starting a run (warm the ~2s WebView2/three.js init under the countdown).</summary>
    void Preload();

    /// <summary>Spawn the tunnel and kick off the falling intro. No-op (and closes any stray window) when the feature is off.</summary>
    void Show();

    /// <summary>Nudge the zone scheduler bolder/faster as the run goes deeper.</summary>
    void SendZoneHint(int depth, double intensity);

    /// <summary>Set the fall-speed intensity directly.</summary>
    void SetIntensity(double value);

    /// <summary>Fall speed is tied to the pop streak: accelerates as the combo climbs, brakes when it breaks.</summary>
    void SetStreak(int combo, double mult);

    /// <summary>A fullscreen video covers the tunnel — pause the render loop + hush the ambient bed.</summary>
    void SetVideoPlaying(bool on);

    /// <summary>Spawn a clickable power-up.</summary>
    void SpawnPowerup(string? id = null, double ahead = 90);

    /// <summary>Play the exit animation then tear the window down (idempotent).</summary>
    void CloseActive();
}
