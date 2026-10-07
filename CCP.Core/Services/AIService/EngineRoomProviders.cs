using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>The Engine Room's provider segment (Z7) — the four legacy radios.</summary>
    public enum CompanionProviderMode
    {
        Off,
        Cloud,
        LocalOllama,
        Custom
    }

    /// <summary>
    /// The Engine Room's pure half, shared by both heads: the segment &lt;-&gt; settings mapping and the
    /// two "Test connection" probes (WPF MainWindow.Patreon.cs BtnTestOllamaConnection_Click /
    /// BtnTestOpenAiConnection_Click). Each probe answers the status line text and its health; the
    /// head writes it. Both only reach the host the user configured; a sandbox's SandboxNet keeps
    /// that to loopback.
    /// </summary>
    public static class EngineRoomProviders
    {
        /// <summary>Settings pair (enabled + provider) → the segmented row's one value.</summary>
        public static CompanionProviderMode ModeFor(bool aiEnabled, AiProviderType provider)
        {
            if (!aiEnabled) return CompanionProviderMode.Off;
            return provider switch
            {
                AiProviderType.Local => CompanionProviderMode.LocalOllama,
                AiProviderType.OpenAiCompatible => CompanionProviderMode.Custom,
                _ => CompanionProviderMode.Cloud
            };
        }

        /// <summary>The inverse, for the write path. Off maps to Cloud + disabled.</summary>
        public static (bool Enabled, AiProviderType Provider) SettingsFor(CompanionProviderMode mode) => mode switch
        {
            CompanionProviderMode.Off => (false, AiProviderType.Cloud),
            CompanionProviderMode.LocalOllama => (true, AiProviderType.Local),
            CompanionProviderMode.Custom => (true, AiProviderType.OpenAiCompatible),
            _ => (true, AiProviderType.Cloud)
        };

        /// <summary>Off and Cloud drop the Live Actions feed (nothing local can populate it there).</summary>
        public static bool ClearsLiveActions(CompanionProviderMode mode) =>
            mode is CompanionProviderMode.Off or CompanionProviderMode.Cloud;

        /// <summary>GET {host}/api/tags with a 3 s budget, exactly as the WPF button.</summary>
        public static async Task<(string Text, bool Healthy)> TestOllamaAsync(string? configuredHost)
        {
            var host = (configuredHost ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(host)) return (Loc.Get("label_status_failed"), false);
            var url = host.TrimEnd('/') + "/api/tags";

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var http = new HttpClient();
                var resp = await http.GetAsync(url, cts.Token);
                sw.Stop();
                return resp.IsSuccessStatusCode
                    ? ($"{Loc.Get("label_status_connected")} · {sw.ElapsedMilliseconds}ms", true)
                    : ($"{Loc.Get("label_status_failed")} · {(int)resp.StatusCode}", false);
            }
            catch (Exception ex)
            {
                return ($"{Loc.Get("label_status_failed")} · {ex.GetType().Name}", false);
            }
        }

        /// <summary>The BYO endpoint probe (OpenAiCompatibleService.TestEndpointAsync, 10 s budget).</summary>
        public static async Task<(string Text, bool Healthy)> TestOpenAiCompatibleAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var service = new OpenAiCompatibleService();
                var diag = await service.TestEndpointAsync(cts.Token);
                if (diag.Success) return ($"{Loc.Get("label_status_connected")} · {diag.ElapsedMs ?? 0}ms", true);
                var codePart = diag.HttpStatusCode.HasValue ? $" (HTTP {diag.HttpStatusCode.Value})" : string.Empty;
                return ($"{Loc.Get("label_status_failed")} · {diag.Message}{codePart}", false);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "MainWindow: OpenAI-compatible test connection failed");
                return ($"{Loc.Get("label_status_failed")} · {ex.GetType().Name}", false);
            }
        }
    }
}
