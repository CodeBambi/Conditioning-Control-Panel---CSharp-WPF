using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Session;

/// <summary>
/// A scripted session as it is PERSISTED: the <c>.session.json</c> file format.
///
/// <para><b>Which "session" this is — the name collides and the collision is upstream's.</b>
/// <see cref="SessionEngine"/> is the ordinary engine, WPF's <c>START</c> button
/// (<c>MainWindow/MainWindow.StartStop.cs:159</c>). THIS is the other one: WPF's
/// <c>Services/Session/SessionEngine.cs:22</c>, a TIMED SCRIPTED session with named phases, a
/// duration and a settings snapshot, which runs <b>on top of</b> the ordinary engine and starts it
/// if it is not already running (<c>MainWindow/MainWindow.Presets.cs:1509-1512</c>). Nothing here
/// replaces the ordinary engine; <see cref="ScriptedSessionRun"/> drives it.</para>
///
/// <para><b>The file format is upstream's <c>SessionDefinition</c></b>
/// (<c>Models/SessionDefinition.cs:21-57</c>), read with upstream's serializer options —
/// camelCase names and camelCase string enums
/// (<c>Services/Session/SessionFileService.cs:16-22</c>).
/// The port keeps ONE type where upstream has two: <c>SessionDefinition</c> (the file) and
/// <c>Session</c> (the runtime, <c>Models/Session.cs:19</c>) differ only in that
/// <c>ToSession()</c> DROPS <c>VibeSummary</c> and <c>ImagePath</c>
/// (<c>Models/SessionDefinition.cs:74-97</c>), which is a loss with no behaviour behind it.</para>
///
/// <para><b>Modelled = what the four shipped files carry.</b> Upstream's <c>SessionSettings</c>
/// also has a <c>StartMinute</c>/<c>EndMinute</c> pair per feature and a <c>TimelineEvents</c>
/// list (<c>Models/Session.cs:822-935</c>, <c>:53</c>); none of the four built-ins writes them, the
/// editor that does is not ported, and the members that read them (<c>CheckDelayedFeatures</c>,
/// <c>Services/Session/SessionEngine.cs:663</c>) are not in this slice. They are not dropped:
/// anything unmodelled
/// lands in <see cref="ExtensionData"/> and is preserved, which is the persistence contract's §6
/// rule and the only reason a later slice can add them without a schema story.</para>
/// </summary>
public sealed class ScriptedSession
{
    /// <summary>Upstream's extension for these files (<c>SessionFileService.cs:128</c>,
    /// <c>:209</c>).</summary>
    public const string FileExtension = ".session.json";

    /// <summary>
    /// Upstream's serializer options, verbatim
    /// (<c>Services/Session/SessionFileService.cs:16-22</c>).
    /// The naming policy and the camelCase enum converter are what make <c>"difficulty": "easy"</c>
    /// and <c>"cornerGifPosition": "bottomLeft"</c> bind at all.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Where the shipped sessions are: beside the binary, as WPF resolves its own
    /// (<c>SessionFileService.cs:37-44</c> — <c>BaseDirectory/assets/sessions</c>). The port links
    /// the same four files read-only from the legacy tree into <c>sessions/</c>, the seventh
    /// instance of the payload-glob convention in <c>CcpClient.Desktop.csproj</c>.
    /// </summary>
    public static string BuiltInFolder => Path.Combine(AppContext.BaseDirectory, "sessions");

    /// <summary>Stable identity, and the key upstream localizes names off
    /// (<c>Models/Session.cs:37-39</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name (<c>Models/SessionDefinition.cs:24</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary>Card glyph; upstream's default is the dartboard (<c>:26</c>).</summary>
    public string Icon { get; set; } = "\U0001f3af";

    /// <summary>One-line card blurb (<c>:31</c>). Upstream's runtime type drops this; the port
    /// keeps it.</summary>
    public string VibeSummary { get; set; } = "";

    /// <summary>The spoiler-free body text (<c>:36</c>).</summary>
    public string Description { get; set; } = "";

    /// <summary>Card image (<c>:41</c>). Every shipped file leaves it empty.</summary>
    public string ImagePath { get; set; } = "";

    /// <summary>The session's length in minutes — the denominator of
    /// <see cref="ScriptedSessionRun.ProgressPercent"/> and the completion threshold
    /// (<c>Services/Session/SessionEngine.cs:513</c>). Upstream default 30 (<c>:44</c>).</summary>
    public int DurationMinutes { get; set; } = 30;

