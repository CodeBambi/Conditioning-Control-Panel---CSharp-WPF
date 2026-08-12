$exe = (Resolve-Path 'client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe').Path
$env:CCP_DATA_ROOT = (Resolve-Path 'spine-tasks\SP-061-chaos-tunnel-backdrop\scratch\evidence\wh\sandbox-root').Path
$psi = New-Object System.Diagnostics.ProcessStartInfo($exe, '--tunnel-demo --tunnel-auto-close 30')
$psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false
$p = [System.Diagnostics.Process]::Start($psi)
Start-Sleep -Seconds 10
Add-Type -AssemblyName System.Drawing
$src = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public class PW {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lparam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    public struct RECT { public int Left, Top, Right, Bottom; }
    public static IntPtr FindByTitle(uint pid, string frag) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint q; GetWindowThreadProcessId(h, out q);
            if (q == pid && IsWindowVisible(h)) {
                var sb = new StringBuilder(512); GetWindowTextW(h, sb, 512);
                if (sb.ToString().Contains(frag)) found = h;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing
$tunnel = [PW]::FindByTitle($p.Id, 'Chaos Tunnel')
Write-Output ('tunnel hwnd=' + $tunnel)
$r = New-Object PW+RECT
[PW]::GetWindowRect($tunnel, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
Write-Output ("rect ($($r.Left),$($r.Top)) ${w}x${h}")
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [PW]::PrintWindow($tunnel, $hdc, 2)  # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)
$g.Dispose()
Write-Output ('PrintWindow ok=' + $ok)
$colors = @{}
for ($x = 0; $x -lt $w; $x += 7) { for ($y = 0; $y -lt $h; $y += 7) { $colors[$bmp.GetPixel($x, $y).ToArgb()] = $true } }
Write-Output ('distinct colors=' + $colors.Count)
$bmp.Save((Resolve-Path 'spine-tasks\SP-061-chaos-tunnel-backdrop\scratch\evidence\wh').Path + '\printwindow-probe.png', [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$p.Kill()
$p.WaitForExit(5000) | Out-Null
