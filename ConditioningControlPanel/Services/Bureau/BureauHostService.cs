using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Discord channel with the pack catalogue — the "requisition office".</summary>
        private const string DiscordCatalogueUrl = "https://discord.com/channels/1456573221489999934/1511409848699584653";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

        private static ChaosWebViewHost? _host;
        private static DispatcherTimer? _heartbeatWatch;
        private static DispatcherTimer? _exitWatchdog;
        private static DateTime _lastHeartbeatUtc;
        private static bool _exiting;
        private static bool _relaunchedOnce;
        private static bool _disposing;
        private static bool _rawHooked;        // pack-drop AdditionalObjects listener attached
        private static bool _installBusy;      // one drag-drop install at a time

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

                // Pack drag-and-drop: file paths only travel via AdditionalObjects, which
                // ChaosWebViewHost's JSON-only pipe drops — so listen on the raw event too.
                var core = _host?.WebView?.CoreWebView2;
                if (core != null && !_rawHooked)
                {
                    core.WebMessageReceived += OnRawWebMessage;
                    _rawHooked = true;
                }

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
                    var packFilter = (string?)o["pack"];
                    var certify = o["certify"]?.Value<bool>() == true;
                    _ = Task.Run(() => HandleNextAsync(packFilter, certify));
                    break;
                case "packs":
                    _ = Task.Run(() => HandlePacksAsync());
                    break;
                case "open-catalogue":
                    OpenCatalogue();
                    break;
                case "pack-drop":
                    break;   // handled by OnRawWebMessage (needs AdditionalObjects for the file path)
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

        private static async Task HandleNextAsync(string? packFilter = null, bool certify = false)
        {
            try
            {
                await BureauIndexService.EnsureBuiltAsync().ConfigureAwait(false);

                // Certify-mode pulls the AI-proposed gold drafts (admin route);
                // without credentials it falls through to regular casework.
                var certifying = certify && !string.IsNullOrEmpty(AdminToken);
                var reqBody = new JObject { ["unified_id"] = App.Settings?.Current?.UnifiedId, ["count"] = ServerFetchCount };
                if (certifying) reqBody["certify"] = true;
                var (status, body) = await ServerAsync(HttpMethod.Post, "/v2/bureau/next", reqBody, admin: certifying)
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
                var proposals = certifying ? body["proposals"] as JObject : null;
                var items = new List<object>();
                foreach (var t in targets)
                {
                    if (items.Count >= BatchDecodeCap) break;
                    var target = (string?)t;
                    if (string.IsNullOrEmpty(target)) continue;
                    var sep = target.IndexOf(':');
                    if (sep != 64) continue;
                    if (!int.TryParse(target[(sep + 1)..], out var frame)) continue;

                    // Locker scoping: only serve cases whose evidence lives in the loaded pack.
                    if (!string.IsNullOrEmpty(packFilter))
                    {
                        if (!BureauIndexService.TryResolve(target[..sep], out var owner, out _)) continue;
                        if (!string.Equals(owner, packFilter, StringComparison.Ordinal)) continue;
                    }

                    var decoded = BureauIndexService.DecodeFrame(target[..sep], frame);
                    if (decoded == null) continue;   // not in this user's packs / decoder divergence — skip
                    var boxes = proposals?[target];
                    if (boxes != null)
                        items.Add(new { target, src = decoded.Value.DataUri, dims = new[] { decoded.Value.W, decoded.Value.H }, boxes });
                    else
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
                var req = new JObject
                {
                    ["unified_id"] = App.Settings?.Current?.UnifiedId,
                    ["target"] = target,
                    ["dims"] = o["dims"],
                    ["boxes"] = o["boxes"] ?? new JArray(),
                };
                if (o["amend"]?.Value<bool>() == true) req["amend"] = true;
                var (status, body) = await ServerAsync(HttpMethod.Post, "/v2/bureau/submit", req).ConfigureAwait(false);

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

        /// <summary>The evidence-lockers list: every image-bearing pack from the catalogue
        /// manifest, marked installed/not, with the locally indexed image count when installed.</summary>
        private static async Task HandlePacksAsync()
        {
            try
            {
                await BureauIndexService.EnsureBuiltAsync().ConfigureAwait(false);
                var packs = App.ContentPacks;
                var available = packs != null
                    ? await packs.GetAvailablePacksAsync().ConfigureAwait(false)
                    : new List<Models.ContentPack>();

                var lockers = new List<object>();
                foreach (var p in available)
                {
                    var installed = packs?.IsPackInstalled(p.Id) == true;
                    var indexed = BureauIndexService.PackCounts.TryGetValue(p.Id, out var n) ? n : 0;
                    if (!installed && p.ImageCount <= 0) continue;   // video-only packs have no casework
                    if (installed && indexed == 0) continue;
                    lockers.Add(new { id = p.Id, name = p.Name, installed, images = installed ? indexed : p.ImageCount });
                }
                Post(new { type = "packs", lockers, catalogueUrl = DiscordCatalogueUrl });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BureauHostService.HandlePacksAsync failed");
                Post(new { type = "packs", lockers = Array.Empty<object>(), catalogueUrl = DiscordCatalogueUrl });
            }
        }

        /// <summary>Open the Discord pack-catalogue channel in the user's default browser
        /// (the game window's own navigation is locked to the site host).</summary>
        private static void OpenCatalogue()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DiscordCatalogueUrl)
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) { App.Logger?.Warning("BureauHostService.OpenCatalogue: {E}", ex.Message); }
        }

        /// <summary>Raw WebMessageReceived listener: only consumes 'pack-drop', whose dropped-file
        /// paths ride the AdditionalObjects channel (CoreWebView2File) that the JSON pipe can't carry.</summary>
        private static void OnRawWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                JObject o;
                try { o = JObject.Parse(e.WebMessageAsJson); } catch { return; }
                if ((string?)o["type"] != "pack-drop") return;

                string? zipPath = null;
                foreach (var obj in e.AdditionalObjects ?? Enumerable.Empty<object>())
                {
                    if (obj is CoreWebView2File file &&
                        file.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        zipPath = file.Path;
                        break;
                    }
                }
                if (zipPath == null)
                {
                    Post(new { type = "pack-install-failed", error = "That wasn't a pack zip. Drop the pack's .zip file." });
                    return;
                }
                _ = Task.Run(() => HandlePackDropAsync(zipPath));
            }
            catch (Exception ex) { App.Logger?.Warning("BureauHostService.OnRawWebMessage: {E}", ex.Message); }
        }

        /// <summary>Install a dropped pack zip: match it to a known catalogue pack by filename,
        /// run the local-zip install pipeline, then re-index and refresh the lockers.</summary>
        private static async Task HandlePackDropAsync(string zipPath)
        {
            if (_installBusy)
            {
                Post(new { type = "pack-install-failed", error = "An intake is already in progress." });
                return;
            }
            _installBusy = true;
            try
            {
                var packs = App.ContentPacks;
                if (packs == null)
                {
                    Post(new { type = "pack-install-failed", error = "Pack service unavailable." });
                    return;
                }

                // Match the zip to a known pack by normalized filename vs pack Name/Id
                // (distribution zips are named after the pack, e.g. "Bambi Core Collection.zip").
                static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                var stem = Norm(System.IO.Path.GetFileNameWithoutExtension(zipPath));
                var available = await packs.GetAvailablePacksAsync().ConfigureAwait(false);
                var pack = available.FirstOrDefault(p => Norm(p.Name) == stem || Norm(p.Id) == stem);
                if (pack == null)
                {
                    Post(new { type = "pack-install-failed", error = "Unrecognized pack zip. Get official packs from the requisition catalogue." });
                    return;
                }
                if (packs.IsPackInstalled(pack.Id))
                {
                    Post(new { type = "pack-install-failed", error = $"'{pack.Name}' is already in custody." });
                    return;
                }

                Post(new { type = "pack-install-progress", msg = $"Taking custody of '{pack.Name}'..." });
                await packs.InstallPackFromLocalZipAsync(pack, zipPath,
                    status => Post(new { type = "pack-install-progress", msg = status })).ConfigureAwait(false);

                Post(new { type = "pack-install-progress", msg = "Cataloguing the new evidence..." });
                await BureauIndexService.RebuildAsync((done, total) =>
                    Post(new { type = "index-progress", done, total, ready = false })).ConfigureAwait(false);
                Post(new
                {
                    type = "index-progress",
                    done = BureauIndexService.Done,
                    total = BureauIndexService.Total,
                    ready = true,
                });

                Post(new { type = "pack-installed", id = pack.Id, name = pack.Name });
                await HandlePacksAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BureauHostService.HandlePackDropAsync failed");
                Post(new { type = "pack-install-failed", error = ex.Message });
            }
            finally { _installBusy = false; }
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
                _rawHooked = false;   // the CoreWebView2 died with the host; a relaunch re-hooks
                App.Logger?.Information("BureauHostService: closed");
            }
            finally { _disposing = false; }
        }
    }
}
