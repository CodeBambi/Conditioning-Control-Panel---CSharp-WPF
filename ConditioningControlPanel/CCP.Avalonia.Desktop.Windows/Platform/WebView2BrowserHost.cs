using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Avalonia.Controls;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Platform;

/// <summary>
/// Windows desktop browser host backed by WebView2.
/// </summary>
/// <remarks>
/// <para>
/// This implementation embeds WebView2 directly into the Avalonia visual tree via
/// <see cref="WebView2NativeControlHost"/>. The <see cref="CreateBrowserControl"/> method
/// returns an Avalonia <see cref="Control"/> that can be placed anywhere (e.g. the dashboard's
/// <c>BrowserContainer</c>), and <see cref="NavigateAsync(Uri)"/> always loads URLs in that
/// embedded control.
/// </para>
/// <para>
/// The explicit pop-out command (<see cref="PopOutAsync(Uri)"/>) opens a separate WinForms
/// browser window so users can detach the browser when desired.
/// </para>
/// <para>
/// HTML5 fullscreen is handled by reparenting the embedded <see cref="WebView2NativeControlHost"/>
/// into a fullscreen Avalonia window. The view is responsible for the actual visual reparenting
/// when <see cref="FullscreenChanged"/> fires.
/// </para>
/// </remarks>
public sealed class WebView2BrowserHost : IBrowserHost, IDisposable
{
    private CoreWebView2Environment? _environment;
    private WebView2NativeControlHost? _embeddedHost;
    private BrowserWindow? _popupWindow;
    private bool _disposed;
    private bool _isAudioMuted;

    /// <summary>WPF parity (BrowserService): the Deeper-player Chromium flags the dashboard browser needs —
    /// autoplay-without-gesture (programmatic v.play()) and no DirectComposition MPO overlay plane
    /// (the black-web-video regression #449/#439). The Chaos tunnel overrides this via object initializer.</summary>
    private const string DefaultBrowserArguments =
        "--autoplay-policy=no-user-gesture-required --disable-direct-composition-video-overlays";

    public WebView2BrowserHost(ILogger<WebView2BrowserHost>? logger = null)
    {
        Logger = logger;

        // B2: default the dashboard browser to WPF's video flags. Overridable via the
        // object initializer (the Chaos tunnel sets its own anti-MPO args, which replaces this).
        AdditionalBrowserArguments = DefaultBrowserArguments;
    }

    /// <summary>
    /// Per-host WebView2 user-data folder <em>name</em>, resolved under
    /// <c>%LOCALAPPDATA%/ConditioningControlPanel/</c> by <see cref="GetUserDataFolderPath"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each host MUST use a distinct folder. WebView2 takes a process-exclusive lock on the user-data
    /// folder at <see cref="CoreWebView2Environment.CreateAsync"/>, so sharing one folder across the
    /// dashboard / Chaos tunnel / DTRH hosts makes the 2nd and 3rd host throw "already in use". That
    /// exception used to be swallowed (Debug.WriteLine only), leaving the dashboard browser blank (#4).
    /// </para>
    /// <para>
    /// WPF parity: the legacy head uses distinct <c>browser_data</c> vs <c>browser_data_dtrh</c> folders
    /// (<c>Chaos/ChaosWebViewHost.cs</c>). The dashboard DI singleton keeps this default; the Chaos tunnel
    /// and DTRH hosts override it via the object initializer.
    /// </para>
    /// </remarks>
    public string UserDataFolder { get; set; } = "avalonia_browser_data";

    /// <summary>
    /// Optional logger surfaced from DI (dashboard singleton) or the object initializer (Chaos tunnel /
    /// DTRH hosts). When set, environment/navigation failures are logged as warnings instead of being
    /// swallowed via <c>Debug.WriteLine</c> (the zero-log swallow was itself a defect — #4).
    /// </summary>
    public ILogger? Logger { get; set; }

