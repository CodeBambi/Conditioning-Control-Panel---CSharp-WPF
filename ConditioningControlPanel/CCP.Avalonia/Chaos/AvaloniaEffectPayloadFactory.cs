using ConditioningControlPanel.Core.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Maps a Trigger-Bubble / chaos effect-variant id to a firable <see cref="EffectPayload"/>.
/// Supplied to the Core <c>BubbleEngine</c> so a gated ambient effect bubble fires the right effect
/// on pop. Variant ids mirror <c>ChaosBubbleVariants</c> (flash/subliminal/pink/spiral/braindrain/…).
/// </summary>
public static class AvaloniaEffectPayloadFactory
{
    public static EffectPayload? ForVariant(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return id!.Trim().ToLowerInvariant() switch
        {
            "flash"             => new FlashPayload(),
            "subliminal"        => new SubliminalPayload(),
            "pink"              => new OverlayPayload("pink_filter"),
            "pink_filter"       => new OverlayPayload("pink_filter"),
            "spiral"            => new OverlayPayload("spiral"),
            "braindrain"        => new OverlayPayload("braindrain"),
            "bambifreeze"       => new BambiFreezePayload(),
            "video"             => new VideoPayload(),
            // "htlink" displays as "Gif Rain": the HypnoTube-link payload is long gone — the
            // variant carries the GifCascade payload now (WPF ChaosBubbleVariants.cs:674-675,
            // EffectBubblePayloadKind.GifCascade). The id stays "htlink" for save/discovery.
            "htlink"            => new GifCascadePayload(),
            "whisper"           => new AudioPayload(),
            "audio"             => new AudioPayload(),
            "bouncing"          => new BouncingTextPayload(),
            "bouncingtext"      => new BouncingTextPayload(),
            "gifcascade"        => new GifCascadePayload(),
            _ => null,
        };
    }
}
