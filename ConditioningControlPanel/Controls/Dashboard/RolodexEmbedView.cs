using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services.Dashboard;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Controls.Dashboard
{
    /// <summary>
    /// THE ROLODEX EMBED - the WebView2 that lays the three.js feature picker straight over the
    /// Home wall (<c>Resources\web\rolodex</c>, plan 9.1). Same shape as
    /// <see cref="ConditioningControlPanel.Controls.SpiralEmbedView"/>: probe the runtime before
    /// building anything, own the environment, hand the page everything it draws over postMessage,
    /// and fail soft into the picker that has no browser in it.
    ///
    /// <para>AIRSPACE - READ BEFORE MOVING THIS. A WebView2 is a native child HWND: it does not
    /// clip to rounded corners, does not fade with an opacity animation, and CANNOT BE DRAWN OVER.
    /// That last one is a rule this host obeys twice. Everything under the rolodex is parked while
    /// it is up (the mosaic canvas, the lock ribbon, the nine cells and their pencils), and the
    /// Replace / Split question that follows a pick is asked only AFTER the view is gone - a WPF
    /// dialog over this rectangle would be a dialog nobody can see. See SpiralEmbedView:48-53 for
    /// the long version.</para>
    ///
    /// <para>NO HOST OBJECTS, EVER. postMessage both ways, like every other hosted surface in the
    /// app (page CLAUDE.md §7.4). The page is handed art as data URIs and localized strings; it
    /// has no way to ask this process for anything else.</para>
    /// </summary>
    public sealed class RolodexEmbedView : Grid, IDisposable
    {
        /// <summary>The virtual host the whole <c>Resources\web</c> tree already answers on.</summary>
        public const string PageHost = "ccp.game";

        /// <summary>Navigation is locked to this. The page has no links; anything else loading
        /// here is either a mistake or something wearing the app's chrome.</summary>
        public const string PageUrl = "https://" + PageHost + "/rolodex/index.html";

        /// <summary>Its own profile. The picker shares nothing with the tunnel or the Arcademy -
        /// it has no cookies, no storage and no network, so a folder of its own costs a directory
        /// and buys a browser that cannot be wedged by somebody else's crash.</summary>
        private const string UserDataFolderName = "browser_data_rolodex";

        private readonly List<string> _pending = new();
        private WebView2? _web;
        private Window? _window;
        private bool _initStarted;
        private bool _disposed;

        /// <summary>True once the page has posted <c>ready</c> and the queue has been flushed.</summary>
        public bool IsReady { get; private set; }

        /// <summary>Edit mode, one pick, exactly once. The page goes inert after it and the host
        /// closes the view (page CLAUDE.md §2).</summary>
        public event Action<string>? Picked;

        /// <summary>Tour mode, the keys in pick order. Fires once; a <see cref="CloseRequested"/>
        /// may still follow it, which is why closing has to be idempotent.</summary>
        public event Action<IReadOnlyList<string>>? TourDone;

        /// <summary>Esc, the close button, or the page giving up. The host owns the teardown.</summary>
        public event Action? CloseRequested;

        /// <summary>No runtime, no core, a dead process or a navigation failure. The host latches
        /// <see cref="RolodexAvailability"/> on this and never asks again this session.</summary>
        public event Action<string>? InitFailed;

        public RolodexEmbedView()
        {
            // The page's own ground colour, before a single byte of it has loaded. There is no
            // transparent WebView2 and no fade into one, so the first frame has to already be the
            // right darkness or the wall flashes (page CLAUDE.md §7.1).
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x07, 0x10));
        }

        // ============================== lifecycle ==============================

        /// <summary>
        /// Build the browser and navigate. Safe to call twice; only the first does anything.
        /// Returns immediately, and every failure path ends at <see cref="InitFailed"/> - including
        /// the runtime probe, which is SYNCHRONOUS, so a machine with no WebView2 can report back
        /// before this call has returned.
        /// </summary>
        public void Start()
        {
            if (_initStarted || _disposed) return;
            _initStarted = true;
            HookWindow();
            _ = InitAsync();
        }

        private async Task InitAsync()
        {
            try
            {
                // PROBE FIRST, like the spiral embed. An install with no runtime should reach the
                // flat picker without ever constructing a control or creating a profile folder.
                string? version = null;
                try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
                catch (Exception ex) { Fail("WebView2 runtime not installed: " + ex.Message); return; }
                if (string.IsNullOrEmpty(version)) { Fail("WebView2 runtime not installed"); return; }

                _web = new WebView2
                {
                    DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0B, 0x07, 0x10),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                Children.Add(_web);

                var userDataFolder = Path.Combine(App.UserDataPath, UserDataFolderName);
                Directory.CreateDirectory(userDataFolder);

                // The app's anti-MPO / anti-occlusion pair, merged through the one composer so a
                // command line never names --disable-features twice (only the last copy survives,
                // and this page spins on rAF: an occlusion tracker that decides a tab-docked view
                // is covered would freeze the rings mid-turn). PrefersReducedMotionArgument is the
                // same override every other hosted surface gets - the page ALSO receives the
                // app's own flag in `init`, and honours whichever is stricter.
                var options = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = ChaosWebViewHost.ComposeBrowserArguments(
                        "--disable-direct-composition-video-overlays "
                        + "--disable-features=CalculateNativeWinOcclusion "
                        + "--disable-backgrounding-occluded-windows",
                        ChaosWebViewHost.PrefersReducedMotionArgument(
                            App.Settings?.Current?.MotionLevel ?? Models.MotionLevel.Full)),
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
                s.AreDefaultScriptDialogsEnabled = false;
                s.IsWebMessageEnabled = true;

                // Allow, not DenyCors: the card faces are drawn into a canvas from these images,
                // and a tainted canvas cannot be read back (DtrhHostService.cs:107-113).
                var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
                if (Directory.Exists(webRoot))
                {
                    core.SetVirtualHostNameToFolderMapping(
                        PageHost, webRoot, CoreWebView2HostResourceAccessKind.Allow);
                }
                else
                {
                    // Silently: a build with no web folder has bigger problems than this picker,
                    // and the navigation below fails into the flat one a beat later anyway.
                    App.Logger?.Debug("[Rolodex] no Resources/web folder to map");
                }

                core.NavigationStarting += OnNavigationStarting;
                core.NavigationCompleted += OnNavigationCompleted;
                core.WebMessageReceived += OnWebMessageReceived;
                core.ProcessFailed += OnProcessFailed;

                core.Navigate(PageUrl);
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        /// <summary>Locked to the page's own origin. It has no links and fetches nothing, so any
        /// other navigation is cancelled rather than followed.</summary>
        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Uri) ||
                !e.Uri.StartsWith("https://" + PageHost + "/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) Fail("navigation failed: " + e.WebErrorStatus);
        }

        private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
            => Fail("browser process failed: " + e.ProcessFailedKind);

        // ============================== the bridge ==============================

        /// <summary>
        /// Send a message. Queued until the page says <c>ready</c> and flushed in order - the
        /// whole scene arrives in one <c>init</c>, and the host builds it the instant the view is
        /// in the tree rather than waiting on a handshake it would then have to remember.
        /// </summary>
        public void Post(object? message)
        {
            if (_disposed || message == null) return;
            try
            {
                var json = message is JToken token
                    ? token.ToString(Formatting.None)
                    : JsonConvert.SerializeObject(message);

                if (IsReady && _web?.CoreWebView2 != null) _web.CoreWebView2.PostWebMessageAsJson(json);
                else _pending.Add(json);
            }
            catch (Exception ex) { App.Logger?.Debug("[Rolodex] Post: {E}", ex.Message); }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // The browser's message loop. Nothing here may block or go modal - which is also why
            // the host closes the view before it asks the user anything.
            try
            {
                var msg = RolodexBridgeRule.Parse(e.WebMessageAsJson);
                switch (msg.Kind)
                {
                    case RolodexMessageKind.Ready:
                        IsReady = true;
                        foreach (var queued in _pending)
                        {
                            try { _web?.CoreWebView2?.PostWebMessageAsJson(queued); }
                            catch (Exception ex) { Diag.Swallowed(ex, "the page went away mid-flush"); }
                        }
                        _pending.Clear();
                        break;

                    case RolodexMessageKind.Pick:
                        Raise(() => Picked?.Invoke(msg.Key!), "Picked");
                        break;

                    case RolodexMessageKind.TourDone:
                        Raise(() => TourDone?.Invoke(msg.Keys ?? Array.Empty<string>()), "TourDone");
                        break;

                    case RolodexMessageKind.Close:
                        Raise(() => CloseRequested?.Invoke(), "CloseRequested");
                        break;

                    case RolodexMessageKind.Log:
                        App.Logger?.Write(RolodexBridgeRule.LogLevel(msg.Level), "[Rolodex][page]: {Msg}", msg.Text);
                        break;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("[Rolodex] OnWebMessageReceived: {E}", ex.Message); }
        }

        /// <summary>A throwing host handler must not take the browser down with it.</summary>
        private static void Raise(Action raise, string what)
        {
            try { raise(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Rolodex] {What} handler threw", what); }
        }

        // ============================== render visibility ==============================

        private void HookWindow()
        {
            try
            {
                _window = Window.GetWindow(this);
                if (_window != null) _window.StateChanged += OnWindowStateChanged;
            }
            catch (Exception ex) { App.Logger?.Debug("[Rolodex] HookWindow: {E}", ex.Message); }
        }

        /// <summary>
        /// A minimize / restore cycle can leave the controller's IsVisible stuck false, and a
        /// picker that came back as a black rectangle is a picker the user has to guess at. Same
        /// two-lever bounce <c>ChaosWebViewHost.KickRenderVisibility</c> ships: set the controller
        /// directly through the SDK's internals (allowed to miss on a future SDK), then bounce the
        /// ELEMENT's Visibility so the wrapper re-pushes visibility whichever way it was stuck.
        /// </summary>
        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_disposed || _web == null || _window == null) return;
            if (_window.WindowState == WindowState.Minimized) return;

            try
            {
                bool? controllerWas = null;
                try
                {
                    var t = _web.GetType();
                    object? ctrl = t.GetProperty("CoreWebView2Controller",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Public)?.GetValue(_web)
                        ?? t.GetField("_coreWebView2Controller",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(_web);
                    if (ctrl is CoreWebView2Controller c)
                    {
                        controllerWas = c.IsVisible;
                        c.IsVisible = true;
                    }
                }
                catch (Exception ex) { Diag.Swallowed(ex, "SDK internals moved, the element bounce still re-drives the wrapper"); }

                _web.Visibility = Visibility.Hidden;
                _web.Visibility = Visibility.Visible;

                App.Logger?.Information("[Rolodex] render visibility kicked (window restored; controller={Ctrl})",
                    controllerWas?.ToString() ?? "n/a");
            }
            catch (Exception ex) { App.Logger?.Debug("[Rolodex] KickRenderVisibility: {E}", ex.Message); }
        }

        // ============================== teardown ==============================

        private void Fail(string reason)
        {
            App.Logger?.Information("[Rolodex] embed unavailable: {Reason} - falling back", reason);
            Dispose();
            Raise(() => InitFailed?.Invoke(reason), "InitFailed");
        }

        /// <summary>Tear the browser down and empty the view. Idempotent, and the ONE way an HWND
        /// on this wall is ever allowed to end.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            IsReady = false;
            _pending.Clear();
            try
            {
                if (_window != null)
                {
                    _window.StateChanged -= OnWindowStateChanged;
                    _window = null;
                }

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
            catch (Exception ex) { App.Logger?.Debug("[Rolodex] dispose: {E}", ex.Message); }
        }
    }
}
