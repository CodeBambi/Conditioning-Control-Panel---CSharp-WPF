# SP-006 headed smoke: launch Debug exe, observe via UIA that the window renders the
# CapabilityProbes phase trace entry AND truthful demonstrator capability states
# (integration proof, capability contract §9), close gracefully, confirm exit 0.
$ErrorActionPreference = 'Stop'

$exe = Join-Path $PSScriptRoot '..\..\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$proc = [System.Diagnostics.Process]::Start($exe)
Write-Output "launched pid=$($proc.Id)"
Start-Sleep -Seconds 4

function Get-WindowText([int]$processId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
    if ($null -eq $window) { return $null }
    $textCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
    $lines = @()
    foreach ($t in $texts) { $lines += $t.Current.Name }
    return @{ Title = $window.Current.Name; Lines = $lines }
}

$first = Get-WindowText $proc.Id
if ($null -eq $first) { Write-Output 'FAIL: window not found'; $proc.Kill(); exit 1 }
Write-Output "window: $($first.Title)"
$first.Lines | ForEach-Object { Write-Output "  text: $_" }
$all = $first.Lines -join "`n"

$checks = @(
    'CapabilityProbes: ok',
    'capability display-session: Available',
    'windows desktop session',
    'capability atomic-filesystem: Available',
    'verified by real I/O'
)
foreach ($needle in $checks) {
    if ($all -notlike "*$needle*") { Write-Output "FAIL: missing '$needle'"; $proc.Kill(); exit 1 }
}
Write-Output 'capability states render truthfully (observed, not believed)'

# Graceful close -> single guarded teardown entry point -> exit 0.
$null = $proc.CloseMainWindow()
if (-not $proc.WaitForExit(10000)) { Write-Output 'FAIL: process did not exit within 10s'; $proc.Kill(); exit 1 }
Write-Output "exit code: $($proc.ExitCode)"
if ($proc.ExitCode -ne 0) { Write-Output 'FAIL: non-zero exit'; exit 1 }
Write-Output 'SMOKE PASS'
