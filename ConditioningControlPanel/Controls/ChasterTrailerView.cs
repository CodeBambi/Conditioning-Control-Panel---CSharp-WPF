using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// THE TRAILER PLAYER for Circe's tab: a WebView2 onto <c>Resources/web/chaster/trailers.html</c>,
    /// the saved picker mockup's vignette engine (27 looping 320x200 scenes, one per price row).
    /// The page's hover popup owns ONE of these and only ever switches the scene, so the browser
    /// is created once and reused across every row.
    ///
    /// <para>Copied from <see cref="SpiralEmbedView"/>'s shape: probe the runtime before building
    /// anything, own browser profile, the same anti-occlusion arguments (the argument string is
    /// CONSTANT per user-data folder, or environment creation fails), navigation locked to the
    /// one virtual host, and every failure ends at <see cref="Failed"/> so the host can draw its
    /// still picture instead. Never a transparent window: the popup that hosts this is opaque.</para>
    ///
    /// <para><see cref="BrowserEnabled"/> is a static test seam: the render tests realize the page
    /// with no window behind it, where a browser can neither be built nor wanted.</para>
    /// </summary>
    public sealed class ChasterTrailerView : Grid
    {
        public const string VirtualHost = "ccp.chaster";
        private const string PagePath = "/trailers.html";
        private const string UserDataFolderName = "browser_data_chaster";

        /// <summary>Test seam: false skips the browser entirely and reports <see cref="Failed"/>.</summary>
        internal static bool BrowserEnabled = true;

        private WebView2? _web;
        private bool _initStarted;
        private bool _disposed;
        private bool _failed;
        private string? _pending;

        /// <summary>True once the trailers page has navigated and can mount a scene.</summary>
        public bool IsReady { get; private set; }

        /// <summary>True once this view has given up (no runtime, dead process, bad navigation).</summary>
        public bool HasFailed => _failed;

        /// <summary>Raised (UI thread) when the view gives up. Hosts draw their fallback on this.</summary>
        public event EventHandler<string>? Failed;

        /// <summary>Raised (UI thread) when the page is there and the first scene can mount.</summary>
        public event EventHandler? Ready;

        public ChasterTrailerView()
        {
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x0C, 0x1F));
        }

        /// <summary>Where the trailers page ships: beside the exe, under Resources/web/chaster.</summary>
        public static string WebFolder => Path.Combine(AppContext.BaseDirectory, "Resources", "web", "chaster");

        public static string Url => "https://" + VirtualHost + PagePath;

        /// <summary>The script that swaps the scene. Pure, so the id escaping is testable.</summary>
        internal static string MountScript(string vignetteId)
        {
            var safe = (vignetteId ?? "").Replace("\\", "").Replace("'", "").Replace("\"", "").Replace("<", "").Replace(">", "");
            return "window.__mount && window.__mount('" + safe + "')";
        }

        /// <summary>Create the browser and navigate. Only the first call does anything.</summary>
        public void Start()
        {
            if (_initStarted || _disposed) return;
            _initStarted = true;
            _ = InitAsync();
        }

        /// <summary>Play a scene. Before the page is ready the last asked-for scene waits and
        /// mounts on arrival; after a failure this is a no-op.</summary>
        public void Show(string vignetteId)
        {
            if (_disposed || _failed) return;
            _pending = vignetteId;
            Start();
            if (IsReady) _ = MountAsync(vignetteId);
        }

        private async Task MountAsync(string vignetteId)
        {
            try
            {
                var core = _web?.CoreWebView2;
                if (core == null) return;
                await core.ExecuteScriptAsync(MountScript(vignetteId)).ConfigureAwait(true);
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] trailer mount: {E}", ex.Message); }
        }

        private async Task InitAsync()
        {
            try
            {
                if (!BrowserEnabled) { Fail("browser disabled"); return; }
                if (!File.Exists(Path.Combine(WebFolder, "trailers.html"))) { Fail("trailers page missing"); return; }

                string? version = null;
                try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
                catch (Exception ex) { Fail("WebView2 runtime not installed: " + ex.Message); return; }
                if (string.IsNullOrEmpty(version)) { Fail("WebView2 runtime not installed"); return; }

                _web = new WebView2
                {
                    DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x12, 0x0C, 0x1F),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                Children.Add(_web);

                var userDataFolder = Path.Combine(App.UserDataPath, UserDataFolderName);
                Directory.CreateDirectory(userDataFolder);

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
                s.IsWebMessageEnabled = false;

                core.SetVirtualHostNameToFolderMapping(VirtualHost, WebFolder, CoreWebView2HostResourceAccessKind.Deny);
                core.NavigationStarting += OnNavigationStarting;
                // No popups, ever: a window.open would otherwise get a browser window of its own.
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.NavigationCompleted += OnNavigationCompleted;
                core.ProcessFailed += OnProcessFailed;
                core.Navigate(Url);
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        /// <summary>Locked to the trailers page: the engine has no links, so anything else is a mistake.</summary>
        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Uri) ||
                !e.Uri.StartsWith("https://" + VirtualHost + "/", StringComparison.OrdinalIgnoreCase))
                e.Cancel = true;
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) { Fail("navigation failed: " + e.WebErrorStatus); return; }
            IsReady = true;
            try { Ready?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] trailer Ready handler threw: {E}", ex.Message); }
            if (_pending is { } id) _ = MountAsync(id);
        }

        private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e) =>
            Fail("browser process failed: " + e.ProcessFailedKind);

        private void Fail(string why)
        {
            if (_failed) return;
            _failed = true;
            IsReady = false;
            App.Logger?.Debug("[Chaster] trailer view failed: {Why}", why);
            try
            {
                if (_web != null) { Children.Remove(_web); _web.Dispose(); _web = null; }
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] trailer dispose: {E}", ex.Message); }
            try { Failed?.Invoke(this, why); }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] trailer Failed handler threw: {E}", ex.Message); }
        }

        public void Shutdown()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_web != null) { Children.Remove(_web); _web.Dispose(); _web = null; }
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] trailer shutdown: {E}", ex.Message); }
        }
    }
}
