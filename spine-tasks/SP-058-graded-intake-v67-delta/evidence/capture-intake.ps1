# SP-058 intake-window capture: SP-054's capture-intake.ps1 pattern (EnumWindows by exact
# title WITHOUT the IsWindowVisible filter — the intake window reports vis=False to the OS
# enumerator while painted, the SP-054 recorded quirk; SP-057's drive.ps1 filters on
# visibility and therefore can never see it). SW_RESTORE + HWND_TOPMOST raise, move to the
# requested point, rect printed for verification, then CopyFromScreen.
param(
  [Parameter(Mandatory=$true)][string]$Out,
  [int]$X = 100,
  [int]$Y = 100,
  [string]$Title = 'Graded Intake'
)
$ErrorActionPreference = "Stop"
$src = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Win32C {
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
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
$h = [Win32C]::FindByTitle($Title)
if ($h -eq [IntPtr]::Zero) { Write-Output "NO-WINDOW ($Title)"; exit 1 }
[Win32C]::ShowWindow($h, 9) | Out-Null   # SW_RESTORE
$HWND_TOPMOST = [IntPtr]::new(-1)
# 0x0001 = SWP_NOSIZE (move only — NEVER pass cx/cy=0 without NOSIZE: that squashes the
# window to its minimum, the third-run lesson); NOMOVE (0x0002) deliberately absent.
[Win32C]::SetWindowPos($h, $HWND_TOPMOST, $X, $Y, 0, 0, 0x0001) | Out-Null
[Win32C]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 1200
$rect = New-Object Win32C+RECT
[Win32C]::GetWindowRect($h, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left; $ht = $rect.Bottom - $rect.Top
Write-Output ("rect {0},{1} {2}x{3}" -f $rect.Left, $rect.Top, $w, $ht)
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "saved $Out"
