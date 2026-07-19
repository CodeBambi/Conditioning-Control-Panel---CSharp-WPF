# SP-004 headed smoke: launch Debug exe, observe a background callback reaching the
# window through the dispatch boundary (heartbeat tick text via UIA), close gracefully,
# confirm exit code 0.
$ErrorActionPreference = 'Stop'

$exe = Join-Path $PSScriptRoot '..\..\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$proc = [System.Diagnostics.Process]::Start($exe)
Write-Output "launched pid=$($proc.Id)"

# Let the app start and tick several times (250ms interval).
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

# Wait for more ticks; the heartbeat text must CHANGE (a background callback reached the
# window through the boundary — observed, not believed).
Start-Sleep -Seconds 2
$second = Get-WindowText $proc.Id
$tick1 = $first.Lines | Where-Object { $_ -like 'Heartbeat: tick *' } | Select-Object -First 1
$tick2 = $second.Lines | Where-Object { $_ -like 'Heartbeat: tick *' } | Select-Object -First 1
Write-Output "tick sample 1: $tick1"
Write-Output "tick sample 2: $tick2"
if ([string]::IsNullOrEmpty($tick1) -or [string]::IsNullOrEmpty($tick2) -or $tick1 -eq $tick2) {
    Write-Output 'FAIL: heartbeat tick text absent or not advancing'
    $proc.Kill(); exit 1
}

# Graceful close -> single guarded teardown entry point -> exit 0.
$null = $proc.CloseMainWindow()
if (-not $proc.WaitForExit(10000)) { Write-Output 'FAIL: process did not exit within 10s'; $proc.Kill(); exit 1 }
Write-Output "exit code: $($proc.ExitCode)"
if ($proc.ExitCode -ne 0) { Write-Output 'FAIL: non-zero exit'; exit 1 }
Write-Output 'SMOKE PASS'
