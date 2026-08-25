using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The Subliminals module's persisted dials and its phrase pool — the port's counterpart of
/// the <c>#region Subliminals</c> block WPF reads out of <c>App.Settings.Current</c>
/// (<c>CCP.Core/Models/AppSettings.cs:1232-1290</c>).
///
/// <para><b>Why this is a SECOND document and not two more properties on
/// <see cref="Persistence.SessionPresetDocument"/>.</b> Two reasons, one of them procedural and
/// stated plainly. The procedural one: <c>Persistence/**</c> is outside this packet's File Scope, so the
/// shared session preset was not edited and the fact is recorded as a divergence rather than
/// silently worked around. The substantive one, which is why this shape was chosen rather than
/// merely accepted: <b>fifteen modules editing one document is a chokepoint</b>, exactly as one
/// pinned floor total is, and it has a second cost the floor does not — the store's Degraded load
/// path takes the WHOLE document to defaults, so a hand-broken subliminal phrase list would today
/// reset the user's flash frequency too. One document per module quarantines a module's own
/// corruption to that module, and lets a module land without a schema bump anywhere else. That is
/// the <see cref="Persistence.AssetSelectionDocument"/> precedent the session preset itself cites
/// for existing at all.</para>
///
/// <para>The clamps are WPF's, in the setters, exactly as WPF clamps them — so a hand-edited or
/// out-of-range file is corrected on load and rewritten corrected, which is what WPF does when its
/// own setter runs.</para>
/// </summary>
public sealed class SubliminalPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_subliminal.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>WPF default (<c>AppSettings.cs:1242</c>): 5 messages per minute.</summary>
    public const int DefaultPerMinute = 5;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 30)</c> (<c>AppSettings.cs:1246</c>).</summary>
    public const int MinPerMinute = 1;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 30)</c> (<c>AppSettings.cs:1246</c>).</summary>
    public const int MaxPerMinute = 30;

    /// <summary>WPF default (<c>AppSettings.cs:1249</c>): 2 frames.</summary>
    public const int DefaultDurationFrames = 2;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 10)</c> (<c>AppSettings.cs:1253</c>).</summary>
    public const int MinDurationFrames = 1;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 10)</c> (<c>AppSettings.cs:1253</c>).</summary>
    public const int MaxDurationFrames = 10;

    /// <summary>WPF default (<c>AppSettings.cs:1256</c>): 80 %.</summary>
    public const int DefaultOpacityPercent = 80;

    /// <summary>WPF clamp <c>Math.Clamp(value, 10, 100)</c> (<c>AppSettings.cs:1260</c>).</summary>
    public const int MinOpacityPercent = 10;

    /// <summary>WPF clamp <c>Math.Clamp(value, 10, 100)</c> (<c>AppSettings.cs:1260</c>).</summary>
    public const int MaxOpacityPercent = 100;

    /// <summary>WPF's <c>SubBackgroundColor</c> default (<c>AppSettings.cs:1352</c>).</summary>
    public const string DefaultBackgroundColour = "#000000";

    /// <summary>WPF's <c>SubTextColor</c> default (<c>AppSettings.cs:1366</c>).</summary>
    public const string DefaultTextColour = "#FF00FF";

    /// <summary>WPF's <c>SubBorderColor</c> default (<c>AppSettings.cs:1380</c>).</summary>
    public const string DefaultOutlineColour = "#FFFFFF";

    /// <summary>
    /// WPF's shipped phrase pool, verbatim and in its own order
    /// (<c>AppSettings.cs:1263-1285</c>) — twenty-one phrases, every one of them active by default.
    /// Ported as data because the pool IS the feature: a subliminal module with a different set of
    /// words is a different product.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultPhrases =
    [
        "BAMBI FREEZE",
        "BAMBI RESET",
        "BAMBI SLEEP",
        "BIMBO DOLL",
        "GOOD GIRL",
        "DROP FOR COCK",
        "SNAP AND FORGET",
        "PRIMPED AND PAMPERED",
        "BAMBI DOES AS SHE'S TOLD",
        "BAMBI CUM AND COLLAPSE",
        "ZAP COCK DRAIN OBEY",
        "GIGGLETIME",
        "BAMBI UNIFORM LOCK",
        "COCK ZOMBIE NOW",
        "JUST OBEY",
        "TURN YOUR BRAIN OFF",
        "GOOD GIRLS DONT THINK",
        "DONT THINK SILLY",
        "COCK TURNS MY BRAIN OFF",
        "I CANT RESIST MY TRIGGERS",
        "THERES NO NEED TO THINK",
    ];

    private int _perMinute = DefaultPerMinute;
    private int _durationFrames = DefaultDurationFrames;
    private int _opacityPercent = DefaultOpacityPercent;
    private string _backgroundColour = DefaultBackgroundColour;
    private string _textColour = DefaultTextColour;
    private string _outlineColour = DefaultOutlineColour;
    private Dictionary<string, bool> _phrases = BuildDefaultPool();

    /// <summary>
    /// The Subliminals module's own on/off dial — WPF <c>AppSettings.SubliminalEnabled</c>,
    /// <b>default false</b> (<c>AppSettings.cs:1234-1235</c>).
    ///
    /// <para>The opposite default from Flash Images (<c>:751</c>, true), and it is load-bearing
    /// rather than trivia: WPF's <c>StartEngine</c> calls <c>App.Flash.Start()</c> unconditionally
    /// but reaches <c>App.Subliminal.Start()</c> only <c>if (settings.SubliminalEnabled)</c>
    /// (<c>MainWindow/MainWindow.StartStop.cs:178</c> vs <c>:186-187</c>). A fresh install therefore
    /// shows this row's dot <b>Off</b>, and pressing START changes nothing about it.</para>
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Subliminals per MINUTE. WPF's member is <c>SubliminalFrequency</c>; the port spells out the
    /// unit because WPF's own comment has to ("Messages per minute (1-30)",
    /// <c>AppSettings.cs:1242</c>) and because the scheduler's whole formula is <c>60.0 / this</c>
    /// (<c>SubliminalService.cs:177</c>) — a different period from the flash dial's hour, which is
    /// exactly the kind of near-miss a copied schedule gets wrong. Clamped on every write, WPF's clamp.
    /// </summary>
    public int PerMinute
    {
        get => _perMinute;
        set => _perMinute = Math.Clamp(value, MinPerMinute, MaxPerMinute);
    }

    /// <summary>
    /// How long the card holds, in WPF's own unit: FRAMES. Upstream converts with
    /// <c>Math.Max(100, SubliminalDuration * 17)</c> ms (<c>SubliminalService.cs:615-617</c>) — "~16.6 ms
    /// per frame, minimum 100 ms" by its own comment — so the shipped default of 2 frames is
    /// floored to 100 ms and the dial only starts to matter above 6. The odd unit is kept because
    /// the number a user has in their settings file has to keep meaning what it meant.
    /// Clamped on every write, WPF's clamp.
    /// </summary>
    public int DurationFrames
    {
        get => _durationFrames;
        set => _durationFrames = Math.Clamp(value, MinDurationFrames, MaxDurationFrames);
    }

    /// <summary>WPF's <c>SubliminalOpacity</c> (<c>AppSettings.cs:1256-1261</c>): the card's window
    /// opacity in percent, default 80. Clamped on every write, WPF's clamp.</summary>
    public int OpacityPercent
    {
        get => _opacityPercent;
        set => _opacityPercent = Math.Clamp(value, MinOpacityPercent, MaxOpacityPercent);
    }

    /// <summary>
    /// The card's background colour, as the hex string upstream persists — WPF's
    /// <c>SubBackgroundColor</c> (<c>AppSettings.cs:1352-1357</c>), read on every show at
    /// <c>Services/Subliminal/SubliminalService.cs:622</c>. Held as the raw string because that is
    /// what upstream persists and because an unparseable one has a defined answer rather than an
    /// exception (<see cref="Effects.SubliminalPalette.From"/>). Never null, WPF's own
    /// <c>value ?? "#000000"</c> (<c>:1356</c>).
    /// </summary>
    public string BackgroundColour
    {
        get => _backgroundColour;
        set => _backgroundColour = value ?? DefaultBackgroundColour;
    }

    /// <summary>
    /// The phrase's own colour — WPF's <c>SubTextColor</c> (<c>AppSettings.cs:1366-1371</c>), read
    /// at <c>SubliminalService.cs:623</c>. Never null, WPF's own <c>value ?? "#FF00FF"</c>.
    /// </summary>
    public string TextColour
    {
        get => _textColour;
        set => _textColour = value ?? DefaultTextColour;
    }

    /// <summary>
    /// The outline's colour — WPF's <c>SubBorderColor</c> (<c>AppSettings.cs:1380-1385</c>), read at
    /// <c>SubliminalService.cs:624</c>. Never null, WPF's own <c>value ?? "#FFFFFF"</c>.
    /// </summary>
    public string OutlineColour
    {
        get => _outlineColour;
        set => _outlineColour = value ?? DefaultOutlineColour;
    }

    /// <summary>
    /// WPF's <c>SubBackgroundTransparent</c> (<c>AppSettings.cs:1359-1364</c>), default <b>false</b>:
    /// when on, upstream simply omits the background rectangle from the card
    /// (<c>SubliminalService.cs:969-980</c>) and the desktop shows through the gaps between glyphs.
    ///
    /// <para><b>This port cannot honour it, and says so rather than ignoring it.</b> The card is
    /// composited as one layered window at a single uniform <c>LWA_ALPHA</c>
    /// (<c>Overlay/Win32OverlayPresence.cs</c> calls <c>SetLayeredWindowAttributes</c> with
    /// <c>LWA_ALPHA</c> and nothing else), and the frame it paints is B,G,R,<b>X</b> — the fourth
    /// byte is padding, not per-pixel alpha (<see cref="Overlay.OverlayFrame"/>). There is no value
    /// this rasteriser can write into the background that the OS will treat as "not there". So the
    /// setting is PERSISTED, and the module reports
    /// <see cref="EffectReasonCodes.SubliminalOpaqueBackgroundOnly"/> when it is on: the user's
    /// choice is kept, and the fact that the screen will not obey it is stated in type instead of
    /// being silently dropped.</para>
    ///
    /// <para><b>What it would take.</b> Two mechanisms could express it and neither is this
    /// module's to take: <c>UpdateLayeredWindow</c> (real per-pixel alpha, but mutually exclusive
    /// with <c>SetLayeredWindowAttributes</c>, which would delete the alpha read-back the overlay's
    /// <c>OverlayNotComposited</c> refusal stands on), or <c>LWA_COLORKEY</c> alongside the alpha
    /// (one exact colour punched out, which would also punch out every glyph pixel that happened to
    /// match it). Both change a surface four modules share.</para>
    /// </summary>
    public bool BackgroundTransparent { get; set; }

    /// <summary>
    /// The phrase pool: every phrase the user has, mapped to whether it is active — WPF's
    /// <c>SubliminalPool</c>, a <c>Dictionary&lt;string,bool&gt;</c> (<c>AppSettings.cs:1263-1290</c>).
    /// A null or absent value restores the shipped defaults, which is upstream's own
    /// <c>value ?? new()</c> behaviour at <c>:1289</c> combined with its default initializer.
    /// </summary>
    public Dictionary<string, bool> Phrases
    {
        get => _phrases;
        set => _phrases = value is null or { Count: 0 } ? BuildDefaultPool() : value;
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The phrases that are actually in play right now, in the pool's own order. WPF's
    /// <c>pool.Where(kvp =&gt; kvp.Value).Select(kvp =&gt; kvp.Key)</c>
    /// (<c>SubliminalService.cs:206-207</c>).</summary>
    public IReadOnlyList<string> ActivePhrases() =>
        [.. _phrases.Where(p => p.Value).Select(p => p.Key)];

    private static Dictionary<string, bool> BuildDefaultPool()
    {
        var pool = new Dictionary<string, bool>(DefaultPhrases.Count, StringComparer.Ordinal);
        foreach (var phrase in DefaultPhrases)
        {
            pool[phrase] = true;
        }

        return pool;
    }
}
