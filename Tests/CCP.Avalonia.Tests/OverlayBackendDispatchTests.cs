using System;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The overlay entry points (X11Overlay.SetClickThrough/SetOverrideRedirect/SetOpacity) pick their
/// backend from the window's platform-handle descriptor alone. Fake handles stand in for real
/// windows; the Win32 half itself only runs on Windows (--overlay-check in the windows CI job).
/// </summary>
public sealed class OverlayBackendDispatchTests
{
    [Theory]
    [InlineData("HWND", 42, "Win32")]
    [InlineData("XID", 42, "X11")]
    [InlineData("HWND", 0, "None")]   // not opened yet
    [InlineData("XID", 0, "None")]
    [InlineData("NSWindow", 42, "None")]
    public void Descriptor_PicksBackend(string descriptor, long handle, string expected)
        => Assert.Equal(expected, X11Overlay.BackendOf(new PlatformHandle(new IntPtr(handle), descriptor)).ToString());

    [Fact]
    public void NoHandle_IsNoBackend() => Assert.Equal(OverlayBackend.None, X11Overlay.BackendOf(null));

    [Fact]
    public void Win32Style_AddsOverlayBits_AndTogglesOnlyTransparent()
    {
        const uint avaloniaRebuild = 0x00000100 | 0x00200000;  // WS_EX_WINDOWEDGE | WS_EX_NOREDIRECTIONBITMAP
        var on = Win32Overlay.Style(avaloniaRebuild, true);
        Assert.Equal(avaloniaRebuild | 0x00080000u | 0x08000000u | 0x80u | 0x20u, on);  // LAYERED|NOACTIVATE|TOOLWINDOW|TRANSPARENT
        Assert.Equal(on & ~0x20u, Win32Overlay.Style(on, false));
    }
}
