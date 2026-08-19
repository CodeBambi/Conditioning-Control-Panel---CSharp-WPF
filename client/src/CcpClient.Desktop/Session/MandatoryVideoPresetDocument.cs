using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Effects;

namespace CcpClient.Desktop.Session;

/// <summary>
/// SP-111: the Mandatory Video module's persisted dials — the port's counterpart of WPF's
/// <c>MandatoryVideosEnabled</c> / <c>VideosPerHour</c> / <c>VideoMaxDurationSeconds</c>.
///
/// <para><b>One document per module</b>, on the precedent SP-101 set and every module since has
/// applied (D71/D80): the store's Degraded load path takes the WHOLE document to defaults, so one
/// hand-broken value in a shared file would reset every other module's dials. The clamps are WPF's,
/// in the setters, exactly where WPF puts them.</para>
///
/// <para><b>What is NOT here, and why each one is absent rather than present-and-inert.</b>
/// <c>VideoVolume</c> and the output-device choice (<c>Services/Video/VideoService.cs:1245-1260</c>,
/// <c>:1300-1322</c>) are dials on a soundtrack this row does not play at all — a volume slider over
/// silence is the dead dial §9 D7 refuses. The strict/attention-check settings belong to a subsystem
/// this port does not have. The duration FILTER (min/max clip length at selection time,
/// <c>:63</c>) needs a metadata cache (<c>Services/Video/VideoMetadataCache.cs</c>) the port has not
/// ported, and a filter that silently did nothing would be worse than none. The blurred-background
/// toggle, the hardware-decoding override, remote media and content packs are each a subsystem
/// rather than a dial.</para>
/// </summary>
public sealed class MandatoryVideoPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_video.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>WPF's default: the cap is OFF (<c>VideoService.cs:5509-5510</c> —
    /// <c>VideoMaxDurationSeconds ?? 0</c>, and <c>if (maxSec &lt;= 0) return;</c>).</summary>
    public const int DefaultMaxSeconds = 0;

    /// <summary>Zero means no cap, which is upstream's own encoding. Negative is clamped to it.</summary>
    public const int MinMaxSeconds = 0;

    /// <summary>An hour. Not upstream's — upstream's setting has no ceiling at all — and it exists
    /// because a persisted value with no upper bound is a way to wedge a surface on screen for the
    /// rest of a session. Recorded as a divergence (D126).</summary>
    public const int MaxMaxSeconds = 3600;

    private int _perHour = MandatoryVideoSchedule.DefaultPerHour;
    private int _maxSeconds = DefaultMaxSeconds;

    /// <summary>
    /// The module's own on/off dial — WPF's <c>MandatoryVideosEnabled</c>, the flag its rack row
    /// binds its dot to (<c>Views/Tabs/StudioTabView.xaml.cs:486-487</c>) and the one its scheduler
    /// re-reads on every tick (<c>Services/Video/VideoService.cs:2218</c>). Ships OFF.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How many clips an hour — WPF's <c>VideosPerHour</c>, its slider bounded 1..20 with a default
    /// of 2 (<c>Features/VideoFeatureControl.xaml:79</c>) and its own named ceiling
    /// <c>MaxVideosPerHour = 20</c> (<c>Models/Program/ProgramDefinition.cs:442</c>). It sets the
    /// base INTERVAL, which is then jittered ±20 % and floored at sixty seconds
    /// (<see cref="MandatoryVideoSchedule"/>).
    /// </summary>
    public int PerHour
    {
        get => _perHour;
        set => _perHour = Math.Clamp(value, MandatoryVideoSchedule.MinPerHour, MandatoryVideoSchedule.MaxPerHour);
    }

    /// <summary>
    /// The max-length cap in seconds, or 0 for none — WPF's <c>VideoMaxDurationSeconds</c>, read at
    /// <c>VideoService.cs:5509</c> and disabled at <c>:5510</c> when it is not positive.
    /// </summary>
    public int MaxSeconds
    {
        get => _maxSeconds;
        set => _maxSeconds = Math.Clamp(value, MinMaxSeconds, MaxMaxSeconds);
    }

    /// <summary>The cap as the surface wants it. Zero stays zero, which is "no cap".</summary>
    [JsonIgnore]
    public TimeSpan MaxLength => TimeSpan.FromSeconds(_maxSeconds);

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
