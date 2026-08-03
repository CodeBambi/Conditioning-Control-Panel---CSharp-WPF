using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Lovense "Toy Events" WebSocket client - the INPUT half of the LAN API (toy buttons,
    /// on-toy strength changes, battery, shake, motion). Owned by <see cref="LovenseProviderV2"/>.
    ///
    /// Endpoint: <c>ws://{ip}:20010/v1</c>, with <c>wss://{dashed-ip}.lovense.club:30010/v1</c>
    /// tried first (same HTTPS-then-HTTP fallback the command API needs, because router
    /// DNS-rebinding protection frequently kills the .lovense.club path).
    ///
    /// Design notes:
    ///  * Older Lovense Remote builds have no /v1 endpoint at all. A definitive "this is not a
    ///    WebSocket endpoint" answer (404/400/501) permanently disables the client after ONE
    ///    informational log line - the feature simply does not exist for that user and must never
    ///    surface as an error. Transient failures (Remote closed, network blip) keep retrying with
    ///    capped exponential backoff.
    ///  * The exact handshake frame is NOT published in the Standard API docs. We send two
    ///    plausible access-request shapes; Remote ignores frames it does not understand, so the
    ///    cost is one extra 60-byte send per session. The first few received frames are logged at
    ///    Debug so a real-toy capture can pin the protocol down and this can be trimmed to one.
    ///  * Everything here runs on IO threads. It NEVER touches the UI and never throws to callers.
    /// </summary>
    public sealed class LovenseToyEventsClient : IDisposable
    {
        private const int PingIntervalMs = 5000;
        private const int MinBackoffMs = 2000;
        private const int MaxBackoffMs = 60000;
        private const int MaxLoggedFrames = 5;

        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private readonly object _lock = new();

        private CancellationTokenSource? _cts;
        private Task? _runTask;
        private volatile bool _connected;
        private volatile bool _unsupported;
        private volatile bool _disposed;
        private int _loggedFrames;
        private string[] _candidates = Array.Empty<string>();

        /// <summary>Raised for every recognised toy event. Fires from an IO thread.</summary>
        public event EventHandler<HapticToyEvent>? ToyEvent;

        /// <summary>Raised when the socket comes up / goes down. Fires from an IO thread.</summary>
        public event EventHandler<bool>? ConnectionChanged;

        public bool IsConnected => _connected;

        /// <summary>False once the Remote build has told us the /v1 endpoint does not exist.</summary>
        public bool IsSupported => !_unsupported;

        /// <summary>The endpoint we are currently talking to (diagnostics only).</summary>
        public string? ActiveEndpoint { get; private set; }

        /// <summary>
        /// Starts (or restarts) the background connect/receive loop.
        /// <paramref name="resolvedHttpBase"/> is the base URL the command API already proved
        /// reachable (e.g. <c>http://192.168.1.42:20010</c>); its scheme/host/port produce the
        /// first WS candidate, and the standard alternates follow.
        /// </summary>
        public void Start(string? resolvedHttpBase, string? hostOrIp)
        {
            if (_disposed) return;

            var candidates = BuildCandidates(resolvedHttpBase, hostOrIp);
            if (candidates.Length == 0) return;

            lock (_lock)
            {
                if (_runTask is { IsCompleted: false }) return;   // already running
                _candidates = candidates;
                _unsupported = false;
                _loggedFrames = 0;
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;
                _runTask = Task.Run(() => RunAsync(ct), CancellationToken.None);
            }
        }

        public async Task StopAsync()
        {
            CancellationTokenSource? cts;
            Task? task;
            lock (_lock)
            {
                cts = _cts;
                task = _runTask;
                _cts = null;
                _runTask = null;
            }

            try { cts?.Cancel(); } catch { /* already disposed */ }

            if (task != null)
            {
                try { await Task.WhenAny(task, Task.Delay(2000)).ConfigureAwait(false); }
                catch { /* never throw from teardown */ }
            }

            try { cts?.Dispose(); } catch { }
            SetConnected(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancellationTokenSource? cts;
            lock (_lock) { cts = _cts; _cts = null; _runTask = null; }
            try { cts?.Cancel(); } catch { }
            try { cts?.Dispose(); } catch { }
            try { _sendGate.Dispose(); } catch { }
            _connected = false;
        }

        // ------------------------------------------------------------------
        // Endpoint candidates
        // ------------------------------------------------------------------

        internal static string[] BuildCandidates(string? resolvedHttpBase, string? hostOrIp)
        {
            var list = new List<string>();

            void Add(string? uri)
            {
                if (string.IsNullOrWhiteSpace(uri)) return;
                if (!list.Contains(uri, StringComparer.OrdinalIgnoreCase)) list.Add(uri);
            }

            string? host = null;

            if (!string.IsNullOrWhiteSpace(resolvedHttpBase) &&
                Uri.TryCreate(resolvedHttpBase, UriKind.Absolute, out var baseUri))
            {
                host = baseUri.Host;
                var scheme = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
                Add($"{scheme}://{baseUri.Host}:{baseUri.Port}/v1");
            }

            var plain = LovenseProviderV2.ExtractHost(hostOrIp) ?? host;
            if (!string.IsNullOrWhiteSpace(plain))
            {
                // If the caller handed us the .lovense.club alias, recover the dotted IP from it.
                var dotted = LovenseProviderV2.DottedFromLovenseClubHost(plain) ?? plain;
                if (!LovenseProviderV2.IsLoopback(dotted))
                    Add($"wss://{dotted.Replace('.', '-')}.lovense.club:30010/v1");
                Add($"ws://{dotted}:20010/v1");
            }

            return list.ToArray();
        }

        // ------------------------------------------------------------------
        // Connect / reconnect loop
        // ------------------------------------------------------------------

        private async Task RunAsync(CancellationToken ct)
        {
            var attempt = 0;

            while (!ct.IsCancellationRequested && !_unsupported && !_disposed)
            {
                var opened = false;

                foreach (var candidate in _candidates)
                {
                    if (ct.IsCancellationRequested || _unsupported) break;

                    ClientWebSocket? ws = null;
                    try
                    {
                        ws = new ClientWebSocket();
                        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                        ws.Options.SetRequestHeader(LovensePatterns.PlatformHeaderName,
                                                    LovensePatterns.PlatformHeaderValue);

                        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        connectCts.CancelAfter(TimeSpan.FromSeconds(5));
                        await ws.ConnectAsync(new Uri(candidate), connectCts.Token).ConfigureAwait(false);

                        opened = true;
                        attempt = 0;
                        ActiveEndpoint = candidate;
                        App.Logger?.Information("Lovense Toy Events: connected to {Endpoint}", candidate);
                        SetConnected(true);

                        await SessionAsync(ws, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // shutting down
                    }
                    catch (Exception ex)
                    {
                        if (IsEndpointMissing(ex))
                        {
                            _unsupported = true;
                            App.Logger?.Information(
                                "Lovense Toy Events API not available at {Endpoint} ({Reason}) - toy-button input " +
                                "is disabled for this Lovense Remote version. Command output is unaffected.",
                                candidate, ex.Message);
                        }
                        else
                        {
                            App.Logger?.Debug("Lovense Toy Events: {Endpoint} unreachable ({Reason})",
                                              candidate, ex.Message);
                        }
                    }
                    finally
                    {
                        SetConnected(false);
                        try { ws?.Dispose(); } catch { }
                    }

                    if (opened) break;   // this candidate worked; retry the same list from the top later
                }

                if (ct.IsCancellationRequested || _unsupported || _disposed) break;

                attempt = Math.Min(attempt + 1, 10);
                var delay = opened
                    ? MinBackoffMs
                    : Math.Min(MaxBackoffMs, MinBackoffMs * (int)Math.Pow(2, Math.Min(attempt - 1, 5)));
                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }

            SetConnected(false);
        }

        private static bool IsEndpointMissing(Exception ex)
        {
            if (ex is WebSocketException wse)
            {
                if (wse.WebSocketErrorCode == WebSocketError.NotAWebSocket ||
                    wse.WebSocketErrorCode == WebSocketError.UnsupportedProtocol ||
                    wse.WebSocketErrorCode == WebSocketError.HeaderError)
                    return true;

                var m = wse.Message ?? "";
                if (m.Contains("404") || m.Contains("400") || m.Contains("501") || m.Contains("403"))
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Session
        // ------------------------------------------------------------------

        private async Task SessionAsync(ClientWebSocket ws, CancellationToken ct)
        {
            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var sct = sessionCts.Token;

            await SendHandshakeAsync(ws, sct).ConfigureAwait(false);

            var pingTask = PingLoopAsync(ws, sct);
            try
            {
                await ReceiveLoopAsync(ws, sct).ConfigureAwait(false);
            }
            finally
            {
                try { sessionCts.Cancel(); } catch { }
                try { await pingTask.ConfigureAwait(false); } catch { }
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token)
                                .ConfigureAwait(false);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Access-request handshake. Lovense Remote pops an authorisation prompt on the phone the
        /// first time an app asks. The published Standard API docs do not spell the frame out, so
        /// both known-plausible shapes go out; unknown frames are ignored by Remote.
        /// </summary>
        private async Task SendHandshakeAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var id = Guid.NewGuid().ToString("N").Substring(0, 12);

            await SendTextAsync(ws, LovensePatterns.Serialize(new Dictionary<string, object?>
            {
                ["eventType"] = "access-request",
                ["platform"] = LovensePatterns.PlatformHeaderValue,
                ["clientId"] = id,
                ["version"] = "1"
            }), ct).ConfigureAwait(false);

            await SendTextAsync(ws, LovensePatterns.Serialize(new Dictionary<string, object?>
            {
                ["command"] = "AccessRequest",
                ["platform"] = LovensePatterns.PlatformHeaderValue,
                ["clientId"] = id,
                ["apiVer"] = 1
            }), ct).ConfigureAwait(false);
        }

        private async Task PingLoopAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var ping = LovensePatterns.Serialize(new Dictionary<string, object?> { ["eventType"] = "ping" });
            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    await Task.Delay(PingIntervalMs, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested || ws.State != WebSocketState.Open) break;
                    await SendTextAsync(ws, ping, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { App.Logger?.Debug("Lovense Toy Events ping stopped: {Reason}", ex.Message); }
        }

        private async Task SendTextAsync(ClientWebSocket ws, string json, CancellationToken ct)
        {
            if (ws.State != WebSocketState.Open) return;
            await _sendGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
                        .ConfigureAwait(false);
            }
            finally
            {
                try { _sendGate.Release(); } catch { }
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[8192];
            using var frame = new MemoryStream();

            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Lovense Toy Events receive ended: {Reason}", ex.Message);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close) break;

                frame.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                var text = Encoding.UTF8.GetString(frame.ToArray());
                frame.SetLength(0);
                if (string.IsNullOrWhiteSpace(text)) continue;

                if (_loggedFrames < MaxLoggedFrames)
                {
                    _loggedFrames++;
                    App.Logger?.Debug("Lovense Toy Events frame: {Frame}", text);
                }

                try { HandleFrame(text); }
                catch (Exception ex) { App.Logger?.Debug("Lovense Toy Events parse failed: {Reason}", ex.Message); }
            }
        }

        // ------------------------------------------------------------------
        // Event mapping
        // ------------------------------------------------------------------

        private void HandleFrame(string text)
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray()) HandleElement(el, 0);
                return;
            }
            if (root.ValueKind == JsonValueKind.Object) HandleElement(root, 0);
        }

        private void HandleElement(JsonElement el, int depth)
        {
            if (el.ValueKind != JsonValueKind.Object || depth > 2) return;

            var name = ReadString(el, "eventType", "type", "event", "name");
            var normalized = Normalize(name);

            if (!TryMapKind(normalized, out var kind))
            {
                // Envelope shapes: {"data":{...}} / {"payload":[...]} — recurse once.
                foreach (var prop in new[] { "data", "payload", "events" })
                {
                    if (!el.TryGetProperty(prop, out var inner)) continue;
                    if (inner.ValueKind == JsonValueKind.Object) HandleElement(inner, depth + 1);
                    else if (inner.ValueKind == JsonValueKind.Array)
                        foreach (var e in inner.EnumerateArray()) HandleElement(e, depth + 1);
                }
                return;
            }

            var toyId = ReadString(el, "toyId", "toy", "deviceId", "id") ?? "";
            var value = kind switch
            {
                ToyEventKind.BatteryChanged => ReadNumber(el, "battery", "value", "level", "strength"),
                ToyEventKind.StrengthChanged => ReadNumber(el, "strength", "value", "level", "strengthValue"),
                _ => ReadNumber(el, "value", "strength", "level")
            };

            Raise(new HapticToyEvent
            {
                DeviceKey = "lovense:" + toyId,
                Kind = kind,
                Value = value
            });
        }

        internal static string Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw.Trim().ToLowerInvariant())
            {
                if (c == '_' || c == ' ') sb.Append('-');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        internal static bool TryMapKind(string normalized, out ToyEventKind kind)
        {
            switch (normalized)
            {
                case "button-down":
                case "buttondown":
                    kind = ToyEventKind.ButtonDown; return true;
                case "button-up":
                case "buttonup":
                    kind = ToyEventKind.ButtonUp; return true;
                case "button-pressed":
                case "button-press":
                case "button-click":
                case "buttonpressed":
                    kind = ToyEventKind.ButtonPressed; return true;
                case "function-strength-changed":
                case "strength-changed":
                case "functionstrengthchanged":
                    kind = ToyEventKind.StrengthChanged; return true;
                case "battery-changed":
                case "battery":
                case "batterychanged":
                    kind = ToyEventKind.BatteryChanged; return true;
                case "shake":
                case "toy-shake":
                    kind = ToyEventKind.Shake; return true;
                case "motion-changed":
                case "motion":
                case "motionchanged":
                    kind = ToyEventKind.MotionChanged; return true;
                default:
                    kind = ToyEventKind.ButtonPressed; return false;
            }
        }

        private static string? ReadString(JsonElement el, params string[] names)
        {
            foreach (var n in names)
            {
                if (!el.TryGetProperty(n, out var v)) continue;
                if (v.ValueKind == JsonValueKind.String) return v.GetString();
                if (v.ValueKind == JsonValueKind.Number) return v.ToString();
            }
            return null;
        }

        private static double ReadNumber(JsonElement el, params string[] names)
        {
            foreach (var n in names)
            {
                if (!el.TryGetProperty(n, out var v)) continue;
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
                if (v.ValueKind == JsonValueKind.String &&
                    double.TryParse(v.GetString(), System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out var s)) return s;
                if (v.ValueKind == JsonValueKind.True) return 1;
                if (v.ValueKind == JsonValueKind.False) return 0;
            }
            return 0;
        }

        private void Raise(HapticToyEvent e)
        {
            try { ToyEvent?.Invoke(this, e); }
            catch (Exception ex) { App.Logger?.Debug("Lovense ToyEvent handler threw: {Reason}", ex.Message); }
        }

        private void SetConnected(bool value)
        {
            if (_connected == value) return;
            _connected = value;
            try { ConnectionChanged?.Invoke(this, value); }
            catch (Exception ex) { App.Logger?.Debug("Lovense ToyEvents ConnectionChanged handler threw: {Reason}", ex.Message); }
        }
    }
}
