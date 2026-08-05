param([int]$TargetPid, [string]$Title = 'The Loom', [string]$Out = '')

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class W32E {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 public struct R { public int Left, Top, Right, Bottom; }
 public static IntPtr FindByTitle(uint pid, string title, StringBuilder log) {
   IntPtr found = IntPtr.Zero;
   EnumWindows((h, l) => {
     uint p; GetWindowThreadProcessId(h, out p);
     if (p != pid || !IsWindowVisible(h)) return true;
     var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
     R r; GetWindowRect(h, out r);
     log.AppendLine(string.Format("{0} title=[{1}] rect={2},{3} {4}x{5}", h, sb, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
     if (sb.ToString() == title) { found = h; return false; }
     return true; }, IntPtr.Zero);
   return found; }
}
'@

$log = New-Object System.Text.StringBuilder
$h = [W32E]::FindByTitle([uint32]$TargetPid, $Title, $log)
Write-Output $log.ToString()
if ($h -eq [IntPtr]::Zero) { Write-Output 'NO MATCHING HWND'; exit 1 }

if ($Out -ne '') {
  [W32E]::SetWindowPos($h, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0013) | Out-Null  # TOPMOST, NOMOVE|NOSIZE|NOACTIVATE
  Start-Sleep -Milliseconds 700
  $r = New-Object W32E+R
  [W32E]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
  Write-Output ("capture rect: {0},{1} {2}x{3}" -f $r.Left, $r.Top, $w, $hh)
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
  $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
  [W32E]::SetWindowPos($h, [IntPtr]::new(-2), 0, 0, 0, 0, 0x0013) | Out-Null  # NOTOPMOST
  Write-Output ("saved: {0} bytes" -f (Get-Item $Out).Length)
}
