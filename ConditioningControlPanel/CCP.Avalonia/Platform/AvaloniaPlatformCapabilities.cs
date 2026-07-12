using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Lightweight, Avalonia-head implementation of <see cref="IPlatformCapabilities"/>.
/// Combines <see cref="OperatingSystem"/> checks with a quick inspection of the
/// registered platform seam implementations to decide whether a feature is real or
/// a stub on the current head.
/// </summary>
public sealed class AvaloniaPlatformCapabilities : IPlatformCapabilities
{
    public AvaloniaPlatformCapabilities(
        IHotkeyProvider hotkeyProvider,
        IInputHook inputHook,
        IWallpaperProvider wallpaperProvider,
        IBrowserHost browserHost,
        ISingleInstanceService singleInstanceService)
    {
        IsWindows = OperatingSystem.IsWindows();
        IsLinux = OperatingSystem.IsLinux();
        IsMacOS = OperatingSystem.IsMacOS();
        IsDesktop = IsWindows || IsLinux || IsMacOS;
        IsMobile = OperatingSystem.IsAndroid();

        // Desktop heads (Windows/Linux/macOS) can show overlays; mobile cannot.
        SupportsOverlays = IsDesktop;

        // The Windows desktop head replaces these stubs with Win32 implementations.
        // Linux/macOS currently keep the no-op Avalonia stubs, so these features
        // are degraded there until native desktop heads are implemented.
        SupportsGlobalHotkeys = hotkeyProvider is not AvaloniaHotkeyProvider;
        SupportsInputHooks = inputHook is not AvaloniaInputHook;
        SupportsWallpaperOverride = wallpaperProvider is not AvaloniaWallpaperProvider;

        // System tray is desktop-only. On Linux it may still fail if DBus/AppIndicator
        // is unavailable, but AvaloniaTrayIcon degrades gracefully rather than crashing.
        SupportsSystemTray = IsDesktop;

        // Click-through requires native interop: WS_EX_TRANSPARENT on Windows; on Linux
        // the X11 overlay backend (XFixes >= 2 input shape), detected via a cheap static
        // probe rather than by resolving the DI-registered IOverlaySurface (which would
        // risk a capability<->backend construction cycle and couple this shared project
        // to a head-specific registration). macOS has no overlay backend yet -> false.
        // NOTE: this only makes click-through features SELECTABLE in the UI; it never
        // enables any conditioning feature by itself (features stay default-off, opt-in).
        SupportsClickThrough = ClickThroughCapabilityPlan.Compute(
            IsWindows, IsLinux, IsLinux && LinuxClickThroughProbe.IsX11InputShapeAvailable());

        // WebView2 is Windows-only. The Avalonia head currently falls back to the
        // system default browser everywhere.
        SupportsWebView2Browser = browserHost is not AvaloniaBrowserHost && IsWindows;

        // Desktop heads now register a real file-lock/named-pipe single-instance service.
        SupportsSingleInstance = singleInstanceService is not AvaloniaSingleInstanceService;

        // Webcam/screen capture is not exposed as a platform seam yet.
        // TODO: add IScreenCaptureService or similar and update this check.
        SupportsScreenCapture = IsDesktop;

        // LibVLC is registered for all heads; the Android head replaces the desktop
        // discovery registration with VideoLAN.LibVLC.Android initialization.
        SupportsLibVLC = true;
    }

    public bool IsWindows { get; }
    public bool IsLinux { get; }
    public bool IsMacOS { get; }
    public bool IsDesktop { get; }
    public bool IsMobile { get; }

    public bool SupportsGlobalHotkeys { get; }
    public bool SupportsInputHooks { get; }
    public bool SupportsWallpaperOverride { get; }
    public bool SupportsSystemTray { get; }
    public bool SupportsOverlays { get; }
    public bool SupportsClickThrough { get; }
    public bool SupportsWebView2Browser { get; }
    public bool SupportsSingleInstance { get; }
    public bool SupportsScreenCapture { get; }
    public bool SupportsLibVLC { get; }
}
