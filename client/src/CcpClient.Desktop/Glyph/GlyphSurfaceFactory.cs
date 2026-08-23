namespace CcpClient.Desktop.Glyph;

/// <summary>The host platform a glyph backend is selected for.</summary>
public enum GlyphHostPlatform
{
    /// <summary>Not one of the platforms this build knows about.</summary>
    Unknown,

    Windows,

    Linux,

    MacOs,
}

/// <summary>
/// Chooses the per-pixel-alpha backend for a platform.
///
/// <para><b>Selection is not availability.</b> Picking a backend by platform is allowed;
/// <c>runtime-capability-contract.md</c> §2 rule 2 bans platform checks from producing
/// <c>Available</c>, and nothing here produces it — only <see cref="Win32GlyphSurface.Present"/>
/// and <see cref="Win32GlyphSurface.Paint"/> do, and only after the operating system returned the
/// surface's own content.</para>
/// </summary>
public static class GlyphSurfaceFactory
{
    /// <summary>
    /// The exact manual gate a Linux box must run before any Linux per-pixel-alpha claim is made.
    /// A constant so it travels with the refusal to whoever hits it, not only in a record file
    /// nobody reads at 2am.
    ///
    /// <para><b>Why it is a different gate from the overlay's, and harder.</b> The overlay's Linux
    /// gate asks for an always-on-top click-through window. This one needs that AND a 32-bit ARGB
    /// visual with a running compositing manager, because per-pixel alpha on X11 is not a window
    /// attribute at all — it is a property of the visual the window was created with, and it does
    /// nothing without a compositor to honour it. Step 3 is the one that corresponds to this
    /// packet's central trap, and step 5 is expected to FAIL under Wayland even where the picture
    /// is correct, exactly as the video gate's step 4 does.</para>
    /// </summary>
    public const string LinuxManualGate =
        "MANUAL GATE (Linux, undischarged): on a real X11 desktop session - NOT WSLg - composite a glyph surface "
        + "and prove it five ways: "
        + "(1) the window was created on a 32-bit-depth ARGB visual (XGetVisualInfo depth 32 with an alpha mask), "
        + "with its own colormap, and `xprop -id <xid> _NET_WM_STATE` lists _NET_WM_STATE_ABOVE; "
        + "(2) a compositing manager is RUNNING (the _NET_WM_CM_S0 selection has an owner), because without one "
        + "an ARGB visual composites nothing and the window shows garbage or black - this is the Linux form of "
        + "this packet's ghost and it must be a typed refusal, not a surface; "
        + "(3) THE DIFFERENTIAL: with a known opaque colour behind the surface, an XGetImage of the ROOT window "
        + "reads back that colour where the frame's alpha is 0, the glyph's own colour where the alpha is 255, "
        + "and BLACK where the frame is opaque black - the same three-value distinction the Windows harness "
        + "makes, because a count of draw calls is not evidence; "
        + "(4) an empty XFixes ShapeInput region really passes a click to the application underneath, confirmed "
        + "by a human; "
        + "(5) the same run repeated under a native Wayland session, where the expected outcome is that the PROOF "
        + "is unavailable - a Wayland client cannot read back the composited output - so the honest result there "
        + "is a refusal even if the picture happens to be right. "
        + "This machine cannot discharge it: the port's Linux environment is WSLg, whose XWayland root has no "
        + "_NET_CLIENT_LIST (client/docs/port-lessons.md:52 @ a8d32c219), so window enumeration and z-order cannot be trusted "
        + "there at all.";

    /// <summary>Why Wayland is a refusal rather than a harder Linux. Same substance as the
    /// overlay's note, plus the read-back that this capability's evidence depends on.</summary>
    public const string WaylandNote =
        "Under a native Wayland compositor there is no protocol an ordinary application can use to guarantee an "
        + "always-on-top click-through surface (wlr-layer-shell is a wlroots extension Mutter does not implement) "
        + "AND no way for a client to read back what was composited, which is the instrument every alpha claim in "
        + "this capability rests on. So Wayland is doubly a refusal: the surface cannot be guaranteed and the "
        + "proof cannot be taken. The practical route there is XWayland - the pinned Avalonia 12.1.1 graph ships "
        + "Avalonia.X11 and Avalonia.FreeDesktop and no Wayland package at all - which makes the X11 route the "
        + "only one worth implementing, and its guarantees under XWayland are themselves compositor-dependent.";

    /// <summary>The backend for the platform this process is running on.</summary>
    public static IGlyphSurface Create() => CreateFor(CurrentPlatform());

    /// <summary>
    /// The backend for a named platform. Both branches are reachable from either OS, so the refusal
    /// path is testable on a Windows box and the Windows path is testable nowhere else — the honest
    /// asymmetry rather than a hidden one.
    /// </summary>
    public static IGlyphSurface CreateFor(GlyphHostPlatform platform) => platform switch
    {
        GlyphHostPlatform.Windows => new Win32GlyphSurface(),

        GlyphHostPlatform.Linux => new UnsupportedGlyphSurface(
            GlyphReasonCodes.GlyphMechanismAbsent,
            "no Linux per-pixel-alpha backend is implemented or verified in this build, and nothing was "
            + "attempted. The route on an X11 session is an override-redirect top-level window created on a "
            + "32-bit ARGB visual with its own colormap, _NET_WM_STATE_ABOVE, an empty XFixes ShapeInput region "
            + "for click-through, and a RUNNING compositing manager - without which an ARGB visual composites "
            + "nothing. " + WaylandNote + " Until the gate below runs, a caller must treat the visual half of its "
            + "effect as absent and say so - never composite into a surface nobody can confirm. " + LinuxManualGate),

        GlyphHostPlatform.MacOs => new UnsupportedGlyphSurface(
            GlyphReasonCodes.GlyphMechanismAbsent,
            "macOS is outside this port's supported Windows/Linux target set (architecture.md), so no per-pixel "
            + "alpha backend exists here and nothing was attempted"),

        _ => new UnsupportedGlyphSurface(
            GlyphReasonCodes.GlyphMechanismAbsent,
            "this platform is not one this build has a per-pixel-alpha backend for, and nothing was attempted"),
    };

    /// <summary>The platform this process is on, as a selection input only.</summary>
    public static GlyphHostPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return GlyphHostPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return GlyphHostPlatform.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return GlyphHostPlatform.MacOs;
        }

        return GlyphHostPlatform.Unknown;
    }
}
