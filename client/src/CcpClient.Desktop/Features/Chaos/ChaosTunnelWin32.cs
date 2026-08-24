using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Features.Chaos;

/// <summary>
/// The tunnel window's Win32 surface (WPF ChaosTunnelService.cs `:541-573` parity): the
/// z-order and ex-style constants both the window (sink-to-bottom) and the service
/// (dashboard z-guard) use. Windows-only; every caller guards on OperatingSystem.IsWindows().
/// </summary>
public static class ChaosTunnelWin32
{
    public static readonly IntPtr HwndBottom = new(1);

    public const uint SwpNosize = 0x0001;
    public const uint SwpNomove = 0x0002;
    public const uint SwpNoactivate = 0x0010;
    public const uint SwpNoownerzorder = 0x0200;
    public const uint GwHwndprev = 3;

    // Absorb clicks (NO WS_EX_TRANSPARENT — clicks reach the page for power-up raycasting,
    // Chaos/ChaosTunnelService.cs:31) but never steal focus / show in Alt-Tab.
    public const uint WsExNoactivate = 0x08000000;
    public const uint WsExToolwindow = 0x00000080;

    /// <summary>Click-through at the Win32 level: a window carrying it is skipped by hit
    /// testing, so its mouse input lands on whatever is underneath instead.</summary>
    public const uint WsExTransparent = 0x00000020;

    /// <summary>
    /// The tunnel's ex-style, rebuilt FROM A KNOWN BASE rather than OR-ed onto whatever the
    /// framework handed in. Upstream rejects the plain OR for exactly this reason and clears
    /// WS_EX_TRANSPARENT first before rebuilding "from a known base every (re)show"
    /// (<c>ConditioningControlPanel/Services/BubbleService.cs:4886-4894</c>); the pointer
    /// surface builds from a known base for the same cited reason
    /// (<c>Pointer/Win32PointerSurface.cs:850-852</c>).
    ///
    /// <para>The OUTCOME this defends is that CLICKS REACH THE PAGE — the tunnel is the one
    /// interactive hosted surface and its power-up raycasting is nothing but mouse input on the
    /// page (WPF <c>Chaos/ChaosTunnelService.cs:31</c>). A contributed WS_EX_TRANSPARENT would
    /// make the window silently stop receiving those clicks and nothing anywhere would refuse.
    ///
    /// <para>NO LIVE DEFECT EXISTS, and that was established rather than assumed. Decompiled
    /// against the pinned Avalonia 12.1.1, <c>WindowImpl.UpdateWindowProperties</c> rebuilds the
    /// base it hands to the callback from a LITERAL every time — WS_EX_WINDOWEDGE, plus
    /// WS_EX_NOREDIRECTIONBITMAP when the redirection bitmap is off and WS_EX_APPWINDOW when
    /// ShowInTaskbar — never reading it back off the HWND. Confirmed on a headed Windows run
    /// against this very window: the base arrived as 0x00200100 (0x00240100 with ShowInTaskbar),
    /// no WS_EX_TRANSPARENT, and the shipped window's live GWL_EXSTYLE read back 0x08200080 —
    /// NOACTIVATE + TOOLWINDOW + NOREDIRECTIONBITMAP, transparent CLEAR.</para>
    ///
    /// <para>So this is insurance against ONE case, named exactly because the other case people
    /// assume is not covered. It covers Avalonia's own literal growing the flag in a future
    /// version — the "future backend change" the defect record named. It does NOT cover a second
    /// styles callback: <c>AddWindowStylesCallback</c> is a <c>Delegate.Combine</c>, and a
    /// multicast delegate with a return value hands every callback the SAME original base and
    /// applies only the LAST one's return, so a second callback would discard this window's flags
    /// outright rather than poison its base — last-writer-wins, not a chain (measured on the same
    /// headed run). Nothing here defends against that; not registering a second one does. The seam
    /// is also NOT a one-shot at creation: that headed run saw it fire four times in one second,
    /// once per window-property update.</para>
    /// </summary>
    public static uint TunnelExStyle(uint baseExStyle) =>
        (baseExStyle & ~WsExTransparent) | WsExNoactivate | WsExToolwindow;

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);
}
