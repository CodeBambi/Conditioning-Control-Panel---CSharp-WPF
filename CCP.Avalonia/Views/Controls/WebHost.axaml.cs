using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
    /// <see cref="InvokeScriptAsync"/>. NativeWebView additionally offers WebMessageReceived,
    /// NewWindowRequested, NavigationCompleted, GoBack/GoForward, Stop and Refresh; nothing needs
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

        private readonly Panel _webSlot;
        private readonly Border _fallback;
        private readonly TextBlock _txtReason, _txtSource;
        private readonly NativeWebView? _web;

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
                    // Subscribed once, here, rather than when a caller sets AllowNavigation: the
                    // gate has to be live for the FIRST navigation too, and a caller that assigns
                    // the predicate and the Source in that order would otherwise race the engine.
                    _web.NavigationStarted += OnNavigationStarted;
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
                _txtReason.Text = UnavailableReason!;
            }

            _fallback.IsVisible = _web is null;
            ApplySource();
        }

        private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
        {
            var gate = AllowNavigation;
            if (gate is null) return;
            var target = e.Request;
            // No URL to judge: refuse. A navigation the gate cannot see is exactly the one a
            // hostile page would use to slip past it.
            if (target is null) { e.Cancel = true; return; }
            if (gate(target)) return;
            e.Cancel = true;
            // Host + path only: a signed media URL's query string must not reach the log.
            Log.Warning("WebHost: navigation blocked to {Host}{Path}", target.Host, target.AbsolutePath);
        }

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
            if (_web is not null) { _web.Source = src!; return; }
            // No engine: the panel at least names the page that was meant to load.
            _txtSource.Text = src?.ToString() ?? "";
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
