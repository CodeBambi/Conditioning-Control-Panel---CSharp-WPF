using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Avalonia.Controls;
using ConditioningControlPanel.Core.Platform;
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
    private readonly string _userDataFolder;
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

    public WebView2BrowserHost()
    {
        _userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel",
            "avalonia_browser_data");
        Directory.CreateDirectory(_userDataFolder);

        // B2: default the dashboard browser to WPF's video flags. Overridable via the
        // object initializer (the Chaos tunnel sets its own anti-MPO args, which replaces this).
        AdditionalBrowserArguments = DefaultBrowserArguments;
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

    // Pending virtual-host mapping (applied once the embedded CoreWebView2 initializes).
    private string? _virtualHostName;
    private string? _virtualHostFolder;
    private bool _messagingWired;

    /// <summary>Optional extra Chromium command-line flags applied when the environment is first created
    /// (e.g. the Chaos tunnel's anti-MPO flags so its WebGL swapchain composites below topmost overlays).</summary>
    public string? AdditionalBrowserArguments { get; set; }

    /// <summary>
    /// Creates an Avalonia control that hosts WebView2. The first call initializes the
    /// embedded WebView2 asynchronously; subsequent calls return the same control instance.
    /// </summary>
    public global::Avalonia.Controls.Control? CreateBrowserControl()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_embeddedHost != null)
            return _embeddedHost;

        _embeddedHost = new WebView2NativeControlHost(_environment);
        _embeddedHost.TitleChanged += (_, title) => TitleChanged?.Invoke(this, title);
        _embeddedHost.Navigated += (_, uri) => Navigated?.Invoke(this, uri);
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
            Debug.WriteLine($"Failed to initialize embedded WebView2 for {url}; falling back to popup: {ex.Message}");
            await PopOutAsync(url);
            return;
        }

        if (_embeddedHost?.WebView?.CoreWebView2 is { } core)
        {
            ApplyVirtualHostAndMessaging(core);

            // B4: reject dangerous schemes and force/upgrade https before navigating (WPF BrowserService.Navigate).
            var safe = SanitizeNavigationUrl(url.ToString());
            if (safe == null)
            {
                Debug.WriteLine($"Blocked unsafe navigation URL: {url}");
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

    /// <summary>Serve a local folder under a virtual host name (WebView2 SetVirtualHostNameToFolderMapping).</summary>
    public void SetVirtualHostToFolder(string hostName, string folder)
    {
        _virtualHostName = hostName;
        _virtualHostFolder = folder;
        // Apply immediately if the core is already up (e.g. set after a Navigate).
        if (_embeddedHost?.WebView?.CoreWebView2 is { } core)
            ApplyVirtualHostAndMessaging(core);
    }

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
            if (!string.IsNullOrEmpty(_virtualHostName) && !string.IsNullOrEmpty(_virtualHostFolder))
            {
                core.SetVirtualHostNameToFolderMapping(
                    _virtualHostName, _virtualHostFolder, CoreWebView2HostResourceAccessKind.Deny);
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
        if (!string.IsNullOrWhiteSpace(AdditionalBrowserArguments))
        {
            var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = AdditionalBrowserArguments };
            return await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: _userDataFolder, options: options);
        }
        return await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
    }

    private async Task EnsureBrowserWindowAsync()
    {
        if (_popupWindow != null)
            return;

        _environment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
        _popupWindow = new BrowserWindow(_environment);
        WirePopupEvents();
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
