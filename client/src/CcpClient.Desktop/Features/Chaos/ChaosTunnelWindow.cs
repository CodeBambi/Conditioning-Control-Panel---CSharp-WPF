using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CcpClient.Desktop.Features.Dtrh;

namespace CcpClient.Desktop.Features.Chaos;

/// <summary>
/// The opaque below-Topmost tunnel backdrop window (WPF `ChaosTunnelService.Build`,
/// ConditioningControlPanel/Chaos/ChaosTunnelService.cs:175-215 parity): borderless, black,
/// non-topmost, non-activating, non-focusable, never in the taskbar/Alt-Tab, sized to the
/// primary screen. OPAQUE BY DESIGN (framing d): upstream is opaque because a WebView2 child
/// HWND does not paint reliably in a layered window — no transparency/click-through here
/// (that is the FYP/overlay class, a different contract and a different row).
///
/// v12 API facts (binary-verified against the pinned Avalonia 12.1.1, scratch/apiprobe):
/// `Window.SystemDecorations` exists retyped to the new `WindowDecorations` enum
/// (None/BorderOnly/Full) with a `Window.WindowDecorations` alias — the first attempt's
/// "renamed/obsoleted" comment was half-wrong and its ExtendClientAreaToDecorationsHint
/// substitute is REJECTED (that hint keeps chrome behaviors the tunnel must not have).
/// Ex-styles ride the package's own `Win32Properties.AddWindowStylesCallback` seam
/// (WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW — never WS_EX_TRANSPARENT: clicks reach the
/// page for power-up raycasting, WPF :31). CORRECTED against the decompiled package and a
/// headed run: that seam is NOT a one-shot at creation. `Avalonia.Win32.WindowImpl` invokes it
/// from `UpdateWindowProperties` — on every window-property update (ShowInTaskbar, decorations,
/// resizability, fullscreen exit), four times in one second on the headed run — each time over
/// a base it rebuilds from a literal rather than reading back off the HWND. Hence
/// <see cref="ChaosTunnelWin32.TunnelExStyle"/>, which rebuilds from a known base; that method's
/// remarks carry the measured values and say exactly what the clear does and does not cover.
/// </summary>
public sealed class ChaosTunnelWindow : Window
{
    private readonly Action<string> _log;
    private readonly string _profileDir;
    private readonly Lifecycle.ApplicationHost? _host;
    private NativeWebView? _web;
    private bool _profileRetryUsed;

    /// <summary>
    /// The host is OPTIONAL because this window is the one hosted surface that never had one:
    /// <see cref="ChaosTunnelService"/> hands it only a log delegate. It is threaded through for
    /// the user's motion setting alone (<see cref="Motion.HostedMotion"/>), and a null host
    /// resolves to <see cref="Motion.MotionLevel.Full"/> — the same default upstream falls back to
    /// when settings are not up (<c>Chaos/ChaosWebViewHost.cs:768-770</c>).
    /// </summary>
    public ChaosTunnelWindow(Action<string> log, string profileDir, Lifecycle.ApplicationHost? host = null)
    {
        _log = log;
        _profileDir = profileDir;
        _host = host;
        Title = "CCP — Chaos Tunnel"; // harness identification only; no taskbar/Alt-Tab presence
        WindowDecorations = WindowDecorations.None; // WPF WindowStyle.None parity (:180)
        Background = Brushes.Black;
        Topmost = false;                 // sits under every topmost bubble/overlay/video window (:187)
        ShowInTaskbar = false;
        ShowActivated = false;           // never steals activation (:189)
        Focusable = false;
        CanResize = false;               // WPF ResizeMode.NoResize (:191)
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(0, 0);

        if (OperatingSystem.IsWindows())
        {
            // Ex-styles via the v12 seam (first-attempt lesson ADAPT — same Win32 surface as
            // WPF's post-show SetWindowLong). The result is built FROM A KNOWN BASE rather than
            // OR-ed onto whatever arrived: see ChaosTunnelWin32.TunnelExStyle for the upstream
            // decision it follows and for what the base measurably is.
            Win32Properties.AddWindowStylesCallback(this, static (style, exStyle) =>
                (style, ChaosTunnelWin32.TunnelExStyle(exStyle)));
        }

        Opened += (_, _) =>
        {
            // Span the primary screen (DIP-correct; WPF sizes from SystemParameters at
            // creation :194-196 — Avalonia's honest shape is at Opened when Screens is live).
            try
            {
                var screen = Screens.Primary;
                if (screen is not null)
                {
                    Width = screen.Bounds.Width / screen.Scaling;
                    Height = screen.Bounds.Height / screen.Scaling;
                    Position = new PixelPoint(screen.Bounds.X, screen.Bounds.Y);
                }
            }
            catch (Exception ex)
            {
                _log($"chaos-tunnel: primary-screen sizing failed ({ex.GetType().Name}) — window keeps its declared size");
            }
        };
    }

