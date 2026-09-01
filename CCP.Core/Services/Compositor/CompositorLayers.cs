namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Authoritative z-order constants for compositor layers. Lower renders first (behind).
/// Values intentionally MATCH the Avalonia port's CompositorLayers so effect code stays
/// portable between the two heads - do not renumber without coordinating both repos.
/// </summary>
public static class CompositorLayers
{
    public const int Video = 10;
    public const int MandatoryVideo = 15;
    public const int LockCard = 20;
    public const int Flash = 30;
    public const int Subliminal = 40;
    public const int Bubbles = 45;
    public const int BouncingText = 50;
    public const int BrainDrain = 55;
    public const int Spiral = 60;
    public const int PinkTint = 70;

    // ---------------- Chaos band (100-199, mirrors the Avalonia table) ----------------
    // Chaos visuals sit ABOVE the session effects (PinkTint 70): WPF re-raises chaos windows
    // topmost on every show/arm (ChaosWindowZ), so a sparkle burst renders over the pink tint.
    // The Avalonia head splits ChaosSkiaFxOverlay into granular layers (ChaosFieldFx=100,
    // ChaosEStimArc=125, ChaosVibeTrail=128, ChaosCursorGlow=130; its ChaosFx=118 is the
    // DIFFERENT edge-vignette effect). The WPF ChaosFxLayer still merges bursts/trails/bolts/
    // ripples/cursor glow into ONE layer, so it takes a single free slot inside the band;
    // split into the granular siblings when converging further.
    public const int Fx = 120;
}
