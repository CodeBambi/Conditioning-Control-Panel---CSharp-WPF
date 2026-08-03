using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.GoonGame
{
    // =========================================================================================
    // GOON GAME SIGNALING — CLIENT SIDE OF A SERVER THAT DOES NOT EXIST YET.
    //
    // Phase F builds these endpoints in the private repo CC-Labs-llc/CCP-Server. This file is
    // written to the plan's spec and the JSON below IS the contract Phase F implements — if the
    // server disagrees with a field name here, this file is the thing that was agreed first.
    //
    // Everything is POST against https://codebambi-proxy.vercel.app, with the same auth discipline
    // as RemoteControlService.AuthPostAsync: an X-Auth-Token header carrying
    // App.Settings.Current.AuthToken, unified_id in the body, X-Client-Version + User-Agent set on
    // the HttpClient. Reuse the existing 40/min per-user rate-limit bucket; 429 bodies keep the
    // { error, cap, count, retry_after_seconds } shape the client already knows how to read.
    //
    // ---------------------------------------------------------------------------- POST /v2/goon/invite
    // Host mints a match. Server checks premium OR burns the weekly free pass (server decides,
    // never the client) and stores the room under a short crockford32 code with a ~5 min TTL.
    //   req  { "unified_id":"u_...", "display_name":"Bambi", "app_version":"6.6.3",
    //          "protocol_version":1, "prefer_relay":false }
    //   200  { "ok":true, "code":"7QK4RM", "token":"<host room token>", "role":"host",
    //          "expires_in_sec":300, "pass":"premium"|"weekly_free", "relay_allowed":true }
    //   402  { "error":"no_pass", "next_pass_utc":"2026-08-10T00:00:00Z" }   weekly pass spent
    //   401  { "error":"unauthorized" }
    //   429  { "error":"rate_limited", "cap":"user"|"ip", "count":N, "retry_after_seconds":N }
    //
    // ------------------------------------------------------------------------------ POST /v2/goon/join
    // Guest redeems the code. Same pass check. Server marks the room joined so the host's next
    // /signal poll sees peer_joined.
    //   req  { "unified_id":"u_...", "code":"7QK4RM", "display_name":"Circe",
    //          "app_version":"6.6.3", "protocol_version":1 }
    //   200  { "ok":true, "token":"<guest room token>", "role":"guest", "expires_in_sec":300,
    //          "peer_display_name":"Bambi", "peer_app_version":"6.6.3", "pass":"premium",
    //          "relay_allowed":true }
    //   404  { "error":"unknown_code" }        bad/expired code
    //   409  { "error":"already_joined" }      someone else took the seat
    //   402/401/429 as above
    //
    // ---------------------------------------------------------------------------- POST /v2/goon/signal
    // The SDP/ICE mailbox. ONE call both posts and drains, so a ~2 s short-poll costs one request.
    // Used during match SETUP ONLY — it stops the moment the data channel opens. TTL ~5 min.
    // `data` is an OPAQUE string the server never parses (it is SIPSorcery's own
    // RTCSessionDescriptionInit.toJSON() / RTCIceCandidateInit.toJSON()).
    //   req  { "unified_id":"u_...", "code":"7QK4RM", "token":"...", "role":"host"|"guest",
    //          "since":0,
    //          "msgs":[ { "kind":"offer"|"answer"|"ice"|"bye", "data":"<opaque>" } ] }
    //   200  { "ok":true, "cursor":7,
    //          "msgs":[ { "seq":6, "kind":"offer", "data":"<opaque>", "from":"host" } ],
    //          "peer_joined":true, "peer_gone":false }
    //   404  { "error":"expired" }             room TTL elapsed
    //   401/429 as above
    //   Server: append-only per-room list, `since` is an exclusive cursor, a caller never receives
    //   its own messages back. Cap the list (64 entries) and the payload (8 KB/message).
    //
    //   CROSS-PLATFORM (verified against SIPSorcery 10.0.13): the `data` blobs are byte-identical
    //   to what a browser produces, so a web/RN peer needs no translation layer.
    //     offer/answer -> {"type":"offer"|"answer","sdp":"v=0\r\n..."}   == RTCSessionDescription.toJSON()
    //     ice          -> {"candidate":"candidate:...","sdpMid":"0","sdpMLineIndex":0,
    //                      "usernameFragment":"..."}                     == RTCIceCandidate.toJSON()
    //   A JS client does `pc.setRemoteDescription(JSON.parse(data))` / `pc.addIceCandidate(JSON.parse(data))`
    //   directly. The `candidate` value keeps its "candidate:" prefix, as browsers expect.
    //
    // ----------------------------------------------------------------------------- POST /v2/goon/relay
    // The fallback message mailbox, used when ICE never completes. Same post-and-drain shape, but
    // `data` is a whole GoonMessage frame and the server MAY hold the request open (long-poll) for
    // up to wait_ms before answering with an empty list. Room TTL extends to the match length.
    //   req  { "unified_id":"u_...", "code":"7QK4RM", "token":"...", "role":"host"|"guest",
    //          "since":41, "wait_ms":20000, "msgs":[ "<GoonMessage json>", ... ] }
    //   200  { "ok":true, "cursor":45,
    //          "msgs":[ { "seq":42, "data":"<GoonMessage json>" } ], "peer_online":true }
    //   404  { "error":"expired" }
    //   401/429 as above
    //   Server: its own rate-limit budget (relay is chatty by design — one call every ~2 s per
    //   player for up to ~20 min), 16 KB per frame to match GoonWire.MaxWireBytes, 128-frame ring.
    //   NEVER inspect or persist `data` beyond the ring's TTL: it is match traffic, not content.
    //
    // ---------------------------------------------------------------------------- POST /v2/goon/ledger
    // Phase B's business, listed here only so the surface is documented in one place:
    // both clients write the signed ResultMsg, the server stores the pair under both unified_ids
    // and flags a mismatch as disputed.
    // =========================================================================================

    /// <summary>Result of <see cref="GoonSignalingClient.CreateInviteAsync"/>.</summary>
    public sealed class GoonInviteResult
    {
        public string Code { get; set; } = "";
        public string Token { get; set; } = "";
        public int ExpiresInSec { get; set; }
        /// <summary>"premium" | "weekly_free" — what the server charged for this match.</summary>
        public string Pass { get; set; } = "";
        public bool RelayAllowed { get; set; } = true;
    }

    /// <summary>Result of <see cref="GoonSignalingClient.JoinAsync"/>.</summary>
    public sealed class GoonJoinResult
    {
        public string Token { get; set; } = "";
        public int ExpiresInSec { get; set; }
        public string PeerDisplayName { get; set; } = "";
        public string PeerAppVersion { get; set; } = "";
        public string Pass { get; set; } = "";
        public bool RelayAllowed { get; set; } = true;
    }

    /// <summary>One entry in the SDP/ICE mailbox.</summary>
    public sealed class GoonSignalItem
    {
        public long Seq { get; set; }
        /// <summary>offer | answer | ice | bye</summary>
        public string Kind { get; set; } = "";
        /// <summary>Opaque to the server — SIPSorcery's own toJSON() strings.</summary>
        public string Data { get; set; } = "";
        public string From { get; set; } = "";
    }

    public sealed class GoonSignalPollResult
    {
        public long Cursor { get; set; }
        public List<GoonSignalItem> Messages { get; set; } = new();
        public bool PeerJoined { get; set; }
        public bool PeerGone { get; set; }
    }

    public sealed class GoonRelayPollResult
    {
        public long Cursor { get; set; }
        public List<string> Frames { get; set; } = new();
        public bool PeerOnline { get; set; }
    }

    /// <summary>
    /// One HTTP POST, abstracted so the whole signaling layer is testable with no server and no
    /// network. Returns the status code and the raw body; it must not throw for a non-2xx.
    /// </summary>
    public delegate Task<(HttpStatusCode Status, string Body)> GoonHttpPost(string url, string jsonBody, CancellationToken ct);

    /// <summary>
    /// Thin client over the /v2/goon/* surface documented at the top of this file.
    ///
    /// Failure model: every method returns null on failure and leaves a machine-readable reason in
    /// <see cref="LastError"/> — the lobby needs to tell "your weekly pass is spent" (a product
    /// message) apart from "the endpoint 404s" (Phase F hasn't shipped) apart from "you're offline".
    /// Nothing here throws for a server-side outcome.
    /// </summary>
    public sealed class GoonSignalingClient : IDisposable
    {
        public const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        /// <summary>LastError when the endpoint answered 404 with no room context, i.e. Phase F isn't deployed.</summary>
        public const string ErrorNotDeployed = "not_deployed";
        public const string ErrorNoPass = "no_pass";
        public const string ErrorUnknownCode = "unknown_code";
        public const string ErrorAlreadyJoined = "already_joined";
        public const string ErrorExpired = "expired";
        public const string ErrorUnauthorized = "unauthorized";
        public const string ErrorRateLimited = "rate_limited";
        public const string ErrorNetwork = "network";

        private readonly HttpClient? _httpClient;
        private readonly GoonHttpPost _post;
        private bool _disposed;

        /// <param name="postOverride">
        /// Swap the HTTP layer out. Used by <see cref="GoonFakeSignalingServer"/> for offline
        /// development and by Phase G's two-instance play-test harness.
        /// </param>
        public GoonSignalingClient(GoonHttpPost? postOverride = null)
        {
            if (postOverride != null)
            {
                _post = postOverride;
                return;
            }

            _httpClient = new HttpClient
            {
                // Generous: the relay endpoint long-polls for up to ~20 s server-side.
                Timeout = TimeSpan.FromSeconds(40)
            };
            _httpClient.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");
            _post = DefaultPostAsync;
        }

        /// <summary>Machine-readable reason for the last failure. See the Error* constants.</summary>
        public string? LastError { get; private set; }

        /// <summary>Seconds the server asked us to wait, when <see cref="LastError"/> is rate_limited.</summary>
        public int? RetryAfterSeconds { get; private set; }

        /// <summary>Extra detail for the UI, e.g. the next weekly-pass reset instant.</summary>
        public string? LastErrorDetail { get; private set; }

        // ------------------------------------------------------------------ endpoints

        public async Task<GoonInviteResult?> CreateInviteAsync(string displayName, bool preferRelay, CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new
            {
                unified_id = App.UnifiedUserId ?? "",
                display_name = Sanitize(displayName, 32),
                app_version = UpdateService.AppVersion,
                protocol_version = GoonConsts.ProtocolVersion,
                prefer_relay = preferRelay,
            });

            var json = await CallAsync("/v2/goon/invite", body, ct).ConfigureAwait(false);
            if (json == null) return null;

            var code = json["code"]?.Value<string>();
            var token = json["token"]?.Value<string>();
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(token))
            {
                LastError = "malformed_response";
                App.Logger?.Warning("[GoonSignal] /invite returned no code/token");
                return null;
            }

            return new GoonInviteResult
            {
                Code = code!,
                Token = token!,
                ExpiresInSec = json["expires_in_sec"]?.Value<int?>() ?? 300,
                Pass = json["pass"]?.Value<string>() ?? "",
                RelayAllowed = json["relay_allowed"]?.Value<bool?>() ?? true,
            };
        }

        public async Task<GoonJoinResult?> JoinAsync(string code, string displayName, CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new
            {
                unified_id = App.UnifiedUserId ?? "",
                code = NormalizeCode(code),
                display_name = Sanitize(displayName, 32),
                app_version = UpdateService.AppVersion,
                protocol_version = GoonConsts.ProtocolVersion,
            });

            var json = await CallAsync("/v2/goon/join", body, ct).ConfigureAwait(false);
            if (json == null) return null;

            var token = json["token"]?.Value<string>();
            if (string.IsNullOrEmpty(token))
            {
                LastError = "malformed_response";
                return null;
            }

            return new GoonJoinResult
            {
                Token = token!,
                ExpiresInSec = json["expires_in_sec"]?.Value<int?>() ?? 300,
                PeerDisplayName = json["peer_display_name"]?.Value<string>() ?? "",
                PeerAppVersion = json["peer_app_version"]?.Value<string>() ?? "",
                Pass = json["pass"]?.Value<string>() ?? "",
                RelayAllowed = json["relay_allowed"]?.Value<bool?>() ?? true,
            };
        }

        /// <summary>Post-and-drain against the SDP/ICE mailbox. One call per short-poll tick.</summary>
        public async Task<GoonSignalPollResult?> SignalAsync(
            string code, string token, string role, long since,
            IReadOnlyList<GoonSignalItem>? outgoing, CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new
            {
                unified_id = App.UnifiedUserId ?? "",
                code = NormalizeCode(code),
                token,
                role,
                since,
                msgs = (outgoing ?? Array.Empty<GoonSignalItem>())
                    .Select(m => new { kind = m.Kind, data = m.Data }).ToArray(),
            });

            var json = await CallAsync("/v2/goon/signal", body, ct).ConfigureAwait(false);
            if (json == null) return null;

            var result = new GoonSignalPollResult
            {
                Cursor = json["cursor"]?.Value<long?>() ?? since,
                PeerJoined = json["peer_joined"]?.Value<bool?>() ?? false,
                PeerGone = json["peer_gone"]?.Value<bool?>() ?? false,
            };

            if (json["msgs"] is JArray arr)
            {
                foreach (var m in arr)
                {
                    result.Messages.Add(new GoonSignalItem
                    {
                        Seq = m["seq"]?.Value<long?>() ?? 0,
                        Kind = m["kind"]?.Value<string>() ?? "",
                        Data = m["data"]?.Value<string>() ?? "",
                        From = m["from"]?.Value<string>() ?? "",
                    });
                }
            }

            return result;
        }

        /// <summary>Post-and-drain against the relay mailbox. <paramref name="waitMs"/> is the long-poll hint.</summary>
        public async Task<GoonRelayPollResult?> RelayAsync(
            string code, string token, string role, long since,
            IReadOnlyList<string>? outgoing, int waitMs, CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new
            {
                unified_id = App.UnifiedUserId ?? "",
                code = NormalizeCode(code),
                token,
                role,
                since,
                wait_ms = waitMs,
                msgs = outgoing ?? (IReadOnlyList<string>)Array.Empty<string>(),
            });

            var json = await CallAsync("/v2/goon/relay", body, ct).ConfigureAwait(false);
            if (json == null) return null;

            var result = new GoonRelayPollResult
            {
                Cursor = json["cursor"]?.Value<long?>() ?? since,
                PeerOnline = json["peer_online"]?.Value<bool?>() ?? false,
            };

            if (json["msgs"] is JArray arr)
            {
                foreach (var m in arr)
                {
                    var data = m["data"]?.Value<string>();
                    if (!string.IsNullOrEmpty(data)) result.Frames.Add(data!);
                }
            }

            return result;
        }

        // ------------------------------------------------------------------ plumbing

        /// <summary>
        /// POST + status triage. Returns the parsed body on 2xx, null otherwise with
        /// <see cref="LastError"/> set. Never throws for a server outcome.
        /// </summary>
        private async Task<JObject?> CallAsync(string path, string body, CancellationToken ct)
        {
            LastError = null;
            LastErrorDetail = null;
            RetryAfterSeconds = null;

            HttpStatusCode status;
            string raw;
            try
            {
                (status, raw) = await _post(ProxyBaseUrl + path, body, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastError = ErrorNetwork;
                App.Logger?.Warning(ex, "[GoonSignal] {Path} transport failure", path);
                return null;
            }

            JObject? json = null;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try { json = JObject.Parse(raw); }
                catch { /* non-JSON body — the status code still tells us enough */ }
            }

            if ((int)status >= 200 && (int)status < 300)
            {
                if (json == null)
                {
                    LastError = "malformed_response";
                    return null;
                }
                return json;
            }

            var serverError = json?["error"]?.Value<string>();

            switch ((int)status)
            {
                case 401:
                case 403:
                    LastError = serverError ?? ErrorUnauthorized;
                    break;
                case 402:
                    LastError = serverError ?? ErrorNoPass;
                    LastErrorDetail = json?["next_pass_utc"]?.Value<string>();
                    break;
                case 404:
                    // A 404 with no error body means the route itself isn't there — Phase F hasn't
                    // deployed. The lobby says "Goon Game is warming up", not "bad code".
                    LastError = serverError ?? ErrorNotDeployed;
                    break;
                case 409:
                    LastError = serverError ?? ErrorAlreadyJoined;
                    break;
                case 429:
                    LastError = ErrorRateLimited;
                    RetryAfterSeconds = json?["retry_after_seconds"]?.Value<int?>();
                    break;
                default:
                    LastError = serverError ?? $"http_{(int)status}";
                    break;
            }

            App.Logger?.Warning("[GoonSignal] {Path} -> {Status} ({Error})", path, (int)status, LastError);
            return null;
        }

        /// <summary>
        /// The real HTTP path. Mirrors RemoteControlService.AuthPostAsync: X-Auth-Token per
        /// request (the token can be refreshed mid-session, so it is never baked into the client's
        /// default headers), version headers on the client.
        /// </summary>
        private async Task<(HttpStatusCode, string)> DefaultPostAsync(string url, string jsonBody, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };

            var token = App.Settings?.Current?.AuthToken;
            if (!string.IsNullOrEmpty(token))
                request.Headers.Add("X-Auth-Token", token);

            using var response = await _httpClient!.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (response.StatusCode, body);
        }

        /// <summary>Invite codes are crockford32 and case-insensitive; normalize before it hits the wire.</summary>
        public static string NormalizeCode(string? code) =>
            (code ?? string.Empty).Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");

        private static string Sanitize(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _httpClient?.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// An in-memory implementation of everything documented at the top of this file.
    ///
    /// Two purposes. It lets the real <see cref="GoonWebRtcTransport"/> and
    /// <see cref="GoonRelayTransport"/> be exercised end-to-end with no server (both peers in one
    /// process, or two app instances sharing one of these), and — because it is executable — it
    /// pins the contract Phase F has to match. If the server and this class ever disagree, one of
    /// them is a bug.
    ///
    /// It deliberately does NOT model auth, the weekly pass, or rate limiting: those are server
    /// policy, and faking them here would only teach the client to trust a fake.
    /// </summary>
    public sealed class GoonFakeSignalingServer
    {
        private sealed class Room
        {
            public string Code = "";
            public string HostToken = "";
            public string GuestToken = "";
            public bool Joined;
            public readonly List<GoonSignalItem> Signals = new();
            public readonly List<(long Seq, string From, string Data)> Relay = new();
            public long SignalSeq;
            public long RelaySeq;
        }

        private readonly Dictionary<string, Room> _rooms = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private int _codeCounter;

        /// <summary>Hand this to <see cref="GoonSignalingClient"/>'s constructor.</summary>
        public GoonHttpPost Post => PostAsync;

        private Task<(HttpStatusCode, string)> PostAsync(string url, string jsonBody, CancellationToken ct)
        {
            var req = JObject.Parse(jsonBody);
            var path = new Uri(url).AbsolutePath;

            lock (_gate)
            {
                switch (path)
                {
                    case "/v2/goon/invite":
                    {
                        var room = new Room
                        {
                            Code = "LOOP" + (++_codeCounter).ToString("D2"),
                            HostToken = Guid.NewGuid().ToString("N"),
                            GuestToken = Guid.NewGuid().ToString("N"),
                        };
                        _rooms[room.Code] = room;
                        return Ok(new JObject
                        {
                            ["ok"] = true,
                            ["code"] = room.Code,
                            ["token"] = room.HostToken,
                            ["role"] = "host",
                            ["expires_in_sec"] = 300,
                            ["pass"] = "premium",
                            ["relay_allowed"] = true,
                        });
                    }

                    case "/v2/goon/join":
                    {
                        var code = req["code"]?.Value<string>() ?? "";
                        if (!_rooms.TryGetValue(code, out var room))
                            return Fail(HttpStatusCode.NotFound, GoonSignalingClient.ErrorUnknownCode);
                        if (room.Joined)
                            return Fail(HttpStatusCode.Conflict, GoonSignalingClient.ErrorAlreadyJoined);
                        room.Joined = true;
                        return Ok(new JObject
                        {
                            ["ok"] = true,
                            ["token"] = room.GuestToken,
                            ["role"] = "guest",
                            ["expires_in_sec"] = 300,
                            ["peer_display_name"] = "host",
                            ["peer_app_version"] = UpdateService.AppVersion,
                            ["pass"] = "premium",
                            ["relay_allowed"] = true,
                        });
                    }

                    case "/v2/goon/signal":
                    {
                        var code = req["code"]?.Value<string>() ?? "";
                        if (!_rooms.TryGetValue(code, out var room))
                            return Fail(HttpStatusCode.NotFound, GoonSignalingClient.ErrorExpired);

                        var role = req["role"]?.Value<string>() ?? "host";
                        var since = req["since"]?.Value<long?>() ?? 0;

                        if (req["msgs"] is JArray outgoing)
                        {
                            foreach (var m in outgoing)
                            {
                                room.Signals.Add(new GoonSignalItem
                                {
                                    Seq = ++room.SignalSeq,
                                    Kind = m["kind"]?.Value<string>() ?? "",
                                    Data = m["data"]?.Value<string>() ?? "",
                                    From = role,
                                });
                            }
                        }

                        var mine = new JArray();
                        long cursor = since;
                        foreach (var s in room.Signals)
                        {
                            if (s.Seq <= since || s.From == role) { cursor = Math.Max(cursor, s.Seq); continue; }
                            mine.Add(new JObject { ["seq"] = s.Seq, ["kind"] = s.Kind, ["data"] = s.Data, ["from"] = s.From });
                            cursor = Math.Max(cursor, s.Seq);
                        }

                        return Ok(new JObject
                        {
                            ["ok"] = true,
                            ["cursor"] = cursor,
                            ["msgs"] = mine,
                            ["peer_joined"] = room.Joined,
                            ["peer_gone"] = false,
                        });
                    }

                    case "/v2/goon/relay":
                    {
                        var code = req["code"]?.Value<string>() ?? "";
                        if (!_rooms.TryGetValue(code, out var room))
                            return Fail(HttpStatusCode.NotFound, GoonSignalingClient.ErrorExpired);

                        var role = req["role"]?.Value<string>() ?? "host";
                        var since = req["since"]?.Value<long?>() ?? 0;

                        if (req["msgs"] is JArray outgoing)
                        {
                            foreach (var m in outgoing)
                            {
                                var data = m.Value<string>();
                                if (!string.IsNullOrEmpty(data)) room.Relay.Add((++room.RelaySeq, role, data!));
                            }
                        }

                        var mine = new JArray();
                        long cursor = since;
                        foreach (var (seq, from, data) in room.Relay)
                        {
                            if (seq <= since || from == role) { cursor = Math.Max(cursor, seq); continue; }
                            mine.Add(new JObject { ["seq"] = seq, ["data"] = data });
                            cursor = Math.Max(cursor, seq);
                        }

                        // No long-poll here: the fake answers instantly and the caller's cadence
                        // timer paces it. Real servers hold the request for wait_ms.
                        return Ok(new JObject
                        {
                            ["ok"] = true,
                            ["cursor"] = cursor,
                            ["msgs"] = mine,
                            ["peer_online"] = room.Joined,
                        });
                    }

                    default:
                        return Fail(HttpStatusCode.NotFound, GoonSignalingClient.ErrorNotDeployed);
                }
            }
        }

        private static Task<(HttpStatusCode, string)> Ok(JObject body) =>
            Task.FromResult((HttpStatusCode.OK, body.ToString(Formatting.None)));

        private static Task<(HttpStatusCode, string)> Fail(HttpStatusCode status, string error) =>
            Task.FromResult((status, new JObject { ["error"] = error }.ToString(Formatting.None)));
    }
}
