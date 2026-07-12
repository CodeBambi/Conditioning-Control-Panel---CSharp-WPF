using System;
using System.Runtime.InteropServices;
using System.Text;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Platform;

/// <summary>
/// Windows implementation of <see cref="IForegroundWindowTitleProvider"/> via
/// GetForegroundWindow + GetWindowText (Unicode, 512 buffer), ported verbatim from the WPF
/// head (Services/UI/WindowAwarenessService.cs:60-66 P/Invoke, :549-556 GetActiveWindowTitle).
/// Reads the window TITLE only — no process name, no PID (privacy contract).
/// </summary>
public sealed class WindowsForegroundWindowTitleProvider : IForegroundWindowTitleProvider
{
    // P/Invoke declarations (WPF Services/UI/WindowAwarenessService.cs:60-66)
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    public string? GetForegroundWindowTitle()
    {
        // WPF Services/UI/WindowAwarenessService.cs:549-556
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return null;

        var sb = new StringBuilder(512);
        GetWindowText(handle, sb, 512);
        return sb.ToString();
    }
}
