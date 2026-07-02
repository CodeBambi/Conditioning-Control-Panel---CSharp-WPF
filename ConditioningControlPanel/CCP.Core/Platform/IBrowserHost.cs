namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform embedded browser host.
/// </summary>
public interface IBrowserHost
{
    /// <summary>
    /// Navigates to <paramref name="url"/> in the embedded browser control when visual
    /// embedding is available; otherwise opens the URL in a platform-appropriate fallback.
    /// </summary>
    Task NavigateAsync(Uri url);

    /// <summary>
    /// Executes JavaScript in the active browser context and returns the JSON-encoded result.
    /// </summary>
    Task<string> ExecuteScriptAsync(string script);

    /// <summary>
    /// Raised when the browser document title changes.
    /// </summary>
    event EventHandler<string>? TitleChanged;

    /// <summary>
    /// Raised when the browser finishes navigating to a new URI.
    /// </summary>
    event EventHandler<Uri>? Navigated;

    /// <summary>
    /// True when the browser is currently in fullscreen mode (e.g. HTML5 video fullscreen).
    /// </summary>
    bool IsFullscreen => false;

    /// <summary>
    /// Raised when the browser enters or exits fullscreen mode.
    /// </summary>
    event EventHandler<bool>? FullscreenChanged { add { } remove { } }

    /// <summary>
    /// Creates an Avalonia control that hosts the browser, if visual embedding is supported
    /// on the current platform. Returns null when the host uses a separate window or shell
    /// execution instead. The returned object, when non-null, is an Avalonia <see cref="Avalonia.Controls.Control"/>.
    /// Subsequent calls should return the same control instance so it can be reparented.
    /// </summary>
    object? CreateBrowserControl() => null;

    /// <summary>
    /// Opens <paramref name="url"/> in a separate popup window, if the platform supports it.
    /// The default implementation delegates to <see cref="NavigateAsync"/>, which is sufficient
    /// for hosts that already open a separate window or shell browser.
    /// </summary>
    Task PopOutAsync(Uri url) => NavigateAsync(url);

    /// <summary>Raised when the embedded page posts a JSON message to the host
    /// (via <c>window.chrome.webview.postMessage</c> / the platform equivalent). Default is never raised.</summary>
    event EventHandler<string>? WebMessageReceived { add { } remove { } }

    /// <summary>Post a JSON message from the host to the page. Default no-op on hosts without messaging.</summary>
    void PostWebMessageAsJson(string json) { }

    /// <summary>
    /// Serve a local <paramref name="folder"/> under a virtual host <paramref name="hostName"/> so the page
    /// can load local assets/scripts via <c>https://hostName/...</c> (ESM + import maps resolve). Required by
    /// web-hosted effects like the Chaos tunnel. Default no-op on hosts without virtual-host support.
    /// </summary>
    void SetVirtualHostToFolder(string hostName, string folder) { }
}
