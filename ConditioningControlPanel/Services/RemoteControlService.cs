using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
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
            var unifiedId = App.EffectiveUserId;
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

            var unifiedId = App.EffectiveUserId;
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

            var unifiedId = App.EffectiveUserId;
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
                    ControllerConnectedChanged?.Invoke(this, EventArgs.Empty);
                }

                // Execute commands
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
                        }
                    }

                    // Send status update if we executed commands
                    if (commands.Count > 0)
                    {
                        var lastCmd = commands[commands.Count - 1];
                        await SendStatusAsync(lastCmd["id"]?.ToString(), lastCmd["action"]?.ToString());
                    }
                }
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
            var unifiedId = App.EffectiveUserId;
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

                var body = JsonConvert.SerializeObject(new
                {
                    unified_id = unifiedId,
                    active_services = activeServices,
                    level,
                    last_executed = lastExecuted
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
            }
            catch { }
            return services;
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

                        case "show_pink_filter":
                            if (App.Settings?.Current != null)
                            {
                                var mwPinkOn = System.Windows.Application.Current.MainWindow as MainWindow;
                                mwPinkOn?.EnablePinkFilter(true);
                                if (App.Overlay != null && !App.Overlay.IsRunning) App.Overlay.Start();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "stop_pink_filter":
                            if (App.Settings?.Current != null)
                            {
                                var mwPinkOff = System.Windows.Application.Current.MainWindow as MainWindow;
                                mwPinkOff?.EnablePinkFilter(false);
                                if (App.Overlay != null && !App.Overlay.IsRunning) App.Overlay.Start();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "show_spiral":
                            if (App.Settings?.Current != null)
                            {
                                var mwSpiralOn = System.Windows.Application.Current.MainWindow as MainWindow;
                                mwSpiralOn?.EnableSpiral(true);
                                if (App.Overlay != null && !App.Overlay.IsRunning) App.Overlay.Start();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "stop_spiral":
                            if (App.Settings?.Current != null)
                            {
                                var mwSpiralOff = System.Windows.Application.Current.MainWindow as MainWindow;
                                mwSpiralOff?.EnableSpiral(false);
                                if (App.Overlay != null && !App.Overlay.IsRunning) App.Overlay.Start();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "set_pink_opacity":
                            if (App.Settings?.Current != null && parameters != null)
                            {
                                var pinkVal = parameters["value"]?.Value<int>() ?? 25;
                                App.Settings.Current.PinkFilterOpacity = Math.Clamp(pinkVal, 0, 50);
                                if (App.Overlay != null && !App.Overlay.IsRunning) App.Overlay.Start();
                                App.Overlay?.RefreshOverlays();
                                App.Settings.Save();
                            }
                            break;

                        case "set_spiral_opacity":
                            if (App.Settings?.Current != null && parameters != null)
                            {
                                var spiralVal = parameters["value"]?.Value<int>() ?? 25;
                                App.Settings.Current.SpiralOpacity = Math.Clamp(spiralVal, 0, 50);
                                if (App.Overlay != null && !App.Overlay.IsRunning) App.Overlay.Start();
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

                        case "start_session":
                            // Create a default 30-minute session
                            var session = new Models.Session
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
                            (System.Windows.Application.Current.MainWindow as MainWindow)
                                ?.TriggerPanicFromRemote();
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