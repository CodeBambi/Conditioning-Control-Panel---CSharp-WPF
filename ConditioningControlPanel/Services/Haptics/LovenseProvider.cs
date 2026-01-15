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
                Log.Information("Connecting to Lovense in {Mode} mode at {Url}", _mode, _baseUrl);
                
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

                Log.Information("Lovense response: {Content}", content);
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
                Log.Error(ex, "Lovense connection failed");
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
                Log.Information("Found toy: {Id} - {Name}", _toyId, displayName);
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
            if (!IsConnected || string.IsNullOrEmpty(_toyId)) return;
            try
            {
                // Convert 0-1 to 0-20 scale, ensuring non-zero intensity gives at least level 1
                int i;
                if (intensity <= 0)
                    i = 0;
                else if (intensity < 0.05) // 1-5% gets minimum level 1
                    i = 1;
                else
                    i = Math.Clamp((int)(intensity * 20), 1, 20);

                var sec = Math.Max(1, durationMs / 1000);

                // Log the raw input and converted values
                Log.Information("Vibrate called: raw={Raw:F3}, level={Level}/20, duration={Duration}ms->sec={Sec}",
                    intensity, i, durationMs, sec);

                if (_mode == LovenseConnectionMode.Lan)
                {
                    var cmdJson = $"{{\"command\":\"Function\",\"action\":\"Vibrate:{i}\",\"timeSec\":{sec},\"toy\":\"{_toyId}\"}}";
                    var json = new StringContent(cmdJson, Encoding.UTF8, "application/json");
                    await _client.PostAsync($"{_baseUrl}/command", json);
                }
                else
                {
                    await _client.GetAsync($"{_baseUrl}/command?command=Vibrate&action=Vibrate&intensity={i}&timeSec={sec}&toy={_toyId}");
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Vibrate failed"); }
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
            }
            catch (Exception ex) { Log.Warning(ex, "Stop failed"); }
        }
    }
}