    /// <summary>
    /// Resolves <see cref="UserDataFolder"/> to its full path under
    /// <c>%LOCALAPPDATA%/ConditioningControlPanel/</c> and ensures it exists. Called lazily, right before
    /// <see cref="CoreWebView2Environment.CreateAsync"/>, so a host that is never navigated never creates a folder.
    /// </summary>
    private string GetUserDataFolderPath()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel",
            UserDataFolder);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Mutes/unmutes the embedded browser's audio (BambiCloud / HypnoTube video) via
    /// CoreWebView2.IsMuted. Persistence is the caller's responsibility (AppSettings.BrowserVideoMuted);
    /// the desired state is remembered and re-applied whenever a fresh CoreWebView2 initializes.
    /// </summary>
    public bool IsAudioMuted
    {
        get => _embeddedHost?.WebView?.CoreWebView2?.IsMuted ?? _isAudioMuted;
        set
        {
            _isAudioMuted = value;
            if (_embeddedHost?.WebView?.CoreWebView2 is { } core)
                core.IsMuted = value;
        }
    }

    public bool IsFullscreen { get; private set; }

    public event EventHandler<string>? TitleChanged;
    public event EventHandler<Uri>? Navigated;
    public event EventHandler<bool>? FullscreenChanged;
    public event EventHandler<string>? WebMessageReceived;

    /// <summary>
    /// Raised after the embedded browser process crashed and the native host reset itself for a
    /// lazy re-init on the next navigation (WPF BrowserService.BrowserProcessFailed parity).
    /// </summary>
    public event EventHandler? ProcessFailed;

    /// <summary>
    /// Remembered zoom factor, or null when never explicitly set. Nullable so hosts that never set a
    /// zoom (Chaos tunnel, DTRH, Deeper preview) keep WebView2's default 1.0 untouched.
    /// </summary>
    private double? _zoomFactor;

    /// <summary>
    /// Embedded browser zoom (WPF parity: 0.75 on init, 0.5 on site navigation). Remembered and
    /// re-applied after a fresh CoreWebView2 initializes (e.g. process-crash recovery).
    /// </summary>
    public double ZoomFactor
    {
        get => _embeddedHost?.WebView?.ZoomFactor ?? _zoomFactor ?? 1.0;
        set
        {
            _zoomFactor = value;
            if (_embeddedHost?.WebView is { } webView)
                webView.ZoomFactor = value;
        }
    }

    // Pending virtual-host mappings (applied once the embedded CoreWebView2 initializes). Multiple mappings are
    // supported (e.g. DTRH maps its page root Deny + asset roots Allow); re-registering a host replaces it.
    private readonly List<(string Host, string Folder, BrowserHostResourceAccess Access)> _virtualHostMappings = new();
    private readonly object _virtualHostLock = new();
    private bool _messagingWired;

    /// <summary>Optional extra Chromium command-line flags applied when the environment is first created
    /// (e.g. the Chaos tunnel's anti-MPO flags so its WebGL swapchain composites below topmost overlays).</summary>
    public string? AdditionalBrowserArguments { get; set; }

    /// <summary>
    /// Creates an Avalonia control that hosts WebView2. The first call initializes the
    /// embedded WebView2 asynchronously; subsequent calls return the same control instance.
    /// Declared as <c>object?</c> to exactly match <see cref="IBrowserHost.CreateBrowserControl"/>:
    /// a covariant <c>Control?</c> return does NOT implement the interface member, so interface
    /// dispatch silently fell through to the default-interface-method and returned null - which
    /// left every interface-typed consumer (dashboard, Deeper preview) without an embeddable control.
    /// </summary>
    public object? CreateBrowserControl()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_embeddedHost != null)
            return _embeddedHost;

        _embeddedHost = new WebView2NativeControlHost(_environment);
        _embeddedHost.TitleChanged += (_, title) => TitleChanged?.Invoke(this, title);
        _embeddedHost.Navigated += (_, uri) => Navigated?.Invoke(this, uri);
        _embeddedHost.ProcessFailed += (_, _) => ProcessFailed?.Invoke(this, EventArgs.Empty);
        _embeddedHost.FullscreenChanged += (_, fullscreen) =>
        {
            IsFullscreen = fullscreen;
            FullscreenChanged?.Invoke(this, fullscreen);
        };

        return _embeddedHost;
    }

    public async Task NavigateAsync(Uri url)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var host = CreateBrowserControl();
        if (host == null)
        {
            // Should never happen on Windows, but keep a safe fallback.
            OpenWithSystemBrowser(url);
            return;
        }

        try
        {
            _environment ??= await CreateEnvironmentAsync();
            await _embeddedHost!.EnsureInitializedAsync(_environment);
        }
        catch (Exception ex)
        {
            // #4: this used to be a Debug.WriteLine-only swallow, so environment failures (e.g. the
            // process-exclusive user-data folder lock that fired when hosts shared one folder) left the
            // dashboard browser silently blank. Surface it as a real warning so future failures are diagnosable.
            //
            // WPF parity (MainWindow.Browser.cs InitializeBrowserAsync catch blocks): initialization
            // failures must reach the caller so the dashboard can show the WebView2-runtime error UX
            // in the placeholder + status row, instead of silently popping out a detached window.
            Logger?.LogWarning(ex, "WebView2BrowserHost: failed to initialize embedded WebView2 for {Url}", url);
            if (ex is WebView2RuntimeNotFoundException)
            {
                // Same message shape as WPF BrowserService.CreateBrowserAsync.
                throw new InvalidOperationException(
                    $"WebView2 Runtime is not installed. Please install it from: go.microsoft.com/fwlink/p/?LinkId=2124703\n\nError: {ex.Message}", ex);
            }
            throw;
        }

        // Re-apply the remembered zoom on the (possibly freshly recreated) WebView2 control
        // before navigating, mirroring WPF's ZoomFactor-before-Navigate ordering.
        if (_zoomFactor is { } zoom && _embeddedHost?.WebView is { } zoomView)
            zoomView.ZoomFactor = zoom;

        if (_embeddedHost?.WebView?.CoreWebView2 is { } core)
        {
            ApplyVirtualHostAndMessaging(core);

            // B4: reject dangerous schemes and force/upgrade https before navigating (WPF BrowserService.Navigate).
            var safe = SanitizeNavigationUrl(url.ToString());
            if (safe == null)
            {
                Logger?.LogWarning("WebView2BrowserHost: blocked unsafe navigation URL: {Url}", url);
                return;
            }
            core.Navigate(safe);
        }
        else
        {
            await PopOutAsync(url);
        }
    }

    /// <summary>
    /// WPF parity (BrowserService.Navigate): blocks javascript:/file:/data:/vbscript: schemes and
    /// forces/upgrades the URL to https. Returns the sanitized https URL, or null if it must be blocked.
    /// </summary>
    private static string? SanitizeNavigationUrl(string? url)
    {
        url = url?.Trim() ?? string.Empty;
        if (url.Length == 0) return null;

        var lower = url.ToLowerInvariant();
        if (lower.StartsWith("javascript:") || lower.StartsWith("file:") ||
            lower.StartsWith("data:") || lower.StartsWith("vbscript:"))
        {
            return null;
        }

        if (!lower.StartsWith("http://") && !lower.StartsWith("https://"))
            url = "https://" + url;
        else if (lower.StartsWith("http://"))
            url = "https://" + url.Substring("http://".Length);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;

        return uri.ToString();
    }

    /// <summary>Serve a local folder under a virtual host name with the historical <c>Deny</c> access kind.</summary>
    public void SetVirtualHostToFolder(string hostName, string folder)
        => SetVirtualHostToFolder(hostName, folder, BrowserHostResourceAccess.Deny);

    /// <summary>Serve a local folder under a virtual host name with an explicit cross-origin access kind
    /// (WebView2 <c>SetVirtualHostNameToFolderMapping</c>). Supports multiple simultaneous mappings; a repeat
    /// host name replaces its prior mapping.</summary>
    public void SetVirtualHostToFolder(string hostName, string folder, BrowserHostResourceAccess access)
    {
        lock (_virtualHostLock)
        {
            _virtualHostMappings.RemoveAll(m => string.Equals(m.Host, hostName, StringComparison.OrdinalIgnoreCase));
            _virtualHostMappings.Add((hostName, folder, access));
        }
        // Apply immediately if the core is already up (e.g. set after a Navigate).
        if (_embeddedHost?.WebView?.CoreWebView2 is { } core)
            ApplyVirtualHostAndMessaging(core);
    }

    private static CoreWebView2HostResourceAccessKind ToWebView2Access(BrowserHostResourceAccess access) => access switch
    {
        BrowserHostResourceAccess.DenyCors => CoreWebView2HostResourceAccessKind.DenyCors,
        BrowserHostResourceAccess.Allow => CoreWebView2HostResourceAccessKind.Allow,
        _ => CoreWebView2HostResourceAccessKind.Deny,
    };

    /// <summary>Post a JSON message from host → page.</summary>
    public void PostWebMessageAsJson(string json)
    {
        try { _embeddedHost?.WebView?.CoreWebView2?.PostWebMessageAsJson(json); }
        catch { /* best effort */ }
    }

    /// <summary>Apply the pending virtual-host mapping + enable/wire web messaging on a fresh CoreWebView2.</summary>
    private void ApplyVirtualHostAndMessaging(CoreWebView2 core)
    {
        try
        {
            core.Settings.IsWebMessageEnabled = true;
            (string Host, string Folder, BrowserHostResourceAccess Access)[] mappings;
            lock (_virtualHostLock)
                mappings = _virtualHostMappings.ToArray();
            foreach (var (host, folder, access) in mappings)
            {
                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(folder))
                    continue;
                if (!Directory.Exists(folder))
                {
                    // WPF parity (ChaosWebViewHost): skip + note a missing virtual-host folder instead of throwing.
                    Debug.WriteLine($"WebView2BrowserHost: virtual host '{host}' folder missing: {folder}");
                    continue;
                }
                core.SetVirtualHostNameToFolderMapping(host, folder, ToWebView2Access(access));
            }
            if (!_messagingWired)
            {
                core.WebMessageReceived += (_, e) =>
                {
                    try { WebMessageReceived?.Invoke(this, e.WebMessageAsJson ?? string.Empty); }
                    catch { }
                };
                _messagingWired = true;
            }
            // B5: re-apply the remembered mute preference on this fresh core.
            core.IsMuted = _isAudioMuted;
        }
        catch { /* virtual host / messaging unsupported — degrade gracefully */ }
    }

    public async Task PopOutAsync(Uri url)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await EnsureBrowserWindowAsync();
        }
        catch (Exception ex)
        {
            OpenWithSystemBrowser(url);
            throw new InvalidOperationException($"WebView2 is unavailable; opened the system browser instead. {ex.Message}", ex);
        }

        if (_popupWindow?.WebView.CoreWebView2 != null)
        {
            _popupWindow.WebView.CoreWebView2.Navigate(url.ToString());
        }

        _popupWindow?.Show();
        _popupWindow?.Activate();
    }

    public async Task<string> ExecuteScriptAsync(string script)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_embeddedHost != null)
        {
            await EnsureEnvironmentAndInitializeEmbeddedAsync();
            if (_embeddedHost.WebView?.CoreWebView2 is { } core)
                return await core.ExecuteScriptAsync(script);
        }

        await EnsureBrowserWindowAsync();

        if (_popupWindow?.WebView.CoreWebView2 == null)
            return string.Empty;

        return await _popupWindow.WebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private async Task EnsureEnvironmentAndInitializeEmbeddedAsync()
    {
        if (_disposed) return;
        if (_embeddedHost == null) return;

        _environment ??= await CreateEnvironmentAsync();
        await _embeddedHost.EnsureInitializedAsync(_environment);
    }

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var userDataFolder = GetUserDataFolderPath();
        if (!string.IsNullOrWhiteSpace(AdditionalBrowserArguments))
        {
            var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = AdditionalBrowserArguments };
            return await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder, options: options);
        }
        return await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
    }

    private async Task EnsureBrowserWindowAsync()
    {
        if (_popupWindow != null)
            return;

        var userDataFolder = GetUserDataFolderPath();
        _environment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        _popupWindow = new BrowserWindow(_environment);
        WirePopupEvents();
        // Show the form BEFORE awaiting CoreWebView2 init: the WinForms WebView2 needs a live
        // HWND to bind to, and awaiting EnsureCoreWebView2Async on a never-shown form hangs
        // forever (silently - the pop-out window then never appears).
        _popupWindow.Show();
        await _popupWindow.WebView.EnsureCoreWebView2Async(_environment);
    }

    private void WirePopupEvents()
    {
        if (_popupWindow == null) return;

        _popupWindow.WebView.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (!e.IsSuccess || _popupWindow.WebView.CoreWebView2 == null)
                return;

            var core = _popupWindow.WebView.CoreWebView2;

            core.DocumentTitleChanged += (_, _) =>
                TitleChanged?.Invoke(this, core.DocumentTitle);

            core.NavigationCompleted += (_, _) =>
            {
                if (Uri.TryCreate(core.Source, UriKind.Absolute, out var uri))
                    Navigated?.Invoke(this, uri);
            };

            core.ContainsFullScreenElementChanged += (_, _) =>
            {
                IsFullscreen = core.ContainsFullScreenElement;
                _popupWindow.ApplyFullscreenState(IsFullscreen);
                FullscreenChanged?.Invoke(this, IsFullscreen);
            };

            core.NewWindowRequested += (_, e) =>
            {
                // Keep all navigation inside our single window instead of spawning extra windows.
                e.Handled = true;
                core.Navigate(e.Uri);
            };
        };
    }

    /// <summary>
    /// B1: OAuth / external links must open in the OS default browser, NOT the invisible embedded
    /// WebView2. Overrides the <see cref="IBrowserHost.OpenExternalAsync"/> default (which delegates to
    /// NavigateAsync) so the login page is actually shown. WPF parity: Helpers/BrowserLauncher shell-launch.
    /// </summary>
    public Task OpenExternalAsync(Uri url)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        OpenWithSystemBrowser(url);
        return Task.CompletedTask;
    }

    private static void OpenWithSystemBrowser(Uri url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url.ToString(), UseShellExecute = true });
        }
        catch
        {
            // Best-effort fallback; ignore failures to avoid crashing the dashboard.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _embeddedHost?.Dispose();
        _embeddedHost = null;

        _popupWindow?.Dispose();
        _popupWindow = null;
    }

    private sealed class BrowserWindow : Form
    {
        private readonly FormBorderStyle _normalBorderStyle;
        private readonly FormWindowState _normalWindowState;
        private Rectangle _normalBounds;

        public WebView2 WebView { get; }

        public BrowserWindow(CoreWebView2Environment environment)
        {
            Text = "CCP Browser";
            Width = 1280;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;
            _normalBorderStyle = FormBorderStyle.Sizable;
            _normalWindowState = FormWindowState.Normal;
            _normalBounds = new Rectangle(0, 0, 1280, 800);

            WebView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(WebView);

            FormClosing += (_, e) =>
            {
                // Hide instead of close so the singleton host can be reused.
                e.Cancel = true;
                Hide();
            };
        }

        public void ApplyFullscreenState(bool fullscreen)
        {
            if (fullscreen)
            {
                if (WindowState != FormWindowState.Maximized || FormBorderStyle != FormBorderStyle.None)
                {
                    _normalBounds = Bounds;
                    FormBorderStyle = FormBorderStyle.None;
                    WindowState = FormWindowState.Maximized;
                }
            }
            else
            {
                FormBorderStyle = _normalBorderStyle;
                WindowState = _normalWindowState;
                Bounds = _normalBounds;
            }
        }
    }
}
