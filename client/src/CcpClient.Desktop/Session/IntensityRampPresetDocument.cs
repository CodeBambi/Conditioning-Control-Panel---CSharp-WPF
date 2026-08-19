using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Effects;

namespace CcpClient.Desktop.Session;

/// <summary>
/// SP-108: the Intensity Ramp module's persisted dials — the port's counterpart of the ramp half of
/// WPF's <c>#region Scheduler</c> block (<c>CCP.Core/Models/AppSettings.cs:2575-2641</c>).
///
/// <para><b>One document per module</b>, on the precedent SP-101 set and SP-105/SP-106 applied
/// (divergences D71/D80): the store's Degraded load path takes the WHOLE document to defaults, so
/// one hand-broken value in a shared file would reset every other module's dials. The clamps are
/// WPF's, in the setters, exactly as WPF clamps them.</para>
///
/// <para><b>Two links, where WPF has five, and the three that are missing are missing for a
/// reason.</b> Upstream links flash opacity, spiral opacity, pink-filter opacity, master volume and
/// subliminal volume (<c>AppSettings.cs:2590-2622</c>). Of those, only the spiral's and the pink
/// filter's exist as dials in this port: flash opacity is one of the dozen draw dials the port's
/// flash panel deliberately omits because it draws none of them (<c>Views/Pages/StudioPage.axaml</c>
/// says so at the flash card, §9 D7 — "a disabled dial swallows the gesture and tells the user
/// nothing"), and neither volume has a dial on any ported panel. A persisted flag with no dial
/// behind it would be a switch that silently does nothing, so the three are ABSENT rather than
/// present-and-inert. Recorded as D93.</para>
/// </summary>
public sealed class IntensityRampPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_ramp.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>WPF default (<c>AppSettings.cs:2582</c>): 60 minutes.</summary>
    public const int DefaultDurationMinutes = 60;

    /// <summary>WPF clamp <c>Math.Clamp(value, 10, 180)</c> (<c>AppSettings.cs:2586</c>).</summary>
    public const int MinDurationMinutes = 10;

    /// <summary>WPF clamp <c>Math.Clamp(value, 10, 180)</c> (<c>AppSettings.cs:2586</c>).</summary>
    public const int MaxDurationMinutes = 180;

    /// <summary>
    /// WPF default (<c>AppSettings.cs:2468</c>): <b>1.0</b> — a ramp that ships at neutral gain and
    /// changes nothing until the user moves it. The panel's slider markup carries <c>Value="1.5"</c>
    /// (<c>Features/IntensityRampFeatureControl.xaml:64</c>) but that is overwritten by
    /// <c>LoadFromSettings</c> on the first bind, so 1.0 is what a fresh install really has.
    /// </summary>
    public const double DefaultMultiplier = 1.0;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1.0, 3.0)</c> (<c>AppSettings.cs:2471</c>). The floor
    /// is ONE, not zero: this dial can only ever make an effect stronger.</summary>
    public const double MinMultiplier = 1.0;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1.0, 3.0)</c> (<c>AppSettings.cs:2471</c>).</summary>
    public const double MaxMultiplier = 3.0;

    private int _durationMinutes = DefaultDurationMinutes;
    private double _multiplier = DefaultMultiplier;

    /// <summary>
    /// The module's own on/off dial — WPF <c>AppSettings.IntensityRampEnabled</c>, <b>default
    /// false</b> (<c>AppSettings.cs:2575-2580</c>), and the value WPF's rack row binds its dot to
    /// (<c>Views/Tabs/StudioTabView.xaml.cs:539</c>).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How long the climb takes, in minutes — WPF's <c>RampDurationMinutes</c>
    /// (<c>AppSettings.cs:2582-2587</c>), the denominator of
    /// <c>progress = elapsed / duration</c> (<c>MainWindow/MainWindow.StartStop.cs:485,493</c>).
    /// </summary>
    public int DurationMinutes
    {
        get => _durationMinutes;
        set => _durationMinutes = Math.Clamp(value, MinDurationMinutes, MaxDurationMinutes);
    }

    /// <summary>
    /// What a linked dial is multiplied by at full progress — WPF's <c>SchedulerMultiplier</c>
    /// (<c>AppSettings.cs:2468-2472</c>), read by the ramp tick at
    /// <c>MainWindow.StartStop.cs:486</c>.
    ///
    /// <para>The name is upstream's and it is confusing on purpose-of-record: the setting is
    /// SHARED with the Scheduler block it sits in, and the Intensity Ramp panel edits it
    /// (<c>Features/IntensityRampFeatureControl.xaml.cs:105-114</c>). The port has no Scheduler, so
    /// here it belongs to this module alone and is named for what it does.</para>
    /// </summary>
    public double Multiplier
    {
        get => _multiplier;
        set => _multiplier = Math.Clamp(value, MinMultiplier, MaxMultiplier);
    }

    /// <summary>
    /// The shape of the climb — WPF's <c>RampCurve</c> (<c>AppSettings.cs:2634-2640</c>), default
    /// <see cref="RampCurve.Linear"/>. Not clamped to the known set: see <see cref="RampCurve"/>.
    /// </summary>
    public RampCurve Curve { get; set; } = RampCurve.Linear;

    /// <summary>
    /// Stop the whole session when the climb finishes — WPF's <c>EndSessionOnRampComplete</c>
    /// (<c>AppSettings.cs:2625-2630</c>), <b>default false</b>, read at
    /// <c>MainWindow.StartStop.cs:546</c>.
    /// </summary>
    public bool EndSessionOnComplete { get; set; }

    /// <summary>WPF's <c>RampLinkSpiralOpacity</c> (<c>AppSettings.cs:2597-2602</c>), default false.
    /// Drives <see cref="SpiralPresetDocument.OpacityPercent"/>.</summary>
    public bool LinkSpiralOpacity { get; set; }

    /// <summary>WPF's <c>RampLinkPinkFilterOpacity</c> (<c>AppSettings.cs:2604-2609</c>), default
    /// false. Drives <see cref="PinkFilterPresetDocument.OpacityPercent"/>.</summary>
    public bool LinkPinkFilterOpacity { get; set; }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
