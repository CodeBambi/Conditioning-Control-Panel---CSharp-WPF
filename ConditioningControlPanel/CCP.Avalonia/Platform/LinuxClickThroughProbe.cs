using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Cheap, static, DI-free probe answering one question: is the Linux X11 overlay backend
/// (XFixes input-shape click-through) actually usable on this system?
/// </summary>
/// <remarks>
/// <para><b>Why a static probe and not the DI-registered overlay:</b>
/// <see cref="AvaloniaPlatformCapabilities"/> must not resolve <c>IOverlaySurface</c> (or the
/// Linux head's backend selector) to answer a capability question — that risks a
/// capability&lt;-&gt;backend construction cycle and couples the shared Avalonia project to a
/// head-specific registration. This probe mirrors the availability gate of
/// <c>CCP.Avalonia.Desktop.Linux/Platform/Backends/X11InputShapeBackend.ProbeXFixes</c> and
/// <c>LinuxOverlayBackendPlan.Choose</c>: X display reachable (native X11 OR XWayland) AND
/// XFixes protocol version ≥ 2 (input-shape regions are an XFixes v2 addition — extension
/// presence alone is NOT enough; see linux-overlay-contract.md §3.1/§7.1). Keep the two in
/// sync: if the backend's probe gate changes, change this probe too.</para>
///
/// <para><b>Resource + privacy contract:</b> the probe opens its own short-lived
/// <c>XOpenDisplay</c> connection and ALWAYS closes it before returning (no leaked display,
/// no lingering server resources). It issues only extension-query requests — it never reads
/// window or screen contents and logs nothing. The three calls in the success path
/// (XOpenDisplay → XFixesQueryExtension → XFixesQueryVersion) generate no X protocol errors,
/// so no Xlib error trap is required: XOpenDisplay reports failure by returning NULL, and
/// the version query is only issued after the extension is confirmed present.</para>
///
/// <para>The result is computed at most once per process (thread-safe lazy) — capability
/// objects may be constructed more than once, but the system's XFixes support does not
/// change mid-process.</para>
/// </remarks>
internal static class LinuxClickThroughProbe
{
    // Same sonames as CCP.Avalonia.Desktop.Linux/Platform/Interop/X11Interop.cs.
    private const string LibX11 = "libX11.so.6";
    private const string LibXfixes = "libXfixes.so.3";

    private static readonly Lazy<bool> Probe =
        new(ProbeX11InputShapeSupport, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// True when an X display is reachable and XFixes ≥ 2 is available, i.e. the Linux
    /// head's <c>X11InputShapeBackend</c> would be selected rather than the fallback.
    /// Always false on non-Linux platforms.
    /// </summary>
    public static bool IsX11InputShapeAvailable() => OperatingSystem.IsLinux() && Probe.Value;

    private static bool ProbeX11InputShapeSupport()
    {
        if (!OperatingSystem.IsLinux()) return false;

        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero)
            {
                // No reachable X display (headless, or pure Wayland without XWayland).
                return false;
            }

            if (XFixesQueryExtension(display, out _, out _) == 0)
            {
                return false;
            }

            // Input-shape regions require XFixes protocol v2+ (contract §3.1 row 4).
            return XFixesQueryVersion(display, out int major, out _) != 0 && major >= 2;
        }
        catch (DllNotFoundException)
        {
            // libX11/libXfixes not installed — fallback backend territory.
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (Exception)
        {
            // Never let a capability probe fault the app; degraded (false) is always safe.
            return false;
        }
        finally
        {
            if (display != IntPtr.Zero)
            {
                try
                {
                    XCloseDisplay(display);
                }
                catch
                {
                    // Best effort — never throw from the probe.
                }
            }
        }
    }

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(string? displayName);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibXfixes)]
    private static extern int XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);

    [DllImport(LibXfixes)]
    private static extern int XFixesQueryVersion(IntPtr display, out int majorVersion, out int minorVersion);
}
