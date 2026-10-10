using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.PieceByPiece;

/// <summary>
/// The pure half of WPF <c>PieceByPieceHostService</c> (7.1.5), lifted out of the WPF window host so
/// both heads share one copy: the net lane's whitelist and reply shapes, the media-request reading and
/// reservoir, the friend / Lobby intent frames, the heartbeat ladder and the settings reads. Nothing
/// here touches a window, a WebView or a setting; the head's host (Avalonia
/// <c>Views/Games/GameWindow.Pbp.cs</c>) does that and calls in.
/// </summary>
public static class PbpHostRules
{
    /// <summary>Where <c>/v2/pbp/*</c> lives. The same proxy every other online surface uses.</summary>
    public const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

    /// <summary>The ONLY path prefix the net lane forwards. A whitelist, not a convenience: without it
    /// the handler is an open HTTP proxy that signs whatever the page asks for with the user's token.</summary>
    public const string AllowedPathPrefix = "/v2/pbp/";

    /// <summary>Hard ceiling on one <c>pbp:media-request</c>.</summary>
    public const int MaxMediaPerKind = 64;

    /// <summary>Fallback hold for a video tile when the mandatory-video floor is unset (0).</summary>
    public const int DefaultVideoHoldSec = 15;

    /// <summary>A page silent this long gets one ping; still silent at the next tick, the window closes.</summary>
    public const double HeartbeatSilenceSeconds = 20;

    /// <summary>Progress-aware boot deadline: no sign of life from the page for this long = a failed boot.</summary>
    public static readonly TimeSpan BootDeadline = TimeSpan.FromSeconds(45);

    // ------------------------------------------------------------------ the net lane

    /// <summary>One parsed <c>pbp:net</c> frame. <see cref="Refusal"/> non-null = answer status 0 with that body.</summary>
    public readonly record struct NetCall(string Id, string Method, string Path, string Body, string? Refusal);

    /// <summary>Read <c>pbp:net { id, method, path, body }</c> exactly as WPF OnNetRequest does: a body
    /// that is a string rides as is, an object is serialised compactly, null is empty; any path outside
    /// <see cref="AllowedPathPrefix"/> is <c>forbidden_path</c>, any verb but GET / POST <c>forbidden_method</c>.</summary>
    public static NetCall ReadNet(JObject o)
    {
        var id = (string?)o["id"] ?? "";
        var path = (string?)o["path"] ?? "";
        var method = ((string?)o["method"] ?? "GET").Trim().ToUpperInvariant();
        var body = o["body"]?.Type == JTokenType.String
            ? (string?)o["body"] ?? ""
            : (o["body"] is null || o["body"]!.Type == JTokenType.Null
                ? ""
                : o["body"]!.ToString(Newtonsoft.Json.Formatting.None));
        string? refusal = null;
        if (!path.StartsWith(AllowedPathPrefix, StringComparison.Ordinal)) refusal = "forbidden_path";
        else if (method != "GET" && method != "POST") refusal = "forbidden_method";
        return new NetCall(id, method, path, body, refusal);
    }

    /// <summary>
    /// Send one whitelisted call and return <c>(status, body)</c>. The token is attached HERE, never by
    /// the page. A failure of any kind (timeout, DNS, offline, no base url on a sandboxed head) answers
    /// status 0, the page's "never got an answer". Never throws.
    /// </summary>
    public static async Task<(int Status, string Body)> SendAsync(HttpClient http, string? baseUrl, NetCall call,
        string? authToken, string appVersion, CancellationToken ct = default)
    {
        if (call.Refusal != null) return (0, call.Refusal);
        if (string.IsNullOrEmpty(baseUrl)) return (0, "");
        try
        {
            var verb = call.Method == "POST" ? HttpMethod.Post : HttpMethod.Get;
            using var request = new HttpRequestMessage(verb, baseUrl + call.Path);
            if (call.Method == "POST") request.Content = new StringContent(call.Body, Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(authToken)) request.Headers.Add("X-Auth-Token", authToken);
            request.Headers.Add("X-Client-Version", appVersion);
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ((int)response.StatusCode, text);
        }
        catch (Exception ex)
        {
            Log.Debug("PieceByPiece: net {Method} {Path} failed: {E}", call.Method, call.Path, ex.Message);
            return (0, "");
        }
    }

    public static JObject NetResult(string id, int status, string body) =>
        new() { ["type"] = "pbp:net-result", ["id"] = id, ["status"] = status, ["body"] = body };

    // ------------------------------------------------------------------ identity + settings

    /// <summary>host -&gt; page <c>pbp:identity</c>. THE TOKEN IS THE CCP AUTH TOKEN, not the Patreon
    /// bearer. An empty token is a normal state (<c>online:false</c>): the lobby answers null and solo
    /// play stays free with no account.</summary>
    public static JObject IdentityFrame(string? unifiedId, string? authToken, string? displayName, string appVersion,
        string? serverBase = ProxyBaseUrl)
    {
        var uid = unifiedId ?? "";
        var token = authToken ?? "";
        return new JObject
        {
            ["type"] = "pbp:identity",
            ["unifiedId"] = uid,
            ["displayName"] = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName,
            ["appVersion"] = appVersion,
            // Both halves are needed: an account with no token cannot authenticate, and a token with
            // no account has nothing to name in a request body.
            ["online"] = !string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(token),
            ["net"] = new JObject
            {
                ["serverBase"] = serverBase ?? ProxyBaseUrl,
                ["authToken"] = token,
                ["viaHost"] = true,
            },
        };
    }

    /// <summary>How long a video tile is held: the player's own mandatory-video floor, 15 when unset, 10..30.</summary>
    public static int VideoHoldSec(int configured) =>
        Math.Clamp(configured > 0 ? configured : DefaultVideoHoldSec, 10, 30);

