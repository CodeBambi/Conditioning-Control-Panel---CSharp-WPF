using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Effects;

namespace CcpClient.Desktop.Session;

/// <summary>
/// SP-110: the Lock Card module's persisted dials — the port's counterpart of WPF's
/// <c>LockCardEnabled</c> / <c>LockCardFrequency</c> / <c>LockCardRepeats</c> /
/// <c>LockCardStrict</c> / <c>LockCardPhrases</c>, read into the service at
/// <c>MainWindow/MainWindow.StartStop.cs:206-209</c> and inside its show path
/// (<c>Services/LockCard/LockCardService.cs:267-296</c>).
///
/// <para><b>One document per module</b>, on the precedent SP-101 set and SP-105/SP-106/SP-108/SP-109
/// applied (D71/D80): the store's Degraded load path takes the WHOLE document to defaults, so one
/// hand-broken value in a shared file would reset every other module's dials.</para>
///
/// <para><b>Where the defaults come from.</b> <c>ConditioningControlPanel/CCP.Core/Models/AppSettings.cs</c>
/// — which is under a <c>CCP.*</c> directory but is <b>compiled into the shipping WPF app</b>
/// through its own project reference (<c>ConditioningControlPanel.csproj:52</c>), so it is the
/// shipping product's settings model and behavioural evidence rather than port-attempt code. The
/// five phrases at <c>:3371-3377</c>, the clamps at <c>:3342</c> and <c>:3349</c>, and the
/// ships-off default at <c>:3331</c> are all read from there.</para>
///
/// <para><b>What is NOT here, and why each is absent rather than present-and-inert.</b>
/// <c>LockCardVoiceMode</c> (<c>:3365</c>) solves the card by SPEAKING it into an offline Vosk mic —
/// a microphone capability this port does not have at all, so a switch for it would be a dial that
/// does nothing. The five colour dials (<c>:3386-3418</c>) have no picker on any ported panel; the
/// card paints in upstream's own default palette and the values are not persisted, because a stored
/// colour nothing can write is exactly the dead dial §9 D7 refuses. <c>LockCardPhrasesByMod</c> /
/// <c>ByMode</c> (<c>:1536-1549</c>) belong to the mod system, which the port does not have.</para>
///
/// <para><b>The phrase list has no editor in this build, and that is a deliberate divergence rather
/// than an oversight.</b> Upstream edits it through a <c>TextEditorDialog</c>
/// (<c>Features/LockCardFeatureControl.xaml.cs:203-210</c>). Here the JSON document IS the editor —
/// the same arrangement the two audio modules already have with their clip folders, where the
/// content lives outside the panel and the panel tells the user where it is. The panel lists what
/// will be asked, so the pool is never a mystery.</para>
/// </summary>
public sealed class LockCardPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_lockcard.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>WPF's five built-in phrases, verbatim and in order
    /// (<c>CCP.Core/Models/AppSettings.cs:3371-3377</c>).</summary>
    public static IReadOnlyList<string> DefaultPhrases { get; } =
    [
        "GOOD GIRLS OBEY",
        "I LOVE BEING PROGRAMMED",
        "BAMBI SLEEP",
        "DROP FOR ME",
        "EMPTY AND OBEDIENT",
    ];

    private int _perHour = LockCardSchedule.DefaultPerHour;
    private int _repeats = LockCardSchedule.DefaultRepeats;
    private Dictionary<string, bool> _phrases = DefaultPhrases.ToDictionary(p => p, _ => true, StringComparer.Ordinal);

    /// <summary>
    /// The module's own on/off dial — WPF's <c>LockCardEnabled</c>, the flag its rack row binds its
    /// dot to (<c>Views/Tabs/StudioTabView.xaml.cs:504-505</c>) and the one <c>StartEngine</c> gates
    /// on (<c>MainWindow/MainWindow.StartStop.cs:206</c>). Ships OFF
    /// (<c>AppSettings.cs:3331</c>).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Cards per hour — WPF's <c>LockCardFrequency</c>, clamped <c>Math.Clamp(value, 1, 10)</c>
    /// (<c>AppSettings.cs:3342</c>) and floored again at the point of use
    /// (<c>LockCardService.cs:127</c>).
    /// </summary>
    public int PerHour
    {
        get => _perHour;
        set => _perHour = Math.Clamp(value, LockCardSchedule.MinPerHour, LockCardSchedule.MaxPerHour);
    }

    /// <summary>
    /// How many times the phrase must be typed — WPF's <c>LockCardRepeats</c>, clamped
    /// <c>Math.Clamp(value, 1, 10)</c> (<c>AppSettings.cs:3349</c>).
    /// </summary>
    public int Repeats
    {
        get => _repeats;
        set => _repeats = Math.Clamp(value, LockCardSchedule.MinRepeats, LockCardSchedule.MaxRepeats);
    }

    /// <summary>
    /// WPF's <c>LockCardStrict</c> — "No ESC escape" in its own comment
    /// (<c>AppSettings.cs:3352</c>). <b>In this port it does not gate Escape</b>, and that is
    /// upstream's own rule rather than a weakening: Escape is only swallowed while a panic escape is
    /// genuinely live (<c>LockCardWindow.xaml.cs:632</c>, <c>:610-622</c> — "every failure mode here
    /// has to fall open, not shut"), and this build has no panic hook. What strict still does here is
    /// what it does upstream besides that: the card refuses the window manager's close request, so it
    /// cannot be dismissed by Alt+F4 or by anything but the exit the rule allows
    /// (<c>:678-681</c>). See <see cref="Effects.LockCardTyping.EscapeClosesCard"/>.
    /// </summary>
    public bool Strict { get; set; }

    /// <summary>
    /// The phrase pool and which of its entries are in play — WPF's <c>LockCardPhrases</c>
    /// (<c>AppSettings.cs:3379-3383</c>), whose show path takes exactly the entries whose value is
    /// true (<c>LockCardService.cs:270-273</c>). Null-coalesced on set, as upstream does.
    /// </summary>
    public Dictionary<string, bool> Phrases
    {
        get => _phrases;
        set => _phrases = value ?? new Dictionary<string, bool>(StringComparer.Ordinal);
    }

    /// <summary>The entries in play, in document order — WPF's
    /// <c>LockCardPhrases.Where(p =&gt; p.Value).Select(p =&gt; p.Key)</c>
    /// (<c>LockCardService.cs:270-273</c>). Blank keys are dropped: a card asking for nothing is one
    /// nobody can type.</summary>
    public IReadOnlyList<string> EnabledPhrases() =>
        [.. _phrases.Where(p => p.Value && !string.IsNullOrWhiteSpace(p.Key)).Select(p => p.Key)];

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted
    /// model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
