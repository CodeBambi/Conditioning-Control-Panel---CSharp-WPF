namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// The per-monitor overlay surface the <see cref="CompositorEngine"/> drives. Two implementations:
///   - <see cref="CompositorHostWindow"/>: a WPF AllowsTransparency window whose SKElement rasters
///     the layers on the UI thread (the original, default path).
///   - <see cref="LayeredCompositorHost"/>: a raw Win32 layered window presented OFF the UI thread
///     via UpdateLayeredWindow (the #550 proper-fix path, behind CompositorOffThreadPresent).
/// The engine holds hosts through this interface and branches on the latched present mode only where
/// the two genuinely differ (invalidate vs. hand-off a recorded picture).
/// </summary>
internal interface ICompositorHost
{
    /// <summary>Device name of the screen this host covers (Screen.DeviceName).</summary>
    string ScreenDeviceName { get; }

    /// <summary>True when this is the capture-excluded surface (brain drain).</summary>
    bool IsExcludedSurface { get; }

    /// <summary>This host's monitor rectangle in device pixels.</summary>
    System.Drawing.Rectangle ScreenBoundsPx { get; }

    /// <summary>Monitor DPI scale (dpi / 96); converts DIP-tuned layer math to device px.</summary>
    double DpiScale { get; }

    /// <summary>True while the host window is shown.</summary>
    bool IsVisible { get; }

    /// <summary>Native handle of the host window (IntPtr.Zero before the hwnd exists). Lets the
    /// z-order reconciler (OverlayService.ReassertZOrder) pin hosts below a playing mandatory
    /// video / re-pin them topmost exactly like the legacy per-effect windows.</summary>
    nint WindowHandle { get; }

    /// <summary>Show the (empty, click-through) host window. UI thread.</summary>
    void Show();

    /// <summary>Hide the host window. UI thread.</summary>
    void Hide();

    /// <summary>Tear the host down (topology change or engine dispose). UI thread.</summary>
    void Close();

    /// <summary>Re-target the host after a display-topology change. UI thread.</summary>
    void UpdateScreenBounds(System.Windows.Forms.Screen screen);
}
