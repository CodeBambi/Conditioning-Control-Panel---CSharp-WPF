namespace CcpClient.Desktop.Audio;

/// <summary>The host platform an audio backend is selected for.</summary>
public enum AudioHostPlatform
{
    /// <summary>Not one of the platforms this build knows about.</summary>
    Unknown,

    Windows,

    Linux,

    MacOs,
}

/// <summary>
/// Chooses the audio presence for a platform.
///
/// <para><b>Selection is not availability.</b> <c>runtime-capability-contract.md</c> §2 rule 2 bans
/// a platform check from producing <c>Available</c>, and nothing here produces it — only
/// <see cref="WasapiAudioPresence.Open"/> does, and only after the operating system confirmed an
/// active render session for this process. A platform this build cannot PROVE gets an
/// <see cref="UnsupportedAudioPresence"/> whose reason names the route and the gate that would
/// settle it.</para>
/// </summary>
public static class AudioPresenceFactory
{
    /// <summary>
    /// The exact manual gate a Linux box must run before any Linux audio claim is made. Held as a
    /// constant because it travels with the refusal to whoever hits it, not only in a record file
    /// nobody reads at 2am — the same reason <see cref="Tray.TrayPresenceFactory.LinuxManualGate"/>
    /// is a constant.
    /// </summary>
    public const string LinuxManualGate =
        "MANUAL GATE (Linux, undischarged): on a real Linux desktop session with a running PipeWire or "
        + "PulseAudio server — NOT WSLg — start a session with Mind Wipe enabled and prove it four ways: "
        + "(1) `pactl list short sinks` reports at least one sink, and it enters RUNNING while a cue plays; "
        + "(2) `pactl list sink-inputs` lists a sink input whose application.process.id is THIS process's "
        + "pid while a cue plays, and stops listing it after the session stops — this is the PulseAudio "
        + "analogue of IAudioSessionControl2::GetProcessId and is the fact that would earn Available; "
        + "(3) `pw-dump` (or `pw-top`) shows that node with a non-zero quantum/rate while the clip renders "
        + "— the analogue of IAudioMeterInformation::GetPeakValue, and the one that distinguishes an open "
        + "device from audio actually being rendered; "
        + "(4) a human confirms they heard it — which no automated step on ANY platform discharges, "
        + "Windows included. "
        + "This machine cannot discharge it: the port's WSLg session enumerates a single virtual "
        + "\"RDP Sink\" and ships no pactl at all (client/docs/audio-backend-spike.md:24,88), so every "
        + "check above would either be unrunnable or would pass against a sink with no physical output "
        + "— exactly the false positive the gate exists to exclude.";

    /// <summary>Builds the presence for the platform this process is on, over the SoundFlow backend.</summary>
    public static IAudioPresence Create(Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return CreateFor(CurrentPlatform(), log);
    }

    /// <summary>
    /// The presence for a named platform. Both branches are reachable from either OS, so the refusal
    /// path is testable on a Windows box and the Windows path is testable nowhere else — the honest
    /// asymmetry, stated rather than hidden (the <see cref="Tray.TrayPresenceFactory"/> precedent).
    /// </summary>
    public static IAudioPresence CreateFor(AudioHostPlatform platform, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return platform switch
        {
            AudioHostPlatform.Windows => new WasapiAudioPresence(new SoundFlowAudioBackend(log), log),

            // Linux CAN play audio, and this port's own backend already does it there: SoundFlow
            // ships libminiaudio.so per-RID and enumerated a real endpoint under WSLg during the
            // SP-017 spike (client/docs/audio-backend-spike.md:13,39). So the refusal is NOT "no
            // sound on Linux" — it is that this build has no way to EARN the claim. Available here
            // means the OS confirmed a render session for this process, and that proof is WASAPI
            // (IAudioSessionManager2 / IAudioSessionControl2 / IAudioMeterInformation). The
            // PipeWire-Pulse analogue exists — sink-inputs carry application.process.id — and is not
            // implemented or verified in this build. Playing anyway and reporting success would be
            // precisely the stub this capability was written to make impossible, because on audio a
            // stub is indistinguishable from working.
            AudioHostPlatform.Linux => new UnsupportedAudioPresence(
                AudioReasonCodes.RenderReadbackAbsent,
                "no Linux audio READ-BACK is implemented or verified in this build, so no audio claim can be "
                + "earned here and nothing is attempted. The backend itself works on Linux (SoundFlow ships "
                + "libminiaudio.so per-RID and enumerated an endpoint under WSLg — audio-backend-spike.md:13,39); "
                + "what is missing is the OS-side proof that the sound is really being rendered, which on "
                + "Windows is IAudioSessionControl2::GetProcessId plus IAudioMeterInformation::GetPeakValue "
                + "and on Linux would be the PipeWire/PulseAudio sink-input for this pid. Until the gate below "
                + "runs, a caller must stay quiet and report its module as NOT running — never play anyway and "
                + "call that success. " + LinuxManualGate),

            AudioHostPlatform.MacOs => new UnsupportedAudioPresence(
                AudioReasonCodes.RenderReadbackAbsent,
                "macOS is outside this port's supported Windows/Linux target set (architecture.md), so no "
                + "CoreAudio backend or read-back exists here and nothing was attempted"),

            _ => new UnsupportedAudioPresence(
                AudioReasonCodes.RenderReadbackAbsent,
                "this platform is not one this build has an audio read-back for, and nothing was attempted"),
        };
    }

    /// <summary>The platform this process is on, as a selection input only.</summary>
    public static AudioHostPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return AudioHostPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return AudioHostPlatform.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return AudioHostPlatform.MacOs;
        }

        return AudioHostPlatform.Unknown;
    }
}