    /// <summary>Difficulty band (<c>:45</c>), a camelCase string in the file.</summary>
    public ScriptedSessionDifficulty Difficulty { get; set; } = ScriptedSessionDifficulty.Easy;

    /// <summary>The completion award before penalties and multipliers
    /// (<c>Services/Session/SessionEngine.cs:369</c>). Carried here because it is in the file; the
    /// award itself is not in this slice.</summary>
    public int BonusXP { get; set; } = 400;

    /// <summary>Whether the rack offers it (<c>:47</c>); upstream's start button refuses a row that
    /// is not available (<c>MainWindow/MainWindow.Presets.cs:1463</c>).</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>Whether the rack shows the corner-GIF opt-in (<c>:50</c>). The window it opts into
    /// is not in this slice.</summary>
    public bool HasCornerGifOption { get; set; }

    /// <summary>The sentence beside that opt-in (<c>:51</c>).</summary>
    public string CornerGifDescription { get; set; } = "";

    /// <summary>The dials this session imposes while it runs (<c>:54</c>).</summary>
    public ScriptedSessionSettings Settings { get; set; } = new();

    /// <summary>The named phases, in file order (<c>:57</c>,
    /// <c>Models/Session.cs:47</c>).</summary>
    public List<ScriptedSessionPhase> Phases { get; set; } = [];

    /// <summary>Unknown-member preservation (persistence contract §6).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>
    /// Parse one file's text. Returns null on malformed JSON, which is upstream's own answer —
    /// <c>catch (JsonException) { return null; }</c> (<c>SessionFileService.cs:105-108</c>) — so a
    /// hand-edited or truncated file is a row that does not appear, never a crash.
    /// </summary>
    public static ScriptedSession? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ScriptedSession>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Read one <c>.session.json</c>. Null when the file is absent or unreadable
    /// (<c>SessionFileService.cs:82-108</c>).</summary>
    public static ScriptedSession? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every readable session in a folder — upstream's <c>LoadBuiltInSessions</c>
    /// (<c>Services/Session/SessionFileService.cs:202-217</c>) — ordered by file name so the rack a
    /// later slice draws is the same on every machine, where upstream takes whatever order
    /// <c>Directory.GetFiles</c> gives it. An absent folder is an empty list, never a throw: the
    /// shipped files are content beside the binary, and a published tree missing them is a degraded
    /// install rather than a crash.
    /// </summary>
    public static IReadOnlyList<ScriptedSession> ReadFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return
        [
            .. Directory.GetFiles(folder, "*" + FileExtension)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .Select(ReadFile)
                .OfType<ScriptedSession>(),
        ];
    }

    /// <summary>The four shipped sessions (<see cref="BuiltInFolder"/>).</summary>
    public static IReadOnlyList<ScriptedSession> ReadBuiltIns() => ReadFolder(BuiltInFolder);
}

/// <summary>Upstream's four difficulty bands (<c>Models/Session.cs:8-14</c>), camelCase in the
/// file.</summary>
public enum ScriptedSessionDifficulty
{
    /// <summary>Upstream <c>Easy</c>.</summary>
    Easy,

    /// <summary>Upstream <c>Medium</c>.</summary>
    Medium,

    /// <summary>Upstream <c>Hard</c>.</summary>
    Hard,

    /// <summary>Upstream <c>Extreme</c>.</summary>
    Extreme,
}

/// <summary>Where the corner GIF sits (<c>Models/Session.cs:938-944</c>). Persisted only; the
/// window is not in this slice.</summary>
public enum ScriptedCornerPosition
{
    /// <summary>Upstream <c>TopLeft</c>.</summary>
    TopLeft,

    /// <summary>Upstream <c>TopRight</c>.</summary>
    TopRight,

    /// <summary>Upstream <c>BottomLeft</c> — every shipped file's value.</summary>
    BottomLeft,

    /// <summary>Upstream <c>BottomRight</c>.</summary>
    BottomRight,
}

