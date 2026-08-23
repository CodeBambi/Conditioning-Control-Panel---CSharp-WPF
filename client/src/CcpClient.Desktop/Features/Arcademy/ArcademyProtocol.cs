using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CcpClient.Desktop.Motion;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// Protocol v1 of the Arcademy page↔host bridge — the frames slices 1 and 2 of the board row
/// own (the boot handshake and the settings projection), authored to the
/// <see cref="Goon.GoonProtocol"/> discipline: every type is either EMITTED here, or classified
/// as belonging to a later slice, or refused as not-vocabulary. Nothing arriving from the page is
/// dropped silently.
///
/// <para><b>C# stays the settings owner</b> (<c>ArcademyHostService.cs:24-27</c>): the page gets ONE
/// already-resolved camelCase projection at <c>init</c> and posts typed messages back; every gated
/// field arriving from the page is re-clamped here before it reaches the store, so a stale or
/// hand-edited page cannot raise its own ceiling. The page clamps again on receipt
/// (<c>arcademy/shell/settings.js:17-21</c>), which is why neither side alone can widen one.</para>
///
/// <para><b>Two init fields are projected EMPTY, and both are measured rather than assumed.</b>
/// <c>lexicon</c> and <c>palette</c> (<c>:542-543</c>) are the MOD-RESOLVED display tables —
/// <c>MergeModTable(NeutralLexicon, "lexicon.json")</c> (<c>:1102-1137</c>), whose neutral half
/// exists so a creator mod can override values. This build resolves no arcademy mod root
/// (<c>ModArcademyRoot</c>, <c>:1139-1149</c>, has no counterpart here), and the page renders the
/// identical English without the host table: <c>core/lexicon.js</c>'s <c>t(key, fallback)</c> falls
/// back to the CALLER's inline value first, and those values are the neutral table's values
/// verbatim (sampled across the 318 rows: <c>retake</c> → "Retake", <c>dt_commit</c> → "COMMIT
/// ROW", <c>dv_bell</c> → "The bell. Time is up."). Transcribing 318 rows of data that no code in
/// this build reads would add a drift surface and change nothing on screen; the table arrives with
/// the mod resolution that gives it a purpose. <c>meta</c> (<c>:568</c>) is empty for a different
/// reason: the meta store is slice 3, and <c>new JObject()</c> is upstream's own value when its
/// store is absent.</para>
/// </summary>
public static class ArcademyProtocol
{
    /// <summary>The protocol version this host speaks: <c>ArcademyHostService.cs:39</c>
    /// (<c>private const int Protocol = 1</c>) and <c>arcademy/bridge.js:28</c>
    /// (<c>export const PROTOCOL = 1</c>). The page treats a mismatch as FATAL
    /// (<c>arcademy/boot.js:172-178</c>), so this number is a contract, not a hint.</summary>
    public const int Version = 1;

    private static readonly JsonSerializerOptions PageOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Serialize a host→page frame. camelCase like every other web core here; dictionary
    /// KEYS are left verbatim, which is load-bearing for the per-game bag (<c>dt_hard_mode</c> must
    /// not become <c>dtHardMode</c>).</summary>
    public static string SerializeForPage<TMessage>(TMessage message) =>
        JsonSerializer.Serialize(message, PageOptions);

    // ============================ host → page ============================

