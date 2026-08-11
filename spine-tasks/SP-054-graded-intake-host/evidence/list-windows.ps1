$src = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class WinEnum3 {
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public static string ListForPid(uint target) {
        var sb2 = new StringBuilder();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != target) return true;
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            sb2.AppendLine(h.ToString() + " vis=" + IsWindowVisible(h) + " title='" + sb + "'");
            return true;
        }, IntPtr.Zero);
        return sb2.ToString();
    }
}
'@
Add-Type -TypeDefinition $src
$proc = Get-Process CcpClient.Desktop -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output 'NO-PROC'; exit 1 }
[WinEnum3]::ListForPid([uint32]$proc.Id)