/// <summary>
/// One named phase on the session's timeline (<c>Models/Session.cs:949-954</c>).
///
/// <para>A phase carries NO settings of its own — it is a label plus the minute it begins, and the
/// engine's only use of it is to decide which one is current
/// (<c>Services/Session/SessionEngine.cs:540-562</c>).
/// Everything that changes over the session's length is a ramp on <see
/// cref="ScriptedSessionSettings"/>, not a property of the phase.</para>
/// </summary>
public sealed class ScriptedSessionPhase
{
    /// <summary>Minutes from the session's start at which this phase begins.</summary>
    public int StartMinute { get; set; }

    /// <summary>The name a user reads (<c>Models/Session.cs:952</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary>The sentence under it (<c>:953</c>).</summary>
    public string Description { get; set; } = "";

    /// <summary>Unknown-member preservation (persistence contract §6).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// The dials a scripted session imposes — upstream's <c>SessionSettings</c>
/// (<c>Models/Session.cs:819-936</c>), in the shape the four shipped files write.
///
/// <para><b>This models the FILE, not the port's dials.</b> Several members here have no
/// counterpart in any of the port's preset documents (flash draw dials, the bubble burst schedule,
/// the corner GIF, the mind-wipe escalation multiplier), and <see cref="ScriptedSessionDials"/>
/// applies only what the port really has. They are modelled anyway because a session file that
/// round-trips through this type must not lose them, and because the values are what a later
/// slice's editor and rack read.</para>
///
/// <para><b>Two of them upstream itself never reads.</b> <c>flashScale</c> and
/// <c>flashSmallSize</c> are written by the definitions and the editor
/// (<c>Models/Session.cs:264</c>, <c>:367</c>) and shown in the spoiler text (<c>:705</c>), but
/// <c>ApplySessionSettings</c> never assigns them to anything
/// (<c>Services/Session/SessionEngine.cs:1150-1160</c> assigns frequency, opacity, image count,
/// clickable, hydra and flash audio — and nothing else). The port does not apply them either; that
/// is upstream's behaviour, not an omission here.</para>
/// </summary>
public sealed class ScriptedSessionSettings
{
    // ---- Flash images (Models/Session.cs:822-834) ----

    /// <summary>Upstream <c>FlashEnabled</c>.</summary>
    public bool FlashEnabled { get; set; }

    /// <summary>Upstream <c>FlashPerHour</c>, default 10 (<c>:825</c>).</summary>
    public int FlashPerHour { get; set; } = 10;

    /// <summary>Upstream <c>FlashPerHourEnd</c> — the frequency ramp's destination (<c>:826</c>).
    /// Ramps are not in this slice.</summary>
    public int FlashPerHourEnd { get; set; } = 10;

    /// <summary>Upstream <c>FlashImages</c>, default 2 (<c>:827</c>).</summary>
    public int FlashImages { get; set; } = 2;

    /// <summary>Upstream <c>FlashOpacity</c>, default 100 (<c>:828</c>).</summary>
    public int FlashOpacity { get; set; } = 100;

    /// <summary>Upstream <c>FlashOpacityEnd</c> (<c>:829</c>). Ramp destination.</summary>
    public int FlashOpacityEnd { get; set; } = 100;

    /// <summary>Upstream <c>FlashScale</c> (<c>:830</c>) — written by the editor, read by nobody at
    /// runtime.</summary>
    public int FlashScale { get; set; } = 100;

    /// <summary>Upstream <c>FlashClickable</c>, default true (<c>:831</c>).</summary>
    public bool FlashClickable { get; set; } = true;

    /// <summary>Upstream <c>FlashHydra</c> — clicking spawns more (<c>:832</c>).</summary>
    public bool FlashHydra { get; set; }

    /// <summary>Upstream <c>FlashAudioEnabled</c>, default true (<c>:833</c>).</summary>
    public bool FlashAudioEnabled { get; set; } = true;

    /// <summary>Upstream <c>FlashSmallSize</c> (<c>:834</c>) — written, never read at
    /// runtime.</summary>
    public bool FlashSmallSize { get; set; }

    // ---- Subliminals (:837-843) ----

    /// <summary>Upstream <c>SubliminalEnabled</c>.</summary>
    public bool SubliminalEnabled { get; set; }

    /// <summary>Upstream <c>SubliminalPerMin</c>, default 5 (<c>:840</c>).</summary>
    public int SubliminalPerMin { get; set; } = 5;

    /// <summary>Upstream <c>SubliminalFrames</c>, default 2 (<c>:841</c>).</summary>
    public int SubliminalFrames { get; set; } = 2;

