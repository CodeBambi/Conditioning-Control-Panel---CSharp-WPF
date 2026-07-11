namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Launch seam for the DTRH ("Down the Rabbit Hole") web roguelite (task-board row #6). The Windows
/// head implements it via a dedicated focusable WebView2 game <c>Window</c> (three.js/WebGL); other
/// heads have no implementation and resolve to null from DI. The Lab tab triggers <see cref="Launch"/>
/// when <c>AppSettings.ChaosWebGameEnabled</c> is on.
///
/// Owner ruling 2026-07-10: the Avalonia DTRH is WEB-ONLY (no native-game fallback), lives in a
/// dedicated <c>Window</c> that is NEVER <c>Topmost</c>, launches windowed, and goes borderless-
/// fullscreen ONLY when the page toggles the HTML5 Fullscreen API — it is NOT a compositor layer and
/// NOT an ambient overlay (normal focusable interactive window; overlay click-through does not apply).
/// Mirrors the established <see cref="IChaosTunnelService"/> head-seam pattern.
/// </summary>
public interface IChaosWebGameService
{
    /// <summary>True while the game window is up.</summary>
    bool IsRunning { get; }

    /// <summary>Build the game window + WebView2 page and navigate to the DTRH entry point. Idempotent:
    /// no-op (and re-focuses the existing window) when a window is already up; no-op when the feature is
    /// off. The 2c Lab-tab launch hook must guard <c>--smoke-test</c> so this is never called headless.</summary>
    void Launch();

    /// <summary>Tear the game window down (idempotent).</summary>
    void Close();
}
