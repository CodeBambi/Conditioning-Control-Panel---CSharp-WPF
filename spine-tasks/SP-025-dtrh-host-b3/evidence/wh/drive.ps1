# SP-024 headed driver (owner DISPLAY3 convention 2026-07-21): locate the DTRH evidence
# window, move it to DISPLAY3 ((-2576,1091) 2560x1440) via SetWindowPos, VERIFY with
# GetWindowRect before any capture, dump the UIA tree (picker content evidence), and
# drive real clicks/keys at UIA-reported element rects.
param(
  [Parameter(Mandatory=$true)][string]$Action,   # dump | click | capture | clickrel | key
  [string]$Arg = "",                             # click: element Name; capture: out path; clickrel: "x,y" window-relative; key: esc|enter
  [string]$TitleLike = "save picker",
  [string]$ProcessName = "CcpClient.Desktop",
  [switch]$NoMove
)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W3 {
  public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, System.IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(System.IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte s, uint f, UIntPtr e);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$proc = Get-Process $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Error "no process $ProcessName"; exit 1 }
$tpid = $proc.Id
$found = [IntPtr]::Zero
$titles = @()
$cb = [W3+EnumProc]{
  param($h, $l)
  $wp = 0; [W3]::GetWindowThreadProcessId($h, [ref]$wp) | Out-Null
  if ($wp -eq $tpid -and [W3]::IsWindowVisible($h)) {
    $sb = New-Object System.Text.StringBuilder 512
    [W3]::GetWindowText($h, $sb, 512) | Out-Null
    $titles += $sb.ToString()
    if ($sb.ToString() -like "*$TitleLike*") { $script:found = $h; return $false }
  }
  return $true
}
[W3]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
if ($found -eq [IntPtr]::Zero) { Write-Error "no window like '$TitleLike' on pid $tpid (titles: $($titles -join ' | '))"; exit 1 }
$h = $found
if (-not $NoMove) {
  # DISPLAY3 owner convention: position + verify BEFORE any capture/click.
  [W3]::SetWindowPos($h, [IntPtr]::Zero, -2576, 1091, 0, 0, 0x0001 -bor 0x0040) | Out-Null
  Start-Sleep -Milliseconds 350
}
$r = New-Object W3+RECT
[W3]::GetWindowRect($h, [ref]$r) | Out-Null
Write-Output "GetWindowRect: ($($r.Left),$($r.Top))-($($r.Right),$($r.Bottom)) [$($r.Right-$r.Left)x$($r.Bottom-$r.Top)]"
if (-not $NoMove -and ($r.Left -ne -2576 -or $r.Top -ne 1091)) { Write-Error "placement NOT on DISPLAY3 origin"; exit 2 }

function Dump-Uia {
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
  $walker = [System.Windows.Automation.TreeWalker]::ContentViewWalker
  function Walk($el, $depth) {
    if ($null -eq $el) { return }
    $ct = $el.Current.ControlType.ProgrammaticName -replace 'ControlType.',''
    $br = $el.Current.BoundingRectangle
    $name = $el.Current.Name
    if ($ct -in @('Button','Text','Edit') -or $name) {
      Write-Output ("  " * $depth + "$ct [$([int]$br.X),$([int]$br.Y) $([int]$br.Width)x$([int]$br.Height)] '$name'")
    }
    $child = $walker.GetFirstChild($el)
    while ($null -ne $child) { Walk $child ($depth + 1); $child = $walker.GetNextSibling($child) }
  }
  Walk $root 0
}

function Click-Point($x, $y) {
  # Raise first: a raw click lands on whatever is TOP at the point (SP-023/SP-024 lesson).
  [W3]::SetWindowPos($h, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
  Start-Sleep -Milliseconds 250
  [W3]::SetCursorPos($x, $y) | Out-Null
  Start-Sleep -Milliseconds 120
  [W3]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 80
  [W3]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
  [W3]::SetWindowPos($h, [IntPtr]::new(-2), 0, 0, 0, 0, 0x0001 -bor 0x0002) | Out-Null
  Write-Output "clicked screen ($x,$y)"
}

switch ($Action) {
  "dump" { Dump-Uia }
  "click" {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Arg)
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Subtree, $cond)
    if ($null -eq $el) { Write-Error "no UIA element named '$Arg'"; exit 3 }
    $br = $el.Current.BoundingRectangle
    Click-Point ([int]($br.X + $br.Width / 2)) ([int]($br.Y + $br.Height / 2))
  }
  "clickrel" {
    $parts = $Arg.Split(',')
    Click-Point ($r.Left + [int]$parts[0]) ($r.Top + [int]$parts[1])
  }
  "capture" {
    Start-Sleep -Milliseconds 250
    $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    New-Item -ItemType Directory -Force -Path (Split-Path $Arg) | Out-Null
    $bmp.Save($Arg, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    # pixel stats: never a black surface (SP-023 evidence class)
    $dark = 0; $total = 0; $colors = @{}
    for ($x = 0; $x -lt $w; $x += 7) { for ($y = 0; $y -lt $hh; $y += 7) {
      $p = $bmp.GetPixel($x, $y); $total++
      if ($p.R -lt 20 -and $p.G -lt 20 -and $p.B -lt 20) { $dark++ }
      $colors[($p.R -band 0xF0) * 65536 + ($p.G -band 0xF0) * 256 + ($p.B -band 0xF0)] = 1
    } }
    $bmp.Dispose()
    Write-Output ("captured {0}x{1} -> {2}; dark={3:P1} distinct-colors~{4}" -f $w, $hh, $Arg, ($dark / $total), $colors.Count)
  }
  "key" {
    $vk = if ($Arg -eq "esc") { 0x1B } else { 0x0D }
    [W3]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [W3]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
    Write-Output "key $Arg sent"
  }
}
