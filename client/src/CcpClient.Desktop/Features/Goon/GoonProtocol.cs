using System.Text.Json;
using CcpClient.Desktop.Features.Dtrh;

namespace CcpClient.Desktop.Features.Goon;

/// <summary>
/// Protocol v1 of the Goon page↔host bridge, for the PRACTICE unit
/// (goon-game-census.md §7.1). The frame catalogue is <c>GoonHostService.cs:29-54</c> and every
/// type below maps to a line in it or to a page send/handler site — nothing is invented.
///
/// <para><b>4 OUT (host→page), and why each is in the subset:</b>
/// <c>init</c> (<c>GoonHostService.cs:29</c>, built <c>:311-354</c>) and <c>manifest</c>
/// (<c>:30</c>, built <c>:372-378</c>) are the two the census names. <c>end-run {reason}</c>
/// (<c>:33</c>) is here because the exit handshake does not complete without it — the page
/// waits on <c>bridge.on('end-run', …)</c> at <c>boot.js:379</c> and otherwise falls through
/// its own 1.2 s fallback (<c>boot.js:2449</c>). <c>fullscreen {on}</c> (<c>:31</c>, "always
/// the REAL window state") is here because the page's F11 sends <c>fullscreen-set</c> and then
/// reads only the ECHOED state (<c>boot.js:2504-2513</c>), so without the echo F11 is a dead
/// key.</para>
///
/// <para><b>1 OUT, TYPED BUT NEVER EMITTED:</b> <c>ping</c> (<c>GoonHostService.cs:32</c>). The page answers it
/// with <c>pong</c> (<c>boot.js:377</c>), but this host ships no paint-stall watchdog prod
/// (upstream posts one at <c>GoonHostService.cs:1540</c>, off the paint-stall rule whose
/// constant is at <c>:75</c>). Classified rather than silently absent — the
/// <see cref="Intake.IntakeProtocol"/> authoring discipline.</para>
///
/// <para><b>1 OUT, THE REFUSAL:</b> <c>net-post-result {id,status,body}</c> (<c>:34</c>).
/// Upstream proxies the page's server calls; <b>this host refuses them in-process</b>. See
/// <see cref="BuildNetPostResult"/> — that method is the port's "no network" enforcement
/// point, not a convenience.</para>
///
/// <para><b>9 IN (page→host):</b> <c>ready</c> (<c>:42</c>; sent at <c>goon/bridge.js:106</c>),
/// <c>log</c> (<c>:42</c>; <c>goon/bridge.js:98-102</c>), <c>heartbeat {t,paint,vis}</c> and
/// <c>pong</c> and <c>boot-error</c> and <c>fullscreen-set</c> (<c>:43</c>;
/// <c>boot.js:2605-2612</c>, <c>:377</c>, <c>goon/bridge.js:109</c>, <c>boot.js:2508-2511</c>),
/// <c>exit</c> / <c>exit-done</c> (<c>:44</c>; <c>boot.js:2448</c>, <c>:2464</c>), and
/// <c>net-post {id,path,body}</c> (<c>:47</c>; <c>goon/bridge.js:169-179</c>).</para>
///
/// <para><b>Out of vocabulary for THIS host (typed, tolerated, never acted on):</b> the
/// transfer-cache family (<c>GoonHostService.cs:48-49</c>), the received-inbox family
/// (<c>GoonHostService.cs:50-51</c>), the Discord family (<c>GoonHostService.cs:52-54</c>), and
/// the two upstream stubs <c>toy-pattern</c>/<c>toy-stop</c>
/// (<c>:45</c>) and <c>match-result</c> (<c>:46</c>). None is reachable in a build with
/// <c>assetCache:false</c>, <c>mediaTransfer:false</c> and no Discord block, and every one of
/// them is logged by presence and shape rather than dropped.</para>
/// </summary>
public static class GoonProtocol
{
    /// <summary>The protocol version this host speaks: <c>GoonHostService.cs:60</c>
    /// (<c>private const int Protocol = 1</c>) and <c>goon/bridge.js:30</c>
    /// (<c>export const PROTOCOL = 1</c>).</summary>
    public const int Version = 1;

