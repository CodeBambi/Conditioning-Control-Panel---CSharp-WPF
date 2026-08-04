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
                // LAN-only target. Certificate errors are tolerated for loopback and for the
                // *.lovense.club alias (which resolves to a private LAN address); everything
                // else is validated normally.
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) =>
                {
                    if (errors == System.Net.Security.SslPolicyErrors.None) return true;
                    var host = msg.RequestUri?.Host ?? "";
                    return IsLoopback(host) ||
                           host.EndsWith(".lovense.club", StringComparison.OrdinalIgnoreCase);
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
                    App.Logger?.Debug("Lovense: {Base} did not answer ({Reason})", candidate, ex.Message);
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

            var count = ApplyToys(ParseToysResponse(body), raiseIfChanged: false);
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
            _connected = false;

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
                ApplyToys(ParseToysResponse(body), raiseIfChanged: true);
                _lastPollUtc = DateTime.UtcNow;
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                App.Logger?.Debug("Lovense GetToys poll failed: {Reason}", ex.Message);
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
                if (st.Matches(target)) return;   // unchanged -> suppress the send entirely
            }

            await SendLevelsAsync(toy, st, target, ct).ConfigureAwait(false);
        }

        /// <summary>Builds the comma-combined action string and posts it. Different action verbs
        /// legitimately combine in ONE Function call ("Vibrate:5,Rotate:10").</summary>
        private async Task SendLevelsAsync(LovenseToy toy, DeviceState st,
                                           SortedDictionary<string, int> target, CancellationToken ct)
        {
            // Only one send in flight per device: level-set semantics mean a skipped tick is
            // harmless (the next one carries the newer value) and a queue would only add latency.
            if (!st.Gate.Wait(0)) return;
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in target)
                {
                    var fragment = LovensePatterns.FormatActionFragment(kv.Key, kv.Value);
                    if (fragment == null) continue;
                    if (sb.Length > 0) sb.Append(',');
                    sb.Append(fragment);
                }
                if (sb.Length == 0)
                {
                    lock (_lock) st.Remember(target);   // nothing sendable; don't retry every tick
                    return;
                }

                var payload = LovensePatterns.BuildFunctionPayload(toy.Id, sb.ToString(), 0, stopPrevious: true);
                await PostToAsync(_baseUrl!, payload, CommandTimeout, ct).ConfigureAwait(false);

                lock (_lock)
                {
                    st.Remember(target);
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
            finally
            {
                try { st.Gate.Release(); } catch { }
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
                // The cheapest liveness check already carries the registry, so use it.
                ApplyToys(ParseToysResponse(body), raiseIfChanged: true);
                _lastPollUtc = DateTime.UtcNow;
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Lovense ping failed: {Reason}", ex.Message);
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
        // Maintenance loop: 20 s toy poll + 25 s keep-alive refresh
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

                if (ct.IsCancellationRequested || !_connected || _baseUrl == null) continue;

                try
                {
                    if (DateTime.UtcNow - _lastPollUtc >= ToyPollInterval)
                        await RefreshDevicesAsync(ct).ConfigureAwait(false);

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
        /// without the user noticing a dead toy.</summary>
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
                    due.Add((toy, st, st.Snapshot()));
                }
            }

            foreach (var item in due)
            {
                if (ct.IsCancellationRequested) return;
                await SendLevelsAsync(item.Toy, item.State, item.Levels, ct).ConfigureAwait(false);
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
        /// (routers with DNS-rebinding protection break the alias), then whatever authority the
        /// user literally typed if it adds anything new.
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

        /// <summary>Returns the scheme+host+port the user typed, but only when it carried its own
        /// port (a bare host adds nothing beyond the two standard candidates).</summary>
        private static string? NormalizeAuthority(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim();
            var hadScheme = s.Contains("://");
            if (!hadScheme) s = "http://" + s;
            if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return null;
            if (!hadScheme && u.IsDefaultPort) return null;
            return u.GetLeftPart(UriPartial.Authority);
        }

        // ==================================================================
        // GetToys parsing (dual shape)
        // ==================================================================

        /// <summary>
        /// Parses a GetToys response. <c>data.toys</c> arrives as an ESCAPED JSON STRING on some
        /// firmware/app combinations and as a plain object on others (and a few builds hand back an
        /// array) - all three are accepted, plus the bare toy map that Lovense Connect returns.
        /// </summary>
        internal static Dictionary<string, LovenseToy> ParseToysResponse(string? body)
        {
            var result = new Dictionary<string, LovenseToy>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(body)) return result;

            JsonDocument? nested = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return result;

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

                if (have) CollectToys(toys, result);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Lovense GetToys parse failed: {Reason}", ex.Message);
            }
            finally
            {
                nested?.Dispose();
            }

            return result;
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
                RaiseError("Lovense Remote is no longer reachable.");
            }
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

        /// <summary>Per-device send state: the last quantized levels (for unchanged-suppression),
        /// the keep-alive clock, the failure counter, and a 1-slot gate so only one request per
        /// device is ever in flight.</summary>
        private sealed class DeviceState : IDisposable
        {
            private readonly Dictionary<string, int> _last = new(StringComparer.Ordinal);

            public readonly SemaphoreSlim Gate = new(1, 1);
            public DateTime LastSendUtc = DateTime.MinValue;
            public int ConsecutiveFailures;

            public bool HasNonZero
            {
                get
                {
                    foreach (var v in _last.Values) if (v > 0) return true;
                    return false;
                }
            }

            public bool Matches(SortedDictionary<string, int> target)
            {
                if (_last.Count != target.Count) return false;
                foreach (var kv in target)
                    if (!_last.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
                return true;
            }

            public void Remember(SortedDictionary<string, int> target)
            {
                _last.Clear();
                foreach (var kv in target) _last[kv.Key] = kv.Value;
            }

            public SortedDictionary<string, int> Snapshot()
            {
                var copy = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var kv in _last) copy[kv.Key] = kv.Value;
                return copy;
            }

            public void Clear()
            {
                _last.Clear();
                LastSendUtc = DateTime.MinValue;
            }

            public void Dispose()
            {
                try { Gate.Dispose(); } catch { }
            }
        }
    }
}
