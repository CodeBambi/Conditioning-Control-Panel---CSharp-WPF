using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// Protocol v1 (slice b2; dtrh-admission.md §7): the FULL message vocabulary of the
/// DTRH page↔host bridge, extracted from the READ-ONLY payload (bridge.js blob
/// 13af3f4d: PROTOCOL=1; boot.js handler/send sites) and the WPF host behavior
/// (DtrhHostService.cs OnPageMessage/post sites — File.cs:line cites in SP-024 record
/// Step 1). Nothing here is invented: every type maps to a payload send or handler site.
///
/// Tolerance decision (Step 1 consult): unknown types, forward-version envelopes, and
/// malformed frames are TYPED outcomes — logged presence+shape only, never silent drops,
/// never crashes (page-side mirror: bridge.js preBuffers unknown types, bridge.js:19-24).
/// Messages whose FEATURES land in later slices classify as Deferred(slice) — typed,
/// not stubbed.
/// </summary>
public static class DtrhProtocol
{
    /// <summary>The protocol version this host speaks (bridge.js:11, DtrhHostService.cs:31).</summary>
    public const int Version = 1;

    private static readonly JsonSerializerOptions PageOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ============================ page → host ============================

    /// <summary>The 22 page→host message types (payload send sites; SP-024 record Step 1 table
    /// + SP-049's loom-reveal from the v6.6.3 loomStudio.js rack).</summary>
    public abstract record DtrhPageMessage
    {
        private DtrhPageMessage() { }

        public sealed record Ready(int Protocol) : DtrhPageMessage;

        public sealed record Log(string? Msg) : DtrhPageMessage;

        public sealed record Heartbeat(double T) : DtrhPageMessage;

        public sealed record Exit : DtrhPageMessage;

        public sealed record ExitDone : DtrhPageMessage;

        public sealed record FullscreenSet(bool On) : DtrhPageMessage;

        public sealed record BootError(string? Msg) : DtrhPageMessage;

        public sealed record Pong(double T) : DtrhPageMessage;

        public sealed record VnSpeaking(bool On) : DtrhPageMessage;

        public sealed record Sfx(string? Name, double Scale) : DtrhPageMessage;

        public sealed record FirePayload(string? Kind, int? Strength, double? DurationMult) : DtrhPageMessage;

        public sealed record FreezeState(bool On) : DtrhPageMessage;

        public sealed record Bark(string? Event, JsonElement Raw) : DtrhPageMessage;

        public sealed record MetaCommand(string? Op, JsonElement Raw) : DtrhPageMessage;

        public sealed record RequestRun(JsonElement? Setup) : DtrhPageMessage;

        public sealed record RunStarted(string? Difficulty, string? Mode) : DtrhPageMessage;

        public sealed record RunEnded(
            double Score, double DurationSec, string? Difficulty, JsonElement Raw) : DtrhPageMessage;

        public sealed record AssetStats(JsonElement Raw) : DtrhPageMessage;

        public sealed record LoomSave(string? Name, bool Overwrite, JsonElement Raw) : DtrhPageMessage;

        public sealed record LoomDelete(string? Slug) : DtrhPageMessage;

        /// <summary>SP-049: the rack tile's 📂 — show the saved GIF in the OS file manager
        /// (loomStudio.js:749; WPF LoomHostService.cs:108-117 + DtrhHostService.cs:336).</summary>
        public sealed record LoomReveal(string? Slug) : DtrhPageMessage;

        public sealed record ReportBug : DtrhPageMessage;
    }

    /// <summary>Parse outcome: every frame lands here — typed, never thrown, never dropped.</summary>
    public abstract record DtrhPageParseResult
    {
        private DtrhPageParseResult() { }

        /// <summary>A known v1 type with a well-shaped envelope.</summary>
        public sealed record Parsed(DtrhPageMessage Message) : DtrhPageParseResult;

        /// <summary>Well-formed frame, unknown type (a newer page or a typo). Tolerated.</summary>
        public sealed record UnknownType(string Type) : DtrhPageParseResult;

        /// <summary>The envelope declares a protocol newer than this build
        /// (<c>protocol</c> &gt; 1). Tolerated: logged, never acted on.</summary>
        public sealed record ForwardVersion(string Type, int Protocol) : DtrhPageParseResult;

        /// <summary>Unparseable / missing-or-non-string type.</summary>
        public sealed record Malformed(string Reason) : DtrhPageParseResult;
    }

    /// <summary>Dispatch classification: who owns the message's FEATURE in this slice.</summary>
    public abstract record DtrhDispatchClass
    {
        private DtrhDispatchClass() { }

        /// <summary>b1/b2-owned: real handler wired (ready/log/heartbeat/exit/fullscreen-set/boot-error).</summary>
        public sealed record Handled : DtrhDispatchClass
        {
            public static readonly Handled Instance = new();
        }

