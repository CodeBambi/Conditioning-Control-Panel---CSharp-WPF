using System;

namespace ConditioningControlPanel.Core.Services.Bark;

/// <summary>
/// Floor-holding signals consulted by the bark gate (WPF EvaluateGate, BarkService.cs:1334-1362).
/// The WPF head reads these from static singletons (App.Audio.IsWhisperAudioPlaying,
/// ChaosNarrator.IsPlaying, App.AvatarWindow.IsSpeaking/IsCompanionBusy); the port injects this
/// seam instead. Every member defaults to "not blocking" so heads without an equivalent subsystem
/// degrade safely (the gate passes) and fakes compile without bodies.
/// </summary>
public interface IBarkGateSignals
{
    /// <summary>A subliminal/flash whisper is audible (WPF App.Audio.IsWhisperAudioPlaying,
    /// BarkService.cs:1342). Default false — the Avalonia head has no whisper-audio flag yet.</summary>
    bool IsWhisperAudioPlaying => false;

    /// <summary>The Chaos narrator holds the floor (WPF ChaosNarrator.IsPlaying, BarkService.cs:1347).
    /// Default false — DTRH/Chaos went web-only in the port; no native narrator exists.</summary>
    bool IsNarratorPlaying => false;

    /// <summary>The avatar is mid text-speech (WPF App.AvatarWindow.IsSpeaking, BarkService.cs:1361).
    /// Default false.</summary>
    bool IsAvatarSpeaking => false;

    /// <summary>The companion chat exchange is active within the window (WPF
    /// App.AvatarWindow.IsCompanionBusy(windowMs), BarkService.cs:1441). Default false.</summary>
    bool IsCompanionBusy(int windowMs) => false;
}

/// <summary>
/// Default gate signals for the Avalonia head: avatar speech maps to the existing
/// <see cref="IAvatarWindowService.IsSpeaking"/> seam; whisper/narrator keep their non-blocking
/// interface defaults until the port grows those subsystems (noted on the task board).
/// </summary>
public sealed class BarkGateSignals : IBarkGateSignals
{
    private readonly IAvatarWindowService? _avatar;

    public BarkGateSignals(IAvatarWindowService? avatar = null) => _avatar = avatar;

    public bool IsAvatarSpeaking => _avatar?.IsSpeaking ?? false;
}
