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
    /// ponytail: directory opt-in, emotes, session list/progress in the status push are not here yet;
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
            return await _http.SendAsync(req).ConfigureAwait(false);
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

        /// <summary>WPF CleanupSession: everything the controller started stops; the subject's own run stays.</summary>
        private void Cleanup()
        {
            if (!IsActive) return;
            _loop?.Cancel();
            _loop = null;
            (IsActive, SessionCode, ConnectPin, Tier, ControllerIdle, _idleSince, _autoDisconnected) = (false, null, null, null, false, null, false);
            (_remoteSetStrictLock, _pollBackedOff, _lastControllerCommand) = (false, false, DateTime.MinValue);
            try { _stopEffects(false); } catch (Exception ex) { Log.Warning(ex, "[RemoteControl] stop effects failed"); }
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
                                                   result["controller_idle"]?.Value<bool>() ?? false);

                string? lastId = null, lastAction = null;
                foreach (var cmd in result["commands"] as JArray ?? new JArray())
                {
                    var action = cmd["action"]?.ToString();
                    if (string.IsNullOrEmpty(action) || !IsActive) continue;
                    RunCommand(action, cmd["params"] as JObject);
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
        private bool ApplyControllerState(bool serverConnected, bool idle)
        {
            var connected = serverConnected;
            if (connected && _autoDisconnected)
            {
                if (!idle) (_autoDisconnected, _idleSince) = (false, null);
                else connected = false;   // still idle after the auto-disconnect: no reconnect
            }
            if (!serverConnected) _autoDisconnected = false;

            var changed = connected != ControllerConnected;
            if (changed)
            {
                ControllerConnected = connected;
                if (!connected) ControllerLeft();
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
                ControllerLeft();
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
        private void ControllerLeft()
        {
            RemoteCommands.RemoteHaptics.Stop();
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

        private void RunCommand(string action, JObject? parameters)
        {
            (_lastStatus, _lastReason) = ("ok", null);
            _lastControllerCommand = Now();          // WPF NoteControllerActivity: every command, refused or not
            RemoteCommands.RemoteHaptics.NoteCommand();
            var strictBefore = CoreSettings.Current.StrictLockEnabled;
            var reason = RemoteCommandGate.Screen(action, LockdownService.Current?.IsActive == true);
            if (reason == null)
            {
                Log.Information("[RemoteControl] Executing: {Action}", action);
                var (done, refused) = CoreDispatch.Invoke(() =>
                {
                    try { return _execute(action, parameters); }
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
            if (action == "enable_strict_lock" && !strictBefore) _remoteSetStrictLock = true;
            CommandReceived?.Invoke(this, action);
        }

        /// <summary>WPF PushStatusNowAsync: a settings change (share avatar) reaches the controller now,
        /// not at the next ~15 s push. No-op without a session.</summary>
        public Task PushStatusNowAsync() => IsActive ? SendStatusAsync(null, null) : Task.CompletedTask;

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