    /// <summary>Upstream <c>SubliminalOpacity</c>, default 80 (<c>:842</c>).</summary>
    public int SubliminalOpacity { get; set; } = 80;

    /// <summary>Session-specific phrase pool; empty means "leave the user's pool alone"
    /// (<c>:843</c>, and the guard at <c>Services/Session/SessionEngine.cs:1187</c>).</summary>
    public List<string> SubliminalPhrases { get; set; } = [];

    // ---- Audio (:846-850) ----

    /// <summary>Upstream <c>AudioWhispersEnabled</c>.</summary>
    public bool AudioWhispersEnabled { get; set; }

    /// <summary>Upstream <c>WhisperVolume</c>, default 50 (<c>:849</c>).</summary>
    public int WhisperVolume { get; set; } = 50;

    /// <summary>Upstream <c>AudioDuckLevel</c>, default 100 (<c>:850</c>): how far other audio is
    /// ducked, 0-100.</summary>
    public int AudioDuckLevel { get; set; } = 100;

    // ---- Bouncing text (:853-859) ----

    /// <summary>Upstream <c>BouncingTextEnabled</c>.</summary>
    public bool BouncingTextEnabled { get; set; }

    /// <summary>Upstream <c>BouncingTextSpeed</c>, default 5 (<c>:856</c>).</summary>
    public int BouncingTextSpeed { get; set; } = 5;

    /// <summary>Upstream <c>BouncingTextSize</c>, default 100 % (<c>:857</c>).</summary>
    public int BouncingTextSize { get; set; } = 100;

    /// <summary>Upstream <c>BouncingTextOpacity</c>, default 100 (<c>:858</c>).</summary>
    public int BouncingTextOpacity { get; set; } = 100;

    /// <summary>Session-specific phrases (<c>:859</c>).</summary>
    public List<string> BouncingTextPhrases { get; set; } = [];

    // ---- Pink filter (:862-866) ----

    /// <summary>Upstream <c>PinkFilterEnabled</c>.</summary>
    public bool PinkFilterEnabled { get; set; }

    /// <summary>Upstream <c>PinkFilterStartMinute</c> (<c>:863</c>): a non-zero value means the
    /// filter is OFF at t=0 and a delayed start turns it on
    /// (<c>Services/Session/SessionEngine.cs:1283-1292</c>).</summary>
    public int PinkFilterStartMinute { get; set; }

    /// <summary>Upstream <c>PinkFilterStartOpacity</c>, default 10 (<c>:865</c>).</summary>
    public int PinkFilterStartOpacity { get; set; } = 10;

    /// <summary>Upstream <c>PinkFilterEndOpacity</c> (<c>:866</c>) — the ramp's
    /// destination.</summary>
    public int PinkFilterEndOpacity { get; set; } = 10;

    // ---- Spiral (:869-873) ----

    /// <summary>Upstream <c>SpiralEnabled</c>.</summary>
    public bool SpiralEnabled { get; set; }

    /// <summary>Upstream <c>SpiralStartMinute</c> (<c>:870</c>).</summary>
    public int SpiralStartMinute { get; set; }

    /// <summary>Upstream <c>SpiralOpacity</c>, default 15 (<c>:872</c>).</summary>
    public int SpiralOpacity { get; set; } = 15;

    /// <summary>Upstream <c>SpiralOpacityEnd</c> (<c>:873</c>) — the ramp's destination.</summary>
    public int SpiralOpacityEnd { get; set; } = 15;

    // ---- Bubbles (:876-885) ----

    /// <summary>Upstream <c>BubblesEnabled</c>.</summary>
    public bool BubblesEnabled { get; set; }

    /// <summary>Upstream <c>BubblesStartMinute</c> (<c>:877</c>).</summary>
    public int BubblesStartMinute { get; set; }

    /// <summary>Upstream <c>BubblesFrequency</c>, default 5 — spawns a MINUTE
    /// (<c>:879</c>, <c>Services/BubbleService.cs:188</c>).</summary>
    public int BubblesFrequency { get; set; } = 5;

    /// <summary>Upstream <c>BubblesIntermittent</c> (<c>:880</c>): bursts instead of a steady
    /// spawn. The burst schedule is not in this slice.</summary>
    public bool BubblesIntermittent { get; set; }

    /// <summary>Upstream <c>BubblesClickable</c>, default true (<c>:881</c>).</summary>
    public bool BubblesClickable { get; set; } = true;

