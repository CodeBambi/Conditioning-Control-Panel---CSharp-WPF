using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Compositor;

/// <summary>
/// A single full-screen compositor window per monitor. Hosts a <see cref="CompositorControl"/>
/// that renders directly via Skia on Avalonia's GPU render thread.
/// On Windows, the native window is styled so mouse clicks pass through to underlying
/// windows while overlays remain visible on top.
/// </summary>
public partial class CompositorWindow : Window
{
    private readonly CompositorEngine _engine;
    private readonly CompositorControl _control;
    private readonly ILogger<CompositorWindow>? _logger;
    private readonly bool _excludeFromCapture;

    /// <summary>The monitor this window covers.</summary>
    public ScreenInfo Screen { get; }

    /// <summary>True when this is the capture-excluded surface (hosts brain drain only).</summary>
    public bool ExcludesFromCapture => _excludeFromCapture;

    public CompositorWindow(ScreenInfo screen, CompositorEngine engine, bool excludeFromCapture = false)
    {
        InitializeComponent();
        _engine = engine;
        _excludeFromCapture = excludeFromCapture;
        Screen = screen;
        _logger = App.Services?.GetService<ILogger<CompositorWindow>>();

        // Use the full monitor bounds (taskbar included) so overlays cover the
        // entire screen, matching the WPF head which positions on physical bounds,
        // not the working area.
        Position = new PixelPoint((int)screen.Bounds.X, (int)screen.Bounds.Y);
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;

        _control = new CompositorControl(engine, screen, excludeFromCapture);
        Content = _control;

        Opened += OnWindowOpened;
        Activated += OnWindowActivated;
        Deactivated += OnWindowDeactivated;
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
            OnWindowStateChanged();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _logger?.LogDebug("CompositorWindow deactivated");
    }

    private void OnWindowStateChanged()
    {
        _logger?.LogDebug("CompositorWindow WindowState changed to {State}", WindowState);
        ApplyNativeTransparency();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        // Re-apply transparency when the window is activated (e.g. when the
        // main window is minimized and Windows tries to activate this window).
        // This ensures the click-through styles remain effective.
        ApplyNativeTransparency();
    }

    public CompositorControl GetControl() => _control;

    /// <summary>
    /// Applies native click-through styles so mouse clicks pass through to underlying
    /// windows. Uses WS_EX_LAYERED | WS_EX_TRANSPARENT together — this is the reliable
    /// click-through combination. WS_EX_TRANSPARENT alone fails once the window becomes
    /// the foreground/active window (e.g. when the main app is minimized and Windows
    /// activates this topmost overlay), which then captures all clicks and locks the
    /// desktop. This matches the proven click-through overlays already in this repo
    /// (AvaloniaKeywordHighlightService, ChaosWin32Helper), which render correctly with
    /// LAYERED + TransparencyLevelHint.Transparent under Avalonia 12's DWM-composited GPU
    /// pipeline. Must be called immediately after Show().
    /// </summary>
    public void ApplyNativeTransparency()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
            {
                _logger?.LogWarning("ApplyNativeTransparency: no native handle");
                return;
            }

            var exStyle = (uint)GetWindowLong(hwnd, GWL_EXSTYLE).ToInt64();
            var before = exStyle;
            exStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            var after = exStyle;
            _logger?.LogDebug("ApplyNativeTransparency: EXSTYLE before={Before:X} after={After:X}", before, after);
            SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_NOZORDER);

            // NOTE: We intentionally do NOT call SetWindowSubclass here. Subclassing the
            // window proc on an Avalonia-managed HWND races with Avalonia v12's own
            // window-proc management and intermittently faults with a native access
            // violation (0xC0000005) on the render thread before the compositor timer
            // can fire even once. The subclass was only used to force WM_NCHITTEST ->
            // HTTRANSPARENT and WM_MOUSEACTIVATE -> MA_NOACTIVATE, but those are already
            // provided by WS_EX_TRANSPARENT + WS_EX_NOACTIVATE respectively, so the
            // subclass is redundant and removing it is strictly safer.

            // Capture affinity, applied AFTER the SWP_FRAMECHANGED flush and re-asserted on
            // Opened/Activated/WindowState just like the ex-styles. The excluded surface hosts
            // brain drain only (WPF parity: OverlayService brain-drain windows call
            // SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) "so we don't capture ourselves"
            // and stay out of streams/recordings/OCR captures).
            // GUARDRAIL: the main surface must NEVER get a SetWindowDisplayAffinity call —
            // subliminals (and flash/spiral/pink tint) are visible in capture BY DESIGN
            // (WPF SubliminalService deliberately sets WDA_NONE).
            if (_excludeFromCapture)
            {
                var affinityOk = SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
                if (!affinityOk)
                    _logger?.LogWarning("ApplyNativeTransparency: SetWindowDisplayAffinity failed (error {Error})", Marshal.GetLastWin32Error());
            }

            _logger?.LogDebug("ApplyNativeTransparency: applied EXSTYLE (no subclass), excludeFromCapture={Excluded}", _excludeFromCapture);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ApplyNativeTransparency: exception");
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        ApplyNativeTransparency();
    }

    protected override void OnClosed(EventArgs e)
    {
        Opened -= OnWindowOpened;

        base.OnClosed(e);
    }

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOZORDER = 0x0004;
    // Excludes the window from screenshots, screen recording, and GDI screen capture
    // (Windows 10 2004+). See the overlay-clickthrough skill for the affinity rules.
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLong64(IntPtr hWnd, int nIndex);
    private static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 4 ? GetWindowLong32(hWnd, nIndex) : GetWindowLong64(hWnd, nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 4 ? SetWindowLong32(hWnd, nIndex, dwNewLong) : SetWindowLong64(hWnd, nIndex, dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
}
