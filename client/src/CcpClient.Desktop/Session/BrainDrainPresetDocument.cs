using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Effects;

namespace CcpClient.Desktop.Session;

/// <summary>
/// SP-109: the Brain Drain module's persisted dials — <b>the AUDIO half's dials only</b>.
///
/// <para><b>Upstream's one flag drives two mechanisms, and this document is deliberately half of
/// it.</b> <c>BrainDrainEnabled</c> starts the paced audio service
/// (<c>MainWindow/MainWindow.StartStop.cs:241-244</c>) AND a desktop-wide blur
/// (<c>Services/Notifications/OverlayService.cs:382-386</c>). Upstream's own panel labels the two
/// halves in as many words — "VISUAL half: the screen blur's strength (BrainDrainBlurStrength)"
/// (<c>Views/Controls/Studio/BrainDrainFeatureControl.xaml.cs:170-174</c>) — and its comment at
/// <c>MainWindow.StartStop.cs:239-241</c> names the other one: "strength comes from the dedicated
/// blur slider, NOT from BrainDrainIntensity (that is the AUDIO half's per-minute trigger
/// probability)". So <see cref="IntensityPercent"/> below is unambiguously the AUDIO dial, and
/// <c>BrainDrainBlurStrength</c> and <c>BrainDrainMeltEnabled</c> are deliberately ABSENT: the port
/// cannot draw the blur (it needs a per-frame desktop read-back at 30–60 FPS,
/// <c>OverlayService.cs:1965-1995</c>), and a persisted strength for a blur nobody can see is the
/// dead dial §9 D7 refuses. The absence is stated on the row, in the panel and in the arm result —
/// see <see cref="EffectReasonCodes.BrainDrainVisualHalfAbsent"/>.</para>
/// </summary>
public sealed class BrainDrainPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_braindrain.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The port's own default gain, 50 %, matching the sibling audio module
    /// (<c>MindWipeService.cs:26</c>) — <b>a divergence, and a necessary one</b>. Upstream plays this
    /// module's clips at the app-wide master volume
    /// (<c>BrainDrainService.cs:298-302</c>: <c>Math.Clamp(App.Settings.Current.MasterVolume, 0,
    /// 100) / 100f</c>), and the port has no master-volume dial on any panel. Reading a master volume
    /// that does not exist would be inventing a number; leaving the module at full scale would be
    /// louder than upstream's default. A per-module dial the user can actually move is the honest
    /// third option.
    /// </summary>
    public const int DefaultVolumePercent = 50;

    /// <summary>Gain floor, in the percent the panel shows.</summary>
    public const int MinVolumePercent = 0;

    /// <summary>Gain ceiling, in the percent the panel shows.</summary>
    public const int MaxVolumePercent = 100;

    private int _intensityPercent = BrainDrainSchedule.DefaultIntensity;
    private int _volumePercent = DefaultVolumePercent;

    /// <summary>
    /// The module's own on/off dial — WPF's <c>BrainDrainEnabled</c>, the flag its rack row binds its
    /// dot to (<c>Views/Tabs/StudioTabView.xaml.cs:513-514</c>). Ships OFF. In the port it switches
    /// the AUDIO half only.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The AUDIO half's dial: roughly the percent chance of a clip per minute — WPF's
    /// <c>BrainDrainIntensity</c>, clamped <c>Math.Clamp(value, 1, 100)</c>
    /// (<c>BrainDrainService.cs:46-50</c>) and read into the trigger probability at <c>:183</c>.
    /// Upstream's own comment insists this is the audio dial and not the blur's
    /// (<c>MainWindow/MainWindow.StartStop.cs:239-241</c>).
    /// </summary>
    public int IntensityPercent
    {
        get => _intensityPercent;
        set => _intensityPercent = Math.Clamp(
            value, BrainDrainSchedule.MinIntensity, BrainDrainSchedule.MaxIntensity);
    }

    /// <summary>
    /// The finer tick — WPF's <c>BrainDrainHighRefresh</c>, which selects a 500 ms window instead of
    /// 5 s (<c>BrainDrainService.cs:60-69</c>). It changes the GRAIN, not the rate: the probability
    /// is normalised by the number of ticks in a minute (<c>:183</c>), so the same intensity produces
    /// the same amount of sound either way, in smaller or larger steps.
    ///
    /// <para>Upstream's same flag also doubles the blur's capture rate from 30 to 60 FPS
    /// (<c>OverlayService.cs:1986-1987</c>). That half has no counterpart here, which is the whole
    /// point of this document being half a document.</para>
    /// </summary>
    public bool HighRefresh { get; set; }

    /// <summary>Playback gain as a percentage. Port-local — see <see cref="DefaultVolumePercent"/>
    /// for why upstream has no per-module equivalent.</summary>
    public int VolumePercent
    {
        get => _volumePercent;
        set => _volumePercent = Math.Clamp(value, MinVolumePercent, MaxVolumePercent);
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
