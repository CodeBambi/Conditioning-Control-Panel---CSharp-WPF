using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The title source for <c>WindowAwarenessService</c>, per OS. Windows = WPF's exact read
/// (Services/UI/WindowAwarenessService.cs GetActiveWindowTitle: user32 GetForegroundWindow +
/// GetWindowText, 512 chars); Linux = <see cref="X11ActiveWindow"/>. "" when unknown, never throws.
/// </summary>
internal static class ActiveWindowTitle
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    public static string Read()
    {
        if (OperatingSystem.IsWindows()) return ReadWindows();
        return X11ActiveWindow.ReadTitle();
    }

    internal static string ReadWindows()
    {
        try
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return "";
            var sb = new StringBuilder(512);
            GetWindowText(handle, sb, 512);
            return sb.ToString();
        }
        catch { return ""; }
    }
}