    /// <summary>How this host treats each host→page type.</summary>
    public enum GoonEmitClass
    {
        /// <summary>One of the 5 types this host really posts.</summary>
        EmittedByHost,

        /// <summary>In the catalogue and handled by the page, but this host never sends it
        /// (<c>ping</c> — no paint-stall watchdog here).</summary>
        PageAttestedNeverEmitted,

        /// <summary>Catalogue vocabulary belonging to a subsystem this unit does not build
        /// (transfer cache, received inbox, Discord, haptics).</summary>
        OwnerGatedNeverEmitted,

        /// <summary>Not goon host→page vocabulary at all.</summary>
        NotGoonVocabulary,
    }

    /// <summary>The emission classification of every host→page type in the catalogue. A test
    /// pins this table, so widening the bridge fails a fact instead of happening quietly.</summary>
    public static GoonEmitClass ClassifyHostEmit(string type) => type switch
    {
        "init" or "manifest" or "fullscreen" or "end-run" or "net-post-result" => GoonEmitClass.EmittedByHost,
        "ping" => GoonEmitClass.PageAttestedNeverEmitted,
        "cache-state" or "cache-list" or "cache-progress" or "encode-request" or "cache-put-result"
            or "goon-recv-result" or "discord" or "peer-card" or "haptics-state" => GoonEmitClass.OwnerGatedNeverEmitted,
        _ => GoonEmitClass.NotGoonVocabulary,
    };

    // ============================ host → page ============================

