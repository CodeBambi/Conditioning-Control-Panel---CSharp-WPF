using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services
{
    public class SessionProgressInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";
        [JsonProperty("icon")]
        public string Icon { get; set; } = "";
        [JsonProperty("elapsed_seconds")]
        public int ElapsedSeconds { get; set; }
        [JsonProperty("total_seconds")]
        public int TotalSeconds { get; set; }
        [JsonProperty("is_paused")]
        public bool IsPaused { get; set; }
        [JsonProperty("current_phase")]
        public string CurrentPhase { get; set; } = "";
    }

    public class RemoteControlService : IDisposable
    {
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        // 5s gives comfortable headroom under the server's 40/min per-user poll cap
        // (12/min steady-state) while keeping perceived latency negligible.
        private const double PollIntervalSeconds = 5.0;
        // Status pushes are throttled — the controller UI doesn't need 3s-fresh status.
        // Push immediately on command execution or controller-connected state change.
        private const double StatusPushIntervalSeconds = 15.0;
        // When a status push hits 429, skip subsequent pushes for this long.
        private const double StatusBackoffSeconds = 60.0;

        private readonly HttpClient _httpClient;
        private DispatcherTimer? _pollTimer;
        private bool _disposed;

        private static readonly Random _pinRng = new();

        public bool IsActive { get; private set; }
        public string? SessionCode { get; private set; }
        public string? ConnectPin { get; private set; }
        public string? Tier { get; private set; }
        public bool ControllerConnected { get; private set; }
        public bool ControllerIdle { get; private set; }

        public event EventHandler? ControllerConnectedChanged;
        public event EventHandler? ControllerIdleChanged;
        public event EventHandler<string>? CommandReceived;
        public event EventHandler? SessionStarted;
        public event EventHandler? SessionEnded;

        // Direct reference to MainWindow — Application.Current.MainWindow becomes null when hidden to tray
        public MainWindow? MainWindowRef { get; set; }

        public Func<List<object>>? GetAvailableSessionsCallback { get; set; }
        public Func<SessionProgressInfo?>? GetSessionProgressCallback { get; set; }
        public Func<string, Models.Session?>? FindSessionByIdCallback { get; set; }

        public RemoteControlService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _httpClient.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");
        }

        /// <summary>
        /// Creates a POST request with the X-Auth-Token header attached.
        /// All V2 endpoints require authentication.
        /// </summary>
        private async Task<HttpResponseMessage> AuthPostAsync(string url, string jsonBody)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            var token = App.Settings?.Current?.AuthToken;
            if (!string.IsNullOrEmpty(token))
                request.Headers.Add("X-Auth-Token", token);
            return await _httpClient.SendAsync(request);
        }

        /// <summary>
        /// Starts a remote control session with the given tier.
        /// </summary>
        public async Task<string?> StartSessionAsync(string tier)
        {
            var unifiedId = App.UnifiedUserId;
            if (string.IsNullOrEmpty(unifiedId))
            {
                App.Logger?.Warning("[RemoteControl] Cannot start: no unified ID");
                return null;
            }

            try
            {
                // Generate a random 4-digit PIN for controller authentication
                var pin = _pinRng.Next(0, 10000).ToString("D4");

                var body = JsonConvert.SerializeObject(new { unified_id = unifiedId, tier, connect_pin = pin });
                using var response = await AuthPostAsync($"{ProxyBaseUrl}/v2/remote/start", body);

                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("[RemoteControl] Start failed: {Status} {Body}", response.StatusCode, json);
                    return null;
                }

                var result = JObject.Parse(json);
                SessionCode = result["code"]?.ToString();
                ConnectPin = pin;
                Tier = tier;
                IsActive = true;
                _consecutivePollFailures = 0;
                _consecutivePollSuccesses = 0;
                _totalCommandsReceived = 0;
                _lastHealthLog = DateTime.MinValue;
                _sessionStartTime = DateTime.UtcNow;
                _currentPollInterval = PollIntervalSeconds;
                _lastStatusPushUtc = DateTime.MinValue;
                _statusBackoffUntil = DateTime.MinValue;

                // Start polling
                _pollTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(PollIntervalSeconds)
                };
                _pollTimer.Tick += async (s, e) => await PollForCommandsAsync();
                _pollTimer.Start();

                App.Logger?.Information("[RemoteControl] Session started: {Code}, tier: {Tier}", SessionCode, tier);
                SessionStarted?.Invoke(this, EventArgs.Empty);

                return SessionCode;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RemoteControl] Start error");
                return null;
            }
        }

        /// <summary>
        /// Stops the remote control session.
        /// </summary>
        public async Task StopSessionAsync()
        {
            if (!IsActive) return;

            var unifiedId = App.UnifiedUserId;
            if (!string.IsNullOrEmpty(unifiedId))
            {
                try
                {
                    var body = JsonConvert.SerializeObject(new { unified_id = unifiedId });
                    using var response = await AuthPostAsync($"{ProxyBaseUrl}/v2/remote/stop", body);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "[RemoteControl] Stop request failed");
                }
            }

            CleanupSession();
            App.Logger?.Information("[RemoteControl] Session stopped");
        }

        private void CleanupSession()
        {
            _pollTimer?.Stop();
            _pollTimer = null;
            _consecutivePollFailures = 0;
            _consecutivePollSuccesses = 0;
            _currentPollInterval = PollIntervalSeconds;
            _controllerIdleSince = null;
            _controllerAutoDisconnected = false;
            _lastStatusPushUtc = DateTime.MinValue;
            _statusBackoffUntil = DateTime.MinValue;
            IsActive = false;
            SessionCode = null;
            ConnectPin = null;
            Tier = null;
            ControllerIdle = false;

            // Stop all effects that were triggered by the remote controller
            DispatcherHelper.RunOnUISync(() => StopAllRemoteEffects());

            // Reset overlay level bypass when remote session ends
            if (App.Overlay != null)
                App.Overlay.BypassLevelCheck = false;

            if (ControllerConnected)
            {
                ControllerConnected = false;
                ControllerConnectedChanged?.Invoke(this, EventArgs.Empty);
            }

            SessionEnded?.Invoke(this, EventArgs.Empty);
        }

        private bool _pollInProgress;
        private int _consecutivePollFailures;
        private int _consecutivePollSuccesses;
        private int _totalCommandsReceived;
        private DateTime _lastHealthLog = DateTime.MinValue;
        private DateTime _sessionStartTime = DateTime.MinValue;
        private double _currentPollInterval = PollIntervalSeconds;
        private const double MaxBackoffSeconds = 60.0;
        private const int HealthLogIntervalSeconds = 30;
        private DateTime? _controllerIdleSince;
        private bool _controllerAutoDisconnected;
        private const double IdleAutoDisconnectSeconds = 120.0; // 2 minutes
        private DateTime _lastStatusPushUtc = DateTime.MinValue;
        private DateTime _statusBackoffUntil = DateTime.MinValue;

        private async Task PollForCommandsAsync()
        {
            if (!IsActive) return;
            if (_pollInProgress) return; // Skip if previous poll still running (timer re-entrance)
            _pollInProgress = true;
            try
            {
                var unifiedId = App.UnifiedUserId;
                if (string.IsNullOrEmpty(unifiedId)) return;

                try
                {
                var body = JsonConvert.SerializeObject(new { unified_id = unifiedId });
                using var response = await AuthPostAsync($"{ProxyBaseUrl}/v2/remote/poll", body);

                if (!response.IsSuccessStatusCode)
                {
                    _consecutivePollFailures++;
                    _consecutivePollSuccesses = 0;

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        App.Logger?.Warning("[RemoteControl] Session expired during poll");
                        CleanupSession();
                    }
                    else if (response.StatusCode == (System.Net.HttpStatusCode)429)
                    {
                        // Rate limited — exponential backoff
                        _currentPollInterval = Math.Min(_currentPollInterval * 2, MaxBackoffSeconds);
                        if (_pollTimer != null)
                            _pollTimer.Interval = TimeSpan.FromSeconds(_currentPollInterval);
                        App.Logger?.Warning("[RemoteControl] Rate limited (429) [code={Code}], backing off to {Interval}s", SessionCode ?? "?", _currentPollInterval);
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        App.Logger?.Warning("[RemoteControl] Auth failure (401), consecutive: {Count}", _consecutivePollFailures);
                        if (_consecutivePollFailures >= 3)
                        {
                            App.Logger?.Error("[RemoteControl] 3 consecutive auth failures — terminating session");
                            CleanupSession();
                        }
                    }
                    else if (_consecutivePollFailures >= 5)
                    {
                        App.Logger?.Error("[RemoteControl] Poll failed: {Status} (consecutive failures: {Count})", response.StatusCode, _consecutivePollFailures);
                    }
                    else
                    {
                        App.Logger?.Warning("[RemoteControl] Poll failed: {Status}", response.StatusCode);
                    }
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JObject.Parse(json);

                // Track success — recover from backoff if needed
                var wasBackedOff = _currentPollInterval > PollIntervalSeconds;
                var wasFailingConsecutively = _consecutivePollFailures > 0;
                _consecutivePollSuccesses++;
                _consecutivePollFailures = 0;

                if (wasBackedOff)
                {
                    _currentPollInterval = PollIntervalSeconds;
                    if (_pollTimer != null)
                        _pollTimer.Interval = TimeSpan.FromSeconds(PollIntervalSeconds);
                    App.Logger?.Information("[RemoteControl] Recovered from backoff, restoring {Interval}s poll interval", PollIntervalSeconds);
                }
                else if (wasFailingConsecutively && _consecutivePollSuccesses == 1)
                {
                    App.Logger?.Information("[RemoteControl] Poll recovered after failures");
                }

                // Periodic health log
                var now = DateTime.UtcNow;
                if ((now - _lastHealthLog).TotalSeconds >= HealthLogIntervalSeconds)
                {
                    _lastHealthLog = now;
                    var uptime = now - _sessionStartTime;
                    App.Logger?.Information(
                        "[RemoteControl] Health: code={Code} uptime={Uptime} polls_ok={Successes} cmds_total={Cmds} controller={Status}",
                        SessionCode,
                        $"{(int)uptime.TotalMinutes}m{uptime.Seconds}s",
                        _consecutivePollSuccesses,
                        _totalCommandsReceived,
                        ControllerConnected ? (ControllerIdle ? "idle" : "active") : "disconnected");
                }

                // Update controller connection status.
                // The server only sets controller_connected=false on explicit disconnect
                // (POST /remote/disconnect), NOT on ping staleness.
                var serverConnected = result["controller_connected"]?.Value<bool>() ?? false;
                var connected = serverConnected;
                var idle = result["controller_idle"]?.Value<bool>() ?? false;

                if (connected && _controllerAutoDisconnected)
                {
                    if (!idle)
                    {
                        // Controller is actively pinging again — treat as reconnect
                        _controllerAutoDisconnected = false;
                        _controllerIdleSince = null;
                        // Fall through to normal connect flow below
                    }
                    else
                    {
                        // Still idle after auto-disconnect — suppress reconnect
                        connected = false;
                    }
                }

                // Only clear auto-disconnect flag when the SERVER itself reports
                // the controller as disconnected — NOT when our local override
                // suppressed reconnect above (which also sets connected=false).
                if (!serverConnected)
                    _controllerAutoDisconnected = false;

                var controllerConnectedChanged = connected != ControllerConnected;
                if (controllerConnectedChanged)
                {
                    ControllerConnected = connected;
                    if (connected)
                    {
                        // Stop the engine so only the controller triggers effects
                        if (MainWindowRef?.IsEngineRunning == true)
                            MainWindowRef.StopEngine();

                        // Ensure overlay service is ready for remote commands
                        EnsureOverlayRunning();
                    }
                    else
                    {
                        // Controller disconnected. By default we leave effects running
                        // so a new controller can see the current state and the sub
                        // isn't snapped to a halt mid-session. Opt-in setting stops them.
                        if (App.Settings?.Current?.StopEffectsOnRemoteDisconnect == true)
                            StopRemoteTriggeredEffects();
                    }
                    ControllerConnectedChanged?.Invoke(this, EventArgs.Empty);
                }

                // Track idle status for UI + auto-disconnect timeout
                if (idle != ControllerIdle)
                {
                    ControllerIdle = idle;
                    _controllerIdleSince = idle ? DateTime.UtcNow : null;
                    ControllerIdleChanged?.Invoke(this, EventArgs.Empty);
                }

                // Auto-disconnect controller after prolonged idle
                if (ControllerConnected && idle && _controllerIdleSince != null)
                {
                    var idleDuration = (DateTime.UtcNow - _controllerIdleSince.Value).TotalSeconds;
                    if (idleDuration >= IdleAutoDisconnectSeconds)
                    {
                        App.Logger?.Information("[RemoteControl] Controller idle for {Seconds:F0}s — auto-disconnecting", idleDuration);
                        _controllerAutoDisconnected = true;
                        ControllerConnected = false;
                        if (App.Settings?.Current?.StopEffectsOnRemoteDisconnect == true)
                            StopRemoteTriggeredEffects();
                        ControllerConnectedChanged?.Invoke(this, EventArgs.Empty);
                        ControllerIdle = false;
                        ControllerIdleChanged?.Invoke(this, EventArgs.Empty);
                    }
                }

                // Execute commands
                string? lastCmdId = null;
                string? lastAction = null;
                var commands = result["commands"] as JArray;
                if (commands != null && commands.Count > 0)
                {
                    _totalCommandsReceived += commands.Count;
                    App.Logger?.Information("[RemoteControl] Poll returned {Count} command(s), session total: {Total}", commands.Count, _totalCommandsReceived);
                }
                if (commands != null)
                {
                    foreach (var cmd in commands)
                    {
                        var action = cmd["action"]?.ToString();
                        var id = cmd["id"]?.ToString();
                        if (!string.IsNullOrEmpty(action))
                        {
                            App.Logger?.Information("[RemoteControl] Executing: {Action} (id: {Id})", action, id);
                            ExecuteCommand(action, cmd["params"] as JObject);
                            CommandReceived?.Invoke(this, action);
                            lastCmdId = id;
                            lastAction = action;
                        }
                    }
                }

                // Throttle status pushes. Push immediately on command execution or
                // controller-connected state change; otherwise only every ~15s.
                // This roughly halves client→server traffic and keeps us well under
                // the server's per-user 40/min cap on both /poll and /status.
                var statusDue = (DateTime.UtcNow - _lastStatusPushUtc).TotalSeconds >= StatusPushIntervalSeconds;
                if (lastCmdId != null || controllerConnectedChanged || statusDue)
                {
                    await SendStatusAsync(lastCmdId, lastAction);
                }
            }
            catch (TaskCanceledException)
            {
                _consecutivePollFailures++;
                if (_consecutivePollFailures >= 3)
                    App.Logger?.Warning("[RemoteControl] Poll timeout (consecutive: {Count})", _consecutivePollFailures);
            }
            catch (Exception ex)
            {
                _consecutivePollFailures++;
                App.Logger?.Warning(ex, "[RemoteControl] Poll error (consecutive: {Count})", _consecutivePollFailures);
            }
            }
            finally
            {
                _pollInProgress = false;
            }
        }

        private async Task SendStatusAsync(string? lastCmdId = null, string? lastAction = null)
        {
            // Skip while we're in backoff from a previous 429 on /status.
            if (DateTime.UtcNow < _statusBackoffUntil) return;

            var unifiedId = App.UnifiedUserId;
            if (string.IsNullOrEmpty(unifiedId)) return;

            try
            {
                var activeServices = GetActiveServices();
                var level = App.Settings?.Current?.PlayerLevel ?? 1;

                object? lastExecuted = null;
                if (lastCmdId != null)
                {
                    lastExecuted = new
                    {
                        id = lastCmdId,
                        action = lastAction,
                        status = "ok",
                        at = DateTime.UtcNow.ToString("o")
                    };
                }

                // Get available sessions and current session progress
                var availableSessions = GetAvailableSessionsCallback?.Invoke();
                var sessionInfo = GetSessionProgressCallback?.Invoke();

                var body = JsonConvert.SerializeObject(new
                {
                    unified_id = unifiedId,
                    active_services = activeServices,
                    level,
                    last_executed = lastExecuted,
                    available_sessions = availableSessions,
                    session_info = sessionInfo
                });

                using var response = await AuthPostAsync($"{ProxyBaseUrl}/v2/remote/status", body);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == (System.Net.HttpStatusCode)429)
                    {
                        _statusBackoffUntil = DateTime.UtcNow.AddSeconds(StatusBackoffSeconds);
                        App.Logger?.Warning(
                            "[RemoteControl] Status push rate limited (429) [code={Code}] — suppressing status pushes for {Seconds}s",
                            SessionCode ?? "?", StatusBackoffSeconds);
                    }
                    else
                    {
                        App.Logger?.Warning(
                            "[RemoteControl] Status push failed: {Status} [code={Code}]",
                            response.StatusCode, SessionCode ?? "?");
                    }
                }
                else
                {
                    _lastStatusPushUtc = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[RemoteControl] Status update error");
            }
        }

        private List<string> GetActiveServices()
        {
            var services = new List<string>();
            try
            {
                if (App.Settings?.Current?.PinkFilterEnabled == true) services.Add("pink_filter");
                if (App.Settings?.Current?.SpiralEnabled == true) services.Add("spiral");
                if (App.Settings?.Current?.StrictLockEnabled == true) services.Add("strict_lock");
                if (App.Settings?.Current?.PanicKeyEnabled == false) services.Add("no_panic");
                if (App.Autonomy?.IsEnabled == true) services.Add("autonomy");
                if (App.IsSessionRunning) services.Add("session");
                if (App.Flash?.IsRunning == true) services.Add("flash_loop");
                if (App.Video?.IsRunning == true) services.Add("video_loop");
                if (App.Subliminal?.IsRunning == true) services.Add("subliminal_loop");
                if (App.LockCard?.IsRunning == true) services.Add("lock_card");
                if (App.MindWipe?.IsRunning == true) services.Add("mind_wipe");
                if (App.BouncingText?.IsRunning == true) services.Add("bounce_text");
                if (App.Wallpaper?.IsActive == true) services.Add("wallpaper");
            }
            catch { }
            return services;
        }

        private void StopAllRemoteEffects()
        {
            try
            {
                App.Logger?.Information("[RemoteControl] Stopping all remote effects");

                App.KillAllAudio();
                App.Autonomy?.CancelActivePulses();
                App.Autonomy?.Stop();

                App.Video?.Stop();
                App.Flash?.Stop();
                App.Subliminal?.Stop();
                App.Bubbles?.Stop();
                App.BouncingText?.Stop();
                App.BubbleCount?.Stop();
                App.MindWipe?.Stop();
                App.BrainDrain?.Stop();
                App.LockCard?.Stop();
                App.Wallpaper?.Deactivate();

                // Force close any open game/lock windows
                LockCardWindow.ForceCloseAll();
                BubbleCountWindow.ForceCloseAll();

                // Turn off overlays
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.PinkFilterEnabled = false;
                    App.Settings.Current.SpiralEnabled = false;
                    App.Settings.Current.StrictLockEnabled = false;
                    App.Settings.Current.PanicKeyEnabled = true;
                }
                App.Overlay?.RefreshOverlays();

                App.InteractionQueue?.ForceReset();

                // Stop session engine and main engine if running
                MainWindowRef?.StopSessionFromRemote();

                // Sync checkbox state and bring window to front
                if (MainWindowRef != null)
                {
                    MainWindowRef.EnablePinkFilter(false);
                    MainWindowRef.EnableSpiral(false);
                    MainWindowRef.RestoreFromTrayForRemote();
                    MainWindowRef.ShowAvatarTube();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RemoteControl] Failed to stop remote effects");
            }
        }

        /// <summary>
        /// Lighter cleanup for controller disconnect — stops remote-triggered effects
        /// but preserves the user's engine and autonomy state.
        /// </summary>
        private void StopRemoteTriggeredEffects()
        {
            try
            {
                App.Logger?.Information("[RemoteControl] Controller disconnected — cleaning up remote effects only");

                App.Autonomy?.CancelActivePulses();

                App.Video?.Stop();
                App.Flash?.Stop();
                App.Subliminal?.Stop();
                App.Bubbles?.Stop();
                App.BouncingText?.Stop();
                App.BubbleCount?.Stop();
                App.MindWipe?.Stop();
                App.BrainDrain?.Stop();
                App.LockCard?.Stop();
                App.Wallpaper?.Deactivate();

                LockCardWindow.ForceCloseAll();
                BubbleCountWindow.ForceCloseAll();

                // Reset overlays that were enabled by remote
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.PinkFilterEnabled = false;
                    App.Settings.Current.SpiralEnabled = false;
                    App.Settings.Current.StrictLockEnabled = false;
                    App.Settings.Current.PanicKeyEnabled = true;
                }
                App.Overlay?.RefreshOverlays();

                App.InteractionQueue?.ForceReset();

                // Restore window visibility but don't stop engine/autonomy
                if (MainWindowRef != null)
                {
                    MainWindowRef.EnablePinkFilter(false);
                    MainWindowRef.EnableSpiral(false);
                    MainWindowRef.RestoreFromTrayForRemote();
                    MainWindowRef.ShowAvatarTube();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RemoteControl] Failed to clean up remote effects on disconnect");
            }
        }

        private void EnsureOverlayRunning()
        {
            if (App.Overlay == null) return;
            if (!App.Overlay.IsRunning)
            {
                App.Overlay.BypassLevelCheck = true;
                App.Overlay.Start();
            }
            else if (!App.Overlay.BypassLevelCheck)
            {
                App.Overlay.BypassLevelCheck = true;
            }
        }

        private void ExecuteCommand(string action, JObject? parameters)
        {
            DispatcherHelper.RunOnUISync(() =>
            {
                try
                {
                    switch (action)
                    {
                        // Light tier
                        case "trigger_flash":
                            App.Flash?.TriggerFlashOnce();
                            break;

                        case "trigger_subliminal":
                            App.Subliminal?.FlashSubliminal();
                            break;

                        case "start_flash":
                            if (App.Settings?.Current != null)
                                App.Settings.Current.FlashEnabled = true;
                            App.Flash?.Start();
                            break;

                        case "stop_flash":
                            App.Flash?.Stop();
                            break;

                        case "start_subliminal":
                            if (App.Settings?.Current != null)
                                App.Settings.Current.SubliminalEnabled = true;
                            App.Subliminal?.Start();
                            break;

                        case "stop_subliminal":
                            App.Subliminal?.Stop();
                            break;

                        case "trigger_custom_subliminal":
                            var customText = parameters?["text"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(customText))
                                App.Subliminal?.FlashSubliminalCustom(customText);
                            break;

                        case "show_pink_filter":
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.PinkFilterEnabled = true;
                                MainWindowRef?.EnablePinkFilter(true);
                                EnsureOverlayRunning();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "stop_pink_filter":
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.PinkFilterEnabled = false;
                                MainWindowRef?.EnablePinkFilter(false);
                                EnsureOverlayRunning();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "show_spiral":
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.SpiralEnabled = true;
                                MainWindowRef?.EnableSpiral(true);
                                EnsureOverlayRunning();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "stop_spiral":
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.SpiralEnabled = false;
                                MainWindowRef?.EnableSpiral(false);
                                EnsureOverlayRunning();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "set_pink_opacity":
                            if (App.Settings?.Current != null && parameters != null)
                            {
                                var pinkVal = parameters["value"]?.Value<int>() ?? 25;
                                App.Settings.Current.PinkFilterOpacity = Math.Clamp(pinkVal, 0, 50);
                                EnsureOverlayRunning();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "set_spiral_opacity":
                            if (App.Settings?.Current != null && parameters != null)
                            {
                                var spiralVal = parameters["value"]?.Value<int>() ?? 25;
                                App.Settings.Current.SpiralOpacity = Math.Clamp(spiralVal, 0, 50);
                                EnsureOverlayRunning();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "start_bubbles":
                            App.Bubbles?.Start(bypassLevelCheck: true);
                            break;

                        case "stop_bubbles":
                            App.Bubbles?.Stop();
                            break;

                        // Standard tier
                        case "trigger_video":
                            App.Video?.TriggerVideo();
                            break;

                        case "start_video":
                            App.Video?.Start();
                            break;

                        case "stop_video":
                            App.Video?.Stop();
                            break;

                        case "trigger_haptic":
                            _ = App.Haptics?.TriggerAsync("remote_control", 0.7, 2000);
                            break;

                        case "duck_audio":
                            App.Audio?.Duck(80);
                            break;

                        case "unduck_audio":
                            App.Audio?.ForceUnduck();
                            break;

                        // Full tier
                        case "start_autonomy":
                            App.Autonomy?.Start();
                            break;

                        case "stop_autonomy":
                            App.Autonomy?.Stop();
                            break;

                        case "trigger_bubble_count":
                            App.BubbleCount?.TriggerGame(forceTest: true);
                            break;

                        case "trigger_lock_card":
                            App.LockCard?.ShowLockCard();
                            break;

                        case "start_lock_card":
                            if (App.Settings?.Current != null) App.Settings.Current.LockCardEnabled = true;
                            App.LockCard?.Start();
                            break;

                        case "stop_lock_card":
                            App.LockCard?.Stop();
                            break;

                        case "trigger_mind_wipe":
                            if (App.MindWipe?.AudioFileCount > 0)
                                App.MindWipe?.TriggerOnce();
                            break;

                        case "start_mind_wipe":
                            var freq = App.Settings?.Current?.MindWipeFrequency ?? 6;
                            var vol = (App.Settings?.Current?.MindWipeVolume ?? 50) / 100.0;
                            App.MindWipe?.Start(freq, vol);
                            break;

                        case "stop_mind_wipe":
                            App.MindWipe?.Stop();
                            break;

                        case "start_bounce_text":
                            App.BouncingText?.Start(bypassLevelCheck: true);
                            break;

                        case "stop_bounce_text":
                            App.BouncingText?.Stop();
                            break;

                        case "start_session":
                            // Look up requested session by ID, fall back to generic
                            var sessionId = parameters?["session_id"]?.ToString();
                            Models.Session? session = null;
                            if (!string.IsNullOrEmpty(sessionId))
                            {
                                session = FindSessionByIdCallback?.Invoke(sessionId);
                            }
                            // Fall back to a generic session that preserves
                            // the user's current settings (so effects keep running)
                            if (session == null)
                            {
                                var cur = App.Settings?.Current;
                                session = new Models.Session
                                {
                                    Id = "remote_session",
                                    Name = "Remote Session",
                                    Icon = "🎮",
                                    DurationMinutes = 30,
                                    Difficulty = Models.SessionDifficulty.Medium,
                                    BonusXP = 200,
                                    Settings = new Models.SessionSettings
                                    {
                                        FlashEnabled = cur?.FlashEnabled ?? true,
                                        FlashPerHour = cur?.FlashFrequency ?? 10,
                                        FlashOpacity = cur?.FlashOpacity ?? 100,
                                        FlashImages = cur?.SimultaneousImages ?? 1,
                                        FlashClickable = cur?.FlashClickable ?? false,
                                        FlashAudioEnabled = cur?.FlashAudioEnabled ?? false,
                                        SubliminalEnabled = cur?.SubliminalEnabled ?? true,
                                        SubliminalPerMin = cur?.SubliminalFrequency ?? 5,
                                        SubliminalOpacity = cur?.SubliminalOpacity ?? 100,
                                        SubliminalFrames = cur?.SubliminalDuration ?? 5,
                                        MandatoryVideosEnabled = cur?.MandatoryVideosEnabled ?? false,
                                        BubblesEnabled = cur?.BubblesEnabled ?? false,
                                    }
                                };
                            }
                            if (MainWindowRef != null)
                            {
                                MainWindowRef.StartSessionFromRemote(session);
                            }
                            if (parameters?["strict_lock"]?.Value<bool>() == true)
                            {
                                if (App.Settings?.Current != null)
                                {
                                    App.Settings.Current.StrictLockEnabled = true;
                                    App.Settings.Save();
                                }
                            }
                            break;

                        case "pause_session":
                            MainWindowRef
                                ?.PauseSessionFromRemote();
                            break;

                        case "resume_session":
                            MainWindowRef
                                ?.ResumeSessionFromRemote();
                            break;

                        case "stop_session":
                            MainWindowRef
                                ?.StopSessionFromRemote();
                            break;

                        case "enable_strict_lock":
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.StrictLockEnabled = true;
                                App.Settings.Save();
                            }
                            break;

                        case "disable_strict_lock":
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.StrictLockEnabled = false;
                                App.Settings.Save();
                            }
                            break;

                        case "disable_panic":
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.PanicKeyEnabled = false;
                                App.Settings.Save();
                            }
                            break;

                        case "enable_panic":
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.PanicKeyEnabled = true;
                                App.Settings.Save();
                            }
                            break;

                        case "trigger_wallpaper":
                            if (App.Wallpaper?.IsActive == true)
                                App.Wallpaper.Shuffle();
                            else
                                App.Wallpaper?.Activate();
                            break;

                        case "stop_wallpaper":
                            App.Wallpaper?.Deactivate();
                            break;

                        case "trigger_panic":
                            StopAllRemoteEffects();
                            break;

                        default:
                            App.Logger?.Warning("[RemoteControl] Unknown action: {Action}", action);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "[RemoteControl] Error executing command: {Action}", action);
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pollTimer?.Stop();
            _httpClient.Dispose();
        }
    }
}