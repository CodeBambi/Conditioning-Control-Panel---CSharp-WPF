using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CcpClient.Desktop.Motion;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// Protocol v1 of the Arcademy page↔host bridge — the frames slices 1 to 4 and 6 of the board row own
/// (the boot handshake, the settings projection, the meta store, the class payout and the panic
/// ladder's <c>suspend</c>/<c>end-run</c> pair), authored to the
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
/// the mod resolution that gives it a purpose. <c>meta</c> (<c>:568</c>) was the second one and is
/// no longer empty: slice 3 landed the store, so the projection carries
/// <see cref="ArcademyMetaStore.Snapshot"/> when a session has one, and <c>new JObject()</c> —
/// upstream's own value on the same line — when it does not.</para>
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
    /// <param name="s">The Arcademy settings document (the ceilings and the per-game bag).</param>
    /// <param name="f">The app-wide values the projection reads but the Arcademy does not own.</param>
    /// <param name="meta">The meta store's whole blob (<c>_meta?.Snapshot()</c>, <c>:568</c>).
    /// Absent means an empty object, which is upstream's own value for a session with no meta
    /// store — <c>?? new JObject()</c> on the same line.</param>
    public static object BuildInit(ArcademySettingsDocument s, ArcademyAppFacts f, JsonObject? meta = null) => new
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
        meta = meta ?? new JsonObject(),                                        // :568
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

    /// <summary>
    /// The suspend/resume push (<c>Suspend</c>, <c>:294-303</c>, posted at <c>:300</c>): the engine
    /// drops every effect NOW and the class pauses. <c>reason</c> is protocol vocabulary —
    /// <c>"video"</c> | <c>"audio-only"</c> | <c>"panic"</c> (<c>:293</c>) — and this build sends
    /// two of the three: <c>"panic"</c> from the ladder (slice 6) and <c>"video"</c> from
    /// native-state suspension (slice 5, <see cref="ArcademySession.NativeVideoChanged"/>).
    /// <c>"audio-only"</c> is never sent, because there is no audio-only session in this build to
    /// send it about.
    ///
    /// <para><b>A panic suspend is the one suspend with no natural end</b> (<c>:342-345</c>): a
    /// video un-suspends when it ends, an audio-only session when it does, and this one only lifts
    /// on the page's own <c>resume-request</c>, which the host grants.</para>
    /// </summary>
    public static object BuildSuspend(bool on, string reason) => new { type = "suspend", on, reason };

    /// <summary>The graceful wind-down ask (<c>CloseActive</c>, <c>:254</c>):
    /// <c>{type:"end-run", reason:"host"}</c>. The page winds itself down and answers
    /// <c>exit-done</c>; upstream bounds that wait at 1200ms (<c>ArmExitWatchdog</c>,
    /// <c>:1988-1993</c>) so a wedged page cannot trap the user behind their own panic press.
    /// The literal <c>"host"</c> is upstream's, on every close path including the panic one.</summary>
    public static object BuildEndRun(string reason = "host") => new { type = "end-run", reason };

    /// <summary>The settings echo (<c>:1187</c>): <c>{type:"setting", key, value}</c> carrying the
    /// value that is ACTUALLY STORED after clamping. The page's rows stay visibly "pending" until
    /// this lands (<c>arcademy/shell/settings.js:17-21</c>), so a dropped write is visible rather
    /// than invisible.</summary>
    public static object BuildSetting(string key, object? value) => new { type = "setting", key, value };

    /// <summary>The per-key meta reply (upstream <c>ArcademyMetaStore.cs:147</c>) — the answer to every
    /// <c>meta-command</c>, carrying the POST-write value so the page's cache converges on what C#
    /// actually stored even when a write was clamped or refused.</summary>
    public static object BuildMeta(string key, JsonNode? value) => new { type = "meta", key, value };

    /// <summary>The whole-blob meta push (<c>SnapshotMessage</c>,
    /// upstream <c>ArcademyMetaStore.cs:107-113</c>), sent after a HOST-side write such as an attendance
    /// credit. The page handles both shapes and says which one matters: "the authoritative streak
    /// arrives only that way" (<c>arcademy/core/store.js:35-38</c>).</summary>
    public static object BuildMetaSnapshot(int rev, JsonObject state) => new { type = "meta", rev, state };

    /// <summary>
    /// The <c>payout-result</c> reply (<c>ArcademyHostService.cs:1410-1421</c>) — the ONLY source
    /// of an XP number on the page (<c>arcademy/shell/shell.js:1331</c>).
    ///
    /// <para><b><c>levelUp</c> is a real before/after comparison</b>, exactly as upstream fills it
    /// by reading <c>PlayerLevel</c> either side of <c>AddXP</c> (<c>:1390</c>, <c>:1399</c>). The
    /// comparison is made in <c>ArcademySession.ClassEnd</c> and arrives here already stamped on the
    /// value; this method reports it and decides nothing. A payout that was never banked — no ledger
    /// wired, a retake's zero, an unreadable ledger — reports <c>false</c>, because no level moved.
    /// See <see cref="ArcademyClassPayout.ArcademyPayout.XpBanked"/> for which of those it was.</para>
    ///
    /// <para><b>The port builds no level-up CEREMONY.</b> This frame is the one field upstream's
    /// payout already carries; what the page then does with it (<c>arcademy/shell/shell.js:1338</c>
    /// announces it, <c>arcademy/boot.js:193</c> adds a log suffix) is the payload's own behaviour
    /// over a true fact. Nothing here plays a sound, animates, raises a balloon or changes an
    /// avatar.</para>
    /// </summary>
    public static object BuildPayoutResult(ArcademyClassPayout.ArcademyPayout p) => new
    {
        type = "payout-result",                 // :1413
        gameKey = p.GameKey,
        xp = p.Xp,
        levelUp = p.LevelUp,                    // :1416 — see the remarks above
        streak = p.Streak,
        perfectAttendance = p.PerfectAttendance,
        classesToday = p.ClassesToday,
        // Additive: the report card reads it to explain a 0 XP line. Older pages ignore it (:1419).
        retake = p.Retake,
    };

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
    /// <c>meta-command</c> (<c>:463</c>) moved to <b>handled</b> with slice 3,
    /// <c>class-started</c>/<c>class-ended</c>/<c>class-left</c> (<c>:466-480</c>) with slice 4,
    /// and <c>resume-request</c> (<c>:481</c>) with slice 6.
    ///
    /// <para><b><c>resume-request</c> is the PANIC ladder's frame, not slice 5's</b>, and the
    /// source says so: <c>OnResumeRequest</c> refuses every reason but <c>"panic"</c> —
    /// "only panic resumes on request" (<c>:349-352</c>) — because a video suspend lifts when the
    /// video ends (<c>:1720-1726</c>) and an audio-only one when the session does (<c>:1846</c>).
    /// The row was classified to 5 while the ladder was unported; slice 6 is where it actually
    /// belongs, so the one row still named for a later slice is
    /// <c>assets-request</c> → 7 (<c>:484</c>).</para>
    /// </summary>
    public static ArcademyHandling Classify(string type) => type switch
    {
        "ready" or "log" or "heartbeat" or "pong" or "boot-error" or "fullscreen-request"
            or "set-setting" or "exit" or "exit-done"
            or "meta-command" or "class-started" or "class-ended" or "class-left"
            or "resume-request"
            => ArcademyHandling.HandledHere,
        "assets-request" => ArcademyHandling.LaterSlice,
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

        /// <summary>One meta-store command (<c>:463</c> → <c>ArcademyMetaStore.Handle</c>,
        /// <c>:118</c>). <c>op</c> and <c>key</c> stay raw: the key's normalization and the op
        /// vocabulary are the store's, not the parser's.</summary>
        public sealed record MetaCommand(string? Op, string? Key, JsonElement? Value) : ArcademyPageMessage;

        /// <summary>A class began (<c>:466-470</c>).</summary>
        public sealed record ClassStarted(string? GameKey, int GradeTier) : ArcademyPageMessage;

        /// <summary>A class finished — the XP + attendance payout (<c>:471-473</c>). The WHOLE
        /// frame rides here because every field is read defensively and separately by
        /// <see cref="ArcademyClassPayout.Compute"/>, which is the port of upstream's own reason
        /// for reading them that way (<c>:1359-1366</c>).</summary>
        public sealed record ClassEnded(JsonElement Fields) : ArcademyPageMessage;

        /// <summary>The closing bracket for <c>class-started</c> (<c>:474-480</c>): leaving a class
        /// with Esc ENDS no class, so nothing is graded, paid or credited.</summary>
        public sealed record ClassLeft(string? GameKey) : ArcademyPageMessage;

        /// <summary>The page asking to come back from a PANIC suspend (<c>:481</c> →
        /// <c>OnResumeRequest</c>, <c>:346-370</c>). The reason stays raw: the host is the only
        /// thing that may un-freeze a class, and it refuses every reason but <c>"panic"</c>
        /// (<c>:349</c>). A missing reason READS AS "panic" upstream (<c>:348</c>,
        /// <c>(string?)o["reason"] ?? "panic"</c>), so it is null here rather than defaulted —
        /// the substitution belongs to the handler that owns that rule.</summary>
        public sealed record ResumeRequest(string? Reason) : ArcademyPageMessage;

        /// <summary>Page-initiated wind-down (<c>:487</c>): the page's own Esc-HOLD ladder, which
        /// winds itself down and then answers <c>exit-done</c>. Upstream latches <c>_exiting</c>
        /// and arms the same bounded wait a host-side close arms (<c>:488-490</c>).</summary>
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
        ["meta-command"] = r => new ArcademyPageMessage.MetaCommand(
            GetString(r, "op"),
            GetString(r, "key"),
            r.TryGetProperty("value", out var mv) ? mv.Clone() : null),
        ["class-started"] = r => new ArcademyPageMessage.ClassStarted(
            GetString(r, "gameKey"), GetInt(r, "gradeTier") ?? 0),
        ["class-ended"] = r => new ArcademyPageMessage.ClassEnded(r),
        ["class-left"] = r => new ArcademyPageMessage.ClassLeft(GetString(r, "gameKey")),
        ["resume-request"] = r => new ArcademyPageMessage.ResumeRequest(GetString(r, "reason")),
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
