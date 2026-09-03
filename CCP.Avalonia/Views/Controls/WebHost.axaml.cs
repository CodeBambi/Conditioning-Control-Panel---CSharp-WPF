using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

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
