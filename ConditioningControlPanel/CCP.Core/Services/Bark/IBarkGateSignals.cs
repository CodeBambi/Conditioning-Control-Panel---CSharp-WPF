using System;
using ConditioningControlPanel.Core.Platform;

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
    /// BarkService.cs:1342). Default false — the port's whisper-busy window is consulted from
    /// the shared <see cref="IAudioPlayer"/> when one is registered (see <see cref="BarkGateSignals"/>).</summary>
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
/// <see cref="IAvatarWindowService.IsSpeaking"/> seam; whisper-audio maps to
/// <see cref="IAudioPlayer.IsWhisperAudioPlaying"/> (WPF parity: App.Audio.IsWhisperAudioPlaying,
/// BarkService.cs:1342). Narrator keeps its non-blocking default — DTRH/Chaos went web-only in
/// the port, so there is no native narrator to hold the floor (noted on the task board).
/// </summary>
public sealed class BarkGateSignals : IBarkGateSignals
{
    private readonly IAvatarWindowService? _avatar;
    private readonly IAudioPlayer? _audio;

    public BarkGateSignals(IAvatarWindowService? avatar = null, IAudioPlayer? audio = null)
    {
        _avatar = avatar;
        _audio = audio;
    }

    // BARK-3: whisper-audio busy window from the shared audio player (WPF parity:
    // App.Audio.IsWhisperAudioPlaying, BarkService.cs:1342). The port's subliminal/flash
    // whisper playback is not yet wired, so nothing marks the window today — the gate is
    // INERT-BUT-READY and lights up automatically when whisper voice audio is ported.
    public bool IsWhisperAudioPlaying => _audio?.IsWhisperAudioPlaying ?? false;

    public bool IsAvatarSpeaking => _avatar?.IsSpeaking ?? false;
}