    private static readonly JsonSerializerOptions PageOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Serialize a host→page frame (camelCase — the page's handlers read camelCase,
    /// <c>boot.js:321-351</c>).</summary>
    public static string SerializeForPage<TMessage>(TMessage message) =>
        JsonSerializer.Serialize(message, PageOptions);

    /// <summary>
    /// The <c>init</c> frame, transcribed field-for-field from the two upstream copies:
    /// <c>GoonHostService.cs:311-354</c> (the host's) and <c>goon/bridge.js:387-470</c> (the page's
    /// own synthesized one). Deltas, each deliberate and each recorded as a divergence:
    ///
    /// <list type="bullet">
    /// <item><c>solo: true</c> — a frame-ROOT field (<c>goon/bridge.js:391</c>, read as <c>m.solo</c>
    /// at <c>boot.js:323</c>), which the C# host does not send at all. This build is
    /// practice-only, so it carries the standalone default. D256.</item>
    /// <item><c>net.serverBase</c> empty and <c>net.authToken</c> empty — upstream sends the
    /// proxy base (<c>:323</c>) and the CCP auth token (<c>:328</c> → <c>SafeAuthToken()</c> at
    /// <c>:851-855</c>). <b>This host names no server and reads no token.</b> D251, D252.</item>
    /// <item><c>net.viaHost: true</c> (<c>:329</c>) — kept TRUE deliberately. Hosted, it routes
    /// every server call the page makes into <c>net-post</c>, which this host refuses
    /// in-process; the alternative (<c>false</c>) makes the page issue a real <c>fetch()</c>
    /// itself (<c>goon/bridge.js:181-192</c>). True is the only setting under which no page→server
    /// traffic exists at all. D252.</item>
    /// <item><c>discord</c> absent — upstream builds a block at <c>:353</c>. No Discord here.
    /// D257.</item>
    /// <item><c>identity.unifiedId</c> empty (upstream <c>App.UnifiedUserId ?? ""</c> at
    /// <c>:317</c>): there is no account. The page reads <c>anonymous</c> with <c>=== true</c>
    /// (<c>boot.js:335</c>), so an absent field is correctly NOT anonymous — a host has an
    /// account upstream, and this one has no accounts at all.</item>
    /// </list>
    /// </summary>
    /// <param name="displayName">Upstream <c>SafeDisplayName()</c> (<c>:318</c> → <c>:860-868</c>),
    /// whose own fallback is the literal "Player".</param>
    /// <param name="appVersion">Upstream <c>UpdateService.AppVersion</c> (<c>:319</c>); here the
    /// port's version authority, <c>VersionSelfCheck.GetInformationalVersion</c>.</param>
    /// <param name="caps">Computed from the door refusals — see <see cref="GoonDoors.CapsFor"/>.</param>
    /// <param name="fullscreen">The REAL window state (<c>:347</c>, catalogue <c>:31</c>).</param>
    public static object BuildInit(string displayName, string appVersion, GoonCaps caps, bool fullscreen) => new
    {
        type = "init",
        protocol = Version,
        solo = true,
        identity = new
        {
            unifiedId = "",
            displayName,
            appVersion,
        },
        net = new
        {
            serverBase = "",
            authToken = "",
            viaHost = true,
        },
        caps = new
        {
            haptics = caps.Haptics,
            brainDrain = caps.BrainDrain,
            spiral = caps.Spiral,
            camera = caps.Camera,
            video = caps.Video,
            mediaTransfer = caps.MediaTransfer,
            canHost = caps.CanHost,
            assetCache = caps.AssetCache,
        },
        consent = new
        {
            liveDurationSec = GoonConsent.LiveDurationSec,
            toyCap = GoonConsent.ToyCap,
            payloadMinGapMs = GoonConsent.PayloadMinGapMs,
        },
        fullscreen,
    };

    /// <summary>
    /// The <c>manifest</c> frame (<c>GoonHostService.cs:372-378</c>). The port already ships
    /// both halves (census §7.1.1): the enumerator is <see cref="DtrhUserMedia"/> — the port of
    /// the very file upstream reuses here (<c>:359-362</c>, "one enumerator for every web
    /// core") — and the frame is <c>DtrhProtocol.BuildManifest</c> (<c>DtrhProtocol.cs:268-277</c>),
    /// field-for-field identical minus one.
    ///
    /// <para><b>That one field is <c>received</c> (<c>GoonHostService.cs:377</c>), and it is a
    /// FRAME-SHAPE STUB here, documented rather than hidden.</b> Upstream it is the
    /// accepted-artifact list, and upstream wipes the inbox immediately before listing
    /// (<c>GoonHostService.cs:368</c>) precisely so the rows
    /// "are always empty". A practice-only build has no media channel to fill it and no inbox
    /// to wipe, so it is always empty — for a different reason, which is why it is stated. The
    /// page's priming path is kept alive by the field's presence (<c>boot.js:356-360</c>).</para>
    ///
    /// <para><b>What this frame must never become</b> is a route by which the inventory leaves
    /// the machine (census §6.2). It has no channel to leave by: the URLs it carries are
    /// loopback <c>{mediaOrigin}/umedia/…</c> on 127.0.0.1, nothing is persisted beyond the
    /// frame, and the counts-only logging rule of <see cref="DtrhUserMedia"/> holds here too —
    /// never a name, never a path.</para>
    /// </summary>
    public static object BuildManifest(
        IReadOnlyList<DtrhProtocol.DtrhManifestEntry> images,
        IReadOnlyList<DtrhProtocol.DtrhManifestEntry> videos,
        int skipped,
        bool truncated) => new
        {
            type = "manifest",
            images,
            videos,
            skipped,
            truncated,
            received = Array.Empty<object>(),
        };

    /// <summary>The host-initiated wind-down (catalogue <c>:33</c>; the page finishes its exit
    /// on it at <c>boot.js:379</c>).</summary>
    public static object BuildEndRun(string reason) => new { type = "end-run", reason };

    /// <summary>The fullscreen echo — <b>always the REAL window state</b> (catalogue <c>:31</c>);
    /// the page's affordances read the echoed state, never the requested one.</summary>
    public static object BuildFullscreen(bool on) => new { type = "fullscreen", on };

    /// <summary>
    /// <b>THE "NO NETWORK" ENFORCEMENT POINT.</b> Every server call the page can make arrives
    /// here as a <c>net-post</c> frame (<c>goon/bridge.js:169-179</c>, because <c>init</c> sets
    /// <c>viaHost:true</c>), and this host answers it locally, immediately, and refuses. No
    /// socket is opened, no path is proxied, no header is attached, no token is read.
    ///
    /// <para><b>Immediately matters.</b> Unanswered, the page's promise sits on
    /// <c>NET_TIMEOUT_MS = 45000</c> (<c>goon/bridge.js:126</c>) and then resolves <c>status 0</c>,
    /// which <c>net/signaling.js:506-510</c> maps to "could not reach the server". A 45-second
    /// spinner ending in a false sentence is the exact failure this packet forbids.</para>
    ///
    /// <para><b>The status is deliberately not shopped for.</b> No page-rendered status tells
    /// the truth here — <c>0</c> gives "could not reach the server", <c>404</c> gives "warming
    /// up", <c>403 no_host_access</c> gives "hosting is a supporter perk"
    /// (<c>ui/sheets.js:116-170</c>) — and there is no host-supplied free-text slot
    /// (<c>lastErrorDetail</c> is written only for HTTP 402, <c>net/signaling.js:542</c>). That
    /// is precisely why the true sentence lives in host chrome. <c>501</c> is used because it is
    /// the honest HTTP word for "this build does not implement it", and the refusal's own
    /// <c>Missing</c> text rides in the body so the transcript carries the reason even though
    /// the page cannot render it.</para>
    /// </summary>
    public static object BuildNetPostResult(string id, GoonDoors.GoonDoorRefusal refusal) => new
    {
        type = "net-post-result",
        id,
        status = NetPostRefusedStatus,
        body = SerializeForPage(new
        {
            error = "not_in_this_build",
            detail = refusal.Missing,
        }),
    };

    /// <summary>The status this host answers every <c>net-post</c> with. 501 Not Implemented:
    /// the honest word for a route this build does not have.</summary>
    public const int NetPostRefusedStatus = 501;

    /// <summary>
    /// The three consent defaults the <c>init</c> frame carries. Upstream reads them off
    /// <c>ConsentSheetMsg</c> rather than inventing them — <c>GoonHostService.cs:310</c>, "the
    /// engine's own defaults, never a fork" — so the port transcribes the three constants and
    /// ports none of <c>GoonContracts.cs</c>.
    /// </summary>
    public static class GoonConsent
    {
        /// <summary><c>GoonContracts.cs:97</c> — <c>LiveDurationSecDefault = 720</c> (12 min),
        /// reaching the frame at <c>GoonHostService.cs:343</c>.</summary>
        public const int LiveDurationSec = 720;

        /// <summary><c>GoonContracts.cs:297</c> — <c>ToyCap = 0.7</c>, "can only LOWER the
        /// receiver's own cap", reaching the frame at <c>GoonHostService.cs:344</c>. (The page's
        /// standalone frame sends 0 at <c>goon/bridge.js:445</c>; the HOST's value is 0.7 and this is
        /// a host.)</summary>
        public const double ToyCap = 0.7;

        /// <summary><c>GoonContracts.cs:108</c> — <c>PayloadMinGapMs = 30000</c> (1 per 30 s),
        /// reaching the frame at <c>GoonHostService.cs:345</c>.</summary>
        public const int PayloadMinGapMs = 30000;
    }

    // ============================ page → host ============================

    /// <summary>The 9 page→host frames this host handles.</summary>
    public abstract record GoonPageMessage
    {
        private GoonPageMessage() { }

        /// <summary>Boot completed; the host flushes init + manifest (<c>goon/bridge.js:106</c>).</summary>
        public sealed record Ready(int Protocol) : GoonPageMessage;

        /// <summary>Tunnelled page log, 400-char clamped page-side (<c>goon/bridge.js:98-102</c>).</summary>
        public sealed record Log(string? Msg) : GoonPageMessage;

        /// <summary><c>{t, paint?, vis}</c> — <c>paint</c> is OMITTED, not zeroed, on a host
        /// without rAF (<c>boot.js:2606-2609</c>), so it is nullable here for the same reason:
        /// "no frame counter" is a different fact from "no frames".</summary>
        public sealed record Heartbeat(double T, long? Paint, string? Vis) : GoonPageMessage;

        /// <summary>Answer to a <c>ping</c> this host never sends (<c>boot.js:377</c>) — handled
        /// anyway, because a frame that arrives must never be dropped silently.</summary>
        public sealed record Pong(double T) : GoonPageMessage;

        /// <summary>A fatal page boot failure (<c>goon/bridge.js:109</c>).</summary>
        public sealed record BootError(string? Msg) : GoonPageMessage;

        /// <summary>F11, page-side (<c>boot.js:2508-2511</c>). Fullscreen is host-owned on
        /// purpose: the browser Fullscreen API eats the first Escape, which would break the
        /// page's exit ladder.</summary>
        public sealed record FullscreenSet(bool On) : GoonPageMessage;

        /// <summary>The page asks to leave (<c>boot.js:2448</c>).</summary>
        public sealed record Exit : GoonPageMessage;

        /// <summary>The page is finished; the window may go (<c>boot.js:2464</c>).</summary>
        public sealed record ExitDone : GoonPageMessage;

        /// <summary>A server call (<c>goon/bridge.js:178</c>). Refused in-process — see
        /// <see cref="BuildNetPostResult"/>. <c>Path</c> is carried for the transcript only and
        /// is never dereferenced, fetched or forwarded.</summary>
        public sealed record NetPost(string Id, string? Path) : GoonPageMessage;
    }

    /// <summary>Parse outcome: every frame lands here — typed, never thrown, never dropped.</summary>
    public abstract record GoonPageParseResult
    {
        private GoonPageParseResult() { }

        /// <summary>A known v1 type with a well-shaped envelope.</summary>
        public sealed record Parsed(GoonPageMessage Message) : GoonPageParseResult;

        /// <summary>Well-formed frame, out of THIS host's vocabulary (the transfer-cache,
        /// inbox, Discord and haptics families, and the two upstream stubs). Tolerated.</summary>
        public sealed record UnknownType(string Type) : GoonPageParseResult;

        /// <summary>The envelope declares a protocol newer than this build. Tolerated.</summary>
        public sealed record ForwardVersion(string Type, int Protocol) : GoonPageParseResult;

        /// <summary>Unparseable / missing-or-non-string type.</summary>
        public sealed record Malformed(string Reason) : GoonPageParseResult;
    }

    private static readonly Dictionary<string, Func<JsonElement, GoonPageMessage>> Parsers = new()
    {
        ["ready"] = r => new GoonPageMessage.Ready(GetInt(r, "protocol") ?? 0),
        ["log"] = r => new GoonPageMessage.Log(GetString(r, "msg")),
        ["heartbeat"] = r => new GoonPageMessage.Heartbeat(
            GetDouble(r, "t") ?? 0, GetLong(r, "paint"), GetString(r, "vis")),
        ["pong"] = r => new GoonPageMessage.Pong(GetDouble(r, "t") ?? 0),
        ["boot-error"] = r => new GoonPageMessage.BootError(GetString(r, "msg")),
        ["fullscreen-set"] = r => new GoonPageMessage.FullscreenSet(GetBool(r, "on") ?? false),
        ["exit"] = _ => new GoonPageMessage.Exit(),
        ["exit-done"] = _ => new GoonPageMessage.ExitDone(),
        ["net-post"] = r => new GoonPageMessage.NetPost(GetString(r, "id") ?? "", GetString(r, "path")),
    };

    /// <summary>Parse one page→host frame. NEVER throws: bad frames are typed outcomes.</summary>
    public static GoonPageParseResult ParsePageMessage(string json)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return new GoonPageParseResult.Malformed($"json: {ex.GetType().Name}");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new GoonPageParseResult.Malformed($"root is {root.ValueKind}, not an object");
        }

        if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            return new GoonPageParseResult.Malformed("missing or non-string 'type'");
        }

        var type = typeProp.GetString()!;
        if (GetInt(root, "protocol") is > Version)
        {
            return new GoonPageParseResult.ForwardVersion(type, GetInt(root, "protocol")!.Value);
        }

        return Parsers.TryGetValue(type, out var parser)
            ? new GoonPageParseResult.Parsed(parser(root))
            : new GoonPageParseResult.UnknownType(type);
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

    private static long? GetLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v)
            ? v
            : null;

    private static double? GetDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var v)
            ? v
            : null;
}
