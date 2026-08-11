using System.Text.Json;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: protocol v1 of the Graded Intake page↔host bridge — the FULL typed vocabulary,
/// C#-pinned per SP-050's obligation table (web-core host surface: MSGS + WIN/STORES).
/// Every type maps to a payload send/handler site (web-shim.js, boot.js, ui/fullscreen.js)
/// or a WPF host site (IntakeHostService.cs) — nothing invented.
///
/// **6 OUT (host→page, IntakeHostService.cs):** `init` (OnPageReady :241-292),
/// `fullscreen {on}` echo of the ACTUAL state (:~295, :354), `end-run {reason}` (:~156),
/// `session-drafted {ok,name,path}` (:496, :504), `loom-result {op:"save",ok,slug,error}`
/// (:568, :574), `intake-save-image-result {ok,path,error}` (:624).
///
/// **12 IN (page→host):** `ready`, `log`, `heartbeat`, `pong` (ChaosWebViewHost.cs:577-602 +
/// IntakeHostService.cs:296-308), `quiz-result {result}` (:302), `boot-error {msg}` (:305),
/// `fullscreen-set {on}` (:308-309), `exit` (:311-314), `exit-done` (:316-325),
/// `intake-close` (:315 — jumpscare ABORT: immediate teardown, NO result, NO spend),
/// `loom-save {name,gifBase64,params,overwrite}` (:318, :562-577 → the SHARED loom store),
/// `intake-save-image {pngBase64,index}` (:321-323, :589-635).
///
/// **ping / payload-state — TYPED per the packet's authoring obligation** (the packet PINS
/// OR TYPES them): page-attested (web-shim.js:218-227 — payload-state video cover,
/// ping→pong) but archaeology REFUTED every C# emit site for intake (grep-verified:
/// payload-state emits only in DtrhHostService.cs:809,850 + DtrhHostOrchestrator.cs:401,407;
/// ping only in a DTRH test). They are typed here as vocabulary constants with the pinned
/// classification <see cref="IntakeEmitClass.PageAttestedNeverEmitted"/> — this host NEVER
/// emits them (the intake host raises no native video cover and no ping; the page tolerates
/// both absences — its ping→pong handler simply never fires).
///
/// **Out-of-vocabulary for this host (typed pins):** `loom-delete`, `loom-reveal`,
/// `loom-list` are the DTRH/studio vocabulary — the intake page sends ONLY `loom-save`
/// (grep-verified over the intake tree; boot.js:2119). They classify as UnknownType here —
/// tolerated, never acted on (advisor Correction 2).
///
/// Tolerance discipline (b2 parity): unknown types, forward-version envelopes, and
/// malformed frames are TYPED outcomes — logged presence+shape only, never silent drops,
/// never crashes.
/// </summary>
public static class IntakeProtocol
{
    /// <summary>The protocol version this host speaks (contracts.PROTOCOL=1, web-shim.js:6;
    /// IntakeHostService.cs:44).</summary>
    public const int Version = 1;

    /// <summary>Page-attested vocabulary this host NEVER emits (the authoring obligation,
    /// typed — archaeology refuted every C# emit site).</summary>
    public const string PingType = "ping";

    /// <summary>Page-attested vocabulary this host NEVER emits (the authoring obligation,
    /// typed — archaeology refuted every C# emit site).</summary>
    public const string PayloadStateType = "payload-state";

    /// <summary>The pinned emission classification for the authoring obligation.</summary>
    public enum IntakeEmitClass
    {
        /// <summary>One of the 6 C#-pinned OUT types with a real emit site in this host.</summary>
        EmittedByHost,

        /// <summary>Page-attested (web-shim.js:218-227), C# emit sites refuted by archaeology
        /// — typed vocabulary, NEVER emitted by this host.</summary>
        PageAttestedNeverEmitted,

        /// <summary>Not intake bridge vocabulary at all (e.g. a typo or DTRH-only types).</summary>
        NotIntakeVocabulary,
    }

