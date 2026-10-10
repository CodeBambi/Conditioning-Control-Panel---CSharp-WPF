using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The subject side of Remote Control: the relay protocol of WPF RemoteControlService
    /// (ConditioningControlPanel/Services/RemoteControlService.cs) - start/stop/poll/status against
    /// /v2/remote/*, the PIN, the 429 backoff, the 401/404 endings, the controller connect/idle state
    /// and the 120 s idle auto-disconnect. Commands go through <see cref="RemoteCommandGate"/> first and
    /// then to the executor on the UI thread (<see cref="CoreDispatch.Invoke"/>).
    /// ponytail: directory opt-in, session list/progress in the status push are not here yet;
    /// add them with the tab parts that use them.
    /// </summary>
    public sealed class RemoteRelay : IDisposable
    {
        private const string RealBaseUrl = "https://codebambi-proxy.vercel.app";
        public const double PollIntervalSeconds = 5.0;          // WPF :53, under the 40/min poll cap
        public const double MaxBackoffSeconds = 60.0;           // WPF :400
        public const double StatusPushIntervalSeconds = 15.0;   // WPF :56
        public const double StatusBackoffSeconds = 60.0;        // WPF :58
        public const double IdleAutoDisconnectSeconds = 120.0;  // WPF :404

        /// <summary>CCP_REMOTE_BASE_URL is honoured only for a loopback host; a CCP_USERDATA_DIR sandbox without
        /// one gets null (no network), so tests and Keincheck never reach the real relay.</summary>
        internal static string? ResolveBaseUrl(string? overrideUrl, bool sandboxed) =>
            LoopbackUrl.IsHonoured(overrideUrl, out var u) ? u.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : sandboxed ? null : RealBaseUrl;

        /// <summary>WPF MainWindow.RemoteControl.cs BuildRemotePairingUrl: the PIN rides in the fragment so it
        /// never reaches a server log or a Referer.</summary>
        public static string PairingUrl(string code, string? pin) =>
            string.IsNullOrEmpty(pin) ? $"https://cclabs.app/remote/#code={code}" : $"https://cclabs.app/remote/#code={code}&pin={pin}";

        public static string NewPin(Random r) => r.Next(0, 10000).ToString("D4");

        private static readonly Random PinRng = new();
        private readonly HttpClient _http;
        private readonly string? _baseUrl;
        private readonly Func<string?> _token, _unifiedId;
        private readonly Func<string, JObject?, string?> _execute;
        private readonly Action<bool> _stopEffects;
        private CancellationTokenSource? _loop;
        private int _pollBusy, _consecutiveFailures;
        private DateTime? _idleSince;
        private bool _autoDisconnected;
        private DateTime _lastStatusPush = DateTime.MinValue, _statusBackoffUntil = DateTime.MinValue;
        private string _lastStatus = "ok";
        private string? _lastReason;
        private bool _remoteSetStrictLock, _pollBackedOff;
        private DateTime _lastControllerCommand = DateTime.MinValue;
        public const double HotPollSeconds = 1.0, HotWindowSeconds = 60.0, ConnectedHeartbeatSeconds = 5.0;   // WPF Screen.cs (d39969827)

        /// <summary>WPF PollBaseSeconds: 1 s while a connected controller sent a command in the last minute or
        /// a remote haptic loops, else 5 s.</summary>
        private double PollBase => ControllerConnected
            && ((Now() - _lastControllerCommand).TotalSeconds < HotWindowSeconds || RemoteCommands.RemoteHaptics.IsLooping)
            ? HotPollSeconds : PollIntervalSeconds;

        /// <summary>Clock seam for the idle and throttle rules.</summary>
        internal Func<DateTime> Now = () => DateTime.UtcNow;
        /// <summary>False in tests: they drive <see cref="PollOnceAsync"/> themselves.</summary>
        internal bool AutoPoll = true;

        public bool IsActive { get; private set; }
        /// <summary>When the last session ended (WPF LastEndedUtc): Circe keeps the remote cap for a short grace after.</summary>
        public DateTime? LastEndedUtc { get; private set; }
        public string? SessionCode { get; private set; }
        public string? ConnectPin { get; private set; }
        public string? Tier { get; private set; }
        public bool ControllerConnected { get; private set; }
        public bool ControllerIdle { get; private set; }
        public bool LastStartFailedAuth { get; private set; }
        /// <summary>False when no relay is reachable by rule (a sandbox without a loopback override).</summary>
        public bool HasRelay => _baseUrl != null;
        public double PollInterval { get; private set; } = PollIntervalSeconds;

        public event EventHandler? ControllerConnectedChanged, ControllerIdleChanged, SessionEnded;

        // ---- Remote Control v2 (WPF Services/Remote/RemoteControlService.V2.cs): what the HUD pill reads.
        /// <summary>The three signals the subject can send up: more, easy, stop.</summary>
        public static readonly string[] SignalKinds = { "more", "easy", "stop" };
        /// <summary>The name the controller typed when connecting, or null.</summary>
        public string? ControllerName { get; private set; }
        /// <summary>When the current controller connected (UTC), or null when nobody is connected.</summary>
        public DateTime? ControllerConnectedSinceUtc { get; private set; }
        /// <summary>Human words for the last command that landed, e.g. "Spiral", "Toy buzz".</summary>
        public string? LastActionLabel { get; private set; }
        /// <summary>Raised when a new command lands (and <see cref="LastActionLabel"/> moved).</summary>
        public event EventHandler? LastActionChanged;
        /// <summary>The subject's Easy factor (see <see cref="RemoteCommands.EasyFactor"/>).</summary>
        public double EasyFactor => RemoteCommands.EasyFactor;
        public event EventHandler? EasyChanged { add => RemoteCommands.EasyChanged += value; remove => RemoteCommands.EasyChanged -= value; }

        /// <summary>WPF SendSignalAsync: the subject's own button, "more", "easy" or "stop". Easy and Stop act
        /// HERE first (they mean something even if the network never answers), then the signal goes up the
        /// emote channel (same auth and rate limit, no debounce: a Stop right after an emote is never
        /// swallowed). True when the signal reached the server. The controller stays connected either way.</summary>
        public async Task<bool> SendSignalAsync(string kind)
        {
            kind = (kind ?? "").Trim().ToLowerInvariant();
            if (Array.IndexOf(SignalKinds, kind) < 0 || !IsActive) return false;
            if (kind == "easy") OnUi(RemoteCommands.ApplyEasy);
            else if (kind == "stop") OnUi(() => _stopEffects(true));
            var uid = _unifiedId();
            if (string.IsNullOrEmpty(uid) || _baseUrl == null) return false;
            try
            {
                using var resp = await PostAsync("/v2/remote/emote", new { unified_id = uid, text = kind, icon = "", kind = "signal" }).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) { Log.Information("[RemoteControl] Signal sent: {Kind}", kind); return true; }
                Log.Warning("[RemoteControl] Signal {Kind} not sent: {Status}", kind, resp.StatusCode);
                return false;
            }
            catch (Exception ex) { Log.Warning(ex, "[RemoteControl] Signal {Kind} failed", kind); return false; }
        }

        /// <summary>Runs on the UI thread and waits; posted instead if a stalled UI cancels the wait.</summary>
        private static void OnUi(Action work)
        {
            void Safe() { try { work(); } catch (Exception ex) { Log.Warning(ex, "[RemoteControl] local signal action failed"); } }
            var (done, _) = CoreDispatch.Invoke(() => { Safe(); return 0; }, TimeSpan.FromSeconds(10));
            if (!done) CoreDispatch.Post(Safe);
        }
        /// <summary>Raised for every command that ran (WPF CommandReceived); refused ones are not.</summary>
        public event EventHandler<string>? CommandReceived;

        /// <param name="execute">Runs one command on the UI thread; returns a refusal reason or null.</param>
        /// <param name="stopEffects">WPF StopAllRemoteEffects(force) / StopRemoteTriggeredEffects.</param>
        public RemoteRelay(Func<string?> token, Func<string?> unifiedId, string appVersion,
            Func<string, JObject?, string?> execute, Action<bool> stopEffects, HttpMessageHandler? handler = null)
        {
            (_token, _unifiedId, _execute, _stopEffects) = (token, unifiedId, execute, stopEffects);
            _baseUrl = handler != null ? RealBaseUrl : ResolveBaseUrl(
                Environment.GetEnvironmentVariable("CCP_REMOTE_BASE_URL"),
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR")));
            _http = handler != null ? new HttpClient(handler) : new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(15);
            _http.DefaultRequestHeaders.Add("X-Client-Version", appVersion);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{appVersion}");
        }

        private async Task<HttpResponseMessage> PostAsync(string path, object body)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path)
            { Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json") };
            var token = _token();
            if (!string.IsNullOrEmpty(token)) req.Headers.Add("X-Auth-Token", token);
            var response = await _http.SendAsync(req).ConfigureAwait(false);
            // Contract D (WPF AuthPostAsync): every remote-control door is keyed on the account; a merge
            // tombstone answers 409 merged and the handler swaps to the canonical. Callers still see the 409
            // and fail this one call the way they already do.
            await MergedAccountRecovery.TryHandleAsync(response).ConfigureAwait(false);
            return response;
        }

        /// <summary>WPF StartSessionAsync: the new session code, or null (logged; see <see cref="LastStartFailedAuth"/>).</summary>
        public async Task<string?> StartAsync(string tier)
        {
            LastStartFailedAuth = false;
            var uid = _unifiedId();
            if (string.IsNullOrEmpty(uid)) { Log.Warning("[RemoteControl] Cannot start: no unified ID"); return null; }
            if (_baseUrl == null) { Log.Warning("[RemoteControl] Sandboxed without a loopback CCP_REMOTE_BASE_URL: no relay"); return null; }
            try
            {
                var pin = NewPin(PinRng);
                using var resp = await PostAsync("/v2/remote/start", new { unified_id = uid, tier, connect_pin = pin }).ConfigureAwait(false);
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    LastStartFailedAuth = resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
                    Log.Warning("[RemoteControl] Start failed: {Status}", resp.StatusCode);
                    return null;
                }
                var code = JObject.Parse(json)["code"]?.ToString();
                if (string.IsNullOrEmpty(code)) return null;
                (SessionCode, ConnectPin, Tier, IsActive) = (code, pin, tier, true);
                (_consecutiveFailures, PollInterval, _lastStatusPush, _statusBackoffUntil) = (0, PollIntervalSeconds, DateTime.MinValue, DateTime.MinValue);
                if (AutoPoll) StartLoop();
                Log.Information("[RemoteControl] Session started: {Code}, tier: {Tier}", code, tier);
                return code;
            }
            catch (Exception ex) { Log.Error(ex, "[RemoteControl] Start error"); return null; }
        }

        private void StartLoop()
        {
            var cts = _loop = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(PollInterval), cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    await PollOnceAsync().ConfigureAwait(false);
                }
            });
        }

        /// <summary>WPF StopSessionAsync: tell the relay, then end here.</summary>
        public async Task StopAsync()
        {
            if (!IsActive) return;
            var uid = _unifiedId();
            try { using var _ = await PostAsync("/v2/remote/stop", new { unified_id = uid }).ConfigureAwait(false); }
            catch (Exception ex) { Log.Warning(ex, "[RemoteControl] Stop request failed"); }
            Cleanup();
        }

        /// <summary>WPF RemoteControlService.EndSessionNow (the leash cut): end HERE first, so nothing the
        /// controller started keeps running while the network answers, then tell the relay, unawaited.</summary>
        public void EndSessionNow()
        {
            if (!IsActive) return;
            var uid = _unifiedId();
            Cleanup();
            _ = Task.Run(async () =>
            {
                try { using var _ = await PostAsync("/v2/remote/stop", new { unified_id = uid }).ConfigureAwait(false); }
                catch (Exception ex) { Log.Warning(ex, "[RemoteControl] Stop request failed"); }
            });
        }

        /// <summary>WPF CleanupSession: everything the controller started stops; the subject's own run stays.</summary>
        private void Cleanup()
        {
            if (!IsActive) return;
            _loop?.Cancel();
            _loop = null;
            (IsActive, SessionCode, ConnectPin, Tier, ControllerIdle, _idleSince, _autoDisconnected) = (false, null, null, null, false, null, false);
            LastEndedUtc = DateTime.UtcNow;
            (_remoteSetStrictLock, _pollBackedOff, _lastControllerCommand) = (false, false, DateTime.MinValue);
            (_lastOptInTags, _lastOptInStatus) = (null, null);
            // StopAsync and the poll loop land here off the UI thread, and the stops close windows: run
            // them on the UI like ControllerLeft, posted if a stalled UI cancels the Invoke.
            void StopEffects()
            {
                try { _stopEffects(false); } catch (Exception ex) { Log.Warning(ex, "[RemoteControl] stop effects failed"); }
                // WPF ResetV2SessionState: Easy hands the subject's own opacity back and returns to 1.
                try { RemoteCommands.ResetEasy(); } catch (Exception ex) { Log.Warning(ex, "[RemoteControl] easy reset failed"); }
            }
            var (done, _) = CoreDispatch.Invoke(() => { StopEffects(); return 0; }, TimeSpan.FromSeconds(10));
            if (!done) CoreDispatch.Post(StopEffects);
            (ControllerName, ControllerConnectedSinceUtc) = (null, null);
            if (LastActionLabel != null) { LastActionLabel = null; LastActionChanged?.Invoke(this, EventArgs.Empty); }
            if (ControllerConnected) { ControllerConnected = false; ControllerConnectedChanged?.Invoke(this, EventArgs.Empty); }
            SessionEnded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>One WPF PollForCommandsAsync pass. Re-entry is skipped, as the WPF timer did.</summary>
        public async Task PollOnceAsync()
        {
            if (!IsActive || Interlocked.Exchange(ref _pollBusy, 1) == 1) return;
            try
            {
                var uid = _unifiedId();
                if (string.IsNullOrEmpty(uid)) return;
                using var resp = await PostAsync("/v2/remote/poll", new { unified_id = uid }).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _consecutiveFailures++;
                    if (resp.StatusCode == HttpStatusCode.NotFound) { Log.Warning("[RemoteControl] Session expired during poll"); Cleanup(); }
                    else if ((int)resp.StatusCode == 429)
                    {   // from at least 5 s, so hot mode backs off as hard as cold (WPF d39969827)
                        _pollBackedOff = true;
                        PollInterval = Math.Min(Math.Max(PollInterval, PollIntervalSeconds) * 2, MaxBackoffSeconds);
                    }
                    else if (resp.StatusCode == HttpStatusCode.Unauthorized && _consecutiveFailures >= 3)
                    { Log.Error("[RemoteControl] 3 consecutive auth failures - terminating session"); Cleanup(); }
                    else Log.Warning("[RemoteControl] Poll failed: {Status}", resp.StatusCode);
                    return;
                }
                var result = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                _consecutiveFailures = 0;
                _pollBackedOff = false;
                var changed = ApplyControllerState(result["controller_connected"]?.Value<bool>() ?? false,
                                                   result["controller_idle"]?.Value<bool>() ?? false,
                                                   Remote.RemoteControllerName.Sanitize(
                                                       result["controller_name"]?.Type == JTokenType.String ? result["controller_name"]!.ToString() : null));

                string? lastId = null, lastAction = null;
                // Fetched before a panic or a leave: never runs after it, however late its dispatch lands.
                var gen = (Volatile.Read(ref _leaveGeneration), RemoteCommands.PanicGeneration);
                foreach (var cmd in result["commands"] as JArray ?? new JArray())
                {
                    var action = cmd["action"]?.ToString();
                    if (string.IsNullOrEmpty(action) || !IsActive) continue;
                    RunCommand(action, cmd["params"] as JObject, gen);
                    (lastId, lastAction) = (cmd["id"]?.ToString(), action);
                }

                var heartbeat = ControllerConnected ? ConnectedHeartbeatSeconds : StatusPushIntervalSeconds;
                if (lastId != null || changed || (Now() - _lastStatusPush).TotalSeconds >= heartbeat)
                    await SendStatusAsync(lastId, lastAction).ConfigureAwait(false);
                if (!_pollBackedOff) PollInterval = PollBase;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                Log.Warning(ex, "[RemoteControl] Poll error (consecutive: {Count})", _consecutiveFailures);
            }
            finally { Interlocked.Exchange(ref _pollBusy, 0); }
        }

        /// <summary>WPF's connect/idle block (RemoteControlService.cs:537-625). True when connected changed.</summary>
        private bool ApplyControllerState(bool serverConnected, bool idle, string? controllerName = null)
        {
            var connected = serverConnected;
            if (connected && _autoDisconnected)
            {
                if (!idle) (_autoDisconnected, _idleSince) = (false, null);
                else connected = false;   // still idle after the auto-disconnect: no reconnect
            }
            if (!serverConnected) _autoDisconnected = false;

            ControllerName = connected ? controllerName : null;
            var changed = connected != ControllerConnected;
            if (changed)
            {
                ControllerConnected = connected;
                ControllerConnectedSinceUtc = connected ? Now() : null;
                if (!connected) { ControllerLeft(); _ = RepublishDirectoryIfOptedInAsync(); }
                ControllerConnectedChanged?.Invoke(this, EventArgs.Empty);
            }
            if (idle != ControllerIdle)
            {
                ControllerIdle = idle;
                _idleSince = idle ? Now() : null;
                ControllerIdleChanged?.Invoke(this, EventArgs.Empty);
            }
            if (ControllerConnected && idle && _idleSince is { } since && (Now() - since).TotalSeconds >= IdleAutoDisconnectSeconds)
            {
                Log.Information("[RemoteControl] Controller idle for {Seconds:F0}s - auto-disconnecting", (Now() - since).TotalSeconds);
                (_autoDisconnected, ControllerConnected, ControllerIdle) = (true, false, false);
                ControllerConnectedSinceUtc = null;
                ControllerLeft();
                _ = RepublishDirectoryIfOptedInAsync();
                ControllerConnectedChanged?.Invoke(this, EventArgs.Empty);
                ControllerIdleChanged?.Invoke(this, EventArgs.Empty);
                changed = true;
            }
            return changed;
        }

        // WPF HandleControllerDisconnectCleanup: default leaves effects running; the opt-in stops them.
        // A remote haptic never outlives its controller, and what could trap the subject is handed back
        // (WPF 71cfc4185 ReleaseRemoteSafetyState, ccp-bugs #1340): panic key on, the controller's own
        // strict lock off; a strict lock the subject set stays.
        // Runs through the same UI dispatch as commands (and so PanicKeyUiSync, which Avalonia leaves unset,
        // is on the UI thread), after bumping the leave generation that refuses anything still in flight.
        private void ControllerLeft()
        {
            Interlocked.Increment(ref _leaveGeneration);
            // Avalonia cancels a timed-out Invoke (AvaloniaCoreDispatch): after a UI stall the release is posted
            // instead, so it still runs. Running it twice is harmless (every step is idempotent).
            var (done, _) = CoreDispatch.Invoke(() => { ReleaseOnLeave(); return 0; }, TimeSpan.FromSeconds(10));
            if (!done) CoreDispatch.Post(ReleaseOnLeave);
        }

        private void ReleaseOnLeave()
        {
            RemoteCommands.RemoteHaptics.Stop();
            // What the controller put on the screen comes down with it (pink filter, spiral, Melt haze).
            try { RemoteCommands.ControllerLeft(); } catch (Exception ex) { Log.Warning(ex, "[RemoteControl] overlay release failed"); }
            var s = CoreSettings.Current;
            var changed = false;
            if (_remoteSetStrictLock) { _remoteSetStrictLock = false; if (s.StrictLockEnabled) { s.StrictLockEnabled = false; changed = true; } }
            if (!s.PanicKeyEnabled) { s.PanicKeyEnabled = true; changed = true; }
            if (changed)
            {
                CoreSettings.Save();
                try { LockdownService.PanicKeyUiSync?.Invoke(); } catch { }
                Log.Information("[RemoteControl] Controller left: panic key back on, controller's strict lock released");
            }
            if (CoreSettings.Current.StopEffectsOnRemoteDisconnect)
                try { _stopEffects(false); } catch (Exception ex) { Log.Warning(ex, "[RemoteControl] stop effects failed"); }
        }

        private int _leaveGeneration;

        private void RunCommand(string action, JObject? parameters, (int Leave, int Panic) gen)
        {
            (_lastStatus, _lastReason) = ("ok", null);
            _lastControllerCommand = Now();          // WPF NoteControllerActivity: every command, refused or not
            RemoteCommands.RemoteHaptics.NoteCommand();
            // The Leash (owner, 2026-09-26; WPF RemoteControlService.ExecuteCommand): a leashed account keeps
            // its way out. From ANY remote session Strict Lock never goes on, the panic key never goes off,
            // and a session start loses its strict_lock flag. Asked first, before any other gate.
            string? reason = null;
            var leash = Leash.LeashRemoteRule.Screen(action, Leash.LeashRemoteRule.AsksStrictLock(parameters), Leash.LeashGuard.Check());
            if (leash == Leash.LeashRemoteVerdict.Refuse) reason = "not while on a leash";
            else if (leash == Leash.LeashRemoteVerdict.StripStrict)
            {
                parameters = parameters == null ? null : (JObject)parameters.DeepClone();
                if (parameters != null) parameters["strict_lock"] = false;
                Log.Information("[RemoteControl] start_session asked for strict lock; dropped, the account is leashed");
            }
            reason ??= RemoteCommandGate.Screen(action, LockdownService.Current?.IsActive == true);
            if (reason == null)
            {
                Log.Information("[RemoteControl] Executing: {Action}", action);
                var (done, refused) = CoreDispatch.Invoke(() =>
                {
                    if (gen.Leave != Volatile.Read(ref _leaveGeneration)) return "the controller left";
                    if (gen.Panic != RemoteCommands.PanicGeneration) return "stopped by panic";
                    var strictBefore = CoreSettings.Current.StrictLockEnabled;
                    try
                    {
                        var refusal = _execute(action, parameters);
                        // Set here, in the dispatched call, so even a late-landing switch-on is the controller's.
                        if (refusal == null && action == "enable_strict_lock" && !strictBefore) _remoteSetStrictLock = true;
                        return refusal;
                    }
                    catch (Exception ex) { Log.Error(ex, "[RemoteControl] Error executing command: {Action}", action); return "error"; }
                }, TimeSpan.FromSeconds(10));
                reason = done ? refused : "timed out";
            }
            if (reason != null)
            {
                // WPF ReportCommandRefused: say so on the status push, never a silent "ok".
                (_lastStatus, _lastReason) = ("fail", reason);
                Log.Warning("[RemoteControl] {Action} not delivered: {Reason}", action, reason);
                return;
            }
            // WPF NoteLandedCommand: the HUD's "Last: ..." line.
            LastActionLabel = Remote.RemoteActionLabels.For(action);
            LastActionChanged?.Invoke(this, EventArgs.Empty);
            CommandReceived?.Invoke(this, action);
        }

        public const int EmoteDebounceMs = 300;   // WPF RemoteControlService.cs:434
        private DateTime _lastEmoteSent = DateTime.MinValue;

        /// <summary>WPF IsWithinDebounceWindow: a send now would be swallowed as a double click.</summary>
        public bool IsWithinDebounceWindow => (Now() - _lastEmoteSent).TotalMilliseconds < EmoteDebounceMs;

        /// <summary>WPF SendEmoteAsync (RemoteControlService.cs:848): POST /v2/remote/emote. Errors as WPF:
        /// "session not active", "no unified id", "debounced" (silent), "rate_limited" + retry seconds, "http N".</summary>
        public async Task<(bool ok, string? error, int? retryAfterSeconds)> SendEmoteAsync(string text, string icon, string kind)
        {
            if (!IsActive) return (false, "session not active", null);
            var uid = _unifiedId();
            if (string.IsNullOrEmpty(uid)) return (false, "no unified id", null);
            if (IsWithinDebounceWindow) return (false, "debounced", null);
            _lastEmoteSent = Now();
            var trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0) return (false, "text required", null);
            if (trimmed.Length > 60) trimmed = trimmed.Substring(0, 60);
            var safeIcon = icon ?? "";
            if (safeIcon.Length > 8) safeIcon = safeIcon.Substring(0, 8);
            if (kind is not ("preset" or "custom")) return (false, "invalid kind", null);
            try
            {
                using var resp = await PostAsync("/v2/remote/emote", new { unified_id = uid, text = trimmed, icon = safeIcon, kind }).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) { Log.Information("[RemoteControl] Emote sent (kind={Kind}, len={Len})", kind, trimmed.Length); return (true, null, null); }
                if ((int)resp.StatusCode == 429)
                {
                    int? retry = null;
                    try { if (int.TryParse(JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false))["retry_after_seconds"]?.ToString(), out var n)) retry = n; }
                    catch { }
                    return (false, "rate_limited", retry);
                }
                Log.Warning("[RemoteControl] Emote send failed: {Status}", resp.StatusCode);
                return (false, $"http {(int)resp.StatusCode}", null);
            }
            catch (Exception ex) { Log.Warning(ex, "[RemoteControl] Emote send error"); return (false, ex.Message, null); }
        }

        /// <summary>WPF PushStatusNowAsync: a settings change (share avatar) reaches the controller now,
        /// not at the next ~15 s push. No-op without a session.</summary>
        public Task PushStatusNowAsync() => IsActive ? SendStatusAsync(null, null) : Task.CompletedTask;

        /// <summary>WPF OptInToDirectoryAsync (RemoteControlService.cs:247): list the live session in the
        /// directory. Best-effort: false on any failure, the session itself is untouched. The body carries
        /// the PIN the tab already shows; the response body is never logged.</summary>
        public async Task<bool> OptInToDirectoryAsync(List<string>? tags, string? statusText)
        {
            if (!IsActive || string.IsNullOrEmpty(SessionCode) || string.IsNullOrEmpty(ConnectPin))
            {
                Log.Warning("[RemoteControl] OptIn called without active session");
                return false;
            }
            var uid = _unifiedId();
            if (string.IsNullOrEmpty(uid) || _baseUrl == null) return false;
            tags ??= new List<string>();
            statusText ??= "";
            try
            {
                using var resp = await PostAsync("/v2/directory/opt-in",
                    new { unified_id = uid, code = SessionCode, pin = ConnectPin, tags, status_text = statusText }).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    Log.Warning("[RemoteControl] Directory opt-in failed: {Status}", resp.StatusCode);
                    return false;
                }
                // Kept so the entry can be re-published when the controller leaves (the server keeps
                // the claim flag across a disconnect, so the subject would stay "taken").
                (_lastOptInTags, _lastOptInStatus) = (tags, statusText);
                Log.Information("[RemoteControl] Directory opt-in OK ({TagCount} tags, status={StatusLen}c)", tags.Count, statusText.Length);
                return true;
            }
            catch (Exception ex) { Log.Warning(ex, "[RemoteControl] Directory opt-in error"); return false; }
        }

        /// <summary>True once this session's directory opt-in succeeded; cleared when the session ends.</summary>
        public bool DirectoryOptedIn => _lastOptInTags != null;

        private List<string>? _lastOptInTags;
        private string? _lastOptInStatus;

        /// <summary>WPF RepublishDirectoryIfOptedInAsync: after a controller leaves, the entry goes back to available.</summary>
        internal async Task RepublishDirectoryIfOptedInAsync()
        {
            if (_lastOptInTags is not { } tags || !IsActive) return;
            try { await OptInToDirectoryAsync(tags, _lastOptInStatus ?? "").ConfigureAwait(false); }
            catch (Exception ex) { Log.Warning(ex, "[RemoteControl] Directory re-publish after disconnect failed"); }
        }

        private async Task SendStatusAsync(string? lastId, string? lastAction)
        {
            if (Now() < _statusBackoffUntil) return;
            var s = CoreSettings.Current;
            var body = new
            {
                unified_id = _unifiedId(),
                active_services = ActiveServices(s),
                level = s.PlayerLevel,
                last_executed = lastId == null ? null : new { id = lastId, action = lastAction, status = _lastStatus, reason = _lastReason, at = Now().ToString("o") },
                available_sessions = (object?)null,
                session_info = (object?)null,
                share_avatar = s.RemoteShareAvatar,
            };
            try
            {
                using var resp = await PostAsync("/v2/remote/status", body).ConfigureAwait(false);
                if ((int)resp.StatusCode == 429) _statusBackoffUntil = Now().AddSeconds(StatusBackoffSeconds);
                else if (resp.IsSuccessStatusCode) _lastStatusPush = Now();
            }
            catch (Exception ex) { Log.Warning(ex, "[RemoteControl] Status update error"); }
        }

        /// <summary>WPF GetActiveServices, for what this Core drives.</summary>
        private static List<string> ActiveServices(Models.AppSettings s)
        {
            var list = new List<string>();
            if (s.PinkFilterEnabled) list.Add("pink_filter");   // WPF GetActiveServices order
            if (s.SpiralEnabled) list.Add("spiral");
            if (s.StrictLockEnabled) list.Add("strict_lock");
            if (CoreHaptics.Service?.IsConnected == true) list.Add("haptics");   // WPF 7b22ece8c: trigger_haptic will land
            if (!s.PanicKeyEnabled) list.Add("no_panic");
            if (CoreEngine.IsRunning) list.Add("session");
            if (CoreFlash.IsRunning) list.Add("flash_loop");
            if (CoreSubliminal.IsRunning) list.Add("subliminal_loop");
            if (LockCardScheduler.Instance.IsRunning) list.Add("lock_card");
            return list;
        }

        public void Dispose()
        {
            _loop?.Cancel();
            _http.Dispose();
        }
    }

    /// <summary>
    /// Escape integrity (docs/avalonia-decisions.md, 2026-09-30): a controller may never remove the
    /// subject's last means of escape. disable_panic is always refused (WPF saved it); under Lockdown
    /// enable_strict_lock is refused. Verbs that only reduce restraint run as on WPF and never touch the
    /// Lockdown timer; Lockdown restores the pre-lockdown settings when it ends. Strict lock stays the
    /// same setting, so every strict surface keeps its own fall-open.
    /// </summary>
    public static class RemoteCommandGate
    {
        public static string? Screen(string action, bool lockdownActive) =>
            action == "disable_panic" ? "the panic key stays on"   // WPF 719ed9ca5 wording
            : lockdownActive && action == "enable_strict_lock" ? "not during Lockdown"
            : null;
    }
}
