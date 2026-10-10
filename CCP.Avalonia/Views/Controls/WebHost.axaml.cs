using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// A <see cref="NativeWebView"/> that degrades to a legible panel instead of an empty
    /// rectangle when the machine has no web engine.
    ///
    /// This is NOT a ported view. The WPF head talks to Microsoft.Web.WebView2 directly
    /// (ChaosWebViewHost, MainWindow.Browser, SpiralEmbedView) and that package is Windows-only;
    /// Avalonia.Controls.WebView is the cross-platform replacement - WebView2 on Windows,
    /// WebKitGTK or WPE WebKit on Linux - and this control is the single place the head touches
    /// it, so those three hosts have one seam to move onto rather than three.
    ///
    /// Beyond <see cref="Source"/> it surfaces the two things a hosted page has to be driven and
    /// fenced with: <see cref="AllowNavigation"/> (NativeWebView.NavigationStarted, whose args
    /// carry a settable Cancel — so a per-navigation allowlist that also catches redirects is
    /// possible here, unlike what the first pass of these views assumed) and
    /// <see cref="InvokeScriptAsync"/>, plus the live URL (<see cref="CurrentUrl"/>,
    /// <see cref="NavigationCompleted"/>). NativeWebView additionally offers WebMessageReceived,
    /// NewWindowRequested, GoBack/GoForward, Stop and Refresh; nothing needs
    /// them yet, so they are not wrapped. It has NO zoom factor, no document-created script
    /// injection, and no fullscreen-element signal.
    ///
    /// The whole point is the fallback. A Linux box without webkit2gtk installed is the DEFAULT,
    /// not the exception, and a web view with no engine paints nothing: the render proof would
    /// show a blank region and pass. So availability is probed BEFORE anything is constructed, and
    /// the real control is only ever added to the tree when an engine can actually host it.
    /// </summary>
    public partial class WebHost : UserControl
    {
        /// <summary>
        /// True when pages in this process may start audio / video without a click. WPF passes
        /// <c>--autoplay-policy=no-user-gesture-required</c> to every WebView2 it creates (ChaosWebViewHost,
        /// BackRoomHostService, BrowserService); this head hands the same switch to WebView2 in ONE place,
        /// the environment options (<see cref="BrowserArguments"/>, read once per process so the string
        /// stays constant per user-data folder). WebKitGTK has no such switch: false there, and a page
        /// that asks (Arcademy <c>init.autoplayOk</c>) waits for its first click.
        /// <c>CCP_WEBVIEW_AUTOPLAY=off</c> leaves the engine default (the switch is dropped, this reads false).
        /// </summary>
        public static bool AutoplayWithoutGesture { get; } = AutoplayFor(OperatingSystem.IsWindows(), ReadAutoplayEnv());

        private static string? ReadAutoplayEnv()
        {
            try { return Environment.GetEnvironmentVariable("CCP_WEBVIEW_AUTOPLAY"); }
            catch { return null; }
        }

        /// <summary>The rule behind <see cref="AutoplayWithoutGesture"/>: WebView2 only, unless switched off.</summary>
        internal static bool AutoplayFor(bool windows, string? env) =>
            windows && !string.Equals(env?.Trim(), "off", StringComparison.OrdinalIgnoreCase);

        /// <summary>Page to load. Mirrors <see cref="NativeWebView.SourceProperty"/>.</summary>
        public static readonly StyledProperty<Uri?> SourceProperty =
            AvaloniaProperty.Register<WebHost, Uri?>(nameof(Source));

        public Uri? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

        /// <summary>
        /// Per-navigation gate. Return false to cancel. Runs for EVERY navigation the engine
        /// starts — the first one and every redirect, in-page link and script-driven hop after it —
        /// which is what makes it the replacement for WebView2's <c>NavigationStarting</c> allowlist
        /// rather than a one-shot check on <see cref="Source"/>.
        ///
        /// Set it BEFORE assigning <see cref="Source"/>: the gate is read at navigation time, and a
        /// Source assigned first can start navigating before the predicate is in place.
        /// Null means "allow everything", which is the right default for a host that is only ever
        /// pointed at pages the app itself chose.
        /// </summary>
        public Func<Uri, bool>? AllowNavigation { get; set; }

        /// <summary>
        /// The URL the page actually sits on after its last completed navigation: redirects,
        /// in-page links and script hops included, unlike <see cref="Source"/>, which is only
        /// what a caller last asked for. Null until a navigation completes. WPF's
        /// <c>BrowserService.GetCurrentUrl</c>.
        /// </summary>
        public Uri? CurrentUrl { get; private set; }

        /// <summary>
        /// Raised on the UI thread with <see cref="CurrentUrl"/> after every top-level navigation
        /// completes, failed ones included, as WPF's <c>BrowserService.NavigationCompleted</c>
        /// does (BrowserService.cs:1435-1443) - the live-URL signal MainWindow.Browser.cs:132
        /// hangs its status line and catalogue lookup on.
        /// </summary>
        public event Action<Uri>? NavigationCompleted;

        /// <summary>
        /// The seam the engine's NavigationCompleted routes through, internal so a headless test
        /// (which never has an engine) can drive the same path. A completion with no URL raises
        /// nothing: there is nothing live to report.
        /// </summary>
        internal void OnNavigationCompleted(Uri? url)
        {
            if (url is null) return;
            url = FromEngine(url);
            CurrentUrl = url;
            NavigationCompleted?.Invoke(url);
        }

        /// <summary>
        /// A string the page posted (<c>window.invokeCSharpAction</c>, which the engine injects; it
        /// JSON-stringifies objects). The stand-in for WPF's <c>CoreWebView2.WebMessageReceived</c>.
        /// </summary>
        public event Action<string>? WebMessage;

        /// <summary>
        /// One extra request header for a url, or null (the usual answer). The stand-in for WPF's
        /// <c>AddWebResourceRequestedFilter</c> + <c>Request.Headers.SetHeader</c> (JustDropHostService.AttachAuthHeader):
        /// the caller's rule decides, per request, whether its credential rides along. Never logged.
        /// </summary>
        public Func<Uri, (string Name, string Value)?>? RequestHeader { get; set; }

        /// <summary>Requests <see cref="RequestHeader"/> stamped (tests, logs; never the value).</summary>
        internal int StampedRequests { get; private set; }

        private void StampRequestHeader(WebResourceRequestedEventArgs e)
        {
            var rule = RequestHeader;
            if (rule is null) return;
            try
            {
                var uri = e.Request?.Uri;
                if (uri is null || rule(uri) is not { } header) return;
                if (e.Request!.Headers.TrySet(header.Name, header.Value)) StampedRequests++;
            }
            catch (Exception ex) { Log.Debug("WebHost: request header not set: {Error}", ex.Message); }
        }

        /// <summary>The seam the engine's WebMessageReceived routes through (headless tests drive it).</summary>
        internal void OnWebMessage(string? body)
        {
            if (!string.IsNullOrEmpty(body)) WebMessage?.Invoke(body);
        }

        /// <summary>
        /// True when THIS instance built an adapter. <see cref="IsAvailable"/> is the process-wide
        /// probe; the constructor can still fail after it passes, and a caller about to drive the
        /// page through script needs to know about this control, not about the machine.
        /// </summary>
        public bool HasEngine => _web is not null;

        /// <summary>
        /// Runs JS in the current page and returns its result, or null when there is no engine.
        /// The stand-in for <c>CoreWebView2.ExecuteScriptAsync</c>.
        ///
        /// The two adapters do NOT agree on the return shape: WebView2 hands back a JSON literal
        /// (a string result arrives quoted), WebKitGTK hands back the raw value. Callers that read
        /// the result must tolerate both — see <c>EnhancementPlayerWindow.Unquote</c>.
        ///
        /// There is no equivalent of <c>AddScriptToExecuteOnDocumentCreatedAsync</c>, so anything
        /// that must be present before the page's own scripts run has no seam here.
        /// </summary>
        public async Task<string?> InvokeScriptAsync(string javaScript)
        {
            if (_web is null || string.IsNullOrEmpty(javaScript)) return null;
            try { return await _web.InvokeScript(javaScript); }
            catch (Exception ex)
            {
                Log.Debug("WebHost: InvokeScript failed: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// True when some adapter is both installed and able to host a native control. Probed once
        /// per process: <see cref="WebViewAdapterInfo.GetAdapterInfo"/> shells out to the platform
        /// (dlopen of libwebkit2gtk, a WebView2 registry lookup) and the answer cannot change while
        /// the app runs.
        /// </summary>
        public static bool IsAvailable => UnavailableReason is null;

        private static readonly Lazy<string?> _reason = new(ProbeUnavailableReason);

        /// <summary>Null when a web view can be hosted; otherwise the platform's own explanation.</summary>
        public static string? UnavailableReason => _reason.Value;

        private static readonly Lazy<(string Text, string? Command)?> _linuxHint = new(() =>
            ConditioningControlPanel.Services.LinuxDependencies.Check(
                ConditioningControlPanel.Services.LinuxDependencies.All.Where(d => d.Need.StartsWith("web views"))));

        private readonly Panel _webSlot;
        private readonly Border _fallback;
        private readonly TextBlock _txtReason, _txtSource;
        private readonly NativeWebView? _web;

        /// <summary>WebView2's browser switches (WPF GoonHostService.cs:284, ChaosWebViewHost): media starts
        /// without a click, and a game behind another window keeps its timers (a live shared match clock).
        /// ONE constant for every WebHost: they share a user-data folder, and WebView2 refuses a second
        /// environment on a folder whose running browser was started with different switches.
        /// ponytail: WebKitGTK has no such switch here; autoplay there needs the page's own gesture.</summary>
        internal const string WindowsBrowserArguments = AutoplaySwitch + " " + NoThrottlingSwitches;
        private const string AutoplaySwitch = "--autoplay-policy=no-user-gesture-required";
        // CalculateNativeWinOcclusion off (WPF ChaosWebViewHost passes it to every host): the For You
        // ghost parks its window off the virtual desktop, and Chromium would call it occluded, stop
        // rendering and freeze the mirror on a still frame.
        private const string NoThrottlingSwitches = "--disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-features=CalculateNativeWinOcclusion";

        /// <summary>What a WebView2 environment gets: the whole constant, or (CCP_WEBVIEW_AUTOPLAY=off)
        /// the same without the autoplay switch, so <see cref="AutoplayWithoutGesture"/> stays the truth.</summary>
        internal static string BrowserArgumentsFor(bool autoplay) => autoplay ? WindowsBrowserArguments : NoThrottlingSwitches;

        /// <summary>The one string this process hands every WebView2 environment.</summary>
        internal static string BrowserArguments { get; } = BrowserArgumentsFor(AutoplayWithoutGesture);

        /// <summary>The WebView2 profile folder NAME under UserData (Platform/WebProfiles: WPF's own
        /// names, so sign-ins survive the upgrade). Set it before the control is shown: the engine
        /// asks once, when the native view first attaches. Windows only; WebKitGTK ignores it.</summary>
        public string Profile { get; set; } = Platform.WebProfiles.Browser;

        private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
        {
            if (e is not WindowsWebView2EnvironmentRequestedEventArgs win) return;
            win.AdditionalBrowserArguments = string.IsNullOrWhiteSpace(win.AdditionalBrowserArguments)
                ? BrowserArguments
                : win.AdditionalBrowserArguments + " " + BrowserArguments;
            // In UserData, never beside the exe (the default): an update or a mirrored deploy of the
            // install folder would wipe the profile, and Program Files is not writable.
            try
            {
                var folder = Platform.WebProfiles.FolderFor(Profile);
                System.IO.Directory.CreateDirectory(folder);
                win.UserDataFolder = folder;
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "WebHost: profile folder {Profile} unavailable; engine default", Profile); }
        }

        public WebHost()
        {
            AvaloniaXamlLoader.Load(this);
            _webSlot = this.FindControl<Panel>("WebSlot")!;
            _fallback = this.FindControl<Border>("Fallback")!;
            _txtReason = this.FindControl<TextBlock>("TxtReason")!;
            _txtSource = this.FindControl<TextBlock>("TxtSource")!;

            if (IsAvailable)
            {
                // try/catch, not a bare new: the probe says an engine is installed, it does not
                // promise the adapter builds. A throw here must show the panel, not kill the host.
                try
                {
                    _web = new NativeWebView();
                    _web.EnvironmentRequested += OnEnvironmentRequested;
                    _web.AdapterCreated += (_, e) => OnAdapterCreated(CoreWebView2Of(e));
                    _web.AdapterDestroyed += (_, _) => OnAdapterDestroyed();
                    // Subscribed once, here, rather than when a caller sets AllowNavigation: the
                    // gate has to be live for the FIRST navigation too, and a caller that assigns
                    // the predicate and the Source in that order would otherwise race the engine.
                    _web.NavigationStarted += OnNavigationStarted;
                    _web.NavigationCompleted += (_, e) => OnNavigationCompleted(e.Request ?? _web?.Source);
                    _web.WebMessageReceived += (_, e) => OnWebMessage(e.Body);
                    _web.WebResourceRequested += (_, e) => StampRequestHeader(e);   // k9: RequestHeader below
                    _webSlot.Children.Add(_web);
                }
                catch (Exception ex)
                {
                    _web = null;
                    _txtReason.Text = ex.Message;
                }
            }
            else
            {
                // Linux: name the distro's install command (docs/avalonia-linux-install.md) instead of
                // the adapter's generic "Install webkit2gtk 4.0+ package.", which stays the fallback.
                var hint = _linuxHint.Value;
                _txtReason.Text = hint?.Text ?? UnavailableReason!;
                if (hint?.Command is { } command)
                {
                    var copy = this.FindControl<Button>("BtnCopyCommand")!;
                    copy.IsVisible = true;
                    copy.Click += async (_, _) =>
                    {
                        try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } c) await c.SetTextAsync(command); }
                        catch (Exception ex) { Log.Debug("WebHost: copy failed: {Error}", ex.Message); }
                    };
                }
            }

            _fallback.IsVisible = _web is null;
            ApplySource();
        }

        // ---- the app's own pages: WPF's https://ccp.* origins ---------------------------------

        /// <summary>Where this view stands with the app's virtual hosts (Platform/WebView2Hosts).</summary>
        internal enum AppHostState
        {
            /// <summary>No engine yet: an app page is held until the adapter exists.</summary>
            Waiting,
            /// <summary>The hosts are on this web view: <c>https://ccp.game</c> loads as under WPF.</summary>
            Installed,
            /// <summary>No virtual hosts here (WebKitGTK, or the install failed): app pages load from
            /// the loopback server, and callers still see the url they asked for.</summary>
            Loopback,
        }

        internal AppHostState AppHosts { get; private set; } = AppHostState.Waiting;

        /// <summary>The server whose pages this view carries; null = <see cref="Platform.WebAssetServer.Shared"/>
        /// (looked up only when a url looks like an app page, so a plain browser never starts it).</summary>
        internal Platform.WebAssetServer? AppServer { get; set; }

        /// <summary>Test seam: puts the hosts on a live ICoreWebView2 (null = it cannot be done).</summary>
        internal Func<IntPtr, Platform.WebAssetServer, IDisposable?> InstallHosts { get; set; } =
            (core, server) => Platform.WebView2Hosts.TryInstall(core, server);

        private IDisposable? _hosts;
        private Uri? _held;

        private static IntPtr CoreWebView2Of(WebViewAdapterEventArgs e)
        {
            try { return e.TryGetPlatformHandle() is IWindowsWebView2PlatformHandle win ? win.CoreWebView2 : IntPtr.Zero; }
            catch (Exception ex) { Log.Debug("WebHost: no platform handle: {Error}", ex.Message); return IntPtr.Zero; }
        }

        private Platform.WebAssetServer? ServerFor(Uri? url)
        {
            if (url is not { IsAbsoluteUri: true } || url.Scheme != Uri.UriSchemeHttps
                || !url.Host.StartsWith("ccp.", StringComparison.Ordinal)) return null;
            var server = AppServer ?? Platform.WebAssetServer.Shared;
            return server.IsVirtual(url) ? server : null;
        }

        /// <summary>
        /// The engine exists. A WebView2 gets the virtual hosts when the server wants them; if that
        /// fails the server is demoted (every later url is loopback) and this view translates. Then
        /// the page that was waiting loads. <paramref name="coreWebView2"/> is zero off WebView2.
        /// </summary>
        internal void OnAdapterCreated(IntPtr coreWebView2)
        {
            var server = AppServer ?? (Platform.WebAssetServer.VirtualHostsEnabled ? Platform.WebAssetServer.Shared : null);
            if (server is { VirtualHosts: true })
            {
                try { _hosts = coreWebView2 == IntPtr.Zero ? null : InstallHosts(coreWebView2, server); }
                catch (Exception ex) { _hosts = null; Log.Warning("WebHost: virtual hosts failed: {Error}", ex.Message); }
                if (_hosts is null) server.DemoteToLoopback();
            }
            AppHosts = _hosts is null ? AppHostState.Loopback : AppHostState.Installed;
            var held = _held;
            _held = null;
            if (held is not null) ToEngine(held, explicitNavigate: true);
        }

        private void OnAdapterDestroyed()
        {
            try { _hosts?.Dispose(); } catch (Exception ex) { Log.Debug("WebHost: hosts dispose: {Error}", ex.Message); }
            _hosts = null;
            AppHosts = AppHostState.Waiting;
        }

        /// <summary>
        /// What the engine is given for a url a caller asked for: the url itself, an app page's
        /// loopback twin when this view has no virtual hosts, or null while the engine does not exist
        /// yet (an app page must not start loading before its hosts are in place, or
        /// <c>https://ccp.game</c> would be asked of the real network).
        /// </summary>
        internal Uri? EngineUrl(Uri url)
        {
            var server = ServerFor(url);
            if (server is null) return url;
            switch (AppHosts)
            {
                case AppHostState.Installed: return url;
                case AppHostState.Loopback: _translated = true; return server.ToLoopback(url);
                default: return null;
            }
        }

        /// <summary>The url a caller sees for one the engine reports: a translated app page reads as
        /// the <c>https://ccp.*</c> url the caller asked for, so its same-origin guard holds on both transports.</summary>
        internal Uri FromEngine(Uri url)
        {
            if (AppHosts != AppHostState.Loopback || !_translated) return url;
            var server = AppServer ?? Platform.WebAssetServer.Shared;
            return server.IsLoopback(url) ? server.ToVirtual(url) : url;
        }

        private bool _translated;

        /// <summary>May the engine load this? A sandbox loads no real site; an app page is no site
        /// (it never leaves the machine) but only where the hosts stand in front of it.</summary>
        private bool Permitted(Uri? url) =>
            ServerFor(url) is not null ? AppHosts == AppHostState.Installed : ConditioningControlPanel.Services.SandboxNet.Allows(url);

        private void ToEngine(Uri url, bool explicitNavigate)
        {
            if (_web is null) return;
            var engine = EngineUrl(url);
            if (engine is null) { _held = url; return; }
            if (!Permitted(engine)) { if (!explicitNavigate) _web.Source = null!; return; }
            try
            {
                if (explicitNavigate) _web.Navigate(engine);
                else _web.Source = engine;
            }
            catch (Exception ex) { Log.Debug("WebHost: Navigate failed: {Error}", ex.Message); }
        }

        private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
        {
            var target = e.Request;
            // A CCP_USERDATA_DIR sandbox never loads a real site, whatever the caller's gate says; and
            // an app url never reaches a web view whose hosts are not installed.
            if (!Permitted(target)) { e.Cancel = true; return; }
            if (target is not null) target = FromEngine(target);
            var gate = AllowNavigation;
            if (gate is null) return;
            // No URL to judge: refuse. A navigation the gate cannot see is exactly the one a
            // hostile page would use to slip past it.
            if (target is null) { e.Cancel = true; return; }
            if (gate(target)) return;
            e.Cancel = true;
            // Host + path only: a signed media URL's query string must not reach the log.
            Log.Warning("WebHost: navigation blocked to {Host}{Path}", target.Host, target.AbsolutePath);
        }

        /// <summary>
        /// Loads <paramref name="url"/> even when it is already the <see cref="Source"/>: WPF #867, clicking
        /// the site you are on takes you home and Reload reloads. Assigning an equal Source raises no
        /// change, so it would do nothing. Source is kept in step (the fallback panel names it).
        /// </summary>
        public void Navigate(Uri url)
        {
            _explicitNavigate = true;
            try { Source = url; } finally { _explicitNavigate = false; }
            NavigationRequests++;
            // A sandbox never loads a real site (as ApplySource): the panel may name it, the engine never gets it.
            // An app page is not a site: it is served from this machine on either transport.
            if (ServerFor(url) is null && !ConditioningControlPanel.Services.SandboxNet.Allows(url)) { RefusedNavigations++; return; }
            ToEngine(url, explicitNavigate: true);
        }

        /// <summary>The URL as the fallback panel names it: WebAssetServer's ccp_t token never reaches the screen.</summary>
        internal static string WithoutToken(Uri src)
        {
            if (!src.IsAbsoluteUri || src.Query.Length < 2) return src.ToString();
            var query = string.Join('&', src.Query[1..].Split('&').Where(p => !p.StartsWith("ccp_t=", StringComparison.Ordinal)));
            return new UriBuilder(src) { Query = query }.Uri.ToString();
        }

        /// <summary>Count of <see cref="Navigate"/> calls; a headless test's view of them.</summary>
        internal int NavigationRequests { get; private set; }

        /// <summary>Count of <see cref="Navigate"/> calls the sandbox kept from the engine.</summary>
        internal int RefusedNavigations { get; private set; }

        private bool _explicitNavigate;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == SourceProperty) ApplySource();
        }

        private void ApplySource()
        {
            if (_txtSource is null) return; // fired before the XAML loaded
            var src = Source;
            // src! because NativeWebView.Source is annotated non-nullable while its StyledProperty's
            // own default IS null - clearing the page back to null is a real state, not a bug.
            // A sandbox never loads a real site (see OnNavigationStarted); the panel may still name it.
            if (_web is not null)
            {
                if (_explicitNavigate) return;
                _held = null;
                if (src is null) _web.Source = null!;
                else ToEngine(src, explicitNavigate: false);
                return;
            }
            // No engine: the panel at least names the page that was meant to load.
            _txtSource.Text = src is null ? "" : WithoutToken(src);
            _txtSource.IsVisible = src is not null;
        }

        /// <summary>
        /// Walks every adapter the package knows about and returns null as soon as one is installed
        /// and advertises <see cref="WebViewEmbeddingScenario.NativeControlHost"/>. The scenario
        /// check is what keeps a headless render honest: the Headless adapter reports itself
        /// installed but offers only <c>OffscreenRenderer</c>, so it never passes for an embedded
        /// control and <c>--render-all</c> always draws the fallback.
        /// </summary>
        private static string? ProbeUnavailableReason()
        {
            string? firstReason = null;
            foreach (WebViewAdapterType type in Enum.GetValues(typeof(WebViewAdapterType)))
            {
                if (type == WebViewAdapterType.Unknown) continue;
                DetailedWebViewAdapterInfo info;
                try
                {
                    info = WebViewAdapterInfo.GetAdapterInfo(type);
                }
                catch (Exception)
                {
                    // The macOS adapter's static constructor throws off macOS. An adapter that
                    // cannot even be asked about is, for our purposes, not there.
                    continue;
                }
                if (!info.IsSupported) continue;
                if (info.IsInstalled && info.SupportedScenarios.HasFlag(WebViewEmbeddingScenario.NativeControlHost))
                    return null;
                // Prefer the first supported-but-missing engine's message: on Linux that is the
                // actionable "Install webkit2gtk 4.0+ package", not "not supported on this platform".
                if (firstReason is null && !string.IsNullOrWhiteSpace(info.UnavailableReason))
                    firstReason = info.UnavailableReason;
            }
            return firstReason ?? "No native web view on this platform.";
        }
    }
}