    /// <summary>The emission classification of every host→page type (the pinned 6-out table
    /// + the obligation pair). Tests pin this table — a future emit of the obligation pair
    /// fails the pin instead of silently widening the bridge.</summary>
    public static IntakeEmitClass ClassifyHostEmit(string type) => type switch
    {
        "init" or "fullscreen" or "end-run" or "session-drafted" or "loom-result"
            or "intake-save-image-result" => IntakeEmitClass.EmittedByHost,
        PingType or PayloadStateType => IntakeEmitClass.PageAttestedNeverEmitted,
        _ => IntakeEmitClass.NotIntakeVocabulary,
    };

    // ============================ page → host ============================

    /// <summary>The 12 page→host message types (payload send sites + WPF handler sites —
    /// the table in the class comment).</summary>
    public abstract record IntakePageMessage
    {
        private IntakePageMessage() { }

        public sealed record Ready(int Protocol) : IntakePageMessage;

        public sealed record Log(string? Msg) : IntakePageMessage;

        public sealed record Heartbeat(double T) : IntakePageMessage;

        public sealed record Pong(double T) : IntakePageMessage;

        /// <summary>The whole point (web-shim.js:188-190): the completed run the host
        /// profiles, drafts from, spends the pass on, and stamps the card for.</summary>
        public sealed record QuizResult(JsonElement Raw) : IntakePageMessage;

        public sealed record BootError(string? Msg) : IntakePageMessage;

        public sealed record FullscreenSet(bool On) : IntakePageMessage;

        public sealed record Exit : IntakePageMessage;

        public sealed record ExitDone : IntakePageMessage;

        /// <summary>The jumpscare ABORT (web-shim.js:242; IntakeHostService.cs:531-543):
        /// immediate teardown — NO QuizRunResult, NO session drafted, NO pass spend.</summary>
        public sealed record IntakeClose : IntakePageMessage;

        /// <summary>boot.js:2119 — byte-for-byte THE LOOM's message → the SHARED store.</summary>
        public sealed record LoomSave(string? Name, bool Overwrite, JsonElement Raw) : IntakePageMessage;

        /// <summary>boot.js:2142 — a rendered spiral PNG for the user's keepsake folder.</summary>
        public sealed record IntakeSaveImage(string? PngBase64, int Index) : IntakePageMessage;
    }

    /// <summary>Parse outcome: every frame lands here — typed, never thrown, never dropped.</summary>
    public abstract record IntakePageParseResult
    {
        private IntakePageParseResult() { }

        /// <summary>A known v1 type with a well-shaped envelope.</summary>
        public sealed record Parsed(IntakePageMessage Message) : IntakePageParseResult;

        /// <summary>Well-formed frame, unknown type (a newer page, a typo, or DTRH/studio
        /// vocabulary like loom-delete/loom-reveal — out-of-vocabulary HERE). Tolerated.</summary>
        public sealed record UnknownType(string Type) : IntakePageParseResult;

        /// <summary>The envelope declares a protocol newer than this build. Tolerated.</summary>
        public sealed record ForwardVersion(string Type, int Protocol) : IntakePageParseResult;

        /// <summary>Unparseable / missing-or-non-string type.</summary>
        public sealed record Malformed(string Reason) : IntakePageParseResult;
    }

    private static readonly Dictionary<string, Func<JsonElement, IntakePageMessage>> Parsers = new()
    {
        ["ready"] = r => new IntakePageMessage.Ready(GetInt(r, "protocol") ?? 0),
        ["log"] = r => new IntakePageMessage.Log(GetString(r, "msg")),
        ["heartbeat"] = r => new IntakePageMessage.Heartbeat(GetDouble(r, "t") ?? 0),
        ["pong"] = r => new IntakePageMessage.Pong(GetDouble(r, "t") ?? 0),
        ["quiz-result"] = r => r.TryGetProperty("result", out var q) && q.ValueKind == JsonValueKind.Object
            ? new IntakePageMessage.QuizResult(q.Clone())
            : new IntakePageMessage.QuizResult(r.Clone()),
        ["boot-error"] = r => new IntakePageMessage.BootError(GetString(r, "msg")),
        ["fullscreen-set"] = r => new IntakePageMessage.FullscreenSet(GetBool(r, "on") ?? false),
        ["exit"] = _ => new IntakePageMessage.Exit(),
        ["exit-done"] = _ => new IntakePageMessage.ExitDone(),
        ["intake-close"] = _ => new IntakePageMessage.IntakeClose(),
        ["loom-save"] = r => new IntakePageMessage.LoomSave(
            GetString(r, "name"), GetBool(r, "overwrite") ?? false, r.Clone()),
        ["intake-save-image"] = r => new IntakePageMessage.IntakeSaveImage(
            GetString(r, "pngBase64"), GetInt(r, "index") ?? 0),
    };

