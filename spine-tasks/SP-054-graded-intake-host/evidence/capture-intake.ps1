# SP-054 run4 capture: foreground the Graded Intake window, read its REAL rect, capture it.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@
Add-Type -AssemblyName System.Drawing
$src = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class WinEnum {
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public static IntPtr FindByTitle(string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            if (sb.ToString() == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing
$h = [WinEnum]::FindByTitle('Graded Intake')
if ($h -eq [IntPtr]::Zero) { Write-Output 'NO-WINDOW'; exit 1 }
[Win32]::ShowWindow($h, 9) | Out-Null   # SW_RESTORE
$HWND_TOPMOST = [IntPtr]::new(-1)
[Win32]::SetWindowPos($h, $HWND_TOPMOST, 0, 0, 0, 0, 0x0001 -bor 0x0002) | Out-Null  # topmost, no move/size
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 1200
$rect = New-Object Win32+RECT
[Win32]::GetWindowRect($h, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left; $ht = $rect.Bottom - $rect.Top
Write-Output ("rect {0},{1} {2}x{3}" -f $rect.Left, $rect.Top, $w, $ht)
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
$bmp.Save('run4-intake-window.png', [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output 'saved run4-intake-window.png'
