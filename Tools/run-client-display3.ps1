# run-client-display3.ps1 — launch the CCP client on DISPLAY3 (owner convention 2026-07-21: screen 3 is the test surface)
# Usage:  pwsh Tools/run-client-display3.ps1 [-Exe <path>] [-MoveOnly]
#   -Exe      path to the app (default: client/src/CcpClient.Desktop/bin/Debug/net10.0/CcpClient.Desktop.exe)
#   -MoveOnly don't launch; move the already-running window to DISPLAY3
param([string]$Exe = "client/src/CcpClient.Desktop/bin/Debug/net10.0/CcpClient.Desktop.exe", [switch]$MoveOnly)

# DISPLAY3 bounds from the 2026-07-21 topology: (-2576,1091) 2560x1440
$DX = -2576; $DY = 1091; $DW = 2560; $DH = 1440

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WinPos {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
"@

if (-not $MoveOnly) {
    if (-not (Test-Path $Exe)) { Write-Error "not found: $Exe (build first: dotnet build client/CcpClient.sln -c Debug)"; exit 1 }
    $before = Get-Process -Name "CcpClient.Desktop" -ErrorAction SilentlyContinue
    Start-Process $Exe
    $deadline = (Get-Date).AddSeconds(25)
    do {
        Start-Sleep -Milliseconds 400
        $proc = Get-Process -Name "CcpClient.Desktop" -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_ -or -not $before } | Select-Object -First 1
        if (-not $proc) { $proc = Get-Process -Name "CcpClient.Desktop" -ErrorAction SilentlyContinue | Select-Object -First 1 }
    } while ((-not $proc -or $proc.MainWindowHandle -eq 0) -and (Get-Date) -lt $deadline)
} else {
    $proc = Get-Process -Name "CcpClient.Desktop" -ErrorAction SilentlyContinue | Select-Object -First 1
}
if (-not $proc) { Write-Error "no CcpClient.Desktop process/window found"; exit 1 }
$deadline = (Get-Date).AddSeconds(15)
while ($proc.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300; $proc.Refresh() }
if ($proc.MainWindowHandle -eq 0) { Write-Error "process has no main window"; exit 1 }
# place at DISPLAY3 origin + small inset, natural size (0 keeps current size)
[WinPos]::SetWindowPos($proc.MainWindowHandle, [IntPtr]::Zero, ($DX + 60), ($DY + 60), 0, 0, 0x0001) | Out-Null
Write-Output "window moved to DISPLAY3 at ($($DX+60),$($DY+60)) — pid $($proc.Id)"