        /// <summary>Feature lands in a later slice — typed deferral, logged presence+shape
        /// only, never silent, never crashed. NOT a stub: nothing is acted on.</summary>
        public sealed record Deferred(string Slice) : DtrhDispatchClass;
    }

    private static readonly Dictionary<string, Func<JsonElement, DtrhPageMessage>> Parsers = new()
    {
        ["ready"] = r => new DtrhPageMessage.Ready(GetInt(r, "protocol") ?? 0),
        ["log"] = r => new DtrhPageMessage.Log(GetString(r, "msg")),
        ["heartbeat"] = r => new DtrhPageMessage.Heartbeat(GetDouble(r, "t") ?? 0),
        ["exit"] = _ => new DtrhPageMessage.Exit(),
        ["exit-done"] = _ => new DtrhPageMessage.ExitDone(),
        ["fullscreen-set"] = r => new DtrhPageMessage.FullscreenSet(GetBool(r, "on") ?? false),
        ["boot-error"] = r => new DtrhPageMessage.BootError(GetString(r, "msg")),
        ["pong"] = r => new DtrhPageMessage.Pong(GetDouble(r, "t") ?? 0),
        ["vn-speaking"] = r => new DtrhPageMessage.VnSpeaking(GetBool(r, "on") ?? false),
        ["sfx"] = r => new DtrhPageMessage.Sfx(GetString(r, "name"), GetDouble(r, "scale") ?? 0.6),
        ["fire-payload"] = r => new DtrhPageMessage.FirePayload(
            GetString(r, "kind"), GetInt(r, "strength"), GetDouble(r, "durationMult")),
        ["freeze-state"] = r => new DtrhPageMessage.FreezeState(GetBool(r, "on") ?? false),
        ["bark"] = r => new DtrhPageMessage.Bark(GetString(r, "event"), r.Clone()),
        ["meta-command"] = r => new DtrhPageMessage.MetaCommand(GetString(r, "op"), r.Clone()),
        ["request-run"] = r => new DtrhPageMessage.RequestRun(
            r.TryGetProperty("setup", out var s) && s.ValueKind == JsonValueKind.Object ? s.Clone() : null),
        ["run-started"] = r => new DtrhPageMessage.RunStarted(GetString(r, "difficulty"), GetString(r, "mode")),
        ["run-ended"] = r => new DtrhPageMessage.RunEnded(
            GetDouble(r, "score") ?? 0, GetDouble(r, "durationSec") ?? 0, GetString(r, "difficulty"), r.Clone()),
        ["asset-stats"] = r => new DtrhPageMessage.AssetStats(r.Clone()),
        ["loom-save"] = r => new DtrhPageMessage.LoomSave(
            GetString(r, "name"), GetBool(r, "overwrite") ?? false, r.Clone()),
        ["loom-delete"] = r => new DtrhPageMessage.LoomDelete(GetString(r, "slug")),
        ["loom-reveal"] = r => new DtrhPageMessage.LoomReveal(GetString(r, "slug")),
        ["report-bug"] = _ => new DtrhPageMessage.ReportBug(),
    };

