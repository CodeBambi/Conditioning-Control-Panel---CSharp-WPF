using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The Pink Filter module's persisted dials — the port's counterpart of the
/// <c>#region Pink Filter</c> block WPF reads out of <c>App.Settings.Current</c>
/// (<c>CCP.Core/Models/AppSettings.cs:3724-3755</c>).
///
/// <para><b>One document per module</b>, on the precedent set and argued for
/// (<see cref="SubliminalPresetDocument"/>, divergence D71): <c>Persistence/**</c> is outside this
/// packet's File Scope too, and the substantive half of that argument is unchanged — the store's
/// Degraded load path takes the WHOLE document to defaults, so one hand-broken value in a shared
/// file would reset every other module's dials. The owner's call to fold them together is still
/// open and nothing behavioural depends on it.</para>
///
/// <para>The clamps are WPF's, in the setters, exactly as WPF clamps them — so a hand-edited or
/// out-of-range file is corrected on load and rewritten corrected, which is what WPF does when its
/// own setter runs.</para>
/// </summary>
public sealed class PinkFilterPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_pinkfilter.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>WPF default (<c>AppSettings.cs:3733</c>): 10 %.</summary>
    public const int DefaultOpacityPercent = 10;

    /// <summary>WPF clamp <c>Math.Clamp(value, 0, 50)</c> (<c>AppSettings.cs:3737</c>). <b>Zero is
    /// inside the range</b>, and the module has to have an answer for it — see
    /// <see cref="Effects.PinkFilterTint.IsInvisible"/>.</summary>
    public const int MinOpacityPercent = 0;

    /// <summary>WPF clamp <c>Math.Clamp(value, 0, 50)</c> (<c>AppSettings.cs:3737</c>). The ceiling
    /// is 50, not 100: a tint the user could take to fully opaque would black out the desktop it is
    /// drawn over.</summary>
    public const int MaxOpacityPercent = 50;

    private int _opacityPercent = DefaultOpacityPercent;
    private string _colour = string.Empty;

    /// <summary>
    /// The module's own on/off dial — WPF <c>AppSettings.PinkFilterEnabled</c>, <b>default false</b>
    /// (<c>AppSettings.cs:3726</c>).
    ///
    /// <para>Unlike the two paced modules, WPF's start body does <b>not</b> branch on this flag:
    /// <c>App.Overlay.Start()</c> is called unconditionally (<c>MainWindow/MainWindow.StartStop.cs:192-193</c>)
    /// and the SERVICE then reads the flag (<c>OverlayService.cs:370-373</c>). Same outcome, and it
    /// is exactly the shape the port's spine already has: the session arms every module and the
    /// module's own dial decides.</para>
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// WPF's <c>PinkFilterOpacity</c> (<c>AppSettings.cs:3733-3738</c>): the tint's opacity in
    /// percent, default 10, clamped to <c>[0, 50]</c> on every write. The conversion to what the
    /// surface is asked for is linear and WPF says so at the line
    /// (<c>OverlayService.cs:1174-1175</c>).
    /// </summary>
    public int OpacityPercent
    {
        get => _opacityPercent;
        set => _opacityPercent = Math.Clamp(value, MinOpacityPercent, MaxOpacityPercent);
    }

    /// <summary>
    /// WPF's <c>PinkFilterColor</c> (<c>AppSettings.cs:3749-3754</c>): a user-picked <c>#RRGGBB</c>
    /// string, <b>empty by default</b>, where empty means "use the default tint". Held as the raw
    /// string rather than a parsed colour because that is what upstream persists and because the
    /// parse is lenient — an unparseable value falls back rather than failing the load
    /// (<see cref="Effects.PinkFilterColour.TryParseHex"/>). Never null, WPF's own <c>value ?? ""</c>.
    /// </summary>
    public string Colour
    {
        get => _colour;
        set => _colour = value ?? string.Empty;
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
