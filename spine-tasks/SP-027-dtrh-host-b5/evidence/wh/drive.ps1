# SP-027 headed driver (owner DISPLAY3 convention 2026-07-21, rect-persistence BINDING
# 2026-07-22): locate the DTRH evidence window, move it to DISPLAY3 ((-2576,1091)
# 2560x1440) via SetWindowPos, VERIFY with GetWindowRect before any capture — the rect
# line goes to stdout and the run scripts APPEND it into the committed run logs —
# dump the UIA tree, and drive real clicks/keys at UIA-reported element rects.
# SP-027: adds the "keyhold" action (ESC held — the payload's hold-to-exit threshold).
param(
  [Parameter(Mandatory=$true)][string]$Action,   # dump | click | capture | clickrel | key | keyhold | sweep
  [string]$Arg = "",                             # click: element Name; capture: out path; clickrel: "x,y"; key: esc|enter; keyhold: hold-ms; sweep: "passes,cols,rows"
  [string]$TitleLike = "Down the Rabbit Hole",
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
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
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
    # Foreground is load-bearing (diagB1 2026-07-22): keybd_event lands on the
    # OS-foreground window only. Claim + VERIFY, never assume.
    [W3]::SetWindowPos($h, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
    Start-Sleep -Milliseconds 200
    [W3]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 300
    if ([W3]::GetForegroundWindow() -ne $h) { Write-Error "foreground NOT acquired for key drive"; exit 4 }
    $vk = if ($Arg -eq "esc") { 0x1B } else { 0x0D }
    $scan = if ($Arg -eq "esc") { 0x01 } else { 0x1C }
    [W3]::keybd_event($vk, $scan, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [W3]::keybd_event($vk, $scan, 2, [UIntPtr]::Zero)
    Write-Output "key $Arg sent (foreground verified)"
  }
  "vn-clear" {
    # SP-027 b5 (2026-07-22 forensics): on a FRESH slot the hub plays the cheshire
    # hub_welcome fullscreen VN scene (15 beats; cheshireGuide.js:355), whose
    # capture-phase keydown handler swallows EVERY key incl. ESC
    # (cheshireVn.js:484-491 — WPF-shared payload, so WPF behaves identically).
    # The ESC-exit cell must click the scene through first: first click completes
    # the typewriter, second advances (cheshireVn.js:547) → 2 clicks/beat + margin.
    # Clicks are real canvas clicks (b2 non-modal norm); stragglers after the scene
    # just pop bubbles in game mode — harmless.
    $clicks = if ($Arg) { [int]$Arg } else { 40 }
    [W3]::SetWindowPos($h, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
    Start-Sleep -Milliseconds 250
    $cx = [int](($r.Left + $r.Right) / 2); $cy = [int](($r.Top + $r.Bottom) / 2)
    [W3]::SetCursorPos($cx, $cy) | Out-Null
    for ($c = 1; $c -le $clicks; $c++) {
      [W3]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
      Start-Sleep -Milliseconds 50
      [W3]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
      Start-Sleep -Milliseconds 650
      if ($c % 10 -eq 0) { Write-Output "vn-clear $c/$clicks clicks" }
    }
    [W3]::SetWindowPos($h, [IntPtr]::new(-2), 0, 0, 0, 0, 0x0001 -bor 0x0002) | Out-Null
    Write-Output "vn-clear complete ($clicks clicks — hub_welcome clicked through)"
  }
  "keyhold" {
    # SP-027 b5: ESC HELD (default 1500ms — the payload's 1.2s hold-to-exit threshold,
    # boot.js; SP-011 W16 shape). Real keybd_event, never a synthesized message.
    # LOAD-BEARING (diagB1/diagB1v2 forensics 2026-07-22): (1) a real canvas CLICK is
    # the only reliable foreground claim — SetForegroundWindow is foreground-locked
    # while the owner uses the machine (SFW returned True but foreground stayed on the
    # owner's window); (2) scancode 0x01 (scancode-0 keys never reached the page);
    # (3) foreground VERIFIED immediately before keydown, loud exit 4 otherwise.
    $holdMs = if ($Arg) { [int]$Arg } else { 1500 }
    $cx = [int](($r.Left + $r.Right) / 2); $cy = [int](($r.Top + $r.Bottom) / 2)
    Click-Point $cx $cy
    Start-Sleep -Milliseconds 300
    [W3]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 300
    if ([W3]::GetForegroundWindow() -ne $h) { Write-Error "foreground NOT acquired for keyhold (owner input race?)"; exit 4 }
    [W3]::keybd_event(0x1B, 0x01, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $holdMs
    [W3]::keybd_event(0x1B, 0x01, 2, [UIntPtr]::Zero)
    Write-Output "ESC held ${holdMs}ms (real keybd_event, click-focus + foreground verified)"
  }
  "sweep" {
    # SP-026 gameplay sweep: N grid passes over the client area (bubbles pop on click).
    # One topmost raise up front (non-modal host window — the b2 canvas-click norm).
    $parts = $Arg.Split(',')
    $passes = [int]$parts[0]; $cols = [int]$parts[1]; $rows = [int]$parts[2]
    [W3]::SetWindowPos($h, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
    Start-Sleep -Milliseconds 250
    $x0 = $r.Left + 80; $x1 = $r.Right - 80
    $y0 = $r.Top + 100; $y1 = $r.Bottom - 100
    for ($p = 1; $p -le $passes; $p++) {
      for ($cx = 0; $cx -lt $cols; $cx++) {
        for ($cy = 0; $cy -lt $rows; $cy++) {
          $x = [int]($x0 + ($x1 - $x0) * ($cx + (($p + $cy) % 2) * 0.5) / $cols)
          $y = [int]($y0 + ($y1 - $y0) * ($cy + (($p + $cx) % 2) * 0.5) / $rows)
          [W3]::SetCursorPos($x, $y) | Out-Null
          [W3]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
          Start-Sleep -Milliseconds 40
          [W3]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
          Start-Sleep -Milliseconds 30
        }
      }
      Write-Output "sweep pass $p/$passes done"
      Start-Sleep -Milliseconds 400
    }
    [W3]::SetWindowPos($h, [IntPtr]::new(-2), 0, 0, 0, 0, 0x0001 -bor 0x0002) | Out-Null
    Write-Output "sweep complete"
  }
}