    /// <summary>Parse one page→host frame. NEVER throws: bad frames are typed outcomes.</summary>
    public static IntakePageParseResult ParsePageMessage(string json)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return new IntakePageParseResult.Malformed($"json: {ex.GetType().Name}");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new IntakePageParseResult.Malformed($"root is {root.ValueKind}, not an object");
        }

        if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            return new IntakePageParseResult.Malformed("missing or non-string 'type'");
        }

        var type = typeProp.GetString()!;
        if (GetInt(root, "protocol") is > Version)
        {
            return new IntakePageParseResult.ForwardVersion(type, GetInt(root, "protocol")!.Value);
        }

        return Parsers.TryGetValue(type, out var parser)
            ? new IntakePageParseResult.Parsed(parser(root))
            : new IntakePageParseResult.UnknownType(type);
    }

    // ============================ host → page ============================

    private static readonly JsonSerializerOptions PageOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Serialize a host→page message (camelCase — the shim's handlers read
    /// camelCase; web-shim.js:107-141).</summary>
    public static string SerializeForPage<TMessage>(TMessage message) =>
        JsonSerializer.Serialize(message, PageOptions);

    /// <summary>The AI auth block (:272-279): the Patreon bearer handed to the page for ITS
    /// proxy call. Empty token = WPF's own logged-out branch — the page runs its
    /// deterministic local stub, NO network (the privacy boundary verbatim; genuine PARITY,
    /// never a degradation).</summary>
    public sealed record IntakeAiProvision(string ServerBase, string AuthToken);

    /// <summary>The proxy base (IntakeHostService.cs:50).</summary>
    public const string AiProxyBaseUrl = "https://codebambi-proxy.vercel.app";

    /// <summary>init {protocol:1, config, ai} (:241-292). The config provisions are built by
    /// the host window (niche/caps/endless/steerValve/priorRun/m2Test/micEnabled/media/
    /// subjectId/subliminals) and passed as an anonymous object — this builder owns only
    /// the envelope so the shape stays pinned.</summary>
    public static object BuildInit(object config, IntakeAiProvision ai) => new
    {
        type = "init",
        protocol = Version,
        config,
        ai = new { serverBase = ai.ServerBase, authToken = ai.AuthToken },
    };

    /// <summary>fullscreen {on} — the echo of the ACTUAL state (:~295, :354).</summary>
    public static object BuildFullscreen(bool on) => new { type = "fullscreen", on };

    /// <summary>end-run {reason} (:~156) — the graceful wind-down request (exit/window-X
    /// path ONLY, never the abort).</summary>
    public static object BuildEndRun(string reason) => new { type = "end-run", reason };

    /// <summary>session-drafted {ok,name,path} (:496, :504).</summary>
    public static object BuildSessionDrafted(bool ok, string? name, string? path) =>
        new { type = "session-drafted", ok, name, path };

    /// <summary>loom-result {op:"save",ok,slug,error} (:568, :574).</summary>
    public static object BuildLoomResult(string op, bool ok, string? slug, string? error) =>
        new { type = "loom-result", op, ok, slug, error };

    /// <summary>intake-save-image-result {ok,path,error} (:624 —
    /// too-big/bad-image/io-failed).</summary>
    public static object BuildSaveImageResult(bool ok, string? path, string? error) =>
        new { type = "intake-save-image-result", ok, path, error };

    // ============================ field helpers ============================

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

    private static double? GetDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var v)
            ? v
            : null;
}
