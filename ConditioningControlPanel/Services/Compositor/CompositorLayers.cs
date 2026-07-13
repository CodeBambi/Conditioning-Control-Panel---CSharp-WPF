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
    // WPF-side addition (not yet in the Avalonia table — coordinate before porting): the chaos
    // FX particle field (pop bursts / trails / bolts / ripples), above bubbles, below text.
    public const int Fx = 48;
    public const int BouncingText = 50;
    public const int BrainDrain = 55;
    public const int Spiral = 60;
    public const int PinkTint = 70;
}
