param([uint32]$ParentHwnd, [int]$Notches = -40)
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class W32W {
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 public struct R { public int Left, Top, Right, Bottom; }
 public static IntPtr FindClass(IntPtr parent, string cls) {
   IntPtr found = IntPtr.Zero;
   EnumChildWindows(parent, (h, l) => {
     var sb = new StringBuilder(256); GetClassName(h, sb, 256);
     if (sb.ToString() == cls) { found = h; return false; }
     return true; }, IntPtr.Zero);
   return found; }
}
'@
$parent = [IntPtr][long]$ParentHwnd
$render = [W32W]::FindClass($parent, 'Chrome_RenderWidgetHostHWND')
if ($render -eq [IntPtr]::Zero) { Write-Output 'NO RENDER HWND'; exit 1 }
$r = New-Object W32W+R
[W32W]::GetWindowRect($render, [ref]$r) | Out-Null
$cx = [int](($r.Left + $r.Right) / 2); $cy = [int](($r.Top + $r.Bottom) / 2)
$lParam = [IntPtr](($cy -shl 16) -bor ($cx -band 0xFFFF))
for ($i = 0; $i -lt 6; $i++) {
  $wParam = [IntPtr](($Notches * 120) -shl 16)
  [W32W]::SendMessage($render, 0x020A, $wParam, $lParam) | Out-Null  # WM_MOUSEWHEEL
  Start-Sleep -Milliseconds 250
}
Write-Output ("wheel sent x6 ({0} notches each) at {1},{2}" -f $Notches, $cx, $cy)
