using System;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Core.Services.Bark;

/// <summary>
/// Speak seam for the bark decision engine (BARK-1 slice 1). The engine decides WHAT to say
/// (rule + variant + substituted line + resolved audio path) and hands delivery to this seam
/// OUTSIDE its lock — mirroring how the WPF engine calls its private Speak after DecideLocked
/// (WPF Services/Companion/BarkService.cs:805-808). Slice 2 implements this against the
/// AvatarTube bubble (Giggle/GigglePriority routing, mute egg, self-echo guard — WPF
/// BarkService.cs:1578-1628); until then the DI default is <see cref="NullBarkSpeaker"/>.
/// </summary>
public interface IBarkSpeaker
{
    /// <summary>
    /// Deliver one decided bark line.
    /// </summary>
    /// <param name="line">Display text after {key}→ctx substitutions (WPF BarkService.cs:1589).</param>
    /// <param name="audioPath">Resolved voiceline path under the active mod, or null for text-only
    /// (WPF ResolveBarkAudio, BarkService.cs:1286-1307).</param>
    /// <param name="priority">True when the bark preempts (non-Normal class or priority ≥ 100 →
    /// GigglePriority; else queued Giggle — WPF BarkService.cs:1619-1624).</param>
    /// <param name="mood">The rule's authored mood tag (may be empty).</param>
    /// <param name="ctx">The per-fire context (trigger + stamped values) for delivery-side decisions.</param>
    /// Default no-op body so lightweight fakes keep compiling.
    void Speak(string line, string? audioPath, bool priority, string? mood, BarkContext ctx) { }
}

/// <summary>
/// Default speaker: logs that a decision reached the seam (no line text — privacy: the decision
/// log already carries a bounded preview) and drops it. Registered in shared DI until slice 2
/// wires the AvatarTube-backed speaker.
/// </summary>
public sealed class NullBarkSpeaker : IBarkSpeaker
{
    private readonly ILogger<NullBarkSpeaker>? _logger;

    public NullBarkSpeaker(ILogger<NullBarkSpeaker>? logger = null) => _logger = logger;

    public void Speak(string line, string? audioPath, bool priority, string? mood, BarkContext ctx) =>
        _logger?.LogDebug(
            "[BARK] speak seam (no speaker wired): trigger={Trigger} priority={Priority} hasAudio={HasAudio}",
            ctx.Trigger, priority, audioPath != null);
}
