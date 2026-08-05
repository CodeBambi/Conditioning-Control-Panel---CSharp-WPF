param([uint32]$Hwnd)
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class W32C {
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref P p);
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 public struct R { public int Left, Top, Right, Bottom; }
 public struct P { public int X, Y; }
 public static string ListChildren(IntPtr parent) {
   var outp = new StringBuilder();
   EnumChildWindows(parent, (h, l) => {
     var sb = new StringBuilder(256); GetClassName(h, sb, 256);
     R r; GetWindowRect(h, out r);
     outp.AppendLine(string.Format("child {0} class=[{1}] rect={2},{3} {4}x{5}", h, sb, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
     return true; }, IntPtr.Zero);
   return outp.ToString(); }
}
'@
$h = [IntPtr][long]$Hwnd
Write-Output ([W32C]::ListChildren($h))