    /// <summary>
    /// The one <c>init</c> message (<c>ArcademyHostService.BuildInit</c>, <c>:527-578</c> — the
    /// field names are law). Consent and ceiling flags are projected ALREADY RESOLVED
    /// (<c>remoteMediaEnabled</c>, <c>audioAudible</c>, <c>motionLevel</c>, <c>performanceMode</c>)
    /// so the page never sees raw flags it could recombine into a gate the host did not open
    /// (<c>:520-525</c>).
    /// </summary>
    public static object BuildInit(ArcademySettingsDocument s, ArcademyAppFacts f) => new
    {
        type = "init",                                                          // :533
        protocol = Version,                                                     // :534
        platform = new                                                          // :535-540
        {
            isTouch = false,
            hasHaptics = f.HasHaptics,
            host = "desktop",
        },
        modId = f.ModId,                                                        // :541
        lexicon = new JsonObject(),                                             // :542 (mod-resolved; see type remarks)
        palette = new JsonObject(),                                             // :543
        masterIntensity = s.MasterIntensity,                                    // :544
        caps = BuildCaps(s),                                                    // :545
        effectIntensity = f.EffectIntensity,                                    // :547 — one photosensitivity guard app-wide
        audioLevels = BuildAudioLevels(s),                                      // :548
        audioMute = s.AudioMute,                                                // :549
        masterVolume = Math.Clamp(f.MasterVolume / 100.0, 0.0, 1.0),            // :550
        remoteMediaEnabled = f.RemoteMediaEnabled,                              // :551
        remoteMediaRatio = Math.Clamp(f.RemoteMediaRatio / 100.0, 0.0, 1.0),    // :552
        offlineMode = f.OfflineMode,                                            // :553
        audioAudible = f.AudioAudible,                                          // :554
        // ALWAYS false (:555-557): the launch gate refuses on an audio-only day, so a class can
        // only ever meet one starting mid-run, which arrives as a `suspend` push instead (slice 5).
        audioOnlySession = false,                                               // :557
        protectBrowserVideo = f.ProtectBrowserVideo,                            // :558
        motionLevel = ResolvedMotionLevel(f.Motion),                            // :559
        performanceMode = f.PerformanceMode,                                    // :560
        reducedMotion = f.Motion != MotionLevel.Full,                           // :561
        words = BuildWords(f.SubliminalPhrases),                                // :562
        // UTC seeds the content so the day's classes are globally identical (:563)...
        utcDateSeed = f.Now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),   // :564
        // ...and the LOCAL date is what rolls the attendance streak (:565).
        localDate = f.Now.DateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),        // :566
        overrideCalendar = LoadOverrideCalendar(f.OverrideCalendarPath),        // :567
        meta = new JsonObject(),                                                // :568 (the meta store is slice 3)
        settings = BuildSettingsBag(s, f),                                      // :569
        keybinds = ParseJsonObject(s.KeybindsJson),                             // :570
        hideTutorial = s.HideTutorial,                                          // :571
        // The app-wide panic key, projected for ONE reason (:572-574): shell/keybinds.js refuses to
        // let a game bind over it. The page never handles the panic key itself.
        panicKeyEnabled = f.PanicKeyEnabled,                                    // :575
        panicKey = f.PanicKey,                                                  // :576
    };

    /// <summary>The 7-channel caps vector (<c>BuildCaps</c>, <c>:580-590</c>). The canon is
    /// <c>binauralDepth</c>, never <c>audioDepth</c>.</summary>
    public static object BuildCaps(ArcademySettingsDocument s) => new
    {
        flashRate = s.CapFlashRate,
        flashOpacity = s.CapFlashOpacity,
        subDensity = s.CapSubDensity,
        duckDepth = s.CapDuckDepth,
        bubbleRate = s.CapBubbleRate,
        binauralDepth = s.CapBinauralDepth,
        bgIntensity = s.CapBgIntensity,
    };

    /// <summary>The five audio-group gains, each re-clamped to its own ceiling on the way out
    /// (<c>BuildAudioLevels</c>, <c>:592-614</c>): a stored value that somehow sits above its
    /// ceiling is projected AT the ceiling, and a non-finite one falls back to the group's
    /// default.</summary>
    public static object BuildAudioLevels(ArcademySettingsDocument s)
    {
        var levels = s.AudioLevels;
        double Level(string group, double fallback)
        {
            if (levels is not null && levels.TryGetValue(group, out var v) && double.IsFinite(v))
            {
                return Math.Clamp(v, 0.0, ArcademySettingsDocument.AudioCeiling(group));
            }

            return fallback;
        }

        return new
        {
            fx = Level("fx", 0.48),
            voice = Level("voice", 0.85),
            tutorial = Level("tutorial", 0.85),
            drops = Level("drops", 0.4),
            music = Level("music", 1.0),
        };
    }

    /// <summary>
    /// The subliminal vocabulary for the engine's <c>sub_flash</c> channel (<c>BuildWords</c>,
    /// <c>:616-637</c>): the ENABLED phrases, blank-filtered and trimmed, SHUFFLED, then capped at
    /// 60 so init stays small. MAY BE EMPTY, and that is a contract, not a failure — every consumer
    /// degrades to a word-free look (<c>:611-615</c>).
    /// </summary>
    public static string[] BuildWords(IReadOnlyList<string> phrases)
    {
        var active = phrases
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToList();
        if (active.Count == 0)
        {
            return [];
        }

        for (var i = active.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (active[i], active[j]) = (active[j], active[i]);
        }

        return [.. active.Take(60)];
    }

    /// <summary>The persisted flat per-game bag plus <c>localAssets</c> (<c>BuildSettingsBag</c>,
    /// <c>:646-652</c>) — the host-built inventory rides the bag because the page cannot enumerate
    /// a media origin.</summary>
    public static JsonObject BuildSettingsBag(ArcademySettingsDocument s, ArcademyAppFacts f)
    {
        var bag = ParseJsonObject(s.SettingsJson) ?? new JsonObject();
        bag["localAssets"] = ArcademyLocalAssets.Build(
            f.UserMediaRoot, f.MediaOrigin, f.DisabledAssets, f.UseAssetWhitelist);
        return bag;
    }

    /// <summary>
    /// The motion preference projected as the engine's 0..2 scale, where <b>0 means NO motion</b>
    /// (<c>ResolvedMotionLevel</c>, <c>:706-711</c>). The enum counts the other way (Full = 0), so
    /// this INVERTS rather than casts — a cast would tell the engine that full motion means
    /// "strobe nothing" (<c>:700-705</c>).
    /// </summary>
    public static int ResolvedMotionLevel(MotionLevel level) => level switch
    {
        MotionLevel.Off => 0,
        MotionLevel.Reduced => 1,
        _ => 2,
    };

    /// <summary>Server override-calendar (holidays / event weeks) if a cached copy exists beside
    /// the user data (<c>LoadOverrideCalendar</c>, <c>:715-728</c>). Null = run on the seeded
    /// timetable, which is the designed offline fallback, so an unreadable or non-object file is
    /// null too rather than a boot failure.</summary>
    public static JsonObject? LoadOverrideCalendar(string? path)
    {
        try
        {
            if (path is not { Length: > 0 } || !File.Exists(path))
            {
                return null;
            }

            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Parse an opaque stored blob, or null (<c>ParseJsonObject</c>, <c>:1324-1328</c>):
    /// null/blank/unparseable/not-an-object all answer null, never a throw.</summary>
    public static JsonObject? ParseJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The host-owned fullscreen echo — <b>always the REAL window state</b>
    /// (<c>:400</c> at boot, <c>:504-518</c> on request). C# owns the borderless toggle because the
    /// browser Fullscreen API would hijack Esc, which the page's exit ladder needs.</summary>
    public static object BuildFullscreen(bool on) => new { type = "fullscreen", on };

    /// <summary>The settings echo (<c>:1187</c>): <c>{type:"setting", key, value}</c> carrying the
    /// value that is ACTUALLY STORED after clamping. The page's rows stay visibly "pending" until
    /// this lands (<c>arcademy/shell/settings.js:17-21</c>), so a dropped write is visible rather
    /// than invisible.</summary>
    public static object BuildSetting(string key, object? value) => new { type = "setting", key, value };

    // ============================ page → host ============================

    /// <summary>How this host treats a page→host type.</summary>
    public enum ArcademyHandling
    {
        /// <summary>Handled here, in slices 1-2.</summary>
        HandledHere,

        /// <summary>Real Arcademy vocabulary owned by a LATER slice of the board row. Typed,
        /// logged and NOT acted on — a class message must never half-work.</summary>
        LaterSlice,

        /// <summary>Not Arcademy page→host vocabulary at all.</summary>
        NotVocabulary,
    }

    /// <summary>
    /// Every type upstream's <c>OnPageMessage</c> switch handles (<c>:444-497</c>), classified. A
    /// test pins this table, so widening the bridge fails a fact instead of happening quietly.
    /// The later-slice rows name their slice: <c>meta-command</c> → 3 (<c>:463</c>),
    /// <c>class-started</c>/<c>class-ended</c>/<c>class-left</c> → 4 (<c>:466-480</c>),
    /// <c>resume-request</c> → 5 (<c>:481</c>), <c>assets-request</c> → 7 (<c>:484</c>).
    /// </summary>
    public static ArcademyHandling Classify(string type) => type switch
    {
        "ready" or "log" or "heartbeat" or "pong" or "boot-error" or "fullscreen-request"
            or "set-setting" or "exit" or "exit-done" => ArcademyHandling.HandledHere,
        "meta-command" or "class-started" or "class-ended" or "class-left" or "resume-request"
            or "assets-request" => ArcademyHandling.LaterSlice,
        _ => ArcademyHandling.NotVocabulary,
    };

    /// <summary>The page→host frames this host acts on.</summary>
    public abstract record ArcademyPageMessage
    {
        private ArcademyPageMessage() { }

        /// <summary>Boot completed; the host flushes the handshake
        /// (<c>arcademy/bridge.js:140</c>, <c>OnPageReady</c> <c>:388</c>).</summary>
        public sealed record Ready(int Protocol) : ArcademyPageMessage;

        /// <summary>Tunnelled page log (<c>arcademy/bridge.js</c> boot lane, <c>:47</c>).</summary>
        public sealed record Log(string? Msg) : ArcademyPageMessage;

        /// <summary>Liveness (<c>:450-451</c>). The heartbeat WATCHDOG itself is not slices 1-2.</summary>
        public sealed record Heartbeat : ArcademyPageMessage;

        /// <summary>Answer to a <c>ping</c> (<c>:451</c>).</summary>
        public sealed record Pong : ArcademyPageMessage;

        /// <summary>A fatal page boot failure (<c>:454</c>).</summary>
        public sealed record BootError(string? Msg) : ArcademyPageMessage;

        /// <summary>Page-driven fullscreen (<c>:457</c>); the host answers with the REAL state.</summary>
        public sealed record FullscreenRequest(bool On) : ArcademyPageMessage;

        /// <summary>One settings write (<c>:460</c> → <c>OnSetSetting</c>, <c>:1164</c>). The value
        /// stays raw JSON: it may be a number, a bool, a string or the whole keybind object.</summary>
        public sealed record SetSetting(string Key, JsonElement? Value) : ArcademyPageMessage;

        /// <summary>Page-initiated wind-down (<c>:487</c>).</summary>
        public sealed record Exit(string? Reason) : ArcademyPageMessage;

        /// <summary>The page is finished; the window may go (<c>:492</c>).</summary>
        public sealed record ExitDone : ArcademyPageMessage;
    }

    /// <summary>Parse outcome: every frame lands here — typed, never thrown, never dropped.</summary>
    public abstract record ArcademyPageParseResult
    {
        private ArcademyPageParseResult() { }

        /// <summary>A known v1 type with a well-shaped envelope.</summary>
        public sealed record Parsed(ArcademyPageMessage Message) : ArcademyPageParseResult;

        /// <summary>Real vocabulary this build does not own yet (see <see cref="Classify"/>).
        /// Tolerated and named — never acted on, never silently dropped.</summary>
        public sealed record LaterSlice(string Type) : ArcademyPageParseResult;

        /// <summary>Well-formed frame, outside the Arcademy's page→host vocabulary. Upstream logs
        /// exactly this as "unhandled message" (<c>:495-497</c>).</summary>
        public sealed record UnknownType(string Type) : ArcademyPageParseResult;

        /// <summary>The envelope declares a protocol newer than this build.</summary>
        public sealed record ForwardVersion(string Type, int Protocol) : ArcademyPageParseResult;

        /// <summary>Unparseable / missing-or-non-string type.</summary>
        public sealed record Malformed(string Reason) : ArcademyPageParseResult;
    }

    private static readonly Dictionary<string, Func<JsonElement, ArcademyPageMessage>> Parsers = new(StringComparer.Ordinal)
    {
        ["ready"] = r => new ArcademyPageMessage.Ready(GetInt(r, "protocol") ?? 0),
        ["log"] = r => new ArcademyPageMessage.Log(GetString(r, "msg")),
        ["heartbeat"] = _ => new ArcademyPageMessage.Heartbeat(),
        ["pong"] = _ => new ArcademyPageMessage.Pong(),
        ["boot-error"] = r => new ArcademyPageMessage.BootError(GetString(r, "msg")),
        ["fullscreen-request"] = r => new ArcademyPageMessage.FullscreenRequest(GetBool(r, "on") ?? false),
        ["set-setting"] = r => new ArcademyPageMessage.SetSetting(
            GetString(r, "key") ?? "",
            r.TryGetProperty("value", out var v) ? v.Clone() : null),
        ["exit"] = r => new ArcademyPageMessage.Exit(GetString(r, "reason")),
        ["exit-done"] = _ => new ArcademyPageMessage.ExitDone(),
    };

    /// <summary>Parse one page→host frame. NEVER throws: bad frames are typed outcomes.</summary>
    public static ArcademyPageParseResult ParsePageMessage(string json)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return new ArcademyPageParseResult.Malformed($"json: {ex.GetType().Name}");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new ArcademyPageParseResult.Malformed($"root is {root.ValueKind}, not an object");
        }

        if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            return new ArcademyPageParseResult.Malformed("missing or non-string 'type'");
        }

        var type = typeProp.GetString()!;
        if (GetInt(root, "protocol") is > Version)
        {
            return new ArcademyPageParseResult.ForwardVersion(type, GetInt(root, "protocol")!.Value);
        }

        if (Parsers.TryGetValue(type, out var parser))
        {
            return new ArcademyPageParseResult.Parsed(parser(root));
        }

        return Classify(type) == ArcademyHandling.LaterSlice
            ? new ArcademyPageParseResult.LaterSlice(type)
            : new ArcademyPageParseResult.UnknownType(type);
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool? GetBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? p.GetBoolean()
            : null;

    private static int? GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v)
            ? v
            : null;
}
