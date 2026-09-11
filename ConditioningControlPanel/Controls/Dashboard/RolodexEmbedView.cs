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

        /// <summary>Two strikes, the same stand-down BrowserVideoEngine.ReportProcessFailure runs
        /// app-wide: one dead renderer is bad luck, a second one in the same session is a machine
        /// whose browser will not stay up, and the flat shelf takes the rest of the launch.</summary>
        private const int MaxProcessFailures = 2;

        private static int _processFailures;

        private readonly List<string> _pending = new();
        private WebView2? _web;
        private Window? _window;
        private bool _initStarted;
        private bool _disposed;

        /// <summary>True once the page itself has loaded. A cancelled navigation AFTER that is the
        /// lock in <see cref="OnNavigationStarting"/> doing its job, not the picker failing.</summary>
        private bool _navigated;

        /// <summary>Page log lines spent this open. See <see cref="RolodexBridgeRule.MaxLogLinesPerOpen"/>.</summary>
        private int _logLines;

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

        /// <summary>No runtime, no core, or an environment that would not build. The host latches
        /// <see cref="RolodexAvailability"/> on this and never asks again this session, because
        /// none of those come back before a restart.</summary>
        public event Action<string>? InitFailed;

        /// <summary>This open is over and the flat shelf takes the question - but the session is
        /// not over. A page that failed to load once, or a renderer that died once, says nothing
        /// about whether the next pencil can open a rolodex, and latching on either turned one bad
        /// second into a launch with no 3D picker in it.</summary>
        public event Action<string>? OpenAborted;

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

                // The app's anti-MPO / anti-occlusion pair, and NOTHING THAT VARIES. This page
                // spins on rAF, so an occlusion tracker that decided a tab-docked view was covered
                // would freeze the rings mid-turn - but the string itself has to be the same on
                // every open, the way SpiralEmbedView's is. WebView2 ties a user-data folder to the
                // options its environment was built with, and a second environment asking for the
                // same folder with a different command line fails to create at all. The
                // reduced-motion override used to ride along here and it flips with a setting the
                // user can change between two opens, so a motion toggle could leave this picker
                // permanently unable to start. Reduced motion travels in the `init` message
                // instead (`reducedMotion`), which is the copy the page actually reads.
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
                App.Logger?.Debug("[Rolodex] navigation refused: {Uri}", e.Uri);
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess) { _navigated = true; return; }

            // A navigation THIS VIEW cancelled is not a failure worth reporting: the lock above
            // refuses everything that is not the page, and a refusal arrives here looking exactly
            // like a load that did not work. Once the page is up, nothing that fails after it is
            // the picker failing to start either.
            if (_navigated || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
            {
                App.Logger?.Debug("[Rolodex] navigation ended {Status} (page already up: {Up})",
                                  e.WebErrorStatus, _navigated);
                return;
            }

            // One bad load closes THIS open, and the next pencil is allowed to try again.
            Abort("navigation failed: " + e.WebErrorStatus);
        }

        private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            _processFailures++;
            var reason = "browser process failed: " + e.ProcessFailedKind;
            if (_processFailures >= MaxProcessFailures)
            {
                Fail(reason + " (strike " + _processFailures + " this session)");
                return;
            }
            Abort(reason);
        }

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
                        WritePageLog(msg);
                        break;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("[Rolodex] OnWebMessageReceived: {E}", ex.Message); }
        }

        /// <summary>
        /// One page log line, cut short and counted. A page stuck in a loop can post faster than
        /// the log can be written, and the file it would fill is the one users are asked to attach
        /// to a bug report - so a line is capped and an open has a budget, after which the host
        /// says so once and stops listening.
        /// </summary>
        private void WritePageLog(RolodexMessage msg)
        {
            if (_logLines > RolodexBridgeRule.MaxLogLinesPerOpen) return;

            _logLines++;
            if (_logLines > RolodexBridgeRule.MaxLogLinesPerOpen)
            {
                App.Logger?.Information("[Rolodex][page]: {Max} log lines this open - the rest is dropped",
                                        RolodexBridgeRule.MaxLogLinesPerOpen);
                return;
            }

            App.Logger?.Write(RolodexBridgeRule.LogLevel(msg.Level), "[Rolodex][page]: {Msg}",
                              RolodexBridgeRule.ClampPageLog(msg.Text));
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

        /// <summary>The permanent one: there is no rolodex on this machine this session. Only the
        /// runtime probe and an environment that will not build get here.</summary>
        private void Fail(string reason)
        {
            App.Logger?.Information("[Rolodex] embed unavailable: {Reason} - falling back", reason);
            Dispose();
            Raise(() => InitFailed?.Invoke(reason), "InitFailed");
        }

        /// <summary>The one-shot one: this open is over, the flat shelf answers the question this
        /// time, and nothing is latched.</summary>
        private void Abort(string reason)
        {
            if (_disposed) return;
            App.Logger?.Information("[Rolodex] this open is over: {Reason} - the flat picker takes it", reason);
            Dispose();
            Raise(() => OpenAborted?.Invoke(reason), "OpenAborted");
        }

        /// <summary>Tests only. The two-strike count is a session fact, not a product setting.</summary>
        internal static void ResetProcessFailuresForTests() => _processFailures = 0;

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
