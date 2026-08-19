using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Effects;

namespace CcpClient.Desktop.Session;

/// <summary>
/// SP-109: the Mind Wipe module's persisted dials — the port's counterpart of WPF's
/// <c>MindWipeEnabled</c> / <c>MindWipeFrequency</c> / <c>MindWipeVolume</c>, read into the service
/// at <c>MainWindow/MainWindow.StartStop.cs:230</c>.
///
/// <para><b>One document per module</b>, on the precedent SP-101 set and SP-105/SP-106/SP-108
/// applied (D71/D80): the store's Degraded load path takes the WHOLE document to defaults, so one
/// hand-broken value in a shared file would reset every other module's dials. The clamps are WPF's,
/// in the setters, exactly where WPF puts them.</para>
///
/// <para><b>What is NOT here, and why each one is absent rather than present-and-inert.</b>
/// <c>MindWipeLoop</c> and its crossfade pair (<c>MindWipeService.cs:294-380</c>) are a second
/// playback mode with an A/B crossfade engine — a feature of its own, not a dial, and it is recorded
/// as a divergence rather than shipped as a switch that does nothing.
/// <c>MindWipeAudioPath</c> — the custom-clip override that wins over the folder
/// (<c>:139-146</c>) — has no file picker on any ported panel, and a persisted path nothing can
/// write is exactly the dead dial §9 D7 refuses. Session mode's escalating frequency
/// (<c>:213-235</c>, <c>:709-727</c>) belongs to the scripted preset session, which this port does
/// not have at all (D99).</para>
/// </summary>
public sealed class MindWipePresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_mindwipe.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>WPF default volume: <c>0.5</c>, i.e. 50 % (<c>MindWipeService.cs:26</c>).</summary>
    public const int DefaultVolumePercent = 50;

    /// <summary>WPF clamp <c>Math.Clamp(value, 0, 1)</c> on the 0..1 gain
    /// (<c>MindWipeService.cs:104</c>), expressed here in the percent the panel shows.</summary>
    public const int MinVolumePercent = 0;

    /// <summary>WPF clamp <c>Math.Clamp(value, 0, 1)</c> (<c>MindWipeService.cs:104</c>).</summary>
    public const int MaxVolumePercent = 100;

    private int _perHour = MindWipeSchedule.DefaultPerHour;
    private int _volumePercent = DefaultVolumePercent;

    /// <summary>
    /// The module's own on/off dial — WPF's <c>MindWipeEnabled</c>, the flag its rack row binds its
    /// dot to (<c>Views/Tabs/StudioTabView.xaml.cs:509-510</c>) and the one
    /// <c>StartEngine</c> gates on (<c>MainWindow/MainWindow.StartStop.cs:229</c>). Ships OFF.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How many clips an hour the roll aims for — WPF's <c>MindWipeFrequency</c>, clamped
    /// <c>Math.Clamp(value, 1, 180)</c> (<c>MindWipeService.cs:98-99</c>). It sets the ODDS of each
    /// ten-second window, not the spacing (<see cref="MindWipeSchedule"/>).
    /// </summary>
    public int PerHour
    {
        get => _perHour;
        set => _perHour = Math.Clamp(value, MindWipeSchedule.MinPerHour, MindWipeSchedule.MaxPerHour);
    }

    /// <summary>
    /// Playback gain as a percentage — WPF's <c>MindWipeVolume</c>, divided by 100 on the way into
    /// the service (<c>MainWindow/MainWindow.StartStop.cs:230</c>) and clamped to 0..1 there
    /// (<c>MindWipeService.cs:104</c>). Stored as percent because that is what the panel shows and
    /// what WPF persists.
    /// </summary>
    public int VolumePercent
    {
        get => _volumePercent;
        set => _volumePercent = Math.Clamp(value, MinVolumePercent, MaxVolumePercent);
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
