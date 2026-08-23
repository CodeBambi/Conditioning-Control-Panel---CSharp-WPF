using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// The Arcademy's own settings tier, persisted as a DEDICATED document on the client's
/// persistence machinery — the <see cref="Intake.IntakeSettingsDocument"/> /
/// <c>Haptics/HapticSettingsDocument</c> shape. Upstream keeps these keys in
/// <c>settings.json</c> (<c>Models/AppSettings.cs:6524-6681</c>, the "#region The Arcademy"
/// block); the deviation is the port's standing one for feature-owned keys and is recorded here
/// rather than migrated.
///
/// <para><b>Everything in here is a CEILING the page may use less of and never more</b>
/// (<c>AppSettings.cs:6526-6529</c>). The host re-clamps every field arriving from the page
/// (<see cref="ArcademySettingsEcho"/>) and these setters clamp again, so neither a stale page nor
/// a hand-edited file can raise its own limit.</para>
///
/// <para><b>DELIBERATELY NOT DUPLICATED here</b> (<c>AppSettings.cs:6531-6537</c>): the
/// photosensitivity guard (one knob app-wide), the asset-source vocabulary, master volume, the
/// subliminal pool and the motion/performance level. Those are app-wide values the Arcademy
/// READS; they reach the projection through <see cref="ArcademyAppFacts"/>, never through a second
/// copy that could disagree.</para>
///
/// <para><b>The NaN rule is folded into the setters.</b> Upstream clamps twice — in the setter
/// (<c>:6540-6547</c>) and again in <c>ValidateAndCorrect</c> (<c>:6722-6729</c> →
/// <c>ClampArcademy</c>, <c>:6753-6762</c>), where a non-finite value resolves to <c>1.0</c>
/// "rather than poisoning every multiply downstream with NaN". A document has no separate
/// validation pass, so the setter answers non-finite with the same <c>1.0</c>; every observable
/// value is identical and no NaN can ever reach the projection.</para>
/// </summary>
public sealed class ArcademySettingsDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "arcademy.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>0..1, or 1.0 for a non-finite value (see the type remarks).</summary>
    private static double Dial(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 1.0;

    private double _masterIntensity = 0.7;

    /// <summary>Master distraction dial, 0..1 — every engine strength is
    /// <c>clampToCaps(channels, caps) × masterIntensity</c> (<c>AppSettings.cs:6538-6547</c>).</summary>
    public double MasterIntensity
    {
        get => _masterIntensity;
        set => _masterIntensity = Dial(value);
    }

    // The 7-channel caps vector, Intake's DEFAULT_CAPS names verbatim (AppSettings.cs:6549-6600;
    // SYNTHESIS-NOTES #9 — the canon is binauralDepth, never audioDepth). 1.0 = no ceiling.

    private double _capFlashRate = 1.0;

    /// <summary><c>AppSettings.ArcademyCapFlashRate</c> (<c>:6550-6556</c>).</summary>
    public double CapFlashRate
    {
        get => _capFlashRate;
        set => _capFlashRate = Dial(value);
    }

    private double _capFlashOpacity = 1.0;

    /// <summary><c>AppSettings.ArcademyCapFlashOpacity</c> (<c>:6558-6564</c>).</summary>
    public double CapFlashOpacity
    {
        get => _capFlashOpacity;
        set => _capFlashOpacity = Dial(value);
    }

    private double _capSubDensity = 1.0;

    /// <summary><c>AppSettings.ArcademyCapSubDensity</c> (<c>:6566-6572</c>).</summary>
    public double CapSubDensity
    {
        get => _capSubDensity;
        set => _capSubDensity = Dial(value);
    }

    private double _capDuckDepth = 1.0;

    /// <summary><c>AppSettings.ArcademyCapDuckDepth</c> (<c>:6574-6580</c>).</summary>
    public double CapDuckDepth
    {
        get => _capDuckDepth;
        set => _capDuckDepth = Dial(value);
    }

    private double _capBubbleRate = 1.0;

    /// <summary><c>AppSettings.ArcademyCapBubbleRate</c> (<c>:6582-6588</c>).</summary>
    public double CapBubbleRate
    {
        get => _capBubbleRate;
        set => _capBubbleRate = Dial(value);
    }

    private double _capBinauralDepth = 1.0;

    /// <summary><c>AppSettings.ArcademyCapBinauralDepth</c> (<c>:6590-6596</c>).</summary>
    public double CapBinauralDepth
    {
        get => _capBinauralDepth;
        set => _capBinauralDepth = Dial(value);
    }

    private double _capBgIntensity = 1.0;

    /// <summary><c>AppSettings.ArcademyCapBgIntensity</c> (<c>:6598-6604</c>).</summary>
    public double CapBgIntensity
    {
        get => _capBgIntensity;
        set => _capBgIntensity = Dial(value);
    }

    private Dictionary<string, double> _audioLevels = DefaultAudioLevels();

    /// <summary>The five audio-group gains the Arcademy mixes — DTRH's group vocabulary and
    /// defaults with page-local storage swapped for host ownership
    /// (<c>AppSettings.cs:6606-6616</c>). All 0..1 gains except <c>music</c>, a 0..2 MULTIPLIER
    /// over the ambient bed. Null restores the defaults, upstream's own setter behaviour
    /// (<c>:6614</c>).</summary>
    public Dictionary<string, double> AudioLevels
    {
        get => _audioLevels;
        set => _audioLevels = value ?? DefaultAudioLevels();
    }

    /// <summary>Ceiling for one audio group — music is a multiplier, the rest are gains
    /// (<c>AppSettings.ArcademyAudioCeiling</c>, <c>:6618-6619</c>).</summary>
    public static double AudioCeiling(string group) =>
        string.Equals(group, "music", StringComparison.Ordinal) ? 2.0 : 1.0;

    /// <summary><c>AppSettings.DefaultArcademyAudioLevels</c> (<c>:6621-6628</c>).</summary>
    public static Dictionary<string, double> DefaultAudioLevels() => new(StringComparer.Ordinal)
    {
        ["fx"] = 0.48,
        ["voice"] = 0.85,
        ["tutorial"] = 0.85,
        ["drops"] = 0.4,
        ["music"] = 1.0,
    };

    /// <summary>Hard on/off over every Arcademy audio group, separate from the gains on purpose:
    /// a comfort mute must not destroy the mix the user tuned
    /// (<c>AppSettings.cs:6630-6640</c>).</summary>
    public bool AudioMute { get; set; }

    /// <summary>Skip the shell's lesson cards (<c>AppSettings.cs:6642-6650</c>).</summary>
    public bool HideTutorial { get; set; }

    private string _keybindsJson = "";

    /// <summary>Serialized <c>{ "&lt;verb&gt;": "&lt;key&gt;" }</c> for the shell's
    /// manifest-declared keybind slots — opaque to C# apart from a size cap, because the verb
    /// vocabulary is per-game (<c>AppSettings.cs:6652-6668</c>). <b>Over the cap the whole blob is
    /// discarded</b>, which is upstream's setter (<c>:6665</c>) and the reason
    /// <see cref="ArcademySettingsEcho"/> refuses an over-budget write well below it.</summary>
    public string KeybindsJson
    {
        get => _keybindsJson;
        set
        {
            var v = value ?? "";
            _keybindsJson = v.Length > MaxKeybindsJsonChars ? "" : v;
        }
    }

    /// <summary>Upstream's hard cap on the keybind blob (<c>AppSettings.cs:6665</c>).</summary>
    public const int MaxKeybindsJsonChars = 8192;

    private string _settingsJson = "";

    /// <summary>The FLAT per-game settings bag (<c>{ "dt_hard_mode": false, … }</c>), one JSON
    /// blob because per-game knobs are declared in game manifests and must not each become a
    /// property (<c>AppSettings.cs:6670-6683</c>). Global settings never live here. <b>Over the cap
    /// the whole bag is discarded</b> (<c>:6680</c>) — the silent total loss
    /// <see cref="ArcademySettingsEcho"/> keeps itself unable to reach.</summary>
    public string SettingsJson
    {
        get => _settingsJson;
        set
        {
            var v = value ?? "";
            _settingsJson = v.Length > MaxSettingsJsonChars ? "" : v;
        }
    }

    /// <summary>Upstream's hard cap on the per-game bag (<c>AppSettings.cs:6680</c>).</summary>
    public const int MaxSettingsJsonChars = 65536;

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted
    /// model). Load-bearing here: the keys this document deliberately does not carry are the ones
    /// later slices add, and a round trip through this build must not eat them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
