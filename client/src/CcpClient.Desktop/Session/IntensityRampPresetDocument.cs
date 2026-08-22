using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Effects;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The Intensity Ramp module's persisted dials — the port's counterpart of the ramp half of
/// WPF's <c>#region Scheduler</c> block (<c>CCP.Core/Models/AppSettings.cs:2574-2640</c>).
///
/// <para><b>One document per module</b>, on the per-module precedent two earlier modules already applied
/// (divergences D71/D80): the store's Degraded load path takes the WHOLE document to defaults, so
/// one hand-broken value in a shared file would reset every other module's dials. The clamps are
/// WPF's, in the setters, exactly as WPF clamps them.</para>
///
/// <para><b>Two links, where WPF has five, and the three that are missing are missing for a
/// reason.</b> Upstream links flash opacity, spiral opacity, pink-filter opacity, master volume and
/// subliminal volume (<c>AppSettings.cs:2589-2621</c>). Of those, only the spiral's and the pink
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

    /// <summary>WPF default (<c>AppSettings.cs:2581</c>): 60 minutes.</summary>
    public const int DefaultDurationMinutes = 60;

    /// <summary>WPF clamp <c>Math.Clamp(value, 10, 180)</c> (<c>AppSettings.cs:2585</c>).</summary>
    public const int MinDurationMinutes = 10;

    /// <summary>WPF clamp <c>Math.Clamp(value, 10, 180)</c> (<c>AppSettings.cs:2585</c>).</summary>
    public const int MaxDurationMinutes = 180;

    /// <summary>
    /// WPF default (<c>AppSettings.cs:2467</c>): <b>1.0</b> — a ramp that ships at neutral gain and
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
    /// false</b> (<c>AppSettings.cs:2574-2579</c>), and the value WPF's rack row binds its dot to
    /// (<c>Views/Tabs/StudioTabView.xaml.cs:539</c>).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How long the climb takes, in minutes — WPF's <c>RampDurationMinutes</c>
    /// (<c>AppSettings.cs:2581-2586</c>), the denominator of
    /// <c>progress = elapsed / duration</c> (<c>MainWindow/MainWindow.StartStop.cs:485,493</c>).
    /// </summary>
    public int DurationMinutes
    {
        get => _durationMinutes;
        set => _durationMinutes = Math.Clamp(value, MinDurationMinutes, MaxDurationMinutes);
    }

    /// <summary>
    /// What a linked dial is multiplied by at full progress — WPF's <c>SchedulerMultiplier</c>
    /// (<c>AppSettings.cs:2467-2471</c>), read by the ramp tick at
    /// <c>MainWindow.StartStop.cs:486</c>.
    ///
    /// <para>The name is upstream's and it is confusing on purpose-of-record: the setting is
    /// SHARED with the Scheduler block it sits in, and the Intensity Ramp panel edits it
    /// (<c>Features/IntensityRampFeatureControl.xaml.cs:102-111</c>). The port has no Scheduler, so
    /// here it belongs to this module alone and is named for what it does.</para>
    /// </summary>
    public double Multiplier
    {
        get => _multiplier;
        set => _multiplier = Math.Clamp(value, MinMultiplier, MaxMultiplier);
    }

    /// <summary>
    /// The shape of the climb — WPF's <c>RampCurve</c> (<c>AppSettings.cs:2631-2639</c>), default
    /// <see cref="RampCurve.Linear"/>. Not clamped to the known set: see <see cref="RampCurve"/>.
    /// </summary>
    public RampCurve Curve { get; set; } = RampCurve.Linear;

    /// <summary>
    /// Stop the whole session when the climb finishes — WPF's <c>EndSessionOnRampComplete</c>
    /// (<c>AppSettings.cs:2624-2629</c>), <b>default false</b>, read at
    /// <c>MainWindow.StartStop.cs:547</c>.
    /// </summary>
    public bool EndSessionOnComplete { get; set; }

    /// <summary>WPF's <c>RampLinkSpiralOpacity</c> (<c>AppSettings.cs:2596-2601</c>), default false.
    /// Drives <see cref="SpiralPresetDocument.OpacityPercent"/>.</summary>
    public bool LinkSpiralOpacity { get; set; }

    /// <summary>WPF's <c>RampLinkPinkFilterOpacity</c> (<c>AppSettings.cs:2603-2608</c>), default
    /// false. Drives <see cref="PinkFilterPresetDocument.OpacityPercent"/>.</summary>
    public bool LinkPinkFilterOpacity { get; set; }

    /// <summary>
    /// WPF's <c>RampLinkFlashOpacity</c> (<c>CCP.Core/Models/AppSettings.cs:2589-2594</c>), default
    /// false. Drives <see cref="VisualsPresetDocument.FlashOpacityPercent"/> — the FIRST of WPF's
    /// five links and, until the Visuals row landed, the one this port had to leave absent
    /// (D93). The remaining two, master volume and subliminal volume, still have no dial on any
    /// ported panel and are still absent.
    /// </summary>
    public bool LinkFlashOpacity { get; set; }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