    public static JObject SettingsFrame(int videoMinDurationSeconds, bool reducedMotion, IEnumerable<string> whispers) =>
        new()
        {
            ["type"] = "pbp:settings",
            ["videoHoldSec"] = VideoHoldSec(videoMinDurationSeconds),
            ["reducedMotion"] = reducedMotion,
            ["whispers"] = new JArray(whispers.Cast<object>().ToArray()),
        };

    // ------------------------------------------------------------------ the player's own library

    /// <summary>The requested count, clamped as WPF does (default 24, 1..<see cref="MaxMediaPerKind"/>).</summary>
    public static int ReadCount(JObject o)
    {
        int asked = 24;
        try { asked = (int?)o["count"] ?? 24; } catch { /* a hand-edited frame: the default */ }
        return Math.Clamp(asked, 1, MaxMediaPerKind);
    }

    /// <summary>The requested kinds, defaulting to all three when the field is off or not a list of strings.</summary>
    public static HashSet<string> ReadKinds(JToken? token)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (token is JArray arr)
        {
            foreach (var t in arr)
            {
                var s = t.Type == JTokenType.String ? (string?)t : null;
                if (!string.IsNullOrWhiteSpace(s)) set.Add(s!.Trim());
            }
        }
        if (set.Count == 0) { set.Add("image"); set.Add("gif"); set.Add("video"); }
        return set;
    }

    /// <summary>
    /// Up to <paramref name="count"/> urls per kind, reservoir-sampled in one walk. GIFs are split OUT
    /// of images: a tile that loops and one that does not are different things to whatever places them.
    /// <paramref name="pool"/> is the active library (relative name, page url, is-image).
    /// </summary>
    public static (string[] Images, string[] Gifs, string[] Videos) SampleMedia(
        IEnumerable<(string Rel, string Url, bool IsImage)> pool, HashSet<string> kinds, int count, Random rng)
    {
        var images = new List<string>(count);
        var gifs = new List<string>(count);
        var videos = new List<string>(count);
        bool wantImages = kinds.Contains("image"), wantGifs = kinds.Contains("gif"), wantVideos = kinds.Contains("video");
        int seenImages = 0, seenGifs = 0, seenVideos = 0;
        try
        {
            foreach (var (rel, url, isImage) in pool)
            {
                bool isGif = rel.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
                if (!isImage) { if (wantVideos) Offer(videos, ref seenVideos, count, url, rng); }
                else if (isGif) { if (wantGifs) Offer(gifs, ref seenGifs, count, url, rng); }
                else if (wantImages) Offer(images, ref seenImages, count, url, rng);
            }
        }
        catch (Exception ex)
        {
            // Whatever was gathered before the walk fell over is still worth sending.
            Log.Debug("PieceByPiece: media scan failed: {E}", ex.Message);
        }
        return (images.ToArray(), gifs.ToArray(), videos.ToArray());
    }

    private static void Offer(List<string> reservoir, ref int seen, int count, string url, Random rng)
    {
        seen++;
        if (reservoir.Count < count) { reservoir.Add(url); return; }
        int j = rng.Next(seen);
        if (j < count) reservoir[j] = url;
    }

    public static JObject MediaFrame((string[] Images, string[] Gifs, string[] Videos) m) =>
        new()
        {
            ["type"] = "pbp:media",
            ["images"] = new JArray(m.Images.Cast<object>().ToArray()),
            ["gifs"] = new JArray(m.Gifs.Cast<object>().ToArray()),
            ["videos"] = new JArray(m.Videos.Cast<object>().ToArray()),
        };

    // ------------------------------------------------------------------ friends drawer + Lobby intents

    /// <summary>A chess lobby row id: the server's opaque <c>p_</c> id, letters, digits, _ and -.</summary>
    public static bool IsTableId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.StartsWith("p_", StringComparison.Ordinal)
        && id.Skip(2).All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-') && id.Length > 2;

    public static JObject ChallengeIntent(string friendId) =>
        new() { ["type"] = "pbp:friend", ["mode"] = "challenge", ["friendId"] = friendId };

    public static JObject AcceptIntent(string challengeId) =>
        new() { ["type"] = "pbp:friend", ["mode"] = "accept", ["challengeId"] = challengeId };

    /// <summary>The Lobby's Join on a chess open table (the page runs its own lobby.join, refusals included).</summary>
    public static JObject JoinIntent(string target) =>
        new() { ["type"] = "pbp:friend", ["mode"] = "join", ["target"] = target };

    /// <summary>The Lobby's "Host a chess table": list a table at the page's default clock.</summary>
    public static JObject HostIntent() => new() { ["type"] = "pbp:friend", ["mode"] = "host" };

    // ------------------------------------------------------------------ watchdog

    public enum WatchStep { Idle, Ok, Ping, Close }

    /// <summary>One 5 s heartbeat tick (WPF StartHeartbeatWatch): asleep until the page is ready, quiet
    /// inside 20 s, one ping, then close. No relaunch ladder: a chess game is not a run worth preserving.</summary>
    public static WatchStep HeartbeatStep(bool ready, double silentSeconds, bool pinged)
    {
        if (!ready) return WatchStep.Idle;
        if (silentSeconds <= HeartbeatSilenceSeconds) return WatchStep.Ok;
        return pinged ? WatchStep.Close : WatchStep.Ping;
    }

    /// <summary>The boot deadline (WPF ArmBootDeadline): a page that never became ready and has shown no
    /// sign of life for <see cref="BootDeadline"/>.</summary>
    public static bool BootTimedOut(bool ready, TimeSpan sinceProgress) => !ready && sinceProgress >= BootDeadline;
}