    /// <summary>Parse one page→host frame. NEVER throws: bad frames are typed outcomes.</summary>
    public static DtrhPageParseResult ParsePageMessage(string json)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return new DtrhPageParseResult.Malformed($"json: {ex.GetType().Name}");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new DtrhPageParseResult.Malformed($"root is {root.ValueKind}, not an object");
        }

        if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            return new DtrhPageParseResult.Malformed("missing or non-string 'type'");
        }

        var type = typeProp.GetString()!;
        // Forward-version tolerance: a declared protocol newer than this build is typed +
        // logged, never acted on — an older host must not misread a newer page's frame.
        if (GetInt(root, "protocol") is > Version)
        {
            return new DtrhPageParseResult.ForwardVersion(type, GetInt(root, "protocol")!.Value);
        }

        return Parsers.TryGetValue(type, out var parser)
            ? new DtrhPageParseResult.Parsed(parser(root))
            : new DtrhPageParseResult.UnknownType(type);
    }

    /// <summary>Per-message ownership in this slice (slice map: dtrh-admission.md §7).</summary>
    public static DtrhDispatchClass Classify(DtrhPageMessage message) => message switch
    {
        // b1/b2-owned (real handlers wired in the host window).
        DtrhPageMessage.Ready or DtrhPageMessage.Log or DtrhPageMessage.Heartbeat
            or DtrhPageMessage.Exit or DtrhPageMessage.FullscreenSet
            or DtrhPageMessage.BootError => DtrhDispatchClass.Handled.Instance,
        // b3 (SP-025): native SFX/whisper/video + freeze + VN mix gate — REAL effects via
        // DtrhNativeEffects, wired in the host window.
        DtrhPageMessage.VnSpeaking or DtrhPageMessage.Sfx or DtrhPageMessage.FirePayload
            or DtrhPageMessage.FreezeState => DtrhDispatchClass.Handled.Instance,
        // Bark arbitration (event → voiceline over CCP bark rules + gates,
        // DtrhHostService.cs:618-650 → BarkService) — SP-032 slice q2 OWNS it: routed
        // through Companion/BarkPipeline on q1's SoundArbitration (DtrhBarkRouting table),
        // wired in the host window's dispatch. m2Test skips routing (WPF _testMode parity).
        DtrhPageMessage.Bark => DtrhDispatchClass.Handled.Instance,
        // b4 (SP-026): progression/payout + Loom + media stats — REAL effects via
        // DtrhMeta / DtrhLoom, wired in the host window. SP-049: LoomReveal joins — the
        // v6.6.3 studio rack's 📂 (shared loomStudio.js emits it from BOTH homes).
        DtrhPageMessage.MetaCommand or DtrhPageMessage.RequestRun or DtrhPageMessage.RunStarted
            or DtrhPageMessage.RunEnded or DtrhPageMessage.AssetStats
            or DtrhPageMessage.LoomSave or DtrhPageMessage.LoomDelete
            or DtrhPageMessage.LoomReveal => DtrhDispatchClass.Handled.Instance,
        // b5 (SP-027): bounded exit-done wait + pong heartbeat stamp — REAL handlers
        // wired in the host window (exit flow) and the watchdog.
        DtrhPageMessage.ExitDone or DtrhPageMessage.Pong => DtrhDispatchClass.Handled.Instance,
        // No slice owns the hub's bug-report affordance yet (host UI, not protocol).
        DtrhPageMessage.ReportBug => new DtrhDispatchClass.Deferred("unassigned/host-ui"),
        _ => new DtrhDispatchClass.Deferred("unassigned"),
    };

    // ============================ host → page ============================

    /// <summary>runSetup: the saved Descent-tab choices (WPF BuildRunSetup raw settings,
    /// DtrhHostService.cs:483-510 — NOT the clamped run values).</summary>
    public sealed record DtrhRunSetup(
        string Difficulty,
        int DurationSec,
        int WaveCount,
        string Motion,
        IReadOnlyList<string>? EnabledVariants,
        double EffectIntensity,
        bool ColorFlashes,
        bool BoonDraftEnabled,
        bool AllowCurses,
        bool DartersEnabled,
        string Key1,
        string Key2);

    public sealed record DtrhManifestEntry(string Name, string Url);

    public sealed record DtrhLoomSpiral(
        string Slug,
        string Url,
        // `params` is a C# keyword; the payload reads `s.params` (DtrhHostService.cs:341-355).
        [property: JsonPropertyName("params")] JsonElement? Params);

    public sealed record DtrhPayoutResult(
        double BaseXp, double SkillMult, double FinalXp, int SparksEarned,
        long PreviousBest, string? RankUp, bool DryRun);

    /// <summary>Serialize a host→page message (camelCase — the payload's bridge handlers
    /// read camelCase fields; boot.js:132-181).</summary>
    public static string SerializeForPage<TMessage>(TMessage message) =>
        JsonSerializer.Serialize(message, PageOptions);

    public static object BuildInit(
        int masterVolume, string modId, object? modContent, DtrhRunSetup runSetup, bool m2Test) => new
    {
        type = "init",
        protocol = Version,
        settings = new { masterVolume },
        modId,
        modContent,
        runSetup,
        m2Test,
    };

    public static object BuildManifest(
        IReadOnlyList<DtrhManifestEntry> images, IReadOnlyList<DtrhManifestEntry> videos,
        int skipped, bool truncated) => new
    {
        type = "manifest",
        images,
        videos,
        skipped,
        truncated,
    };

    public static object BuildMeta(JsonElement state, int rev) => new { type = "meta", state, rev };

    public static object BuildFavorites(IReadOnlyList<string> names) => new { type = "favorites", names };

    public static object BuildRunConfig(JsonElement runConfig) => new { type = "run-config", runConfig };

    public static object BuildPayoutResult(DtrhPayoutResult payout) => new
    {
        type = "payout-result",
        baseXp = payout.BaseXp,
        skillMult = payout.SkillMult,
        finalXp = payout.FinalXp,
        sparksEarned = payout.SparksEarned,
        previousBest = payout.PreviousBest,
        rankUp = payout.RankUp,
        dryRun = payout.DryRun,
    };

    public static object BuildPayloadState(string kind, bool on) => new { type = "payload-state", kind, on };

    public static object BuildFullscreen(bool on) => new { type = "fullscreen", on };

    public static object BuildEndRun(string reason) => new { type = "end-run", reason };

    public static object BuildLoomList(IReadOnlyList<DtrhLoomSpiral> spirals) => new { type = "loom-list", spirals };

    public static object BuildLoomResult(string op, bool ok, string? slug, string? error) =>
        new { type = "loom-result", op, ok, slug, error };

    public static object BuildPing(double t) => new { type = "ping", t };

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
