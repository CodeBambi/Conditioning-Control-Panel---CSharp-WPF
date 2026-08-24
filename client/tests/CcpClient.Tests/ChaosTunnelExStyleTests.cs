using CcpClient.Desktop.Features.Chaos;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The tunnel window's ex-style, which decides ONE user-visible outcome: whether a click
/// lands on the tunnel page or falls through it. The tunnel is the only interactive hosted
/// surface, and its power-up raycasting IS mouse input on the page (WPF
/// <c>Chaos/ChaosTunnelService.cs:31</c>) — a window carrying WS_EX_TRANSPARENT is skipped by
/// hit testing, so the clicks would silently stop arriving and nothing would refuse.
///
/// <para>WHAT THESE FACTS DO NOT PROVE. Nothing here puts a window on a desktop and nothing
/// here sends a click. These pin the ex-style DECISION for every base the framework can hand
/// in. A headed Windows run separately read the shipped window's live GWL_EXSTYLE back as
/// 0x08200080 — NOACTIVATE + TOOLWINDOW + NOREDIRECTIONBITMAP, WS_EX_TRANSPARENT clear, and
/// unchanged across two window-property updates — which establishes that the window is
/// hit-testable at the Win32 level. THAT IS STILL NOT "a click reaches the page": no run has
/// navigated the tunnel page in a WebView2, synthesised a press over it and had the page report
/// the hit. That claim remains unproven, and nothing on Linux is claimed at all.</para>
///
/// <para>WHY A BASE THAT ALREADY CARRIES THE FLAG IS THE INTERESTING INPUT, given that today's
/// base cannot. Measured twice — decompiled against the pinned Avalonia 12.1.1
/// (<c>WindowImpl.UpdateWindowProperties</c> rebuilds the base from a literal every call and
/// never reads the HWND back) and on a headed Windows run against this window (base 0x00200100,
/// 0x00240100 with ShowInTaskbar, no WS_EX_TRANSPARENT). So this is insurance against Avalonia's
/// own literal growing the flag in a later version, which is the "future backend change" the
/// defect record named. It is NOT insurance against a second styles callback: that seam is a
/// <c>Delegate.Combine</c> and is last-writer-wins, so a second callback discards this window's
/// flags outright instead of poisoning its base. Upstream reached the clear-first conclusion the
/// expensive way and rebuilds "from a known base every (re)show"
/// (<c>ConditioningControlPanel/Services/BubbleService.cs:4886-4894</c>).</para>
/// </summary>
public sealed class ChaosTunnelExStyleTests
{
    [Fact]
    public void ExStyle_FromAnyBase_LeavesTheWindowClickable_AndNeverActivatingOrInAltTab()
    {
        // THE DISCRIMINATING CASE, at the top level: a base that already carries the
        // click-through flag is the one a plain OR passes straight through. Asserted here and not
        // only inside the sweep below, so this fact can never pass by iterating nothing.
        Assert.Equal(
            0u,
            ChaosTunnelWin32.TunnelExStyle(ChaosTunnelWin32.WsExTransparent) & ChaosTunnelWin32.WsExTransparent);

        // Every base the seam can plausibly hand in, INCLUDING one that already carries
        // WS_EX_TRANSPARENT — the case a plain OR passes straight through.
        uint[] bases =
        [
            0,
            0x00000100,                                 // Avalonia's own literal: WS_EX_WINDOWEDGE
            0x00000100 | 0x00200000,                    // + WS_EX_NOREDIRECTIONBITMAP
            0x00000100 | 0x00040000,                    // + WS_EX_APPWINDOW (ShowInTaskbar)
            ChaosTunnelWin32.WsExTransparent,           // the click-through contribution
            0x00000100 | ChaosTunnelWin32.WsExTransparent | 0x00080000, // + WS_EX_LAYERED
        ];

        foreach (var start in bases)
        {
            var result = ChaosTunnelWin32.TunnelExStyle(start);

            // THE OUTCOME: the window still receives mouse input, whatever arrived.
            Assert.Equal(0u, result & ChaosTunnelWin32.WsExTransparent);
            // The two the tunnel positively requires (WPF :189, and never in Alt-Tab).
            Assert.Equal(ChaosTunnelWin32.WsExNoactivate, result & ChaosTunnelWin32.WsExNoactivate);
            Assert.Equal(ChaosTunnelWin32.WsExToolwindow, result & ChaosTunnelWin32.WsExToolwindow);
            // Everything else the framework asked for survives: this rebuilds from a known
            // base, it does not replace the base. A transform that returned only its own two
            // flags would pass every assertion above and drop WS_EX_NOREDIRECTIONBITMAP.
            var untouched = start & ~ChaosTunnelWin32.WsExTransparent;
            Assert.Equal(untouched, result & untouched);
        }
    }
}
