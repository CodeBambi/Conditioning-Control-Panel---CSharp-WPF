using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Bureau
{
    /// <summary>
    /// Host for the "Beta Inspection Bureau" labeling game. Unlike the DtRH/intake web-cores the
    /// page is NOT bundled: it is served live from the public site (cclabs.app/bureau), so game
    /// updates ship without an app release. The host is the page's only capability surface:
    ///
    ///   - auth:    proxies /v2/bureau/* to the server with the account's UnifiedId + X-Auth-Token
    ///   - pixels:  resolves hash targets against <see cref="BureauIndexService"/> and streams
    ///              decoded frames to the page as data URIs — image bytes never touch the server
    ///   - admin:   gold certification is only wired when CCP_BUREAU_ADMIN_TOKEN is set in the
    ///              environment (operator machines), and rides the x-admin-token header
    ///
    /// Bridge protocol is documented at the top of the site's bureau/bridge.js. Scaffolding
    /// (hardened WebView2, heartbeat watchdog, relaunch-once recovery) mirrors IntakeHostService.
    /// </summary>
    internal static class BureauHostService
    {
        public const string ProductName = "Beta Inspection Bureau";

        private const int Protocol = 1;
        private const string SiteHost = "cclabs.app";
        private const string StartUrl = "https://cclabs.app/bureau/index.html";
        private const string ServerBase = "https://codebambi-proxy.vercel.app";
        private const int ServerFetchCount = 24;   // over-fetch: not every target resolves locally
        private const int BatchDecodeCap = 6;      // frames per bridge message (keeps postMessage light)

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

        private static ChaosWebViewHost? _host;
        private static DispatcherTimer? _heartbeatWatch;
        private static DispatcherTimer? _exitWatchdog;
        private static DateTime _lastHeartbeatUtc;
        private static bool _exiting;
        private static bool _relaunchedOnce;
        private static bool _disposing;

        public static bool IsActive => _host != null;

        private static string? AdminToken => Environment.GetEnvironmentVariable("CCP_BUREAU_ADMIN_TOKEN");
        private static bool HasAuth =>
            !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId) &&
            !string.IsNullOrEmpty(App.Settings?.Current?.AuthToken);

        /// <summary>Launch the Bureau window (idempotent — a running instance is re-focused).</summary>
        public static void Launch()
        {
            if (_host != null) { _host.FocusWeb(); return; }
            try
            {
                _exiting = false;
                _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
                {
                    StartUrl = StartUrl,
                    PrimaryHost = SiteHost,
                    Mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>(),
                    UserDataFolderName = "browser_data_bureau",
                    InputEnabled = true,
                    StartFullscreen = false,
                    WindowTitle = ProductName,
                    LogTag = "BureauHost",
                    OnReady = OnPageReady,
                    OnMessage = OnPageMessage,
                    OnProcessFailed = OnProcessFailed,
                });
                _host.Show();
                if (_host.Window != null) _host.Window.Closed += (_, _) => DisposeAll();
                StartHeartbeatWatch();
                App.Logger?.Information("BureauHostService: launched → {Url}", StartUrl);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BureauHostService.Launch failed");
                DisposeAll();
            }
        }

        public static void CloseActive()
        {
            try
            {
                if (_host == null) return;
                if (_host.IsReady && !_exiting)
                {
                    _exiting = true;
                    Post(new { type = "end-run" });
                    ArmExitWatchdog();
                }
                else DisposeAll();
            }
            catch { DisposeAll(); }
        }

        // ============================ boot ============================

        private static void OnPageReady()
        {
            try
            {
                _lastHeartbeatUtc = DateTime.UtcNow;
                _host?.FocusWeb();
                Post(new
                {
                    type = "init",
                    protocol = Protocol,
                    admin = !string.IsNullOrEmpty(AdminToken),
                    hasAuth = HasAuth,
                    indexing = new { done = BureauIndexService.Done, total = BureauIndexService.Total, ready = BureauIndexService.IsReady },
                });

                if (!BureauIndexService.IsReady)
                {
                    _ = BureauIndexService.EnsureBuiltAsync((done, total) =>
                            Post(new { type = "index-progress", done, total, ready = false }))
                        .ContinueWith(_ => Post(new
                        {
                            type = "index-progress",
                            done = BureauIndexService.Done,
                            total = BureauIndexService.Total,
                            ready = true,
                        }));
                }
                App.Logger?.Information("BureauHostService: init sent (auth={A}, admin={G}, indexReady={I})",
                    HasAuth, !string.IsNullOrEmpty(AdminToken), BureauIndexService.IsReady);
            }
            catch (Exception ex) { App.Logger?.Warning("BureauHostService.OnPageReady: {E}", ex.Message); }
        }

        // ============================ page messages ============================

        private static void OnPageMessage(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "heartbeat":
                case "pong":
                    _lastHeartbeatUtc = DateTime.UtcNow;
                    break;
                case "next":
                    _ = Task.Run(() => HandleNextAsync());
                    break;
                case "submit":
                    _ = Task.Run(() => HandleSubmitAsync(o));
                    break;
                case "inbox":
                    _ = Task.Run(() => HandleInboxAsync());
                    break;
                case "stats":
                    _ = Task.Run(() => HandleStatsAsync());
                    break;
                case "gold-set":
                    _ = Task.Run(() => HandleGoldSetAsync(o));
                    break;
                case "exit":
                    _exiting = true;
                    ArmExitWatchdog();
                    break;
                case "exit-done":
                    RunOnUi(DisposeAll);
                    break;
            }
        }

        // ============================ handlers ============================

        private static async Task HandleNextAsync()
        {
            try
            {
                await BureauIndexService.EnsureBuiltAsync().ConfigureAwait(false);

                var (status, body) = await ServerAsync(HttpMethod.Post, "/v2/bureau/next",
                    new JObject { ["unified_id"] = App.Settings?.Current?.UnifiedId, ["count"] = ServerFetchCount })
                    .ConfigureAwait(false);

                if (status == 401 || status == 403)
                {
                    Post(new { type = "batch", items = Array.Empty<object>(), auth = false });
                    return;
                }
                if (status != 200 || body == null)
                {
                    App.Logger?.Warning("BureauHostService: /next failed ({Status})", status);
                    Post(new { type = "batch", items = Array.Empty<object>(), exhausted = false });
                    return;
                }

                var targets = body["targets"] as JArray ?? new JArray();
                var items = new List<object>();
                foreach (var t in targets)
                {
                    if (items.Count >= BatchDecodeCap) break;
                    var target = (string?)t;
                    if (string.IsNullOrEmpty(target)) continue;
                    var sep = target.IndexOf(':');
                    if (sep != 64) continue;
                    if (!int.TryParse(target[(sep + 1)..], out var frame)) continue;

                    var decoded = BureauIndexService.DecodeFrame(target[..sep], frame);
                    if (decoded == null) continue;   // not in this user's packs / decoder divergence — skip
                    items.Add(new { target, src = decoded.Value.DataUri, dims = new[] { decoded.Value.W, decoded.Value.H } });
                }

                Post(new
                {
                    type = "batch",
                    items,
                    profile = body["profile"],
                    exhausted = targets.Count == 0,
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BureauHostService.HandleNextAsync failed");
                Post(new { type = "batch", items = Array.Empty<object>(), exhausted = false });
            }
        }

        private static async Task HandleSubmitAsync(JObject o)
        {
            var target = (string?)o["target"] ?? "";
            try
            {
                var (status, body) = await ServerAsync(HttpMethod.Post, "/v2/bureau/submit", new JObject
                {
                    ["unified_id"] = App.Settings?.Current?.UnifiedId,
                    ["target"] = target,
                    ["dims"] = o["dims"],
                    ["boxes"] = o["boxes"] ?? new JArray(),
                }).ConfigureAwait(false);

                var reply = status == 200 && body != null ? body : new JObject();
                reply["type"] = "submit-result";
                reply["target"] = target;
                reply["ok"] = status == 200;
                if (status != 200)
                {
                    reply["code"] = status;
                    reply["error"] = body?["error"]?.ToString() ?? $"server error {status}";
                }
                Post(reply);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BureauHostService.HandleSubmitAsync failed");
                Post(new { type = "submit-result", target, ok = false, error = "the tube jammed — network error" });
            }
        }

        private static async Task HandleInboxAsync()
        {
            try
            {
                var (status, body) = await ServerAsync(HttpMethod.Post, "/v2/bureau/inbox",
                    new JObject { ["unified_id"] = App.Settings?.Current?.UnifiedId }).ConfigureAwait(false);
                Post(new
                {
                    type = "inbox-result",
                    ok = status == 200,
                    entries = status == 200 ? body?["entries"] : new JArray(),
                    xp = status == 200 ? (int?)body?["xp"] ?? 0 : 0,
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BureauHostService.HandleInboxAsync failed");
                Post(new { type = "inbox-result", ok = false });
            }
        }

        private static async Task HandleStatsAsync()
        {
            try
            {
                var (status, body) = await ServerAsync(HttpMethod.Get, "/v2/bureau/stats", null).ConfigureAwait(false);
                if (status != 200 || body == null) { Post(new { type = "stats-result", ok = false }); return; }
                body["type"] = "stats-result";
                body["ok"] = true;
                Post(body);
            }
            catch { Post(new { type = "stats-result", ok = false }); }
        }

        private static async Task HandleGoldSetAsync(JObject o)
        {
            var target = (string?)o["target"] ?? "";
            if (string.IsNullOrEmpty(AdminToken))
            {
                Post(new { type = "gold-result", target, ok = false, error = "no admin credentials" });
                return;
            }
            try
            {
                var (status, body) = await ServerAsync(HttpMethod.Post, "/admin/bureau/gold", new JObject
                {
                    ["target"] = target,
                    ["boxes"] = o["boxes"] ?? new JArray(),
                    ["remove"] = o["remove"] ?? false,
                }, admin: true).ConfigureAwait(false);
                Post(new
                {
                    type = "gold-result",
                    target,
                    ok = status == 200,
                    error = status == 200 ? null : body?["error"]?.ToString() ?? $"server error {status}",
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BureauHostService.HandleGoldSetAsync failed");
                Post(new { type = "gold-result", target, ok = false, error = "network error" });
            }
        }

        // ============================ plumbing ============================

        private static async Task<(int Status, JObject? Body)> ServerAsync(HttpMethod method, string path, JObject? body, bool admin = false)
        {
            using var req = new HttpRequestMessage(method, ServerBase + path);
            var authToken = App.Settings?.Current?.AuthToken;
            if (!string.IsNullOrEmpty(authToken)) req.Headers.TryAddWithoutValidation("X-Auth-Token", authToken);
            if (admin && !string.IsNullOrEmpty(AdminToken)) req.Headers.TryAddWithoutValidation("x-admin-token", AdminToken);
            if (body != null) req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            JObject? parsed = null;
            try { if (!string.IsNullOrWhiteSpace(text)) parsed = JObject.Parse(text); } catch { }
            return ((int)resp.StatusCode, parsed);
        }

        /// <summary>Post to the page from any thread (ChaosWebViewHost.Post is UI-thread only).</summary>
        private static void Post(object msg) => RunOnUi(() => _host?.Post(msg));

        private static void RunOnUi(Action action)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (disp.CheckAccess()) { try { action(); } catch { } }
            else disp.BeginInvoke(() => { try { action(); } catch { } });
        }

        // ============================ watchdogs / recovery ============================

        private static void StartHeartbeatWatch()
        {
            StopHeartbeatWatch();
            _lastHeartbeatUtc = DateTime.UtcNow;
            _heartbeatWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _heartbeatWatch.Tick += (_, _) =>
            {
                if (_host == null || !_host.IsReady || _exiting) return;
                if ((DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds > 20)
                {
                    App.Logger?.Warning("BureauHostService: page heartbeat silent >20s - recovering");
                    Recover("heartbeat-silent");
                }
            };
            _heartbeatWatch.Start();
        }

        private static void StopHeartbeatWatch()
        {
            try { _heartbeatWatch?.Stop(); } catch { }
            _heartbeatWatch = null;
        }

        private static void OnProcessFailed(CoreWebView2ProcessFailedKind kind) => Recover($"process-failed:{kind}");

        private static void Recover(string reason)
        {
            RunOnUi(() =>
            {
                var retry = !_relaunchedOnce;
                App.Logger?.Warning("BureauHostService: recovery ({Reason}) - {Action}",
                    reason, retry ? "relaunching once" : "giving up");
                DisposeAll();
                if (retry)
                {
                    _relaunchedOnce = true;
                    Launch();
                }
            });
        }

        // ============================ teardown ============================

        private static void ArmExitWatchdog()
        {
            CancelExitWatchdog();
            _exitWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _exitWatchdog.Tick += (_, _) => DisposeAll();
            _exitWatchdog.Start();
        }

        private static void CancelExitWatchdog()
        {
            try { _exitWatchdog?.Stop(); } catch { }
            _exitWatchdog = null;
        }

        private static void DisposeAll()
        {
            if (_disposing) return;
            _disposing = true;
            try
            {
                CancelExitWatchdog();
                StopHeartbeatWatch();
                try { _host?.Dispose(); } catch { }
                _host = null;
                _exiting = false;
                App.Logger?.Information("BureauHostService: closed");
            }
            finally { _disposing = false; }
        }
    }
}
