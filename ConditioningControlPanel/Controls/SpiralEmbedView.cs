using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// THE SPIRAL EMBED — the WebView2 window onto cclabs-web's `/embed/spiral` canvas
    /// (CONTRACTS-0812-FINISH §9). One canvas, three hosts: this is the desktop one.
    ///
    /// THE POINT OF THE EMBED, and the reason it is not just "a page in a webview":
    /// the route renders ONLY the canvas and makes NO authenticated request of its own.
    /// It has no session, no token and no cookie worth having. Every number it draws is
    /// handed to it by this class over postMessage. That is law §0.1 made structural —
    /// the desktop's X-Auth-Token never enters a browser, because the browser is never
    /// in a position to need one.
    ///
    /// PROTOCOL (§9 v2, exact):
    ///   host → page   { type: 'spiral:state', devotion_days, stage, relapse_multiplier?,
    ///                   vat_fill_pct?, lit_station_ids?, reduced_motion?, perf_tier?,
    ///                   descent? }
    ///   page → host   { type: 'spiral:ready' }
    /// v2 ADDS ONE OPTIONAL FIELD and changes nothing else. `descent` is the server's
    /// own block, forwarded VERBATIM (DescentBlock.RawJson), and it is present only when
    /// one exists. Absent is the v1 contract and stays legal in both directions: an old
    /// embed drops the unknown key, and a new embed against a v1 host simply never sees
    /// it. See BuildState for why the forward is raw rather than re-authored.
    /// State posted before 'spiral:ready' is QUEUED, not dropped — the canvas's boot and
    /// the descent block's arrival race every launch, and whichever wins, the last state
    /// posted is the state drawn. Note the handshake is 'spiral:ready', NOT the bare
    /// 'ready' the DtRH/Goon bridge uses, so this cannot reuse ChaosWebViewHost's pipe.
    ///
    /// FAILS SOFT, ALWAYS (§9). Missing WebView2 runtime, a dead browser process, a 404
    /// on a route that has not deployed yet — every one of them ends the same way: this
    /// view stays empty and reports failure to its host, which draws the static stage
    /// badge instead. The Spiral is the hook; it is never a dependency of playing.
    ///
    /// AIRSPACE — READ BEFORE MOVING THIS ANYWHERE NEW. A WebView2 is a native child
    /// HWND: it does not clip to rounded corners, does not fade with an opacity
    /// animation, does not scroll inside a ScrollViewer and cannot be drawn over. Every
    /// other WebView2 in this app is either its own window or a plain rectangular slab
    /// in a static tab. See VatGlassCanvas's header for why the vat is Skia instead, and
    /// SpiralRailHost for the mitigation this one ships with.
    /// </summary>
    public sealed class SpiralEmbedView : Grid
    {
        /// <summary>Origin of the embed. Navigation is locked to it; nothing else may load.</summary>
        public const string EmbedHost = "app.cclabs.app";

        private const string EmbedPath = "/embed/spiral";

        /// <summary>Shared browser profile for both modes — one warm cache, one cookie jar (empty).</summary>
        private const string UserDataFolderName = "browser_data_spiral";

        private readonly string _mode;
        private readonly List<string> _pending = new();
        private WebView2? _web;
        private bool _initStarted;
        private bool _disposed;

        /// <summary>True once the canvas has posted 'spiral:ready'.</summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// Raised (UI thread) when this view has given up: no runtime, no core, a dead
        /// process, or a navigation failure. Hosts draw their fallback on this.
        /// </summary>
        public event EventHandler<string>? Failed;

        /// <summary>Raised (UI thread) on 'spiral:ready'.</summary>
        public event EventHandler? Ready;

        /// <summary>
        /// Raised (UI thread) when a navigation to the embed origin COMPLETED SUCCESSFULLY — the
        /// document is there, whatever the canvas does with it next.
        ///
        /// <para><b>This is the splash's cue, and it is deliberately earlier than
        /// <see cref="Ready"/>.</b> A host that waited for the handshake would be waiting on the
        /// page's own boot as well as on the network, and a canvas that loaded but never posted
        /// 'spiral:ready' (an old deploy, a script error) would leave the splash up forever with
        /// a perfectly good spiral behind it. Navigation success is the last moment the HOST can
        /// vouch for on its own, so it is the moment the browser is given its airspace.</para>
        /// </summary>
        public event EventHandler? Navigated;

        /// <param name="mode">"mini" (the 80px rail miniature) or "map" (the expanded surface).</param>
        public SpiralEmbedView(string mode)
        {
            _mode = mode == "map" ? "map" : "mini";
            // Opaque, and matching the rail's own sink so a slow first paint reads as a
            // dark panel rather than a white flash. There is no transparent WebView2.
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x36));
        }

        /// <summary>The full embed URL for this view's mode.</summary>
        public string Url => BuildUrl(_mode);

        /// <summary>
        /// Pure URL builder, split out so the route can be asserted without an STA thread
        /// and a live WebView2. Anything that is not "map" is "mini" — the mode reaches a
        /// query string, so it is normalised here rather than trusted.
        /// </summary>
        internal static string BuildUrl(string mode)
            => $"https://{EmbedHost}{EmbedPath}?mode={(mode == "map" ? "map" : "mini")}";

        // ============================== lifecycle ==============================

        /// <summary>
        /// Create the browser and navigate. Safe to call more than once; only the first
        /// does anything. Returns immediately — everything else happens on the
        /// continuation, and every failure path ends at <see cref="Failed"/>.
        /// </summary>
        public void Start()
        {
            if (_initStarted || _disposed) return;
            _initStarted = true;
            _ = InitAsync();
        }

        private async Task InitAsync()
        {
            try
            {
                // PROBE BEFORE BUILDING. BrowserService is the only other place that calls
                // this, and it is worth the one call here: an install with no WebView2
                // runtime should fall back to the badge without ever constructing a
                // control, creating a user-data folder, or logging an exception trace.
                string? version = null;
                try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
                catch (Exception ex)
                {
                    Fail("WebView2 runtime not installed: " + ex.Message);
                    return;
                }
                if (string.IsNullOrEmpty(version)) { Fail("WebView2 runtime not installed"); return; }

                _web = new WebView2
                {
                    DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1C, 0x1C, 0x36),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                Children.Add(_web);

                var userDataFolder = Path.Combine(App.UserDataPath, UserDataFolderName);
                Directory.CreateDirectory(userDataFolder);

                // Same anti-MPO / anti-occlusion pair every other host in the app uses: the
                // canvas animates on rAF, and Chromium's occlusion tracker will happily
                // decide a rail-docked view is covered and throttle the spin to nothing.
                var options = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments =
                        "--disable-direct-composition-video-overlays " +
                        "--disable-features=CalculateNativeWinOcclusion " +
                        "--disable-backgrounding-occluded-windows",
                };

                var env = await CoreWebView2Environment
                    .CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder, options: options)
                    .ConfigureAwait(true);
                if (_disposed) return;

                await _web.EnsureCoreWebView2Async(env).ConfigureAwait(true);
                if (_disposed) return;

                var core = _web.CoreWebView2;
                if (core == null) { Fail("WebView2 core was null after Ensure"); return; }

                var s = core.Settings;
                s.AreDevToolsEnabled = false;
                s.AreDefaultContextMenusEnabled = false;
                s.IsStatusBarEnabled = false;
                s.AreBrowserAcceleratorKeysEnabled = false;
                s.IsZoomControlEnabled = false;
                s.IsBuiltInErrorPageEnabled = false;
                s.IsWebMessageEnabled = true;

                core.NavigationStarting += OnNavigationStarting;
                core.NavigationCompleted += OnNavigationCompleted;
                core.WebMessageReceived += OnWebMessageReceived;
                core.ProcessFailed += OnProcessFailed;

                core.Navigate(Url);
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        /// <summary>
        /// LOCKED TO THE EMBED ORIGIN. The canvas is a pure renderer with no links, so a
        /// navigation anywhere else is either a mistake or something wearing the app's
        /// chrome. Cancel it; there is no in-app browser to hand it off to.
        /// </summary>
        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Uri) ||
                !e.Uri.StartsWith("https://" + EmbedHost + "/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
            }
        }

        /// <summary>
        /// A failed navigation is the expected state until the embed route deploys, and
        /// it must land on the badge rather than on a blank rectangle. IsBuiltInErrorPage
        /// is off, so without this the user would see the background colour and nothing else.
        /// </summary>
        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) { Fail("navigation failed: " + e.WebErrorStatus); return; }

            // The host has been holding the browser's airspace back behind a splash since the tab
            // opened; this is the handoff. A throwing handler must not take the embed down with it.
            try { Navigated?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] Navigated handler threw: {E}", ex.Message); }
        }

        private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            // No auto-recovery, matching every other host in the app: a browser that died
            // once on a decorative surface gets the badge, not a retry loop.
            Fail("browser process failed: " + e.ProcessFailedKind);
        }

        // ============================== the bridge ==============================

        /// <summary>
        /// Post a §9 state message. Queued until the canvas says 'spiral:ready', and only
        /// the LAST queued state survives — an intermediate reading nobody drew is not
        /// worth replaying, and re-posting a burst of them on ready would animate the dot
        /// through a history the user never watched.
        /// </summary>
        public void PostState(DescentBlock? block)
        {
            if (_disposed) return;
            try
            {
                // ToString, not JsonConvert.SerializeObject: the state is already a JObject
                // and the `descent` branch under it is the server's own tokens, so writing
                // the tree straight out is the one serialisation with nothing in the middle
                // that could re-shape a value on the way to the wire.
                var json = BuildState(block).ToString(Formatting.None);
                if (IsReady && _web?.CoreWebView2 != null)
                {
                    _web.CoreWebView2.PostWebMessageAsJson(json);
                }
                else
                {
                    _pending.Clear();
                    _pending.Add(json);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] PostState: {E}", ex.Message);
            }
        }

        /// <summary>
        /// The §9 payload, built from the block the server already sent us and from the
        /// two host-owned signals the canvas cannot know: how much motion this user
        /// allows, and how much GPU the app currently has to spare.
        ///
        /// `descent` (v2) IS THE SERVER'S BLOCK, UNTOUCHED. The reader parses a subset —
        /// devotion, stage, relapse, vat — and drops the rest on the floor, which is why
        /// the map window drew a naked spiral: day_fill, day_quest, stations, breaks,
        /// dates and thresholds never left this process. Rather than teach desktop to
        /// re-author them (three clients, three chances to author them differently), the
        /// original node is re-emitted byte-faithfully from DescentBlock.RawJson and the
        /// embed runs its OWN strict validation on it. THE APP AUTHORS NOTHING HERE:
        /// every number under this key is a number the server stated. Do not "helpfully"
        /// merge, default or patch a field into it — the moment desktop writes into this
        /// object, the canvas is drawing a record the server never agreed to.
        ///
        /// PRESENCE IS THE CONTRACT, not null-ness. With no block (or a hand-built one
        /// with no raw), the key is ABSENT — that is v1 exactly, which is what an embed
        /// deployed before v2 expects. A null-valued `descent` would be a third state
        /// nobody specified.
        ///
        /// `lit_station_ids` remains DELIBERATELY ABSENT. As of proxy descent block v3
        /// (Spiral W2, 2026-08-29) the server emits `descent.stations[]` itself: personal
        /// keepsakes lit by the day they were banked, world stations pinned to the first
        /// banked day inside their window, and one personal teaser. Before v3 the key was
        /// absent and every keepsake rendered dark. Desktop forwards the block RawJson
        /// verbatim, so nothing here needed to change to carry it, and desktop still
        /// authors no lit list of its own: the station registry lives in cclabs-web
        /// (lib/descent/stations.ts) and the web parser ignores `lit_station_ids` outright
        /// when `stations` is present.
        /// </summary>
        internal static JObject BuildState(DescentBlock? block)
        {
            var state = new JObject
            {
                ["type"] = "spiral:state",
                ["devotion_days"] = block?.DevotionDays ?? 0,
                ["stage"] = block?.Stage?.N ?? 0,
                ["relapse_multiplier"] = block?.Relapse?.Multiplier ?? 1.0,
                ["vat_fill_pct"] = block?.Vat?.FillPct ?? 0.0,
                ["reduced_motion"] = MotionFx.Level != Models.MotionLevel.Full,
                ["perf_tier"] = PerfTier(),
            };

            // Re-parsed with date coercion OFF for the same reason DescentReader.ParseWire
            // turns it off: the default rewrites every ISO-8601-shaped STRING into a
            // DateTime, and re-serialising that emits an invented offset. The whole point
            // of this field is that what arrives at the canvas is what the server sent,
            // so the round trip has to be lossless or it is not a verbatim forward.
            var raw = ParseVerbatim(block?.RawJson);
            if (raw is not null) state["descent"] = raw;

            return state;
        }

        /// <summary>Re-parse a stored block with date coercion off. Null on anything unusable.</summary>
        private static JToken? ParseVerbatim(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonConvert.DeserializeObject<JToken>(
                    json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
            }
            catch (Exception ex)
            {
                // The block already parsed once to exist at all, so this is unreachable in
                // practice — and if it ever is reached, the field drops out and the canvas
                // gets a clean v1 payload rather than a half-forwarded one.
                App.Logger?.Debug("[Spiral] raw descent re-parse failed: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Map the app's own tier onto §9's three-value scale. Quality is the tier the
        /// app considers "indistinguishable from unoptimised", so it is 'high'; the two
        /// escalations are the ones that mean the machine is already busy.
        /// </summary>
        private static string PerfTier() => PerformanceProfile.CurrentTier switch
        {
            Models.PerformanceTier.Performance => "low",
            Models.PerformanceTier.Balanced => "mid",
            _ => "high",
        };

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // Runs on the browser's message loop — nothing here may block or go modal.
            try
            {
                var json = e.WebMessageAsJson;
                if (string.IsNullOrEmpty(json)) return;
                var o = JObject.Parse(json);
                if ((string?)o["type"] != "spiral:ready") return;

                IsReady = true;
                foreach (var queued in _pending)
                {
                    try { _web?.CoreWebView2?.PostWebMessageAsJson(queued); } catch { /* page gone */ }
                }
                _pending.Clear();
                Ready?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] OnWebMessageReceived: {E}", ex.Message);
            }
        }

        // ============================== teardown ==============================

        private void Fail(string reason)
        {
            App.Logger?.Information("[Spiral] embed unavailable ({Mode}): {Reason} - falling back", _mode, reason);
            Dispose();
            try { Failed?.Invoke(this, reason); }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] Failed handler threw: {E}", ex.Message); }
        }

        /// <summary>Tear the browser down and empty the view. Idempotent.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            IsReady = false;
            _pending.Clear();
            try
            {
                if (_web != null)
                {
                    var core = _web.CoreWebView2;
                    if (core != null)
                    {
                        core.NavigationStarting -= OnNavigationStarting;
                        core.NavigationCompleted -= OnNavigationCompleted;
                        core.WebMessageReceived -= OnWebMessageReceived;
                        core.ProcessFailed -= OnProcessFailed;
                    }
                    Children.Remove(_web);
                    _web.Dispose();
                    _web = null;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] dispose: {E}", ex.Message);
            }
        }
    }
}
