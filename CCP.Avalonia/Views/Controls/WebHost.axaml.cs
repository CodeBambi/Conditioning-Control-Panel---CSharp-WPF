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
            CurrentUrl = url;
            NavigationCompleted?.Invoke(url);
        }

        /// <summary>
        /// A string the page posted (<c>window.invokeCSharpAction</c>, which the engine injects; it
        /// JSON-stringifies objects). The stand-in for WPF's <c>CoreWebView2.WebMessageReceived</c>.
        /// </summary>
        public event Action<string>? WebMessage;

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
        private const string NoThrottlingSwitches = "--disable-background-timer-throttling --disable-backgrounding-occluded-windows";

        /// <summary>What a WebView2 environment gets: the whole constant, or (CCP_WEBVIEW_AUTOPLAY=off)
        /// the same without the autoplay switch, so <see cref="AutoplayWithoutGesture"/> stays the truth.</summary>
        internal static string BrowserArgumentsFor(bool autoplay) => autoplay ? WindowsBrowserArguments : NoThrottlingSwitches;

        /// <summary>The one string this process hands every WebView2 environment.</summary>
        internal static string BrowserArguments { get; } = BrowserArgumentsFor(AutoplayWithoutGesture);

        private static void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
        {
            if (e is WindowsWebView2EnvironmentRequestedEventArgs win)
                win.AdditionalBrowserArguments = string.IsNullOrWhiteSpace(win.AdditionalBrowserArguments)
                    ? BrowserArguments
                    : win.AdditionalBrowserArguments + " " + BrowserArguments;
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
                    // Subscribed once, here, rather than when a caller sets AllowNavigation: the
                    // gate has to be live for the FIRST navigation too, and a caller that assigns
                    // the predicate and the Source in that order would otherwise race the engine.
                    _web.NavigationStarted += OnNavigationStarted;
                    _web.NavigationCompleted += (_, e) => OnNavigationCompleted(e.Request ?? _web?.Source);
                    _web.WebMessageReceived += (_, e) => OnWebMessage(e.Body);
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

        private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
        {
            var target = e.Request;
            // A CCP_USERDATA_DIR sandbox never loads a real site, whatever the caller's gate says.
            if (!ConditioningControlPanel.Services.SandboxNet.Allows(target)) { e.Cancel = true; return; }
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
            if (!ConditioningControlPanel.Services.SandboxNet.Allows(url)) { RefusedNavigations++; return; }
            if (_web is null) return;
            try { _web.Navigate(url); }
            catch (Exception ex) { Log.Debug("WebHost: Navigate failed: {Error}", ex.Message); }
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
                if (!_explicitNavigate) _web.Source = ConditioningControlPanel.Services.SandboxNet.Allows(src) ? src! : null!;
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
