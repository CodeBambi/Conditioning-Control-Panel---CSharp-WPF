using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    public class SessionProgressInfo
    {
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "";
        public int ElapsedSeconds { get; set; }
        public int TotalSeconds { get; set; }
        public bool IsPaused { get; set; }
        public string CurrentPhase { get; set; } = "";
    }

    public class RemoteControlService : IDisposable
    {
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        private const double PollIntervalSeconds = 3.0;

        private readonly HttpClient _httpClient;
        private DispatcherTimer? _pollTimer;
        private bool _disposed;

        public bool IsActive { get; private set; }
        public string? SessionCode { get; private set; }
        public string? Tier { get; private set; }
        public bool ControllerConnected { get; private set; }

        public event EventHandler? ControllerConnectedChanged;
        public event EventHandler<string>? CommandReceived;
        public event EventHandler? SessionStarted;
        public event EventHandler? SessionEnded;

        public Func<List<object>>? GetAvailableSessionsCallback { get; set; }
        public Func<SessionProgressInfo?>? GetSessionProgressCallback { get; set; }
        public Func<string, Models.Session?>? FindSessionByIdCallback { get; set; }

        public RemoteControlService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
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
                var body = JsonConvert.SerializeObject(new { unified_id = unifiedId, tier });
                var response = await _httpClient.PostAsync(
                    $"{ProxyBaseUrl}/v2/remote/start",
                    new StringContent(body, Encoding.UTF8, "application/json"));

                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("[RemoteControl] Start failed: {Status} {Body}", response.StatusCode, json);
                    return null;
                }

                var result = JObject.Parse(json);
                SessionCode = result["code"]?.ToString();
                Tier = tier;
                IsActive = true;

                // Start polling
                _pollTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(PollIntervalSeconds)
                };
                _pollTimer.Tick += async (s, e) => await PollForCommandsAsync();
                _pollTimer.Start();

                App.Logger?.Information("[RemoteControl] Session started: {Code}, tier: {Tier}", SessionCode, tier);
                SessionStarted?.Invoke(this, EventArgs.Empty);

                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    (System.Windows.Application.Current?.MainWindow as MainWindow)?.MinimizeToTrayForRemote();
                });

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
                    await _httpClient.PostAsync(
                        $"{ProxyBaseUrl}/v2/remote/stop",
                        new StringContent(body, Encoding.UTF8, "application/json"));
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
            IsActive = false;
            SessionCode = null;
            Tier = null;

            // Stop all effects that were triggered by the remote controller
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => StopAllRemoteEffects());
            }

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

        private async Task PollForCommandsAsync()
        {
            if (!IsActive) return;

            var unifiedId = App.UnifiedUserId;
            if (string.IsNullOrEmpty(unifiedId)) return;

            try
            {
                var body = JsonConvert.SerializeObject(new { unified_id = unifiedId });
                var response = await _httpClient.PostAsync(
                    $"{ProxyBaseUrl}/v2/remote/poll",
                    new StringContent(body, Encoding.UTF8, "application/json"));

                if (!response.IsSuccessStatusCode)
                {
                    // Session may have expired
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        App.Logger?.Warning("[RemoteControl] Session expired during poll");
                        CleanupSession();
                    }
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JObject.Parse(json);

                // Update controller connection status
                var connected = result["controller_connected"]?.Value<bool>() ?? false;
                if (connected != ControllerConnected)
                {
                    ControllerConnected = connected;
                    if (connected)
                    {
                        // Stop the engine so only the controller triggers effects
                        var mw = System.Windows.Application.Current?.MainWindow as MainWindow;
                        if (mw?.IsEngineRunning == true)
                            mw.StopEngine();

                        // Ensure overlay service is ready for remote commands
                        EnsureOverlayRunning();
                    }
                    else
                    {
                        // Controller disconnected — stop all effects they triggered
                        StopAllRemoteEffects();
                    }
                    ControllerConnectedChanged?.Invoke(this, EventArgs.Empty);
                }

                // Execute commands
                string? lastCmdId = null;
                string? lastAction = null;
                var commands = result["commands"] as JArray;
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

                // Always send status so the remote controller sees current CCP state
                await SendStatusAsync(lastCmdId, lastAction);
            }
            catch (TaskCanceledException)
            {
                // Timeout, ignore
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[RemoteControl] Poll error");
            }
        }

        private async Task SendStatusAsync(string? lastCmdId = null, string? lastAction = null)
        {
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

                await _httpClient.PostAsync(
                    $"{ProxyBaseUrl}/v2/remote/status",
                    new StringContent(body, Encoding.UTF8, "application/json"));
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

                // Sync checkbox state and bring window to front
                var mw = System.Windows.Application.Current?.MainWindow as MainWindow;
                if (mw != null)
                {
                    mw.EnablePinkFilter(false);
                    mw.EnableSpiral(false);
                    mw.RestoreFromTrayForRemote();
                    mw.ShowAvatarTube();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RemoteControl] Failed to stop remote effects");
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
            if (System.Windows.Application.Current?.Dispatcher == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
                            App.Flash?.Start();
                            break;

                        case "stop_flash":
                            App.Flash?.Stop();
                            break;

                        case "start_subliminal":
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
                                (System.Windows.Application.Current.MainWindow as MainWindow)?.EnablePinkFilter(true);
                                EnsureOverlayRunning();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "stop_pink_filter":
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.PinkFilterEnabled = false;
                                (System.Windows.Application.Current.MainWindow as MainWindow)?.EnablePinkFilter(false);
                                EnsureOverlayRunning();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "show_spiral":
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.SpiralEnabled = true;
                                (System.Windows.Application.Current.MainWindow as MainWindow)?.EnableSpiral(true);
                                EnsureOverlayRunning();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "stop_spiral":
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.SpiralEnabled = false;
                                (System.Windows.Application.Current.MainWindow as MainWindow)?.EnableSpiral(false);
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
                            session ??= new Models.Session
                            {
                                Id = "remote_session",
                                Name = "Remote Session",
                                Icon = "🎮",
                                DurationMinutes = 30,
                                Difficulty = Models.SessionDifficulty.Medium,
                                BonusXP = 200
                            };
                            var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                            if (mainWindow != null)
                            {
                                mainWindow.StartSessionFromRemote(session);
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
                            (System.Windows.Application.Current.MainWindow as MainWindow)
                                ?.PauseSessionFromRemote();
                            break;

                        case "resume_session":
                            (System.Windows.Application.Current.MainWindow as MainWindow)
                                ?.ResumeSessionFromRemote();
                            break;

                        case "stop_session":
                            (System.Windows.Application.Current.MainWindow as MainWindow)
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