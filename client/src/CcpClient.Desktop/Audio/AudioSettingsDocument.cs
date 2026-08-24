using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Audio;

/// <summary>
/// The APP-WIDE audio settings: how loud this app is, how loud its video is, and which output
/// endpoint its sound goes to. Upstream's <c>#region Audio</c> block
/// (<c>ConditioningControlPanel/Models/AppSettings.cs:1125-1139</c>) plus the endpoint choice at
/// <c>:1238-1255</c>, in this port's persistence shape (schema version + unknown-member
/// preservation, persistence contract §1/§6).
///
/// <para><b>Why it is here and not under <c>Session/</c>, where twelve preset documents live.</b>
/// Those twelve are exactly the set a scripted run BORROWS — <c>Session/ScriptedSessionDials.cs:51-61</c>
/// takes eleven of them by constructor and swaps their values for the length of a run — and none of
/// these three is a session dial: a scripted session does not get to move the user's master volume
/// or re-route the app to a different pair of headphones. The port already keeps app-lifetime
/// documents beside the thing that owns them rather than with the session presets
/// (<c>Motion/MotionSettings.cs</c> → <c>motion.json</c>,
/// <c>Haptics/HapticSettingsDocument.cs</c> → its own file), and it already keeps a module's
/// document beside its module (<c>Effects/PopQuizPresetDocument.cs</c>). The file name follows the
/// same split: <c>audio.json</c>, not <c>session_audio.json</c>.</para>
///
/// <para><b>Its own file rather than more members on the demo settings</b>, for the reason the
/// motion document states: a store's Degraded load takes the WHOLE document to defaults, so one
/// hand-broken value elsewhere must not silently re-route this app's audio to a different endpoint
/// or move a volume the user set.</para>
/// </summary>
public sealed class AudioSettingsDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "audio.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Upstream's own bounds for both volumes (<c>Math.Clamp(value, 0, 100)</c>,
    /// <c>Models/AppSettings.cs:1131</c> and <c>:1138</c>).</summary>
    public const int MinVolume = 0;

    /// <inheritdoc cref="MinVolume"/>
    public const int MaxVolume = 100;

    private int _masterVolume = 32;
    private int _videoVolume = 50;
    private string _outputDeviceName = "";

    /// <summary>
    /// How loud this app is overall, 0..100. <b>32 on a fresh install</b>, which is upstream's own
    /// default (<c>Models/AppSettings.cs:1127</c>, <c>_masterVolume = 32</c>) and not a round number
    /// somebody picked here.
    ///
    /// <para><b>Zero is a real state, not an unset one.</b> Upstream treats
    /// <c>MasterVolume == 0</c> as "ALL audio will be silent" and says so in its own startup
    /// diagnostic (<c>Services/AudioService.cs:535-536</c>); its ducking path returns before
    /// touching another application's sliders for the same reason (<c>:909</c>), and the bark path
    /// surfaces text with no voice (<c>Companion/BarkPipeline.cs:365</c>, WPF
    /// <c>AvatarTube/AvatarTubeWindow.Speech.cs:2188-2189</c>). A consumer that read 0 as "nothing
    /// chosen" and substituted a default would be un-muting a user who muted the app.</para>
    /// </summary>
    public int MasterVolume
    {
        get => _masterVolume;
        set => _masterVolume = Math.Clamp(value, MinVolume, MaxVolume);
    }

    /// <summary>
    /// How loud video playback is, 0..100, UNDER <see cref="MasterVolume"/> rather than instead of
    /// it. <b>50 on a fresh install</b> (<c>Models/AppSettings.cs:1134</c>,
    /// <c>_videoVolume = 50</c>).
    /// </summary>
    public int VideoVolume
    {
        get => _videoVolume;
        set => _videoVolume = Math.Clamp(value, MinVolume, MaxVolume);
    }

    /// <summary>
    /// The render endpoint this app's sound is routed to, by NAME. Empty = the system default,
    /// which is upstream's meaning for an empty choice (<c>Models/AppSettings.cs:1238-1246</c>:
    /// <i>"Empty = system default. Streaming use case: route CCP to a private headset while the
    /// stream's default endpoint stays clean."</i>).
    ///
    /// <para><b>A NAME and no id, which is one field where upstream has two, and the divergence is
    /// forced by this port's backend.</b> Upstream persists an MMDevice id AND a friendly name, and
    /// the name is there because the id moves: <i>"device may have moved IDs across reboots /
    /// driver reinstall — we re-resolve by friendly name"</i>
    /// (<c>Services/AudioService.cs:339-343</c>, field at <c>:1250-1255</c>). This port's backend
    /// has no stable id to persist at all — a miniaudio <c>DeviceInfo.Id</c> is a
    /// process-lifetime native pointer, and passing a stored one to <c>ma_device_init</c> is the
    /// F1 process-fatal crash class observed twice on 2026-07-21
    /// (<c>Audio/SoundFlowAudioBackend.cs</c> header). So the name is the whole persisted choice,
    /// matched against a FRESH enumeration on every init, and a name that is no longer there falls
    /// back to the default exactly as upstream's does
    /// (<c>Audio/SoundArbitration.cs:325-328</c>, WPF <c>AudioService.cs:292-293</c>).</para>
    /// </summary>
    public string OutputDeviceName
    {
        get => _outputDeviceName;
        set => _outputDeviceName = value ?? "";
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted
    /// model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