    /// <summary>Upstream <c>BubblesBurstCount</c>, default 5 (<c>:882</c>).</summary>
    public int BubblesBurstCount { get; set; } = 5;

    /// <summary>Upstream <c>BubblesPerBurst</c>, default 3 (<c>:883</c>).</summary>
    public int BubblesPerBurst { get; set; } = 3;

    /// <summary>Upstream <c>BubblesGapMin</c>, default 5 (<c>:884</c>).</summary>
    public int BubblesGapMin { get; set; } = 5;

    /// <summary>Upstream <c>BubblesGapMax</c>, default 8 (<c>:885</c>).</summary>
    public int BubblesGapMax { get; set; } = 8;

    // ---- Corner GIF (:888-894) ----

    /// <summary>Upstream <c>CornerGifEnabled</c>.</summary>
    public bool CornerGifEnabled { get; set; }

    /// <summary>Upstream <c>CornerGifOpacity</c>, default 20 (<c>:891</c>).</summary>
    public int CornerGifOpacity { get; set; } = 20;

    /// <summary>Upstream <c>CornerGifPath</c> (<c>:892</c>).</summary>
    public string CornerGifPath { get; set; } = "";

    /// <summary>Upstream <c>CornerGifPosition</c>, default bottom-left (<c>:893</c>).</summary>
    public ScriptedCornerPosition CornerGifPosition { get; set; } = ScriptedCornerPosition.BottomLeft;

    /// <summary>Upstream <c>CornerGifSize</c>, default 300 px (<c>:894</c>).</summary>
    public int CornerGifSize { get; set; } = 300;

    // ---- Interactive features (:897-910) ----

    /// <summary>Upstream <c>MandatoryVideosEnabled</c>.</summary>
    public bool MandatoryVideosEnabled { get; set; }

    /// <summary>Upstream <c>VideosPerHour</c> (<c>:900</c>): null means "leave the user's rate"
    /// (<c>Services/Session/SessionEngine.cs:1341-1344</c>).</summary>
    public int? VideosPerHour { get; set; }

    /// <summary>Upstream <c>LockCardEnabled</c>.</summary>
    public bool LockCardEnabled { get; set; }

    /// <summary>Upstream <c>LockCardFrequency</c> (<c>:904</c>): null means "leave the user's
    /// rate".</summary>
    public int? LockCardFrequency { get; set; }

    /// <summary>Upstream <c>BubbleCountEnabled</c>.</summary>
    public bool BubbleCountEnabled { get; set; }

    /// <summary>Upstream <c>BubbleCountFrequency</c> (<c>:909</c>): null means "leave the user's
    /// rate".</summary>
    public int? BubbleCountFrequency { get; set; }

    /// <summary>Upstream <c>MiniGameEnabled</c> (<c>:910</c>). No port counterpart.</summary>
    public bool MiniGameEnabled { get; set; }

    // ---- Mind wipe (:919-923) ----

    /// <summary>Upstream <c>MindWipeEnabled</c>.</summary>
    public bool MindWipeEnabled { get; set; }

    /// <summary>Upstream <c>MindWipeBaseMultiplier</c>, default 1 (<c>:922</c>): the starting
    /// frequency multiplier of session mode's escalation, which the port's module does not have
    /// (<see cref="MindWipePresetDocument"/>).</summary>
    public int MindWipeBaseMultiplier { get; set; } = 1;

    /// <summary>Upstream <c>MindWipeVolume</c>, default 50 (<c>:923</c>).</summary>
    public int MindWipeVolume { get; set; } = 50;

    // ---- Brain drain (:926-930) ----

    /// <summary>Upstream <c>BrainDrainEnabled</c>.</summary>
    public bool BrainDrainEnabled { get; set; }

    /// <summary>Upstream <c>BrainDrainStartMinute</c> (<c>:927</c>).</summary>
    public int BrainDrainStartMinute { get; set; }

    /// <summary>Upstream <c>BrainDrainStartIntensity</c>, default 5 (<c>:929</c>).</summary>
    public int BrainDrainStartIntensity { get; set; } = 5;

    /// <summary>Upstream <c>BrainDrainEndIntensity</c>, default 5 (<c>:930</c>).</summary>
    public int BrainDrainEndIntensity { get; set; } = 5;

    /// <summary>Unknown-member preservation (persistence contract §6).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
