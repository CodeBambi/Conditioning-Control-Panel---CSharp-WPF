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

        // Tracks a headless `ollama serve` process this app spawned, so it can
        // be terminated on app exit instead of leaving the server orphaned.
        // Null means we did not start the server (it was already running, or
        // it was launched by the official installer's auto-start).
        private static Process? _spawnedServer;
        private static readonly object _spawnedServerLock = new();

        // #1079: `ollama serve` writes its startup banner, GPU discovery and one access-log
        // line per HTTP request to stderr. We redirect both pipes (those logs are the single
        // most useful artefact when local AI misbehaves), which means we MUST drain them —
        // an undrained redirected pipe fills its ~4KB OS buffer, the child then blocks
        // forever on its next write, and the server stops servicing HTTP mid-flight. That
        // produced the exact reported symptom: /api/tags answers while the buffer is still
        // short (the connection test goes green) and then the first real inference, which is
        // by far the chattiest thing the server logs, wedges and never returns.
        //
        // The drain keeps a bounded tail for diagnostics. Both caps are hard: at most
        // ServerLogTailMax lines of at most ServerLogLineMax chars, so the tail cannot grow
        // past ~18KB no matter how long the server runs.
        private static readonly Queue<string> _serverLogTail = new();
        private static readonly object _serverLogLock = new();
        private const int ServerLogTailMax = 60;
        private const int ServerLogLineMax = 300;

        // Only the first N lines of a given spawn reach Serilog. Startup output is what
        // matters for triage; the per-request access log after that would bloat crash.log
        // for every user who leaves the app open all day.
        private const int ServerLogLinesToLog = 40;
        private static int _serverLogLinesLogged;

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

            Process? started = null;
            Process? candidate = null;
            try
            {
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = cliPath,
                        Arguments = "serve",
                        // Redirection requires UseShellExecute=false; CreateNoWindow keeps the
                        // console off screen. (WindowStyle is ignored once CreateNoWindow is set,
                        // but is left in place as belt-and-braces for any future shell-exec path.)
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = false
                };
                candidate = proc;

                // Handlers must be attached BEFORE Start so no output is missed, and the
                // Begin*ReadLine calls immediately after are what actually drain the pipes.
                // Without them the redirection above is a deadlock (#1079).
                proc.OutputDataReceived += OnServerOutput;
                proc.ErrorDataReceived += OnServerOutput;

                Interlocked.Exchange(ref _serverLogLinesLogged, 0);
                ClearServerLogTail();

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                started = proc;

                Process? superseded = null;
                lock (_spawnedServerLock)
                {
                    // We only get here when nothing was answering on the host, so any handle
                    // we still hold is a dead or wedged server. Kill it rather than dropping
                    // the handle: an abandoned-but-alive process would hold port 11434, make
                    // this new spawn fail-bind, and then outlive the app as an orphan because
                    // StopSpawnedServer no longer knows about it.
                    superseded = _spawnedServer;
                    _spawnedServer = proc;
                }
                KillQuietly(superseded, "superseded `ollama serve`");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to spawn `ollama serve`");
                // Start() (or a Begin*ReadLine) can throw after the Process object exists.
                // If it never became the tracked server, tear it down here so we neither
                // leak the handle nor orphan a process that did in fact start.
                if (started == null) KillQuietly(candidate, "half-started `ollama serve`");
                return false;
            }

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                var (ok, _) = await TryListModelsAsync(host, ct);
                if (ok) return true;

                // A server that died (usually "address already in use", or a broken install)
                // will never answer. Bail immediately with the reason instead of burning the
                // full 30s and reporting a bare timeout.
                try
                {
                    if (started.HasExited)
                    {
                        App.Logger?.Warning(
                            "`ollama serve` exited with code {Code} before binding {Host}. Server output:{Nl}{Tail}",
                            started.ExitCode, host, Environment.NewLine, GetServerLogTail());
                        return false;
                    }
                }
                catch { /* HasExited/ExitCode can throw on a disposed handle — keep waiting */ }

                await Task.Delay(1000, ct);
            }

            App.Logger?.Warning(
                "`ollama serve` did not answer {Host} within 30s. Server output:{Nl}{Tail}",
                host, Environment.NewLine, GetServerLogTail());
            return false;
        }

        /// <summary>
        /// Drain handler for both of the spawned server's pipes. Runs on a thread-pool thread
        /// that is actively emptying the pipe, so it must be cheap and must never throw —
        /// a slow or throwing handler re-creates the very deadlock it exists to prevent.
        /// </summary>
        private static void OnServerOutput(object? sender, DataReceivedEventArgs e)
        {
            // e.Data is null when the stream closes; blank lines are not worth keeping.
            var line = e.Data;
            if (string.IsNullOrWhiteSpace(line)) return;

            try
            {
                if (line.Length > ServerLogLineMax) line = line.Substring(0, ServerLogLineMax);

                lock (_serverLogLock)
                {
                    _serverLogTail.Enqueue(line);
                    while (_serverLogTail.Count > ServerLogTailMax) _serverLogTail.Dequeue();
                }

                if (Interlocked.Increment(ref _serverLogLinesLogged) <= ServerLogLinesToLog)
                    App.Logger?.Debug("[ollama serve] {Line}", line);
            }
            catch
            {
                // Never let a logging failure propagate into the pipe reader.
            }
        }

        private static void ClearServerLogTail()
        {
            lock (_serverLogLock) { _serverLogTail.Clear(); }
        }

        /// <summary>
        /// The last few lines the spawned server wrote, newest last. Empty when we never
        /// spawned one. Purely diagnostic — safe to call from anywhere.
        /// </summary>
        internal static string GetServerLogTail()
        {
            lock (_serverLogLock)
            {
                return _serverLogTail.Count == 0
                    ? "(no output captured)"
                    : string.Join(Environment.NewLine, _serverLogTail);
            }
        }

        /// <summary>
        /// Kill + dispose a process handle, tolerating every state it can be in (already
        /// exited, never started, handle disposed). Used wherever we drop a server handle.
        /// </summary>
        private static void KillQuietly(Process? proc, string context = "spawned `ollama serve`")
        {
            if (proc == null) return;
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                // Already gone, never started, or the handle is disposed — all expected.
                // Logged rather than silent so a genuinely stuck process leaves a trace.
                App.Logger?.Debug("Could not stop {Context}: {Error}", context, ex.Message);
            }
            finally
            {
                try { proc.Dispose(); } catch { }
            }
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

        /// <summary>
        /// Terminates the headless <c>ollama serve</c> process this app spawned, if any.
        /// Safe to call multiple times. Does NOT touch a server started by the official
        /// installer's auto-start or by the user's own Ollama tray app — only the
        /// process whose handle we captured in <see cref="TryStartHeadlessServerAsync"/>.
        /// Call from App.OnExit so we don't leave a server running after the app closes.
        /// </summary>
        public static void StopSpawnedServer()
        {
            Process? proc;
            lock (_spawnedServerLock)
            {
                proc = _spawnedServer;
                _spawnedServer = null;
            }

            if (proc == null) return;

            // Kills the tree and waits briefly so the OS releases port 11434 before we exit;
            // tolerates a process that already died on its own.
            KillQuietly(proc);
        }

        /// <summary>
        /// Best-effort "is the local server actually there?" check used before we lean on it.
        /// If <paramref name="host"/> already answers, does nothing. If it doesn't, and the
        /// host is loopback, and the Ollama CLI is installed, brings the headless server back
        /// up and waits for it to bind.
        ///
        /// <para>#1079 (second half): the server we spawn is our child and
        /// <see cref="StopSpawnedServer"/> kills it on app exit, so the next launch starts with
        /// nothing listening and nothing was restarting it. This closes that gap at the one
        /// point where it matters — just before the first request of the session — without
        /// standing up a supervisor.</para>
        ///
        /// <para>Deliberately refuses non-loopback hosts: if the user pointed the app at a
        /// remote Ollama, spawning a local one would bind the wrong box's work to this
        /// machine and mask the real connection problem.</para>
        /// </summary>
        public static async Task<bool> EnsureServerRunningAsync(string? host = null, CancellationToken ct = default)
        {
            host ??= DefaultHost;

            var (reachable, _) = await TryListModelsAsync(host, ct);
            if (reachable) return true;

            if (!IsLoopbackHost(host))
            {
                App.Logger?.Information(
                    "Ollama host {Host} is not reachable and is not local — not spawning a server for it", host);
                return false;
            }

            if (FindOllamaCli() == null)
            {
                App.Logger?.Information("Ollama is not reachable at {Host} and no CLI is installed to start", host);
                return false;
            }

            App.Logger?.Information("Ollama not reachable at {Host} — starting the headless server", host);
            return await TryStartHeadlessServerAsync(host, ct);
        }

        private static bool IsLoopbackHost(string host)
        {
            try
            {
                var uri = new Uri(NormalizeHost(host));
                return uri.IsLoopback;
            }
            catch
            {
                return false;
            }
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
