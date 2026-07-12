namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Pure routing logic for <see cref="IPlatformCapabilities.SupportsClickThrough"/>,
/// unit-testable without a display or any native interop.
/// </summary>
/// <remarks>
/// <para>Per-OS contract:</para>
/// <list type="bullet">
/// <item><description><b>Windows</b> — always true (WS_EX_TRANSPARENT is universally
/// available; the Windows desktop head implements it).</description></item>
/// <item><description><b>Linux</b> — true only when the X11 overlay machinery is actually
/// usable on this system (X display reachable AND XFixes protocol ≥ 2, the same
/// availability gate as <see cref="LinuxOverlayBackendPlan"/> /
/// <c>X11InputShapeBackend.ProbeXFixes</c>). Without it the Linux head selects the
/// FallbackBackend, which cannot do per-region click-through (never-trap rule,
/// linux-overlay-contract.md §1.4), so the capability must report false.</description></item>
/// <item><description><b>macOS / mobile / anything else</b> — false: no overlay
/// click-through backend is implemented there yet (macos-overlay-contract.md
/// SafeDegrade policy).</description></item>
/// </list>
/// <para>IMPORTANT: this capability only makes click-through features SELECTABLE in the
/// UI (it hides the "degraded on this platform" notice). It never enables any
/// conditioning feature by itself — feature activation stays default-off, user opt-in.</para>
/// </remarks>
public static class ClickThroughCapabilityPlan
{
    /// <summary>
    /// Computes whether the current head supports per-region click-through overlays.
    /// </summary>
    /// <param name="isWindows">True when running on Windows.</param>
    /// <param name="isLinux">True when running on Linux.</param>
    /// <param name="linuxX11InputShapeAvailable">
    /// Result of the cheap Linux X11 probe (X display reachable AND XFixes ≥ 2).
    /// Ignored on non-Linux platforms.
    /// </param>
    public static bool Compute(bool isWindows, bool isLinux, bool linuxX11InputShapeAvailable)
    {
        if (isWindows) return true;
        if (isLinux) return linuxX11InputShapeAvailable;
        return false;
    }
}
