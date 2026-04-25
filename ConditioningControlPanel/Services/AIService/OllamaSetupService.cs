using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// Pure async logic for the Local-AI onboarding wizard. Handles:
    ///   • detecting whether Ollama is installed/running and whether the target model is pulled
    ///   • downloading the official OllamaSetup.exe
    ///   • running it silently
    ///   • streaming model layers via /api/pull
    ///   • a smoke-test chat call to confirm everything's wired up
    /// No UI dependencies — all progress/cancellation surfaces through IProgress/CancellationToken.
    /// </summary>
    public static class OllamaSetupService
    {
        private const string DefaultHost = "http://localhost:11434/";
        private const string OllamaInstallerUrl = "https://ollama.com/download/OllamaSetup.exe";

        public enum InstallStatus
        {
            NotInstalled,
            InstalledNotRunning,
            RunningNoModel,
            Ready
        }

        public sealed class StatusSnapshot
        {
            public InstallStatus Status { get; init; }
            public bool ServiceReachable { get; init; }
            public bool ExecutableFound { get; init; }
            public string? ExecutablePath { get; init; }
            public List<string> InstalledModels { get; init; } = new();
            public bool TargetModelInstalled { get; init; }
        }

        public sealed class DownloadProgress
        {
            public long BytesReceived { get; init; }
            public long? TotalBytes { get; init; }
            public double? PercentComplete =>
                TotalBytes.HasValue && TotalBytes.Value > 0
                    ? (double)BytesReceived / TotalBytes.Value * 100.0
                    : null;
            public double BytesPerSecond { get; init; }
        }

        public sealed class PullProgress
        {
            public string Status { get; init; } = "";
            public string? Digest { get; init; }
            public long? Total { get; init; }
            public long? Completed { get; init; }
            public double? PercentComplete =>
                Total.HasValue && Total.Value > 0 && Completed.HasValue
                    ? (double)Completed.Value / Total.Value * 100.0
                    : null;
        }

        // -------- Detect --------

        /// <summary>
        /// Probes the system for Ollama: checks the standard install path, queries the
        /// service if reachable, and lists installed models. Cheap (~1-2s timeout total).
        /// </summary>
        public static async Task<StatusSnapshot> DetectAsync(
            string? host = null,
            string? targetModel = null,
            CancellationToken ct = default)
        {
            host ??= DefaultHost;
            var exePath = FindOllamaExecutable();
            var exeFound = !string.IsNullOrEmpty(exePath);

            var (reachable, models) = await TryListModelsAsync(host, ct);

            bool targetInstalled = false;
            if (!string.IsNullOrEmpty(targetModel))
            {
                foreach (var m in models)
                {
                    if (string.Equals(m, targetModel, StringComparison.OrdinalIgnoreCase))
                    {
                        targetInstalled = true;
                        break;
                    }
                }
            }

            InstallStatus status;
            if (!exeFound && !reachable) status = InstallStatus.NotInstalled;
            else if (!reachable) status = InstallStatus.InstalledNotRunning;
            else if (!targetInstalled) status = InstallStatus.RunningNoModel;
            else status = InstallStatus.Ready;

            return new StatusSnapshot
            {
                Status = status,
                ServiceReachable = reachable,
                ExecutableFound = exeFound,
                ExecutablePath = exePath,
                InstalledModels = models,
                TargetModelInstalled = targetInstalled
            };
        }

        private static string? FindOllamaExecutable()
        {
            // Detection only: any of these existing means Ollama is installed.
            // Standard per-user install location (Ollama uses NSIS, installs to %LOCALAPPDATA%).
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new[]
            {
                Path.Combine(localAppData, "Programs", "Ollama", "ollama app.exe"),
                Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe"),
            };
            foreach (var c in candidates)
            {
                try { if (File.Exists(c)) return c; }
                catch { /* ignore - permission errors fall through */ }
            }
            return null;
        }

        /// <summary>
        /// Returns the path to the Ollama CLI binary. Used for headless
        /// <c>ollama serve</c> invocations to bring up the HTTP server without UI.
        /// </summary>
        private static string? FindOllamaCli()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
            try { return File.Exists(path) ? path : null; }
            catch { return null; }
        }

        private static async Task<(bool reachable, List<string> models)> TryListModelsAsync(
            string host, CancellationToken ct)
        {
            var url = NormalizeHost(host) + "api/tags";
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                using var resp = await http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return (false, new List<string>());

                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                var names = new List<string>();
                if (doc.RootElement.TryGetProperty("models", out var arr) &&
                    arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in arr.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var n) &&
                            n.ValueKind == JsonValueKind.String)
                        {
                            var name = n.GetString();
                            if (!string.IsNullOrEmpty(name)) names.Add(name);
                        }
                    }
                }
                return (true, names);
            }
            catch
            {
                return (false, new List<string>());
            }
        }

        private static string NormalizeHost(string host) =>
            host.EndsWith("/", StringComparison.Ordinal) ? host : host + "/";

        // -------- Installer download --------

        /// <summary>
        /// Streams the official Ollama installer to a temp file and reports byte progress.
        /// Throws on cancellation or HTTP error; cleans up the partial file on cancel.
        /// </summary>
        public static async Task<string> DownloadInstallerAsync(
            IProgress<DownloadProgress>? progress = null,
            CancellationToken ct = default)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");

            // Drop any leftover from a previous run.
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var resp = await http.GetAsync(OllamaInstallerUrl,
                HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var totalBytes = resp.Content.Headers.ContentLength;
            using var src = await resp.Content.ReadAsStreamAsync(ct);

            FileStream? dst = null;
            try
            {
                dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long received = 0;
                int n;
                var sw = Stopwatch.StartNew();
                long lastReportBytes = 0;
                var lastReportTime = sw.Elapsed;

                while ((n = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    received += n;

                    var now = sw.Elapsed;
                    if ((now - lastReportTime).TotalMilliseconds >= 200)
                    {
                        var deltaBytes = received - lastReportBytes;
                        var deltaSec = (now - lastReportTime).TotalSeconds;
                        var bps = deltaSec > 0 ? deltaBytes / deltaSec : 0;
                        progress?.Report(new DownloadProgress
                        {
                            BytesReceived = received,
                            TotalBytes = totalBytes,
                            BytesPerSecond = bps
                        });
                        lastReportBytes = received;
                        lastReportTime = now;
                    }
                }

                progress?.Report(new DownloadProgress
                {
                    BytesReceived = received,
                    TotalBytes = totalBytes,
                    BytesPerSecond = 0
                });
            }
            catch (OperationCanceledException)
            {
                dst?.Dispose();
                dst = null;
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
            finally
            {
                dst?.Dispose();
            }

            return tempPath;
        }

        // -------- Run installer silently --------

        /// <summary>
        /// Runs OllamaSetup.exe with NSIS silent flags and waits for the service to come up.
        /// Returns true on success. The installer auto-starts Ollama after it finishes,
        /// so we just need to wait for the API to be reachable.
        /// </summary>
        public static async Task<bool> RunInstallerSilentAsync(
            string installerPath,
            string? host = null,
            CancellationToken ct = default)
        {
            host ??= DefaultHost;

            // NSIS silent flag is /S (uppercase). Ollama's installer is NSIS-based.
            // /D=<path> would override install dir but we accept the per-user default.
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/S",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            // Pump until the installer exits, but bail if cancelled.
            while (!proc.HasExited)
            {
                if (ct.IsCancellationRequested)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    ct.ThrowIfCancellationRequested();
                }
                await Task.Delay(250, ct);
            }

            if (proc.ExitCode != 0)
            {
                App.Logger?.Warning("OllamaSetup.exe exited with code {Code}", proc.ExitCode);
                return false;
            }

            // Wait up to ~60s for the service to come up. The installer launches Ollama
            // automatically but it can take a few seconds to bind 11434.
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                var (ok, _) = await TryListModelsAsync(host, ct);
                if (ok) return true;
                await Task.Delay(1000, ct);
            }

            // Fall back to launching `ollama serve` headlessly if the post-install
            // auto-start didn't bind the port. Don't launch `ollama app.exe` —
            // that's the GUI chat client in newer versions and would pop UI.
            if (await TryStartHeadlessServerAsync(host, ct)) return true;

            return false;
        }

        /// <summary>
        /// Spawns <c>ollama.exe serve</c> with a hidden window so the HTTP server
        /// comes up without flashing UI. Returns true once /api/tags responds.
        /// </summary>
        private static async Task<bool> TryStartHeadlessServerAsync(string host, CancellationToken ct)
        {
            var cliPath = FindOllamaCli();
            if (string.IsNullOrEmpty(cliPath)) return false;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to spawn `ollama serve`");
                return false;
            }

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                var (ok, _) = await TryListModelsAsync(host, ct);
                if (ok) return true;
                await Task.Delay(1000, ct);
            }
            return false;
        }

        /// <summary>
        /// If Ollama is installed but no service is listening, spawn
        /// <c>ollama.exe serve</c> with a hidden window so the HTTP server comes up
        /// without showing UI. Returns true once /api/tags responds.
        /// </summary>
        public static Task<bool> StartServiceAsync(string? host = null, CancellationToken ct = default)
        {
            host ??= DefaultHost;
            return TryStartHeadlessServerAsync(host, ct);
        }

        // -------- Pull model via /api/pull --------

        /// <summary>
        /// Streams Ollama's /api/pull NDJSON output and reports per-event progress.
        /// Ollama caches partial layers, so cancelling and re-running resumes cleanly.
        /// </summary>
        public static async Task PullModelAsync(
            string model,
            string? host = null,
            IProgress<PullProgress>? progress = null,
            CancellationToken ct = default)
        {
            host ??= DefaultHost;
            var url = NormalizeHost(host) + "api/pull";

            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var payload = JsonSerializer.Serialize(new { name = model, stream = true });
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    string status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                    string? digest = root.TryGetProperty("digest", out var d) ? d.GetString() : null;
                    long? total = root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number
                        ? t.GetInt64() : (long?)null;
                    long? completed = root.TryGetProperty("completed", out var c) && c.ValueKind == JsonValueKind.Number
                        ? c.GetInt64() : (long?)null;

                    progress?.Report(new PullProgress
                    {
                        Status = status,
                        Digest = digest,
                        Total = total,
                        Completed = completed
                    });

                    // Ollama emits {"error":"..."} for unknown models, etc.
                    if (root.TryGetProperty("error", out var err))
                    {
                        var msg = err.GetString() ?? "unknown error";
                        throw new InvalidOperationException("Ollama pull failed: " + msg);
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines silently — Ollama occasionally emits them on shutdown.
                }
            }
        }

        // -------- Smoke test --------

        /// <summary>
        /// Sends a one-token "hi" to /api/chat to warm the model and confirm the wiring.
        /// Returns the elapsed wall-clock time and the assistant reply on success.
        /// </summary>
        public static async Task<(bool ok, TimeSpan elapsed, string reply)> SmokeTestAsync(
            string model,
            string? host = null,
            CancellationToken ct = default)
        {
            host ??= DefaultHost;
            var url = NormalizeHost(host) + "api/chat";
            var payload = JsonSerializer.Serialize(new
            {
                model = model,
                messages = new[] { new { role = "user", content = "Say hi in one word." } },
                stream = false,
                think = false
            });

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            var sw = Stopwatch.StartNew();
            try
            {
                using var resp = await http.SendAsync(req, ct);
                sw.Stop();
                if (!resp.IsSuccessStatusCode) return (false, sw.Elapsed, "");

                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var content))
                {
                    return (true, sw.Elapsed, content.GetString() ?? "");
                }
                return (false, sw.Elapsed, "");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                App.Logger?.Warning(ex, "Smoke test failed (model={Model})", model);
                return (false, sw.Elapsed, "");
            }
        }

        // -------- Helpers for human-readable output --------

        public static string FormatBytes(long bytes)
        {
            const double KB = 1024;
            const double MB = KB * 1024;
            const double GB = MB * 1024;
            if (bytes >= GB) return (bytes / GB).ToString("0.0") + " GB";
            if (bytes >= MB) return (bytes / MB).ToString("0.0") + " MB";
            if (bytes >= KB) return (bytes / KB).ToString("0.0") + " KB";
            return bytes + " B";
        }

        public static string FormatRate(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "";
            return FormatBytes((long)bytesPerSecond) + "/s";
        }
    }
}