    /// <summary>The page's messages (ready/sfx/powerup-click/exit-done/log).</summary>
    public event Action<string>? PageMessage;

    /// <summary>Raised once navigation completes (success or failure — the args carry it).</summary>
    public event Action<bool>? NavigationFinished;

    /// <summary>Create the embedded WebView2 surface and navigate (Windows embedded path
    /// only — the service calls this ONLY after the capability probes Available; recorded
    /// surprise #6: never let a webview materialize on an unadmitted path).</summary>
    public void AttachEmbeddedSurface(Uri startUrl)
    {
        _web = new NativeWebView();
        _web.EnvironmentRequested += OnEnvironmentRequested;
        _web.WebMessageReceived += (_, e) => PageMessage?.Invoke(e.Body ?? "");
        _web.NavigationCompleted += (_, e) => NavigationFinished?.Invoke(e.IsSuccess);
        Content = _web;
        try
        {
            _web.Source = startUrl;
        }
        catch (Exception ex) when (DtrhProfileLock.IsStaleProfileLock(ex) && !_profileRetryUsed)
        {
            // The 0x800700AA stale-profile-lock class (slice b5): any second WebView2 host
            // inherits it. Recover honestly (kill the stale children holding OUR profile —
            // the generic msedgewebview2 machinery is reused across features) and retry ONCE;
            // a second failure stands typed (never a crash loop).
            _profileRetryUsed = true;
            _log("chaos-tunnel: navigation threw the stale-profile-lock class (0x800700AA) — recovering (typed, never silent)");
            LogProfileRecovery(DtrhProfileLock.TryRecover(_profileDir));
            _web.Source = startUrl;
        }
    }

    /// <summary>Host→page: synthetic MessageEvent dispatch on window.chrome.webview (the
    /// landed DtrhHostWindow.axaml.cs:1257 shape — WPF PostWebMessageAsJson parity). Caller
    /// is on the UI thread (ExecuteScriptAsync is apartment-bound, recorded surprise #4).</summary>
    public void SendToPage(string json)
    {
        if (_web is null)
        {
            return;
        }

        _ = _web.InvokeScript($"window.chrome.webview.dispatchEvent(new MessageEvent('message',{{data:{json}}}))")
            .ContinueWith(
                t => _log($"chaos-tunnel: host->page dispatch faulted: {t.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Sink to the BOTTOM of the z-order right after show (WPF :201-206, :433-441):
    /// freshly shown, the window would land above the non-topmost game windows and swallow
    /// the UI; at the bottom everything sits over it. The dashboard is the one thing that
    /// must stay UNDER it — the service's z-guard owns that.</summary>
    public void SinkToBottom()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            ChaosTunnelWin32.SetWindowPos(handle, ChaosTunnelWin32.HwndBottom, 0, 0, 0, 0,
                ChaosTunnelWin32.SwpNomove | ChaosTunnelWin32.SwpNosize | ChaosTunnelWin32.SwpNoactivate);
        }
        catch (Exception ex)
        {
            _log($"chaos-tunnel: sink-to-bottom failed ({ex.GetType().Name})");
        }
    }

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = false;
        if (args is Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs wv2)
        {
            wv2.UserDataFolder = _profileDir;
            // WPF parity (:221-229): keep the WebGL swapchain composited through DWM so it
            // stays BELOW the topmost overlays (the app's established anti-MPO flag), and
            // stop Chromium's occlusion tracker from throttling rAF when the game stacks
            // layered windows over the tunnel (skipped frames).
            // The motion switch is space-joined onto exactly these two flags, which is the
            // string upstream builds and the line it appends at (ChaosWebViewHost.cs:832-833).
            var motionArgument = Motion.HostedMotion.BrowserArgument(_host, "chaos-tunnel", _log);
            wv2.AdditionalBrowserArguments =
                "--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion"
                + " " + motionArgument;
            _log($"chaos-tunnel: WebView2 UserDataFolder set (per-surface profile); anti-MPO + occlusion flags applied; {motionArgument}");
        }
    }

    private void LogProfileRecovery(DtrhProfileLock.DtrhProfileRecovery recovery)
    {
        switch (recovery)
        {
            case DtrhProfileLock.DtrhProfileRecovery.Recovered recovered:
                _log($"chaos-tunnel: stale-profile recovery — killed {recovered.KilledCount} stale msedgewebview2 child(ren) holding our profile");
                break;
            case DtrhProfileLock.DtrhProfileRecovery.NothingToKill:
                _log("chaos-tunnel: stale-profile recovery — no profile-matched children found (lock class without stale children; typed)");
                break;
            case DtrhProfileLock.DtrhProfileRecovery.Unsupported unsupported:
                _log($"chaos-tunnel: stale-profile recovery unavailable — {unsupported.Detail}");
                break;
            case DtrhProfileLock.DtrhProfileRecovery.Failed failed:
                _log($"chaos-tunnel: stale-profile recovery FAILED — {failed.Detail}");
                break;
        }
    }
}
