using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Lovense LAN ("Game Mode") provider, v2. Replaces <see cref="LovenseProvider"/>, whose
    /// single <c>_toyId</c> field made only the LAST discovered toy addressable, whose 200 ms
    /// client throttle swallowed every sub-500 ms command, and which carried two disagreeing
    /// intensity mappers plus an undisposed HttpClient.
    ///
    /// What this one does:
    ///  * <b>Connection</b> - derives candidate bases from whatever the user typed (bare ip,
    ///    ip:port, or a full URL) and probes <c>https://{dashed-ip}.lovense.club:30010</c> first,
    ///    then plain <c>http://{ip}:20010</c>. The HTTPS alias is what Lovense documents, but
    ///    consumer routers' DNS-rebinding protection resolves it to nothing, so the plain-HTTP
    ///    fallback is mandatory. The winning base is remembered for the SESSION ONLY - this
    ///    provider never writes settings.
    ///  * <b>Registry</b> - one <see cref="HapticDevice"/> per toy from <c>GetToys</c>, with
    ///    actuators derived from <c>shortFunctionNames</c> (so an Edge exposes two vibe motors and
    ///    a Solace exposes thrust+depth and NO vibrate). Re-polled every 20 s for battery and
    ///    arrivals/departures.
    ///  * <b>Output</b> - level-set semantics. <see cref="SetOutputsAsync"/> quantizes to native
    ///    steps and SUPPRESSES the HTTP send when nothing changed, so the mixer can call at 10 Hz
    ///    all day and the wire stays quiet while levels are steady.
    ///
    /// <para><b>Keep-alive strategy (documented choice):</b> commands go out with
    /// <c>timeSec:0</c> = run until stopped, and a 1 Hz maintenance loop re-sends the current
    /// non-zero state every 25 s as a safety refresh. The alternative (short <c>timeSec</c>
    /// repeats) was rejected: it makes every command a race against its own expiry and produces
    /// audible motor gaps at the seams. Because <c>timeSec:0</c> has no server-side watchdog, WE
    /// own the stop - zero levels are always transmitted explicitly (as action value 0), and
    /// <see cref="StopAllAsync"/> bypasses all suppression.</para>
    ///
    /// Thread-safety: <see cref="Devices"/> hands out snapshot copies, all mutable state lives
    /// under <c>_lock</c>, HTTP happens outside the lock, and events are raised from IO threads
    /// (never touch the UI in a handler without marshalling). Device IO never throws to callers;
    /// failures surface as <see cref="Error"/> plus <see cref="DevicesChanged"/>.
    /// </summary>
    public sealed class LovenseProviderV2 : IHapticProviderV2
    {
        // --- tuning -------------------------------------------------------
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ToyPollInterval = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(25);
        private const int MaxConsecutiveFailures = 3;
        /// <summary>How many extra latched targets one completing send may drive before handing
        /// the rest back to the 1 Hz drain. Bounds how long the per-device gate can be held across
        /// HTTP awaits (and bounds re-entrancy onto the completing call's context).</summary>
        private const int MaxPendingDrainsPerSend = 1;
        /// <summary>Retry ladder after the connection failed out (never after a user Disconnect).</summary>
        private static readonly int[] ReconnectBackoffSeconds = { 5, 10, 30 };

        // --- state --------------------------------------------------------
        private readonly object _lock = new();
        private readonly HttpClient _http;
        private readonly LovenseToyEventsClient _events;
        private readonly Dictionary<string, LovenseToy> _toys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DeviceState> _state = new(StringComparer.OrdinalIgnoreCase);

        private string? _baseUrl;              // session-only; never persisted
        private CancellationTokenSource? _loopCts;
        private Task? _loopTask;
        private DateTime _lastPollUtc = DateTime.MinValue;
        private volatile bool _connected;
        private volatile bool _disposed;
        /// <summary>True after an explicit <see cref="DisconnectAsync"/> - "the user turned it
        /// off" must never auto-reconnect, unlike "we failed out".</summary>
        private volatile bool _userDisconnected;
        private DateTime _nextReconnectUtc = DateTime.MinValue;   // MinValue = no retry armed
        private int _reconnectAttempt;
        private int _pollFailures;

        public string Key => "lovense";
        public string DisplayName => "Lovense (Game Mode)";
        public bool IsConnected => _connected;

        /// <summary>The base URL that actually answered this session (diagnostics / UI).</summary>
        public string? ActiveBaseUrl => _baseUrl;

        /// <summary>True once the Toy Events websocket is up (two-way input available).</summary>
        public bool ToyEventsConnected => _events.IsConnected;

        /// <summary>
        /// Optional explicit address, e.g. "192.168.1.42" or "http://192.168.1.42:20010".
        /// When null the address is read from the haptics settings at connect time.
        /// </summary>
        public string? ConfiguredUrlOverride { get; set; }

        public event EventHandler? DevicesChanged;
        public event EventHandler<HapticToyEvent>? ToyEvent;
        public event EventHandler<string>? Error;

        public LovenseProviderV2()
        {
            var handler = new HttpClientHandler
            {
                // LAN-only target. Certificate errors are tolerated for loopback, for the
                // *.lovense.club alias (which resolves to a private LAN address) and for a private
                // LAN IP typed directly - Lovense Connect's certificate is issued for the alias, so
                // reaching the phone at https://<ip>:30010 (the #858 fallback for networks where the
                // alias cannot resolve) is always a name mismatch. Everything else, including any
                // routable address, is validated normally.
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) =>
                {
                    if (errors == System.Net.Security.SslPolicyErrors.None) return true;
                    var host = msg.RequestUri?.Host ?? "";
                    return IsLoopback(host) ||
                           host.EndsWith(".lovense.club", StringComparison.OrdinalIgnoreCase) ||
                           IsPrivateLanAddress(host);
                }
            };

            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            // Shown inside Lovense Remote's connected-app row - our branding surface. Setting it
            // as a default header guarantees it rides on EVERY request.
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                LovensePatterns.PlatformHeaderName, LovensePatterns.PlatformHeaderValue);

            _events = new LovenseToyEventsClient();
            _events.ToyEvent += OnToyEvent;
        }

        // ==================================================================
        // Connect / disconnect
        // ==================================================================

        public async Task<bool> ConnectAsync(CancellationToken ct)
        {
            if (_disposed) return false;

            _userDisconnected = false;
            lock (_lock) { _reconnectAttempt = 0; _nextReconnectUtc = DateTime.MinValue; _pollFailures = 0; }

            var configured = ResolveConfiguredUrl();
            var candidates = BuildCandidateBases(configured);
            if (candidates.Count == 0)
            {
                RaiseError("No Lovense address configured. Enter the IP shown in Lovense Remote > Game Mode.");
                return false;
            }

            string? winner = null;
            string body = "";
            foreach (var candidate in candidates)
            {
                if (ct.IsCancellationRequested) return false;
                try
                {
                    body = await PostToAsync(candidate, LovensePatterns.BuildGetToysPayload(),
                                             ProbeTimeout, ct).ConfigureAwait(false);
                    winner = candidate;
                    App.Logger?.Information("Lovense: reached Game Mode API at {Base}", candidate);
                    break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    // Information, not Debug (#858): "could not reach Lovense Remote" was the ONLY
                    // thing a failed connect left behind, with every per-candidate reason invisible
                    // at the default level - so nobody could tell a refused port from a TLS
                    // rejection from a DNS failure on the alias.
                    App.Logger?.Information("Lovense: {Base} did not answer ({Reason})", candidate, ex.Message);
                }
            }

            if (winner == null)
            {
                RaiseError($"Could not reach Lovense Remote at {configured ?? "(unset)"}. " +
                           "Check that Game Mode is on and the phone is on the same network.");
                lock (_lock) { _connected = false; }
                return false;
            }

            _baseUrl = winner;
            _connected = true;

            var count = 0;
            if (TryParseToysResponse(body, out var firstToys))
            {
                count = ApplyToys(firstToys, raiseIfChanged: false);
            }
            else
            {
                // Reachable, but GetToys answered with something we cannot read (error envelope /
                // foreign firmware). Never treat that as "no toys" - the 20 s poll retries.
                App.Logger?.Debug("Lovense: GetToys answered unintelligibly at connect: {Body}",
                                  Truncate(body));
            }
            _lastPollUtc = DateTime.UtcNow;
            RaiseDevicesChanged();

            if (count == 0)
            {
                // Reachable but nothing paired yet: stay connected, the 20 s poll will pick toys up.
                RaiseError("Lovense Remote answered but reports no toys. Connect a toy in the app.");
            }

            StartMaintenanceLoop();

            // Two-way input is best-effort and must never block or fail the connect.
            try { _events.Start(_baseUrl, ExtractHost(_baseUrl)); }
            catch (Exception ex) { App.Logger?.Debug("Lovense Toy Events start failed: {Reason}", ex.Message); }

            return true;
        }

        public async Task DisconnectAsync()
        {
            // Explicit user action: this is NOT a failure, so the maintenance loop must not
            // resurrect the connection behind their back.
            _userDisconnected = true;
            _connected = false;
            lock (_lock) { _nextReconnectUtc = DateTime.MinValue; _reconnectAttempt = 0; _pollFailures = 0; }

            try { await StopAllAsync().ConfigureAwait(false); } catch { }
            await StopMaintenanceLoopAsync().ConfigureAwait(false);
            try { await _events.StopAsync().ConfigureAwait(false); } catch { }

            List<DeviceState> states;
            lock (_lock)
            {
                states = _state.Values.ToList();
                _state.Clear();
                _toys.Clear();
                _baseUrl = null;
            }
            foreach (var s in states) s.Dispose();

            RaiseDevicesChanged();
        }

        // ==================================================================
        // Device registry
        // ==================================================================

        public IReadOnlyList<HapticDevice> Devices
        {
            get
            {
                lock (_lock)
                {
                    var list = new List<HapticDevice>(_toys.Count);
                    foreach (var toy in _toys.Values) list.Add(toy.ToDevice(Key));
                    return list;
                }
            }
        }

        /// <summary>Re-reads GetToys. Returns false when the call failed.</summary>
        public async Task<bool> RefreshDevicesAsync(CancellationToken ct = default)
        {
            if (!_connected || _baseUrl == null) return false;
            try
            {
                var body = await PostToAsync(_baseUrl, LovensePatterns.BuildGetToysPayload(),
                                             CommandTimeout, ct).ConfigureAwait(false);
                _lastPollUtc = DateTime.UtcNow;

                // An unreadable body (typically an HTTP-200 error envelope) is an IO FAILURE, not
                // an empty toy list: applying it would remove every toy and dispose its state
                // while the hardware keeps running on its timeSec:0 command.
                if (!TryParseToysResponse(body, out var fresh))
                {
                    NotePollFailure("GetToys returned an unreadable body: " + Truncate(body));
                    return false;
                }

                ApplyToys(fresh, raiseIfChanged: true);
                NotePollSuccess();
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                NotePollFailure("GetToys poll failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Merges a freshly parsed toy map into the registry. Returns the toy count.</summary>
        private int ApplyToys(Dictionary<string, LovenseToy> fresh, bool raiseIfChanged)
        {
            var changed = false;
            var removedStates = new List<DeviceState>();
            int count;

            lock (_lock)
            {
                foreach (var id in _toys.Keys.ToList())
                {
                    if (fresh.ContainsKey(id)) continue;
                    _toys.Remove(id);
                    if (_state.Remove(id, out var st)) removedStates.Add(st);
                    changed = true;
                }

                foreach (var kv in fresh)
                {
                    if (_toys.TryGetValue(kv.Key, out var existing))
                    {
                        if (existing.Merge(kv.Value)) changed = true;
                    }
                    else
                    {
                        _toys[kv.Key] = kv.Value;
                        _state[kv.Key] = new DeviceState();
                        changed = true;
                        App.Logger?.Information("Lovense toy: {Id} {Name} ({Caps}) battery={Battery}",
                            kv.Value.Id, kv.Value.DisplayName, kv.Value.CapabilitySummary, kv.Value.Battery);
                    }
                }

                count = _toys.Count;
            }

            foreach (var s in removedStates) s.Dispose();
            if (changed && raiseIfChanged) RaiseDevicesChanged();
            return count;
        }

        // ==================================================================
        // Output
        // ==================================================================

        public async Task SetOutputsAsync(string deviceId, IReadOnlyList<ActuatorOutput> outputs,
                                          CancellationToken ct)
        {
            if (_disposed || !_connected || _baseUrl == null) return;
            if (string.IsNullOrEmpty(deviceId) || outputs == null || outputs.Count == 0) return;

            LovenseToy? toy;
            DeviceState? st;
            lock (_lock)
            {
                _toys.TryGetValue(deviceId, out toy);
                _state.TryGetValue(deviceId, out st);
            }
            if (toy == null || st == null || !toy.Online) return;

            // Quantize through the ONE mapper, keyed by the exact Lovense action verb.
            var target = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var o in outputs)
            {
                var act = toy.Find(o.Type, o.Index);
                if (act == null || string.IsNullOrEmpty(act.Verb)) continue;
                target[act.Verb] = LovensePatterns.Quantize(o.Intensity, act.Steps);
            }
            if (target.Count == 0) return;

            lock (_lock)
            {
                // Unchanged -> suppress the send entirely. Only while nothing is in flight,
                // though: an in-flight send is about to overwrite the remembered levels, so a
                // target matching the PREVIOUS state would be dropped and never re-issued (the
                // mixer suppresses unchanged targets on its side too). Falling through instead
                // latches it as pending, which the completing send drains.
                if (st.Gate.CurrentCount > 0 && st.Satisfies(target)) return;
            }

            await SendLevelsAsync(toy, st, target, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Drives one device towards <paramref name="target"/>, with LATEST-PENDING semantics.
        /// Only one request per device may be on the wire (CommandTimeout is 4 s and the mixer
        /// ticks at 10 Hz, so overlaps are routine), but a superseded target is LATCHED rather
        /// than dropped: dropping one silently lost go-to-zero commands, and since every command
        /// goes out with <c>timeSec:0</c> (no server-side watchdog) the keep-alive then re-armed
        /// the stale non-zero level every 25 s forever. The completing send drains the latch
        /// (bounded, so it cannot hold the gate indefinitely); anything still latched is picked
        /// up by the next mixer tick or by the 1 Hz maintenance drain.
        /// </summary>
        private async Task SendLevelsAsync(LovenseToy toy, DeviceState st,
                                           SortedDictionary<string, int> target, CancellationToken ct,
                                           bool keepAlive = false)
        {
            if (!st.Gate.Wait(0))
            {
                // Newest wins - these are level SETS, so an older pending target is worthless.
                lock (_lock) st.Pending = target;
                return;
            }

            try
            {
                var current = target;
                var sends = 0;
                while (true)
                {
                    if (_disposed) return;
                    await SendOneAsync(toy, st, current, keepAlive, ct).ConfigureAwait(false);

                    if (_disposed || ct.IsCancellationRequested || !toy.Online) return;
                    if (++sends > MaxPendingDrainsPerSend) return;   // leave it latched; 1 Hz drain has it

                    SortedDictionary<string, int>? next;
                    lock (_lock)
                    {
                        next = st.Pending;
                        st.Pending = null;
                        if (next != null && st.Satisfies(next)) next = null;   // the send already delivered it
                    }
                    if (next == null) return;

                    current = next;
                    keepAlive = false;   // a real target supersedes a refresh
                }
            }
            finally
            {
                try { st.Gate.Release(); } catch { }
            }
        }

        /// <summary>
        /// Composes and posts ONE Function command. The action string always restates the toy's
        /// whole known level set ("Vibrate:5,Rotate:10,Position:40"), not just the caller's slice:
        /// <c>stopPrevious:1</c> cancels everything else the toy was doing, so a Position-only
        /// write used to kill the mixer's Thrusting/Vibrate and the next mixer tick used to kill
        /// the position move (~20 cut-outs/s on a Solace Pro playing a .funscript).
        /// </summary>
        private async Task SendOneAsync(LovenseToy toy, DeviceState st,
                                        SortedDictionary<string, int> target, bool keepAlive,
                                        CancellationToken ct)
        {
            var baseUrl = _baseUrl;
            if (baseUrl == null) return;

            SortedDictionary<string, int> levels;
            lock (_lock) levels = st.Compose(target, includeMotion: !keepAlive);

            var sb = new StringBuilder();
            var delivered = new HashSet<string>(StringComparer.Ordinal);
            List<string>? unsendable = null;

            foreach (var kv in levels)
            {
                var fragment = LovensePatterns.FormatActionFragment(kv.Key, kv.Value);
                if (fragment == null)
                {
                    (unsendable ??= new List<string>()).Add(kv.Key);
                    continue;
                }
                if (sb.Length > 0) sb.Append(',');
                sb.Append(fragment);
                delivered.Add(kv.Key);
            }

            if (unsendable != null) LogUnsendable(toy, st, unsendable);

            if (sb.Length == 0)
            {
                // Nothing this toy can actually be told (e.g. a Constrict-only target). Record it
                // as seen-but-undelivered: it dedupes so the 10 Hz mixer does not spin, but it
                // never counts as running and is never restated.
                lock (_lock) st.Remember(levels, delivered);
                return;
            }

            var payload = LovensePatterns.BuildFunctionPayload(toy.Id, sb.ToString(), 0, stopPrevious: true);

            try
            {
                await PostToAsync(baseUrl, payload, CommandTimeout, ct).ConfigureAwait(false);

                lock (_lock)
                {
                    st.Remember(levels, delivered);
                    st.LastSendUtc = DateTime.UtcNow;
                    st.ConsecutiveFailures = 0;
                }
                if (!toy.Online) MarkOnline(toy);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // shutting down / superseded
            }
            catch (Exception ex)
            {
                HandleIoFailure(toy, st, ex);
            }
        }

        /// <summary>One Debug line per toy+actuator verb for actuators the LAN API cannot express,
        /// so the silence is diagnosable without spamming the log at tick rate.</summary>
        private void LogUnsendable(LovenseToy toy, DeviceState st, List<string> verbs)
        {
            List<string>? fresh = null;
            lock (_lock)
            {
                foreach (var verb in verbs)
                    if (st.ShouldLogUnsendable(verb)) (fresh ??= new List<string>()).Add(verb);
            }
            if (fresh == null) return;

            App.Logger?.Debug("Lovense: {Toy} actuator(s) {Verbs} have no LAN action fragment - " +
                              "those channels stay silent.", toy.Id, string.Join(",", fresh));
        }

        /// <summary>Drives targets that were latched while a send was in flight and that the
        /// completing send did not get to (bounded drain, or the gate was still busy). Runs at
        /// 1 Hz so a go-to-zero can never be stranded even if the mixer stops calling.</summary>
        private async Task DrainPendingAsync(CancellationToken ct)
        {
            List<(LovenseToy Toy, DeviceState State, SortedDictionary<string, int> Levels)>? due = null;

            lock (_lock)
            {
                foreach (var kv in _state)
                {
                    var st = kv.Value;
                    if (st.Gate.CurrentCount == 0) continue;   // a send is in flight; it drains its own latch

                    var pending = st.Pending;
                    if (pending == null) continue;
                    st.Pending = null;

                    if (!_toys.TryGetValue(kv.Key, out var toy) || !toy.Online) continue;
                    if (st.Satisfies(pending)) continue;

                    (due ??= new()).Add((toy, st, pending));
                }
            }
            if (due == null) return;

            foreach (var item in due)
            {
                if (ct.IsCancellationRequested) return;
                await SendLevelsAsync(item.Toy, item.State, item.Levels, ct).ConfigureAwait(false);
            }
        }

        public async Task StopAllAsync()
        {
            var baseUrl = _baseUrl;
            if (baseUrl == null) return;

            List<string> ids;
            lock (_lock)
            {
                ids = _toys.Keys.ToList();
                // Clearing first means the next SetOutputs is never suppressed against a stale
                // "we already sent that" cache.
                foreach (var st in _state.Values) st.Clear();
            }

            try
            {
                if (ids.Count == 0)
                {
                    await SendStopAsync(baseUrl, null).ConfigureAwait(false);
                    return;
                }

                var tasks = new List<Task>(ids.Count);
                foreach (var id in ids) tasks.Add(SendStopAsync(baseUrl, id));
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Panic stop must never throw - it runs on shutdown paths.
                App.Logger?.Debug("Lovense StopAll partial failure: {Reason}", ex.Message);
            }
        }

        private async Task SendStopAsync(string baseUrl, string? toyId)
        {
            try
            {
                await PostToAsync(baseUrl, LovensePatterns.BuildStopPayload(toyId),
                                  StopTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Lovense stop failed for {Toy}: {Reason}", toyId ?? "(all)", ex.Message);
            }
        }

        public async Task<bool> PingAsync()
        {
            var baseUrl = _baseUrl;
            if (baseUrl == null) return false;
            try
            {
                var body = await PostToAsync(baseUrl, LovensePatterns.BuildGetToysPayload(),
                                             PingTimeout, CancellationToken.None).ConfigureAwait(false);
                _lastPollUtc = DateTime.UtcNow;

                // An error envelope answers with HTTP 200 but is NOT liveness: reporting true
                // here (and wiping the registry on the way) is what hid a broken Remote.
                if (!TryParseToysResponse(body, out var fresh))
                {
                    NotePollFailure("ping got an unreadable body: " + Truncate(body));
                    return false;
                }

                // The cheapest liveness check already carries the registry, so use it.
                ApplyToys(fresh, raiseIfChanged: true);
                NotePollSuccess();
                return true;
            }
            catch (Exception ex)
            {
                NotePollFailure("ping failed: " + ex.Message);
                return false;
            }
        }

        // ==================================================================
        // Patterns / presets (used by the pattern editor and Phase F FunScript)
        // ==================================================================

        /// <summary>Plays a built-in Lovense preset ("pulse" | "wave" | "fireworks" | "earthquake").
        /// <paramref name="deviceId"/> null broadcasts. timeSec 0 = until stopped.</summary>
        public Task<bool> SendPresetAsync(string? deviceId, string preset, int timeSec)
        {
            var name = (preset ?? "").Trim().ToLowerInvariant();
            if (!LovensePatterns.Presets.Contains(name))
            {
                RaiseError($"Unknown Lovense preset '{preset}'.");
                return Task.FromResult(false);
            }
            return SendCommandAsync(LovensePatterns.BuildPresetPayload(ResolveToyId(deviceId), name, timeSec));
        }

        /// <summary>
        /// v1 pattern. <paramref name="strengths"/> are 0..1 (quantized to 0-20 by the single
        /// mapper), capped at 50 values; <paramref name="intervalMs"/> is floored at 101 ms per the
        /// API rules. Features default to the toy's own actuator set.
        /// </summary>
        public Task<bool> SendPatternV1Async(string? deviceId, IReadOnlyList<double> strengths,
                                             int intervalMs, int timeSec)
        {
            if (strengths == null || strengths.Count == 0)
            {
                RaiseError("Lovense pattern needs at least one strength value.");
                return Task.FromResult(false);
            }

            var toyId = ResolveToyId(deviceId);
            var codes = new List<string>();
            lock (_lock)
            {
                if (toyId != null && _toys.TryGetValue(toyId, out var toy))
                {
                    foreach (var a in toy.Actuators)
                    {
                        var c = LovensePatterns.PatternFeatureCode(a.Type);
                        if (c != null && !codes.Contains(c)) codes.Add(c);
                    }
                }
            }
            if (codes.Count == 0) codes.Add("v");

            return SendCommandAsync(
                LovensePatterns.BuildPatternV1Payload(toyId, codes, strengths, intervalMs, timeSec));
        }

        /// <summary>PatternV2 keyframe upload (ts ms 0..7,200,000; pos 0..100).</summary>
        public Task<bool> SendPatternV2SetupAsync(IReadOnlyList<(int TimestampMs, int Position)> points)
        {
            if (points == null || points.Count == 0) return Task.FromResult(false);
            return SendCommandAsync(LovensePatterns.BuildPatternV2SetupPayload(points));
        }

        /// <summary>Starts playback of the pattern uploaded by <see cref="SendPatternV2SetupAsync"/>.</summary>
        public Task<bool> SendPatternV2PlayAsync(string? deviceId, int startTimeMs, int offsetTimeMs)
            => SendCommandAsync(LovensePatterns.BuildPatternV2PlayPayload(ResolveToyId(deviceId),
                                                                         startTimeMs, offsetTimeMs));

        /// <summary>Uploads and immediately plays a short keyframe run.</summary>
        public Task<bool> SendPatternV2InitPlayAsync(IReadOnlyList<(int TimestampMs, int Position)> points,
                                                     bool stopPrevious = true)
        {
            if (points == null || points.Count == 0) return Task.FromResult(false);
            return SendCommandAsync(LovensePatterns.BuildPatternV2InitPlayPayload(points, stopPrevious));
        }

        public Task<bool> SendPatternV2StopAsync(string? deviceId)
            => SendCommandAsync(LovensePatterns.BuildPatternV2StopPayload(ResolveToyId(deviceId)));

        public Task<bool> SendPatternV2SyncTimeAsync()
            => SendCommandAsync(LovensePatterns.BuildPatternV2SyncTimePayload());

        private string? ResolveToyId(string? deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return null;   // null = broadcast
            lock (_lock) return _toys.ContainsKey(deviceId) ? deviceId : null;
        }

        private async Task<bool> SendCommandAsync(string payload)
        {
            var baseUrl = _baseUrl;
            if (baseUrl == null || !_connected) return false;
            try
            {
                await PostToAsync(baseUrl, payload, CommandTimeout, CancellationToken.None).ConfigureAwait(false);
                // A pattern/preset takes the toy somewhere our level cache does not know about;
                // forget the cache so the next SetOutputs is not suppressed.
                lock (_lock) { foreach (var st in _state.Values) st.Clear(); }
                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Lovense command failed: {ex.Message}");
                return false;
            }
        }

        // ==================================================================
        // Maintenance loop: 20 s toy poll + latched-target drain + 25 s keep-alive refresh,
        // and - while disconnected by FAILURE (never by the user) - the reconnect ladder.
        // ==================================================================

        private void StartMaintenanceLoop()
        {
            lock (_lock)
            {
                if (_loopTask is { IsCompleted: false }) return;
                _loopCts = new CancellationTokenSource();
                var ct = _loopCts.Token;
                _loopTask = Task.Run(() => MaintenanceLoopAsync(ct), CancellationToken.None);
            }
        }

        private async Task StopMaintenanceLoopAsync()
        {
            CancellationTokenSource? cts;
            Task? task;
            lock (_lock) { cts = _loopCts; task = _loopTask; _loopCts = null; _loopTask = null; }

            try { cts?.Cancel(); } catch { }
            if (task != null)
            {
                try { await Task.WhenAny(task, Task.Delay(2000)).ConfigureAwait(false); } catch { }
            }
            try { cts?.Dispose(); } catch { }
        }

        private async Task MaintenanceLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_disposed)
            {
                try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }

                if (ct.IsCancellationRequested) continue;

                if (!_connected)
                {
                    // Failed out (a WiFi blip / phone screen-off used to kill Lovense for the rest
                    // of the session). Retry on a backoff - but never after a user Disconnect.
                    try { await TryAutoReconnectAsync(ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Lovense reconnect attempt failed: {Reason}", ex.Message);
                    }
                    continue;
                }

                if (_baseUrl == null) continue;

                try
                {
                    if (DateTime.UtcNow - _lastPollUtc >= ToyPollInterval)
                        await RefreshDevicesAsync(ct).ConfigureAwait(false);

                    // Anything latched while a send was in flight must land, zeros above all.
                    await DrainPendingAsync(ct).ConfigureAwait(false);

                    await RunKeepAliveAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Lovense maintenance tick failed: {Reason}", ex.Message);
                }
            }
        }

        /// <summary>Re-sends the current non-zero state every 25 s. <c>timeSec:0</c> means the toy
        /// keeps running on its own, but a refresh survives a Remote restart / brief network drop
        /// without the user noticing a dead toy. Position/Stroke are deliberately EXCLUDED: they
        /// are motion, not a level, and re-asserting a 25 s old position would jerk a stroker.</summary>
        private async Task RunKeepAliveAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            List<(LovenseToy Toy, DeviceState State, SortedDictionary<string, int> Levels)> due = new();

            lock (_lock)
            {
                foreach (var kv in _state)
                {
                    if (!_toys.TryGetValue(kv.Key, out var toy) || !toy.Online) continue;
                    var st = kv.Value;
                    if (!st.HasNonZero) continue;                       // nothing running -> nothing to refresh
                    if (now - st.LastSendUtc < KeepAliveInterval) continue;
                    var levels = st.KeepAliveSnapshot();
                    if (levels.Count == 0) continue;
                    due.Add((toy, st, levels));
                }
            }

            foreach (var item in due)
            {
                if (ct.IsCancellationRequested) return;
                await SendLevelsAsync(item.Toy, item.State, item.Levels, ct, keepAlive: true)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Periodic recovery probe while disconnected-due-to-failures. The base URL that answered
        /// this session is still known, so a transient outage (phone screen-off, WiFi roam) heals
        /// itself instead of requiring a manual reconnect. Explicit user disconnects are excluded.
        /// </summary>
        private async Task TryAutoReconnectAsync(CancellationToken ct)
        {
            if (_disposed || _userDisconnected) return;

            var baseUrl = _baseUrl;
            if (baseUrl == null) return;

            DateTime due;
            lock (_lock) due = _nextReconnectUtc;
            if (due == DateTime.MinValue || DateTime.UtcNow < due) return;

            try
            {
                var body = await PostToAsync(baseUrl, LovensePatterns.BuildGetToysPayload(),
                                             PingTimeout, ct).ConfigureAwait(false);

                if (TryParseToysResponse(body, out var fresh))
                {
                    lock (_lock)
                    {
                        _reconnectAttempt = 0;
                        _nextReconnectUtc = DateTime.MinValue;
                        _pollFailures = 0;
                        // The toys' levels are unknown after an outage - forget them so the next
                        // mixer tick is not suppressed against a stale "we already sent that".
                        foreach (var st in _state.Values) { st.Clear(); st.ConsecutiveFailures = 0; }
                    }
                    _connected = true;

                    ApplyToys(fresh, raiseIfChanged: false);
                    _lastPollUtc = DateTime.UtcNow;

                    App.Logger?.Information("Lovense: reconnected to {Base} after an outage.", baseUrl);
                    RaiseDevicesChanged();   // the manager recomputes IsConnected from this

                    try { _events.Start(baseUrl, ExtractHost(baseUrl)); }
                    catch (Exception ex) { App.Logger?.Debug("Lovense Toy Events restart failed: {Reason}", ex.Message); }
                    return;
                }

                App.Logger?.Debug("Lovense: reconnect probe answered unintelligibly: {Body}", Truncate(body));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                App.Logger?.Debug("Lovense: reconnect probe to {Base} failed ({Reason})", baseUrl, ex.Message);
            }

            lock (_lock)
            {
                var idx = Math.Min(_reconnectAttempt, ReconnectBackoffSeconds.Length - 1);
                _nextReconnectUtc = DateTime.UtcNow.AddSeconds(ReconnectBackoffSeconds[idx]);
                if (_reconnectAttempt < ReconnectBackoffSeconds.Length) _reconnectAttempt++;
            }
        }

        // ==================================================================
        // HTTP
        // ==================================================================

        private async Task<string> PostToAsync(string baseUrl, string json, TimeSpan timeout,
                                               CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/command")
            {
                Content = content
            };

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                                        .ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode} from {baseUrl}");

            return body;
        }

        // ==================================================================
        // Address resolution
        // ==================================================================

        /// <summary>Reads the configured address: the override wins, else the haptics settings.</summary>
        private string? ResolveConfiguredUrl()
        {
            if (!string.IsNullOrWhiteSpace(ConfiguredUrlOverride)) return ConfiguredUrlOverride;
            var url = App.Settings?.Current?.Haptics?.LovenseUrl;
            return string.IsNullOrWhiteSpace(url) ? null : url;
        }

        /// <summary>
        /// Probe order, per the LAN cheat-sheet: the documented HTTPS alias first, then plain HTTP
        /// (routers with DNS-rebinding protection break the alias), then the Game Mode port on the
        /// address itself, then whatever authority the user literally typed if it adds anything new.
        ///
        /// <para>#858: the phone's own <c>&lt;ip&gt;:30010</c> was never probed at all - only the
        /// lovense.club alias carried that port - so a user whose network breaks the alias (DNS
        /// rebinding protection, a DNS-filtering resolver, no internet at all) could never connect
        /// no matter what they typed. Both schemes are tried there: Lovense Connect serves HTTPS on
        /// 30010, some builds/relays answer plain HTTP.</para>
        /// </summary>
        internal static IReadOnlyList<string> BuildCandidateBases(string? configured)
        {
            var list = new List<string>();
            void Add(string? u)
            {
                if (string.IsNullOrWhiteSpace(u)) return;
                if (!list.Contains(u, StringComparer.OrdinalIgnoreCase)) list.Add(u);
            }

            var host = ExtractHost(configured);
            if (string.IsNullOrWhiteSpace(host))
            {
                // Nothing configured: the only address we can guess is a PC-side Lovense Connect.
                Add("http://127.0.0.1:20010");
                return list;
            }

            var dotted = DottedFromLovenseClubHost(host) ?? host;

            if (IsLoopback(dotted))
            {
                Add($"http://{dotted}:20010");
                Add($"https://{dotted}:30010");
            }
            else
            {
                Add($"https://{dotted.Replace('.', '-')}.lovense.club:30010");
                Add($"http://{dotted}:20010");
                Add($"https://{dotted}:30010");
                Add($"http://{dotted}:30010");
            }

            Add(NormalizeAuthority(configured));
            return list;
        }

        /// <summary>Accepts "192.168.1.42", "192.168.1.42:30010" or a full URL.</summary>
        internal static string? ExtractHost(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim();
            if (!s.Contains("://")) s = "http://" + s;
            return Uri.TryCreate(s, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host)
                ? u.Host
                : null;
        }

        /// <summary>"192-168-1-42.lovense.club" -> "192.168.1.42" (null when it is not that alias).</summary>
        internal static string? DottedFromLovenseClubHost(string? host)
        {
            const string suffix = ".lovense.club";
            if (string.IsNullOrWhiteSpace(host)) return null;
            if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;

            var label = host.Substring(0, host.Length - suffix.Length).Replace('-', '.');
            return IPAddress.TryParse(label, out _) ? label : null;
        }

        internal static bool IsLoopback(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (host == "::1") return true;
            return host.StartsWith("127.", StringComparison.Ordinal);
        }

        /// <summary>True for an IPv4 literal on a private / link-local range - a device on the same
        /// LAN, which is the only thing this provider ever talks to over a raw IP.</summary>
        internal static bool IsPrivateLanAddress(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            if (!IPAddress.TryParse(host, out var ip)) return false;
            if (IPAddress.IsLoopback(ip)) return true;
            var b = ip.GetAddressBytes();
            if (b.Length != 4) return false;
            return b[0] == 10                                   // 10.0.0.0/8
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16
                || (b[0] == 169 && b[1] == 254);                // 169.254.0.0/16 link-local
        }

        /// <summary>
        /// The scheme+host+port to probe for whatever the user literally typed, so their own input is
        /// never thrown away. A bare host used to return null (#858) - "192.168.1.42" produced no
        /// candidate of its own at all - and a typed scheme with no port produced a port 80/443 probe,
        /// which on a LAN is a router admin page, not Lovense. Both now resolve to the Game Mode port
        /// for the scheme: 20010 for http, 30010 for https.
        /// </summary>
        private static string? NormalizeAuthority(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim();
            var hadScheme = s.Contains("://");
            if (!hadScheme) s = "http://" + s;
            if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || string.IsNullOrEmpty(u.Host)) return null;
            if (!u.IsDefaultPort) return u.GetLeftPart(UriPartial.Authority);

            // No port of their own: fill in the Game Mode port that goes with the scheme. The
            // duplicate this often produces is dropped by the caller's dedupe.
            var port = string.Equals(u.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 30010 : 20010;
            return $"{u.Scheme}://{u.Host}:{port}";
        }

        // ==================================================================
        // GetToys parsing (dual shape)
        // ==================================================================

        /// <summary>
        /// Parses a GetToys response. <c>data.toys</c> arrives as an ESCAPED JSON STRING on some
        /// firmware/app combinations and as a plain object on others (and a few builds hand back an
        /// array) - all three are accepted, plus the bare toy map that Lovense Connect returns.
        ///
        /// <para>Returns FALSE when the body is not a toy container at all: unparseable, or an
        /// error envelope such as <c>{"code":400,"type":"error"}</c> (which Remote serves with
        /// HTTP 200). That distinction matters: an empty-but-VALID container legitimately removes
        /// toys, whereas returning "no toys" for an error used to drop the whole registry - and
        /// with it every DeviceState - while the hardware kept running on its timeSec:0 command.
        /// A valid container that happens to be empty still returns true.</para>
        /// </summary>
        internal static bool TryParseToysResponse(string? body, out Dictionary<string, LovenseToy> toysOut)
        {
            var result = new Dictionary<string, LovenseToy>(StringComparer.OrdinalIgnoreCase);
            toysOut = result;
            if (string.IsNullOrWhiteSpace(body)) return false;

            JsonDocument? nested = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;

                // Error envelope: Lovense answers HTTP 200 with a non-200 "code" (400 invalid
                // command, 401 toy not found, 403 unsupported, 500/506 server) and/or type "error".
                if (IsErrorEnvelope(root)) return false;

                JsonElement toys = default;
                var have = false;

                if (root.TryGetProperty("data", out var data))
                {
                    if (data.ValueKind == JsonValueKind.String)
                    {
                        // Whole data blob escaped.
                        var s = data.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            nested = JsonDocument.Parse(s!);
                            toys = nested.RootElement.ValueKind == JsonValueKind.Object &&
                                   nested.RootElement.TryGetProperty("toys", out var t0)
                                ? t0
                                : nested.RootElement;
                            have = true;
                        }
                    }
                    else if (data.ValueKind == JsonValueKind.Object &&
                             data.TryGetProperty("toys", out var t))
                    {
                        if (t.ValueKind == JsonValueKind.String)
                        {
                            var s = t.GetString();
                            if (!string.IsNullOrWhiteSpace(s))
                            {
                                nested = JsonDocument.Parse(s!);
                                toys = nested.RootElement;
                                have = true;
                            }
                        }
                        else
                        {
                            toys = t;
                            have = true;
                        }
                    }
                }

                if (!have && root.TryGetProperty("toys", out var rt))
                {
                    if (rt.ValueKind == JsonValueKind.String)
                    {
                        var s = rt.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) { nested = JsonDocument.Parse(s!); toys = nested.RootElement; have = true; }
                    }
                    else { toys = rt; have = true; }
                }

                // Lovense Connect (GET) style: the root IS the toy map.
                if (!have && !root.TryGetProperty("code", out _) && !root.TryGetProperty("data", out _))
                {
                    toys = root;
                    have = true;
                }

                if (!have) return false;   // structurally not a toy container - do NOT remove toys

                CollectToys(toys, result);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Lovense GetToys parse failed: {Reason}", ex.Message);
                return false;
            }
            finally
            {
                nested?.Dispose();
            }
        }

        /// <summary>Recognises the HTTP-200 error envelope Remote returns for a rejected command.</summary>
        private static bool IsErrorEnvelope(JsonElement root)
        {
            var type = ReadString(root, "type");
            if (type != null && type.Equals("error", StringComparison.OrdinalIgnoreCase)) return true;

            var code = ReadInt(root, "code");
            return code.HasValue && code.Value != 200;
        }

        private static void CollectToys(JsonElement toys, Dictionary<string, LovenseToy> into)
        {
            if (toys.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in toys.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                    var toy = ParseToy(prop.Name, prop.Value);
                    if (toy != null) into[toy.Id] = toy;
                }
            }
            else if (toys.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in toys.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var toy = ParseToy(null, el);
                    if (toy != null) into[toy.Id] = toy;
                }
            }
        }

        private static LovenseToy? ParseToy(string? keyId, JsonElement e)
        {
            var id = ReadString(e, "id") ?? keyId;
            if (string.IsNullOrWhiteSpace(id)) return null;

            var name = ReadString(e, "name") ?? "";
            var nick = ReadString(e, "nickName") ?? ReadString(e, "nickname") ?? "";
            var status = ReadString(e, "status");

            int? battery = null;
            var b = ReadInt(e, "battery");
            if (b.HasValue && b.Value >= 0) battery = Math.Clamp(b.Value, 0, 100);

            var names = ReadStringList(e, "shortFunctionNames");
            if (names.Count == 0) names = ReadStringList(e, "fullFunctionNames");
            if (names.Count == 0) names = FallbackFunctionNames(name);

            return new LovenseToy
            {
                Id = id!,
                Name = name,
                NickName = nick,
                Battery = battery,
                Online = status == null || status == "1" || status.Equals("true", StringComparison.OrdinalIgnoreCase),
                Actuators = BuildActuators(names)
            };
        }

        internal static List<LovenseActuator> BuildActuators(IEnumerable<string> functionNames)
        {
            var list = new List<LovenseActuator>();
            var next = new Dictionary<ActuatorType, int>();

            foreach (var raw in functionNames)
            {
                if (!LovensePatterns.TryParseFunctionName(raw, out var type, out var motor, out var verb))
                    continue;

                var index = motor > 0 ? motor - 1 : (next.TryGetValue(type, out var c) ? c : 0);
                if (list.Any(a => a.Type == type && a.Index == index)) continue;

                next[type] = index + 1;
                list.Add(new LovenseActuator
                {
                    Type = type,
                    Index = index,
                    Steps = LovensePatterns.StepsFor(type),
                    Verb = verb
                });
            }

            if (list.Count == 0)
            {
                list.Add(new LovenseActuator
                {
                    Type = ActuatorType.Vibrate,
                    Index = 0,
                    Steps = LovensePatterns.StepsFor(ActuatorType.Vibrate),
                    Verb = "Vibrate"
                });
            }
            return list;
        }

        /// <summary>
        /// Last-ditch capability guess for firmware that omits both function-name arrays. Only the
        /// well-known shapes are listed; anything else becomes a single vibrate motor, which is the
        /// safe default (a wrong extra verb would just be rejected by Remote).
        /// </summary>
        private static List<string> FallbackFunctionNames(string? model)
        {
            var m = (model ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            return m switch
            {
                "nora" => new List<string> { "v", "r" },
                "max" or "max2" => new List<string> { "v", "p" },
                "edge" or "edge2" => new List<string> { "v1", "v2" },
                "lapis" => new List<string> { "v1", "v2", "v3" },
                "solace" => new List<string> { "t", "d" },
                "solacepro" => new List<string> { "t", "pos" },
                "gravity" => new List<string> { "v", "t" },
                "flexer" => new List<string> { "v", "f" },
                "gemini" => new List<string> { "v1", "v2" },
                "tenera" or "tenera2" => new List<string> { "s" },
                "vulse" => new List<string> { "v", "t" },
                "" => new List<string>(),
                _ => new List<string> { "v" }
            };
        }

        private static string? ReadString(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v)) return null;
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        private static int? ReadInt(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
            if (v.ValueKind == JsonValueKind.String &&
                int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                return s;
            return null;
        }

        private static List<string> ReadStringList(JsonElement e, string name)
        {
            var list = new List<string>();
            if (!e.TryGetProperty(name, out var v)) return list;

            if (v.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in v.EnumerateArray())
                {
                    var s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
                }
            }
            else if (v.ValueKind == JsonValueKind.String)
            {
                foreach (var part in (v.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (!string.IsNullOrWhiteSpace(part)) list.Add(part.Trim());
            }
            return list;
        }

        // ==================================================================
        // Failure handling / events
        // ==================================================================

        private void HandleIoFailure(LovenseToy toy, DeviceState st, Exception ex)
        {
            bool lost;
            bool allGone = false;

            lock (_lock)
            {
                st.ConsecutiveFailures++;
                lost = st.ConsecutiveFailures >= MaxConsecutiveFailures && toy.Online;
                if (lost)
                {
                    toy.Online = false;
                    st.Clear();
                    allGone = !_toys.Values.Any(t => t.Online);
                }
            }

            if (!lost)
            {
                App.Logger?.Debug("Lovense send failed for {Toy}: {Reason}", toy.Id, ex.Message);
                return;
            }

            App.Logger?.Warning(ex, "Lovense: lost contact with toy {Toy}", toy.Id);
            RaiseError($"Lost contact with {toy.DisplayName}: {ex.Message}");
            RaiseDevicesChanged();

            if (allGone)
            {
                _connected = false;
                lock (_lock) ArmReconnectNoLock();
                RaiseError("Lovense Remote is no longer reachable. Retrying in the background.");
            }
        }

        /// <summary>Arms the recovery ladder. Caller holds <c>_lock</c>.</summary>
        private void ArmReconnectNoLock()
        {
            if (_userDisconnected || _baseUrl == null) return;
            _reconnectAttempt = 0;
            _nextReconnectUtc = DateTime.UtcNow.AddSeconds(ReconnectBackoffSeconds[0]);
        }

        private void NotePollSuccess()
        {
            lock (_lock) _pollFailures = 0;
        }

        /// <summary>Registry-poll / ping failure (including an HTTP-200 error envelope). After
        /// <see cref="MaxConsecutiveFailures"/> in a row the connection is marked down and the
        /// recovery ladder takes over - it is never silently ignored, and it never wipes toys.</summary>
        private void NotePollFailure(string reason)
        {
            lock (_lock)
            {
                if (!_connected) return;
                if (++_pollFailures < MaxConsecutiveFailures)
                {
                    App.Logger?.Debug("Lovense: {Reason}", reason);
                    return;
                }
                _pollFailures = 0;
                _connected = false;
                ArmReconnectNoLock();
            }

            App.Logger?.Warning("Lovense: {Reason} - marking the connection down, retrying in the background.",
                                reason);
            RaiseError("Lovense Remote stopped answering. Retrying in the background.");
            RaiseDevicesChanged();
        }

        /// <summary>Keeps a diagnostic body short enough for the log.</summary>
        private static string Truncate(string? body)
        {
            if (string.IsNullOrEmpty(body)) return "(empty)";
            var s = body.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= 200 ? s : s.Substring(0, 200) + "...";
        }

        private void MarkOnline(LovenseToy toy)
        {
            lock (_lock) { toy.Online = true; }
            _connected = true;
            RaiseDevicesChanged();
        }

        private void OnToyEvent(object? sender, HapticToyEvent e)
        {
            // Battery updates arriving over the websocket keep the registry fresh between polls.
            if (e.Kind == ToyEventKind.BatteryChanged)
            {
                var id = e.DeviceKey.StartsWith("lovense:", StringComparison.Ordinal)
                    ? e.DeviceKey.Substring("lovense:".Length)
                    : e.DeviceKey;

                var changed = false;
                lock (_lock)
                {
                    if (_toys.TryGetValue(id, out var toy))
                    {
                        var pct = (int)Math.Clamp(e.Value, 0, 100);
                        if (toy.Battery != pct) { toy.Battery = pct; changed = true; }
                    }
                }
                if (changed) RaiseDevicesChanged();
            }

            try { ToyEvent?.Invoke(this, e); }
            catch (Exception ex) { App.Logger?.Debug("Lovense ToyEvent handler threw: {Reason}", ex.Message); }
        }

        private void RaiseDevicesChanged()
        {
            try { DevicesChanged?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { App.Logger?.Debug("Lovense DevicesChanged handler threw: {Reason}", ex.Message); }
        }

        private void RaiseError(string message)
        {
            App.Logger?.Warning("Lovense: {Message}", message);
            try { Error?.Invoke(this, message); }
            catch (Exception ex) { App.Logger?.Debug("Lovense Error handler threw: {Reason}", ex.Message); }
        }

        // ==================================================================
        // Dispose
        // ==================================================================

        /// <summary>
        /// Callers should await <see cref="StopAllAsync"/> / <see cref="DisconnectAsync"/> first -
        /// Dispose deliberately does NOT block on network IO (the old HapticService's
        /// <c>.Wait(1000)</c> on shutdown is exactly the trap being removed).
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connected = false;

            CancellationTokenSource? cts;
            List<DeviceState> states;
            lock (_lock)
            {
                cts = _loopCts;
                _loopCts = null;
                _loopTask = null;
                states = _state.Values.ToList();
                _state.Clear();
                _toys.Clear();
            }

            try { cts?.Cancel(); } catch { }
            try { cts?.Dispose(); } catch { }
            foreach (var s in states) s.Dispose();

            try { _events.ToyEvent -= OnToyEvent; } catch { }
            try { _events.Dispose(); } catch { }
            try { _http.Dispose(); } catch { }
        }

        // ==================================================================
        // Internal model
        // ==================================================================

        internal sealed class LovenseActuator
        {
            public ActuatorType Type;
            public int Index;
            public int Steps = 20;
            /// <summary>Exact Lovense action word, including the motor suffix on multi-motor toys
            /// ("Vibrate", "Vibrate1", "Thrusting", "Position").</summary>
            public string Verb = "";
        }

        internal sealed class LovenseToy
        {
            public string Id = "";
            public string Name = "";
            public string NickName = "";
            public int? Battery;
            public bool Online = true;
            public List<LovenseActuator> Actuators = new();

            public string DisplayName => string.IsNullOrWhiteSpace(NickName)
                ? (string.IsNullOrWhiteSpace(Name) ? Id : Name)
                : NickName;

            public string CapabilitySummary => string.Join("+", Actuators.Select(a => a.Verb));

            public LovenseActuator? Find(ActuatorType type, int index)
            {
                foreach (var a in Actuators)
                    if (a.Type == type && a.Index == index) return a;
                return null;
            }

            /// <summary>Copies mutable fields from a freshly polled toy. Returns true when
            /// anything a consumer can see actually changed.</summary>
            public bool Merge(LovenseToy fresh)
            {
                var changed = false;
                if (Battery != fresh.Battery) { Battery = fresh.Battery; changed = true; }
                if (Online != fresh.Online) { Online = fresh.Online; changed = true; }
                if (!string.Equals(NickName, fresh.NickName, StringComparison.Ordinal))
                {
                    NickName = fresh.NickName; changed = true;
                }
                if (!string.Equals(Name, fresh.Name, StringComparison.Ordinal))
                {
                    Name = fresh.Name; changed = true;
                }
                if (fresh.Actuators.Count != Actuators.Count ||
                    !fresh.Actuators.All(f => Actuators.Any(a => a.Type == f.Type && a.Index == f.Index)))
                {
                    Actuators = fresh.Actuators;
                    changed = true;
                }
                return changed;
            }

            public HapticDevice ToDevice(string providerKey)
            {
                var d = new HapticDevice
                {
                    Id = Id,
                    ProviderKey = providerKey,
                    Name = string.IsNullOrWhiteSpace(Name) ? Id : Name,
                    Nickname = NickName,
                    BatteryPercent = Battery,
                    IsConnected = Online
                };
                foreach (var a in Actuators)
                    d.Actuators.Add(new HapticActuator { Type = a.Type, Index = a.Index, Steps = a.Steps });
                return d;
            }
        }

        /// <summary>Per-device send state: the last quantized level PER ACTUATOR VERB (for
        /// unchanged-suppression and for composing a complete action string), the newest target
        /// that arrived while a send was in flight, the keep-alive clock, the failure counter, and
        /// a 1-slot gate so only one request per device is ever on the wire.
        ///
        /// <para>The map is merged per verb, never replaced: the mixer and the FunScript position
        /// stream write DISJOINT verb sets for the same toy, and a replace made each one erase the
        /// other's remembered levels (which also poisoned the keep-alive).</para></summary>
        private sealed class DeviceState : IDisposable
        {
            private readonly Dictionary<string, LevelEntry> _last = new(StringComparer.Ordinal);
            private readonly HashSet<string> _loggedUnsendable = new(StringComparer.Ordinal);

            public readonly SemaphoreSlim Gate = new(1, 1);
            public DateTime LastSendUtc = DateTime.MinValue;
            public int ConsecutiveFailures;

            /// <summary>Newest target latched while the gate was busy - only the latest matters
            /// (these are level SETS), but it must never be dropped.</summary>
            public SortedDictionary<string, int>? Pending;

            /// <summary>Position/Stroke shape a motion rather than hold a level: they are never
            /// re-asserted by the keep-alive (a stale re-assert would jerk a stroker).</summary>
            public static bool IsMotionVerb(string verb)
                => verb.StartsWith("Position", StringComparison.Ordinal) ||
                   verb.StartsWith("Stroke", StringComparison.Ordinal);

            public bool HasNonZero
            {
                get
                {
                    foreach (var kv in _last)
                        if (kv.Value.Delivered && kv.Value.Step > 0 && !IsMotionVerb(kv.Key)) return true;
                    return false;
                }
            }

            /// <summary>True when every verb in <paramref name="target"/> is already at that level.
            /// Deliberately a SUBSET test, not an equality test: a Position-only write must dedupe
            /// against the position alone and not against the whole remembered map.</summary>
            public bool Satisfies(SortedDictionary<string, int> target)
            {
                foreach (var kv in target)
                    if (!_last.TryGetValue(kv.Key, out var v) || v.Step != kv.Value) return false;
                return true;
            }

            /// <summary>Builds the level set that one Function command must carry: the fresh
            /// target on top of every level already known to be running. Because the LAN API's
            /// <c>stopPrevious:1</c> cancels whatever the toy was doing, a partial command would
            /// kill the other stream (mixer vibe vs. FunScript position) - so every command
            /// restates everything.</summary>
            public SortedDictionary<string, int> Compose(SortedDictionary<string, int> target,
                                                         bool includeMotion)
            {
                var merged = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var kv in _last)
                {
                    if (!kv.Value.Delivered) continue;              // never restate what the wire refused
                    if (!includeMotion && IsMotionVerb(kv.Key)) continue;
                    merged[kv.Key] = kv.Value.Step;
                }
                foreach (var kv in target) merged[kv.Key] = kv.Value;   // the fresh target always wins
                return merged;
            }

            /// <summary>Records what a command actually carried. Verbs that produced no action
            /// fragment (Constrict, a zero Stroke range) are remembered as NOT delivered: they
            /// still dedupe, so the 10 Hz mixer does not spin retrying them, but they never count
            /// as running and are never restated.</summary>
            public void Remember(SortedDictionary<string, int> levels, HashSet<string> deliveredVerbs)
            {
                foreach (var kv in levels)
                    _last[kv.Key] = new LevelEntry(kv.Value, deliveredVerbs.Contains(kv.Key));
            }

            /// <summary>Levels the keep-alive may safely restate (delivered, non-motion).</summary>
            public SortedDictionary<string, int> KeepAliveSnapshot()
            {
                var copy = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var kv in _last)
                {
                    if (!kv.Value.Delivered || IsMotionVerb(kv.Key)) continue;
                    copy[kv.Key] = kv.Value.Step;
                }
                return copy;
            }

            /// <summary>One Debug line per toy+actuator verb, so a silent actuator is diagnosable
            /// without spamming the log at tick rate.</summary>
            public bool ShouldLogUnsendable(string verb) => _loggedUnsendable.Add(verb);

            public void Clear()
            {
                _last.Clear();
                Pending = null;
                LastSendUtc = DateTime.MinValue;
            }

            public void Dispose()
            {
                try { Gate.Dispose(); } catch { }
            }

            private readonly struct LevelEntry
            {
                public readonly int Step;
                /// <summary>False when the verb has no LAN action fragment (see
                /// <see cref="LovensePatterns.FormatActionFragment"/>).</summary>
                public readonly bool Delivered;
                public LevelEntry(int step, bool delivered) { Step = step; Delivered = delivered; }
            }
        }
    }
}
