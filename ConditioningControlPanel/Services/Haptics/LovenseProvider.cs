using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Services.Haptics
{
    public enum LovenseConnectionMode
    {
        Local,
        Lan
    }

    public class LovenseProvider : IHapticProvider
    {
        private readonly HttpClient _client;
        private string _baseUrl = "http://127.0.0.1:20010";
        private string _toyId = "";
        private LovenseConnectionMode _mode = LovenseConnectionMode.Lan;

        // Throttling for continuous mode - don't spam the device
        private int _lastSentLevel = -1;
        private DateTime _lastSentTime = DateTime.MinValue;
        private static readonly TimeSpan MinCommandInterval = TimeSpan.FromMilliseconds(150);

        public string Name => _mode == LovenseConnectionMode.Local ? "Lovense Connect (PC)" : "Lovense Remote (Phone)";
        public bool IsConnected { get; private set; }
        public List<string> ConnectedDevices { get; } = new();
        public LovenseConnectionMode Mode => _mode;

        public event EventHandler<bool> ConnectionChanged;
        public event EventHandler<string> DeviceDiscovered;
        public event EventHandler<string> Error;

        public LovenseProvider()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
            };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        }

        public void SetUrl(string url) => _baseUrl = url.TrimEnd('/');
        public void SetMode(LovenseConnectionMode mode) => _mode = mode;

        public async Task<bool> ConnectAsync()
        {
            try
            {
                App.Logger?.Information("Connecting to Lovense in {Mode} mode at {Url}", _mode, _baseUrl);
                
                string content;
                if (_mode == LovenseConnectionMode.Lan)
                {
                    // Lovense Remote uses POST with JSON
                    var json = new StringContent("{\"command\":\"GetToys\"}", Encoding.UTF8, "application/json");
                    var response = await _client.PostAsync($"{_baseUrl}/command", json);
                    content = await response.Content.ReadAsStringAsync();
                }
                else
                {
                    // Lovense Connect uses GET
                    var response = await _client.GetAsync($"{_baseUrl}/command?command=GetToys");
                    content = await response.Content.ReadAsStringAsync();
                }

                App.Logger?.Information("Lovense response: {Content}", content);
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Lovense Remote returns toys as a JSON string inside data.toys
                if (root.TryGetProperty("data", out var data) && data.TryGetProperty("toys", out var toysElem))
                {
                    // toys might be a string (Remote) or object (Connect)
                    if (toysElem.ValueKind == JsonValueKind.String)
                    {
                        var toysJson = toysElem.GetString();
                        if (!string.IsNullOrEmpty(toysJson))
                        {
                            using var toysDoc = JsonDocument.Parse(toysJson);
                            if (ParseToys(toysDoc.RootElement)) return true;
                        }
                    }
                    else if (toysElem.ValueKind == JsonValueKind.Object)
                    {
                        if (ParseToys(toysElem)) return true;
                    }
                }

                Error?.Invoke(this, "No toys found. Connect toy in Lovense app first.");
                return false;
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, $"Connection failed: {ex.Message}");
                App.Logger?.Error(ex, "Lovense connection failed");
                return false;
            }
        }

        private bool ParseToys(JsonElement toys)
        {
            ConnectedDevices.Clear();
            foreach (var toy in toys.EnumerateObject())
            {
                _toyId = toy.Name;
                var nickName = toy.Value.TryGetProperty("nickName", out var n) ? n.GetString() : "";
                var toyType = toy.Value.TryGetProperty("name", out var t) ? t.GetString() : "";
                var battery = toy.Value.TryGetProperty("battery", out var b) ? b.GetInt32() : 0;
                
                var displayName = string.IsNullOrEmpty(nickName) 
                    ? $"{toyType} ({battery}%)" 
                    : $"{nickName} ({toyType}, {battery}%)";
                    
                ConnectedDevices.Add(displayName);
                DeviceDiscovered?.Invoke(this, displayName);
                App.Logger?.Information("Found toy: {Id} - {Name}", _toyId, displayName);
            }
            if (ConnectedDevices.Count > 0) 
            { 
                IsConnected = true; 
                ConnectionChanged?.Invoke(this, true); 
                return true; 
            }
            return false;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            ConnectedDevices.Clear();
            _toyId = "";
            ConnectionChanged?.Invoke(this, false);
            return Task.CompletedTask;
        }

        public async Task VibrateAsync(double intensity, int durationMs)
        {
            if (!IsConnected || string.IsNullOrEmpty(_toyId))
            {
                App.Logger?.Debug("Lovense.Vibrate: Not connected or no toyId");
                return;
            }
            try
            {
                // Convert 0-1 intensity to 0-20 level
                // Map the full range for good contrast
                int i;
                if (intensity <= 0.05) // Very quiet = off
                    i = 0;
                else // Map 0.05-1.0 to levels 3-20 (skip imperceptible 1-2)
                    i = 3 + (int)((intensity - 0.05) / 0.95 * 17);
                i = Math.Clamp(i, 0, 20);

                // For short durations (sync mode), throttle commands to avoid overwhelming device
                bool continuous = durationMs < 500;

                if (continuous)
                {
                    // Rate limit to max 5 commands/second (200ms minimum interval)
                    // Too many commands overwhelms the device
                    var now = DateTime.UtcNow;
                    var timeSinceLastCmd = now - _lastSentTime;

                    if (timeSinceLastCmd.TotalMilliseconds < 200)
                    {
                        return; // Skip - too soon
                    }

                    App.Logger?.Information("Lovense SEND: level {Level}/20", i);
                    _lastSentLevel = i;
                    _lastSentTime = now;
                }

                App.Logger?.Debug("Lovense.Vibrate: intensity={Raw:F2} -> level={Level}/20, continuous={Continuous}",
                    intensity, i, continuous);

                if (_mode == LovenseConnectionMode.Lan)
                {
                    // Lovense Remote API requires timeSec parameter
                    var sec = Math.Max(1, durationMs / 1000);
                    var cmdJson = $"{{\"command\":\"Function\",\"action\":\"Vibrate:{i}\",\"timeSec\":{sec},\"apiVer\":1}}";
                    App.Logger?.Information("Lovense sending: {Json}", cmdJson);
                    var json = new StringContent(cmdJson, Encoding.UTF8, "application/json");
                    var response = await _client.PostAsync($"{_baseUrl}/command", json);
                    var responseContent = await response.Content.ReadAsStringAsync();
                    App.Logger?.Information("Lovense response: {Status} - {Content}", response.StatusCode, responseContent);
                }
                else
                {
                    // For Lovense Connect (Local mode), use simpler command without timeSec
                    // The device maintains vibration until next command
                    var url = $"{_baseUrl}/command?command=Vibrate&action=Vibrate&intensity={i}&toy={_toyId}";
                    App.Logger?.Debug("Lovense sending GET: {Url} (level {Level})", url, i);
                    var response = await _client.GetAsync(url);
                    var responseContent = await response.Content.ReadAsStringAsync();
                    App.Logger?.Debug("Lovense response: {Status} - {Content}", response.StatusCode, responseContent);
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Vibrate failed"); }
        }

        public async Task StopAsync()
        {
            if (!IsConnected) return;
            try
            {
                if (_mode == LovenseConnectionMode.Lan)
                {
                    var json = new StringContent("{\"command\":\"Function\",\"action\":\"Stop\"}", Encoding.UTF8, "application/json");
                    await _client.PostAsync($"{_baseUrl}/command", json);
                }
                else
                {
                    await _client.GetAsync($"{_baseUrl}/command?command=Vibrate&action=Vibrate&intensity=0");
                }
                _lastSentLevel = 0;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Stop failed"); }
        }

        /// <summary>
        /// Sends a pattern of intensity values to play as a smooth sequence.
        /// Uses averaged intensity with longer duration for smoother playback.
        /// </summary>
        /// <param name="levels">Array of intensity levels (0-20)</param>
        /// <param name="totalDurationMs">Total duration for the entire pattern</param>
        public async Task VibratePatternAsync(int[] levels, int totalDurationMs)
        {
            if (!IsConnected || string.IsNullOrEmpty(_toyId) || levels.Length == 0)
                return;

            try
            {
                // Calculate weighted average - give more weight to higher values for responsiveness
                // Also find max for transient detection
                float sum = 0;
                float weightSum = 0;
                int maxLevel = 0;
                for (int i = 0; i < levels.Length; i++)
                {
                    float weight = 1f + levels[i] / 10f; // Higher levels get more weight
                    sum += levels[i] * weight;
                    weightSum += weight;
                    if (levels[i] > maxLevel) maxLevel = levels[i];
                }
                int avgLevel = (int)(sum / weightSum);

                // If there's a strong transient (max much higher than avg), use max instead
                int levelToSend = (maxLevel > avgLevel + 5) ? maxLevel : avgLevel;
                levelToSend = Math.Clamp(levelToSend, 0, 20);

                var durationSec = Math.Max(1, totalDurationMs / 1000);

                App.Logger?.Information("Lovense.Pattern: {Count} levels, avg={Avg}, max={Max}, sending={Send} for {Duration}s",
                    levels.Length, avgLevel, maxLevel, levelToSend, durationSec);

                // Skip if same level as before (unless it's been a while)
                var timeSinceLastCmd = DateTime.UtcNow - _lastSentTime;
                if (levelToSend == _lastSentLevel && timeSinceLastCmd.TotalMilliseconds < 1000)
                {
                    App.Logger?.Debug("Lovense.Pattern: Skipping same level {Level}", levelToSend);
                    return;
                }

                _lastSentLevel = levelToSend;
                _lastSentTime = DateTime.UtcNow;

                if (_mode == LovenseConnectionMode.Lan)
                {
                    // Use standard Vibrate command with longer duration
                    var cmdJson = $"{{\"command\":\"Function\",\"action\":\"Vibrate:{levelToSend}\",\"timeSec\":{durationSec},\"apiVer\":1}}";
                    App.Logger?.Information("Lovense sending: {Json}", cmdJson);
                    var json = new StringContent(cmdJson, Encoding.UTF8, "application/json");
                    var response = await _client.PostAsync($"{_baseUrl}/command", json);
                    var responseContent = await response.Content.ReadAsStringAsync();
                    App.Logger?.Information("Lovense response: {Status} - {Content}", response.StatusCode, responseContent);
                }
                else
                {
                    var url = $"{_baseUrl}/command?command=Vibrate&action=Vibrate&intensity={levelToSend}&timeSec={durationSec}&toy={_toyId}";
                    await _client.GetAsync(url);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Pattern vibrate failed");
            }
        }

        /// <summary>
        /// Converts a float intensity (0-1) to Lovense level (0-20)
        /// </summary>
        public static int IntensityToLevel(float intensity)
        {
            if (intensity <= 0.02f)
                return 0;
            else if (intensity < 0.25f)
                return 1 + (int)((intensity - 0.02f) / 0.23f * 4);
            else
                return 5 + (int)((intensity - 0.25f) / 0.75f * 15);
        }
    }
}
