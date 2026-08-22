using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The Bouncing Text module's persisted dials — the port's counterpart of the
/// <c>#region Bouncing Text</c> block WPF reads out of <c>App.Settings.Current</c>
/// (<c>CCP.Core/Models/AppSettings.cs:3596-3719</c>).
///
/// <para><b>One document per module</b>, on the established per-module precedent (D71/D80): the store's Degraded
/// load path takes the WHOLE document to defaults, so one hand-broken value in a shared file would
/// reset every other module's dials.</para>
///
/// <para>The clamps are WPF's, in the setters, exactly as WPF clamps them.</para>
/// </summary>
public sealed class BouncingTextPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_bouncing_text.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>WPF default (<c>AppSettings.cs:3605</c>): 5 on a 1-10 scale.</summary>
    public const int DefaultSpeed = 5;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 10)</c> (<c>:3609</c>).</summary>
    public const int MinSpeed = 1;

    /// <summary>WPF clamp (<c>:3609</c>).</summary>
    public const int MaxSpeed = 10;

    /// <summary>WPF default (<c>:3612</c>): 100 %.</summary>
    public const int DefaultSizePercent = 100;

    /// <summary>WPF clamp <c>Math.Clamp(value, 50, 300)</c> (<c>:3616</c>).</summary>
    public const int MinSizePercent = 50;

    /// <summary>WPF clamp (<c>:3616</c>).</summary>
    public const int MaxSizePercent = 300;

    /// <summary>
    /// WPF default (<c>:3619</c>): <b>100 %</b> — and this number is the reason the module was
    /// blocked for nine waves. At full opacity a transparency-backed text window composited as an
    /// opaque rectangle is a BLACK SCREEN with a word on it (D83). It ships at 100 here because the
    /// surface now carries per-pixel alpha and the transparent pixels are transparent, which is
    /// proven by a composited-desktop read-back rather than assumed.
    /// </summary>
    public const int DefaultOpacityPercent = 100;

    /// <summary>WPF clamp <c>Math.Clamp(value, 0, 100)</c> (<c>:3623</c>). <b>Zero is inside the
    /// range</b> and the module has to answer for it.</summary>
    public const int MinOpacityPercent = 0;

    /// <summary>WPF clamp (<c>:3623</c>).</summary>
    public const int MaxOpacityPercent = 100;

    /// <summary>WPF's shipped phrase pool, in its own order, every entry enabled
    /// (<c>:3626-3637</c>).</summary>
    public static readonly string[] DefaultPhrases =
    [
        "GOOD GIRL", "OBEY", "SUBMIT", "BIMBO", "EMPTY",
        "MINDLESS", "OBEDIENT", "PRETTY", "PINK", "DROP",
    ];

    private int _speed = DefaultSpeed;
    private int _sizePercent = DefaultSizePercent;
    private int _opacityPercent = DefaultOpacityPercent;
    private int _colourMode;
    private string _fixedColour = string.Empty;
    private List<string> _phrases = [.. DefaultPhrases];

    /// <summary>WPF <c>BouncingTextEnabled</c>, <b>default false</b> (<c>:3598</c>).</summary>
    public bool Enabled { get; set; }

    /// <summary>WPF <c>BouncingTextSpeed</c> (<c>:3605-3611</c>).</summary>
    public int Speed
    {
        get => _speed;
        set => _speed = Math.Clamp(value, MinSpeed, MaxSpeed);
    }

    /// <summary>WPF <c>BouncingTextSize</c> (<c>:3612-3618</c>), a percentage of the 72 px base.</summary>
    public int SizePercent
    {
        get => _sizePercent;
        set => _sizePercent = Math.Clamp(value, MinSizePercent, MaxSizePercent);
    }

    /// <summary>WPF <c>BouncingTextOpacity</c> (<c>:3619-3625</c>).</summary>
    public int OpacityPercent
    {
        get => _opacityPercent;
        set => _opacityPercent = Math.Clamp(value, MinOpacityPercent, MaxOpacityPercent);
    }

    /// <summary>WPF <c>BouncingTextColorMode</c> (<c>:3652-3658</c>): 0 random, 1 fixed, 2 rainbow.</summary>
    public int ColourMode
    {
        get => _colourMode;
        set => _colourMode = Math.Clamp(value, 0, 2);
    }

    /// <summary>WPF <c>BouncingTextFixedColor</c> (<c>:3659-3665</c>): <c>#RRGGBB</c>, empty meaning
    /// hot pink. Held as the raw string because that is what upstream persists.</summary>
    public string FixedColour
    {
        get => _fixedColour;
        set => _fixedColour = value ?? string.Empty;
    }

    /// <summary>WPF <c>BouncingTextOutline</c> (<c>:3708-3714</c>): a stroke instead of a shadow.</summary>
    public bool Outline { get; set; }

    /// <summary>
    /// The enabled words.
    ///
    /// <para><b>A list, not upstream's dictionary.</b> WPF persists
    /// <c>Dictionary&lt;string,bool&gt;</c> and reads only the entries whose value is true
    /// (<c>BouncingTextService.cs:264-269</c>), so the false entries exist purely to remember a
    /// checkbox. This port stores the enabled set directly, which round-trips through JSON with a
    /// stable ORDER — and order is load-bearing here because a seeded run has to deal the same word.
    /// A recorded divergence.</para>
    /// </summary>
    public List<string> Phrases
    {
        get => _phrases;
        set => _phrases = value is null || value.Count == 0 ? [.. DefaultPhrases] : value;
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted
    /// model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
