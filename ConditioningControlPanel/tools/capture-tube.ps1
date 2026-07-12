<#
.SYNOPSIS
  Live-vision capture for the Avatar Tube window (owner "solid screenshot check").

.DESCRIPTION
  Launches the built Avalonia Windows desktop head, lets it settle, then on a fixed
  cadence for a run window finds the "Avatar Tube" top-level window, crops its rect off
  the virtual screen via GDI CopyFromScreen, and writes timestamped PNGs. A vision judge
  (driver or big-tier) inspects the frames afterward -- the RTB/smoke harness is a proven
  false positive for this layered/GPU topmost window class, so live capture is ground truth.

  Portable across Windows PowerShell 5.1 and PowerShell 7+. No params required.

.PARAMETER BinExe   Path to CCP.Desktop.Windows.exe (default: Debug win-x64, then AnyCPU output).
.PARAMETER FramesDir Output dir for PNGs (default: <repo>/logs/tube-frames).
.PARAMETER SettleSec Seconds to wait after launch before first capture (default 20).
.PARAMETER RunSec    Total capture seconds (default 90).
.PARAMETER IntervalSec Seconds between captures (default 5).
#>
param(
    [string]$BinExe = "",
    [string]$FramesDir = "",
    [int]$SettleSec = 20,
    [int]$RunSec = 90,
    [int]$IntervalSec = 5
)
$ErrorActionPreference = "Continue"

# --- repo root = two levels up from this script (ConditioningControlPanel/tools/) ---
$repoRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, "..", ".."))

if ([string]::IsNullOrWhiteSpace($BinExe)) {
    $rid = [System.IO.Path]::Combine($repoRoot, "ConditioningControlPanel", "CCP.Avalonia.Desktop.Windows", "bin", "Debug", "net8.0-windows10.0.19041.0", "win-x64", "CCP.Desktop.Windows.exe")
    $any = [System.IO.Path]::Combine($repoRoot, "ConditioningControlPanel", "CCP.Avalonia.Desktop.Windows", "bin", "Debug", "net8.0-windows10.0.19041.0", "CCP.Desktop.Windows.exe")
    if (Test-Path $rid) { $BinExe = $rid } elseif (Test-Path $any) { $BinExe = $any } else { $BinExe = $rid }
}
if (-not (Test-Path $BinExe)) {
    Write-Error "CCP.Desktop.Windows.exe not found ($BinExe). Build first (dotnet build ConditioningControlPanel/CCP.Desktop.slnf -c Debug) or pass -BinExe."
    exit 1
}
if ([string]::IsNullOrWhiteSpace($FramesDir)) { $FramesDir = [System.IO.Path]::Combine($repoRoot, "logs", "tube-frames") }
if (-not (Test-Path $FramesDir)) { New-Item -ItemType Directory -Force -Path $FramesDir | Out-Null }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinCap {
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    public static List<IntPtr> EnumVisible(uint target) {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == target && IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    public static string Title(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
}
'@

# NOTE: parameter is $procId, NOT $pid -- $pid is a read-only automatic variable in PowerShell
# and binding it throws "Cannot overwrite variable pid because it is read-only or constant".
function Find-TubeHwnd([uint32]$procId) {
    foreach ($h in [WinCap]::EnumVisible($procId)) {
        $title = [WinCap]::Title($h)
        if ($title -and $title -match 'Avatar' -and $title -match 'Tube') { return $h }
    }
    return [IntPtr]::Zero
}

function Save-TubeFrame([IntPtr]$hwnd, [string]$outPath) {
    $r = New-Object WinCap+RECT
    if (-not [WinCap]::GetWindowRect($hwnd, [ref]$r)) { return $false }
    $w = $r.Right - $r.Left; $hgt = $r.Bottom - $r.Top
    if ($w -le 0 -or $hgt -le 0) { return $false }
    try {
        $bmp = New-Object System.Drawing.Bitmap $w, $hgt, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $hgt)), ([System.Drawing.CopyPixelOperation]::SourceCopy))
        $g.Dispose()
        $bmp.Save($outPath, ([System.Drawing.Imaging.ImageFormat]::Png)); $bmp.Dispose()
        Write-Output ("CAPTURED {0}  L={1} T={2} {3}x{4}" -f $outPath, $r.Left, $r.Top, $w, $hgt)
        return $true
    } catch { Write-Output ("CAPTURE-ERR {0}" -f $_.Exception.Message); return $false }
}

function Stop-Stray {
    foreach ($n in @("CCP.Desktop.Windows", "dotnet")) {
        try { Get-Process -Name $n -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue } catch { }
    }
}

Write-Output ("capture-tube: bin=" + $BinExe)
Write-Output ("capture-tube: frames=" + $FramesDir)
Stop-Stray
Start-Sleep -Seconds 2
$proc = Start-Process -FilePath $BinExe -PassThru -WindowStyle Normal
Write-Output ("capture-tube: launched pid=" + $proc.Id + " settling " + $SettleSec + "s")
Start-Sleep -Seconds $SettleSec

$start = Get-Date; $captured = 0
while (((Get-Date) - $start).TotalSeconds -lt $RunSec) {
    $hwnd = Find-TubeHwnd -procId ([uint32]$proc.Id)
    if ($hwnd -eq [IntPtr]::Zero) {
        Write-Output ("capture-tube: Avatar Tube window not found (pid=" + $proc.Id + ") -- retry")
    } else {
        $stamp = (Get-Date).ToString("yyyyMMdd_HHmmss_fff")
        if (Save-TubeFrame -hwnd $hwnd -outPath ([System.IO.Path]::Combine($FramesDir, "tube_" + $stamp + ".png"))) { $captured++ }
    }
    Start-Sleep -Seconds $IntervalSec
}
Write-Output ("capture-tube: captured " + $captured + " frame(s) into " + $FramesDir)
try { if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Start-Sleep -Seconds 1; Stop-Stray
Write-Output "capture-tube: done."
