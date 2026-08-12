using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Features.Chaos;

/// <summary>
/// The tunnel window's Win32 surface (WPF ChaosTunnelService.cs `:541-573` parity): the
/// z-order and ex-style constants both the window (sink-to-bottom) and the service
/// (dashboard z-guard) use. Windows-only; every caller guards on OperatingSystem.IsWindows().
/// </summary>
internal static class ChaosTunnelWin32
{
    public static readonly IntPtr HwndBottom = new(1);

    public const uint SwpNosize = 0x0001;
    public const uint SwpNomove = 0x0002;
    public const uint SwpNoactivate = 0x0010;
    public const uint SwpNoownerzorder = 0x0200;
    public const uint GwHwndprev = 3;

    // Absorb clicks (NO WS_EX_TRANSPARENT — clicks reach the page for power-up raycasting,
    // ChaosTunnelService.cs:31) but never steal focus / show in Alt-Tab.
    public const uint WsExNoactivate = 0x08000000;
    public const uint WsExToolwindow = 0x00000080;

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);
}
