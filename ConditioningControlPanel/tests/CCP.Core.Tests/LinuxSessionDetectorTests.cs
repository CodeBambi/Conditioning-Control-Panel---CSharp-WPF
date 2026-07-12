using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Unit tests for Linux session type detection (pure logic, no native calls).
/// </summary>
public class LinuxSessionDetectorTests
{
    [Fact]
    public void Detect_X11Session_WithXdgSessionType_ReturnsX11()
    {
        var result = LinuxSessionDetector.Detect(
            xdgSessionType: "x11",
            waylandDisplay: null,
            display: ":0");

        Assert.Equal(LinuxSessionType.X11, result);
    }

    [Fact]
    public void Detect_X11Session_WithDisplayOnly_ReturnsX11()
    {
        var result = LinuxSessionDetector.Detect(
            xdgSessionType: null,
            waylandDisplay: null,
            display: ":0");

        Assert.Equal(LinuxSessionType.X11, result);
    }

    [Fact]
    public void Detect_WaylandSession_WithXdgSessionType_ReturnsWayland()
    {
        var result = LinuxSessionDetector.Detect(
            xdgSessionType: "wayland",
            waylandDisplay: "wayland-0",
            display: null);

        Assert.Equal(LinuxSessionType.Wayland, result);
    }

    [Fact]
    public void Detect_WaylandSession_WithWaylandDisplayOnly_ReturnsWayland()
    {
        var result = LinuxSessionDetector.Detect(
            xdgSessionType: null,
            waylandDisplay: "wayland-0",
            display: null);

        Assert.Equal(LinuxSessionType.Wayland, result);
    }

    [Fact]
    public void Detect_XWaylandSession_BothWaylandAndX11_ReturnsXWayland()
    {
        // XWayland: Wayland session with DISPLAY set (X11 compatibility layer)
        var result = LinuxSessionDetector.Detect(
            xdgSessionType: "wayland",
            waylandDisplay: "wayland-0",
            display: ":0");

        Assert.Equal(LinuxSessionType.XWayland, result);
    }

    [Fact]
    public void Detect_XWaylandSession_WaylandDisplayAndDisplay_ReturnsXWayland()
    {
        // XWayland: Both WAYLAND_DISPLAY and DISPLAY set, no XDG_SESSION_TYPE
        var result = LinuxSessionDetector.Detect(
            xdgSessionType: null,
            waylandDisplay: "wayland-1",
            display: ":1");

        Assert.Equal(LinuxSessionType.XWayland, result);
    }

    [Fact]
    public void Detect_UnknownSession_NoEnvVars_ReturnsUnknown()
    {
        var result = LinuxSessionDetector.Detect(
            xdgSessionType: null,
            waylandDisplay: null,
            display: null);

        Assert.Equal(LinuxSessionType.Unknown, result);
    }

    [Fact]
    public void Detect_UnknownSession_EmptyStrings_ReturnsUnknown()
    {
        var result = LinuxSessionDetector.Detect(
            xdgSessionType: "",
            waylandDisplay: "",
            display: "");

        Assert.Equal(LinuxSessionType.Unknown, result);
    }

    [Theory]
    [InlineData("X11", null, ":0", LinuxSessionType.X11)]
    [InlineData("WAYLAND", "wayland-0", null, LinuxSessionType.Wayland)]
    public void Detect_CaseInsensitive_XdgSessionType(
        string xdgSessionType, string? waylandDisplay, string? display, LinuxSessionType expected)
    {
        var result = LinuxSessionDetector.Detect(xdgSessionType, waylandDisplay, display);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Detect_UnrecognizedXdgSessionType_WithDisplay_ReturnsX11()
    {
        // Unknown XDG_SESSION_TYPE but DISPLAY is set -> fall back to X11
        var result = LinuxSessionDetector.Detect(
            xdgSessionType: "tty",
            waylandDisplay: null,
            display: ":0");

        Assert.Equal(LinuxSessionType.X11, result);
    }
}
