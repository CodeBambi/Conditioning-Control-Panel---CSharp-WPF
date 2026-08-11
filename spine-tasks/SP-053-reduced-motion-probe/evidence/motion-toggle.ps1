# SP-053 HARNESS-ONLY OS animation toggle (consult correction 2: SET never enters
# product code). The exact call the Windows Settings "Animation effects" toggle makes:
# SystemParametersInfo(SPI_SETCLIENTAREAANIMATION=0x1043, 0, pvParam-by-value,
# SPIF_UPDATEINIFILE|SPIF_SENDCHANGE). GET = 0x1042 with a BOOL* out-param.
# Usage:
#   pwsh -File motion-toggle.ps1 -Get            # read + print current state
#   pwsh -File motion-toggle.ps1 -Set 0|1        # set + verify + print
# Restore discipline: the probe run calls -Get first and restores the baseline in a
# finally block so a crashed run never leaves the box in reduced-motion state.
param(
    [switch]$Get,
    [int]$Set = -1
)

$sig = @'
using System;
using System.Runtime.InteropServices;
public static class Spi {
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
    public static extern bool Get(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
    public static extern bool Set(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
}
'@
Add-Type -TypeDefinition $sig -ErrorAction Stop

$SPI_GET = 0x1042
$SPI_SET = 0x1043
$SPIF = 0x01 -bor 0x02  # UPDATEINIFILE | SENDCHANGE

function Read-State {
    $v = 0
    if (-not [Spi]::Get($SPI_GET, 0, [ref]$v, 0)) {
        $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        Write-Output "GET FAILED (win32 $err)"
        exit 2
    }
    return $v
}

if ($Get) {
    $v = Read-State
    Write-Output "ClientAreaAnimation=$v"
    exit 0
}

if ($Set -lt 0) {
    Write-Output "usage: -Get | -Set 0|1"
    exit 1
}

if (-not [Spi]::Set($SPI_SET, 0, [IntPtr]$Set, $SPIF)) {
    $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    Write-Output "SET $Set FAILED (win32 $err)"
    exit 2
}
Start-Sleep -Milliseconds 1500  # settle for the SENDCHANGE broadcast
$verify = Read-State
Write-Output "set=$Set verify ClientAreaAnimation=$verify"
if ($verify -ne $Set) { exit 3 }
exit 0
