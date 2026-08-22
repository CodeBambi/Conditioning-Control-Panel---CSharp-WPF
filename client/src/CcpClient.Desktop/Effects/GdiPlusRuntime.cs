using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// GDI+ for this process: started once, for the life of it.
///
/// <para><b>Why GDI+ at all</b> (and unchanged since): the surface these frame sources
/// feed is a Win32 window filled by a GDI blit, and the pixels have to arrive as a B,G,R,X buffer
/// whatever produces them. GDI+ is part of Windows, needs no package, and — the reason that decided
/// it — it works in a process with NO Avalonia runtime, which is what lets the whole draw path be
/// proven in the pure-logic test project instead of only where a UI toolkit has been initialised. It
/// is Windows-only, exactly like the surface it feeds; the overlay refuses on every other platform
/// anyway (<see cref="Overlay.UnsupportedOverlayPresence"/>), so no capability is lost by the
/// rasteriser being Windows-only, and none is claimed either.</para>
///
/// <para><b>Why it is its own class.</b> The image path and the text path both need the
/// library up, and <c>GdiplusStartup</c> is per process, not per caller. Two lazy initialisers would
/// start it twice and leave the second token dangling.</para>
/// </summary>
public static class GdiPlusRuntime
{
    /// <summary>GDI+ <c>Status.Ok</c>.</summary>
    internal const int Ok = 0;

    private static readonly Lazy<bool> Started = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>True when GDI+ initialised in this process. False means every render returns
    /// null — no frames, no exception, and nothing pretending.</summary>
    public static bool Available => OperatingSystem.IsWindows() && Started.Value;

    private static bool Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var input = new StartupInput { GdiplusVersion = 1 };
        // The token is deliberately dropped: GdiplusShutdown at process exit would race every
        // still-live frame, and a process that is ending does not need its imaging library
        // unloaded politely. One startup, for the life of the process.
        return GdiplusStartup(out _, ref input, out _) == Ok;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInput
    {
        public uint GdiplusVersion;
        public nint DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupOutput
    {
        public nint NotificationHook;
        public nint NotificationUnhook;
    }

    [DllImport("gdiplus.dll")]
    private static extern int GdiplusStartup(out nint token, ref StartupInput input, out StartupOutput output);
}
