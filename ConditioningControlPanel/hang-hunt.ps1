<#
.SYNOPSIS
    Automated UI-hang hunter for Conditioning Control Panel.

    Launches the app in `--stress` mode (hammers bubbles / flash / shared-host churn - the
    render-thread-deadlock suspects), pings its window a few times a second, and the INSTANT
    the UI thread stops responding it auto-captures:
      * a plain-text all-thread managed stack   (dotnet-stack report)   <- read this first
      * a full process dump                      (dotnet-dump collect)   <- for deep WinDbg/SOS
      * the tail of the app log + last [CHAOSMEM]/[WATCHDOG]/[STRESS] lines
    then kills the wedged process, relaunches, and keeps hunting - unattended, for as long as
    you leave it running. Walk away; come back to a folder of frozen-stack reports.

.EXAMPLE
    # Just run it and leave. Ctrl+C to stop.
    powershell -ExecutionPolicy Bypass -File .\hang-hunt.ps1

.EXAMPLE
    # Hunt harder (more bubbles/tick, flip the shared host more often), stop after 3 catches.
    .\hang-hunt.ps1 -SpawnPerTick 5 -ToggleEvery 20 -MaxCaptures 3

.NOTES
    Requires dotnet-stack + dotnet-dump (already installed under ~\.dotnet\tools).
    Nothing here ships to users - `--stress` is dead code in every normal launch.
#>
[CmdletBinding()]
param(
    [int]    $HangSeconds  = 6,      # consecutive unresponsive seconds before we call it a hang
    [int]    $SpawnPerTick = 3,      # bubbles spawned per stress tick   (env CCP_STRESS_SPAWN)
    [int]    $TickMs       = 12,     # stress loop period in ms           (env CCP_STRESS_TICK_MS)
    [int]    $FlashEvery   = 6,      # flash-window churn cadence (ticks) (env CCP_STRESS_FLASH_EVERY)
    [int]    $ToggleEvery  = 40,     # shared-host create/close cadence   (env CCP_STRESS_TOGGLE_EVERY)
    [int]    $MaxCaptures  = 0,      # stop after N hangs caught (0 = run forever)
    [int]    $MaxRuntimeMinutes = 0, # self-terminate after N minutes with a survival report (0 = forever)
    [string] $DumpType     = 'Full', # dotnet-dump type: Full | Heap | Mini | None
    [string] $ExePath      = '',     # explicit exe path; auto-discovered (newest build) if blank
    [string] $OutDir       = ''      # where captures land; default = logs\hang-hunt
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# --- Resolve the exe (newest ConditioningControlPanel.exe under bin) ---------------------------
if (-not $ExePath) {
    $ExePath = Get-ChildItem -Path (Join-Path $scriptDir 'bin') -Recurse -Filter 'ConditioningControlPanel.exe' -ErrorAction SilentlyContinue |
               Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $ExePath -or -not (Test-Path $ExePath)) {
    throw "Could not find ConditioningControlPanel.exe. Build first (dotnet build) or pass -ExePath."
}

# --- Resolve tooling ---------------------------------------------------------------------------
$toolsDir     = Join-Path $env:USERPROFILE '.dotnet\tools'
$dotnetStack  = Join-Path $toolsDir 'dotnet-stack.exe'
$dotnetDump   = Join-Path $toolsDir 'dotnet-dump.exe'
if (-not (Test-Path $dotnetStack)) { $dotnetStack = 'dotnet-stack' }
if (-not (Test-Path $dotnetDump))  { $dotnetDump  = 'dotnet-dump' }

# --- Output dir + app log --------------------------------------------------------------------
if (-not $OutDir) { $OutDir = Join-Path $env:LOCALAPPDATA 'ConditioningControlPanel\logs\hang-hunt' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$logDir   = Join-Path $env:LOCALAPPDATA 'ConditioningControlPanel\logs'
$manifest = Join-Path $OutDir 'captures.log'

# --- Win32: probe whether the main window is pumping messages ---------------------------------
if (-not ([System.Management.Automation.PSTypeName]'HangProbe').Type) {
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HangProbe {
    [DllImport("user32.dll", SetLastError=true)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
                                             uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr hWnd);
    const uint WM_NULL = 0x0000;
    const uint SMTO_ABORTIFHUNG = 0x0002;
    // true = window answered the ping; false = it's wedged / not pumping.
    public static bool IsResponsive(IntPtr hWnd, uint timeoutMs) {
        if (hWnd == IntPtr.Zero) return true;            // no window yet (splash/startup) - not a hang
        // Fast path: OS ghost-window flag. Guarded - this API is undocumented and may be absent
        // on some builds; if the entry point is missing, fall through to SendMessageTimeout.
        try { if (IsHungAppWindow(hWnd)) return false; } catch { }
        IntPtr res;
        IntPtr ok = SendMessageTimeout(hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, timeoutMs, out res);
        return ok != IntPtr.Zero;
    }
}
'@
}

function Write-Line($msg, $color = 'Gray') {
    $ts = (Get-Date).ToString('HH:mm:ss')
    Write-Host "[$ts] $msg" -ForegroundColor $color
}

# --- Capture the frozen state of a wedged pid --------------------------------------------------
function Capture-Hang([int]$procId) {
    $stamp = (Get-Date).ToString('yyyyMMdd_HHmmss')
    $stackFile = Join-Path $OutDir "stack_${stamp}_pid${procId}.txt"
    $dumpFile  = Join-Path $OutDir "dump_${stamp}_pid${procId}.dmp"
    $ctxFile   = Join-Path $OutDir "context_${stamp}_pid${procId}.txt"

    Write-Line "HANG on pid $procId - capturing frozen stacks..." 'Yellow'

    # 1) The money shot: every thread's managed stack, as plain text. The diagnostics IPC is served
    #    by a runtime thread, so this answers even though the UI thread is deadlocked.
    try {
        & $dotnetStack report -p $procId *> $stackFile
        Write-Line "  stack  -> $stackFile" 'Green'
    } catch { Write-Line "  dotnet-stack FAILED: $($_.Exception.Message)" 'Red' }

    # 2) Full dump for deep analysis (WinDbg / dotnet-dump analyze).
    if ($DumpType -ne 'None') {
        try {
            & $dotnetDump collect -p $procId --type $DumpType -o $dumpFile *>> $ctxFile
            Write-Line "  dump   -> $dumpFile ($DumpType)" 'Green'
        } catch { Write-Line "  dotnet-dump FAILED: $($_.Exception.Message)" 'Red' }
    }

    # 3) Context: tail of today's app log + the telemetry lines that matter.
    try {
        $appLog = Get-ChildItem -Path $logDir -Filter 'app-*.log' -ErrorAction SilentlyContinue |
                  Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($appLog) {
            "===== last 120 log lines ($($appLog.Name)) =====" | Out-File $ctxFile -Append -Encoding utf8
            Get-Content $appLog.FullName -Tail 120 | Out-File $ctxFile -Append -Encoding utf8
            "`n===== last [CHAOSMEM]/[WATCHDOG]/[STRESS] lines =====" | Out-File $ctxFile -Append -Encoding utf8
            Select-String -Path $appLog.FullName -Pattern '\[CHAOSMEM\]|\[WATCHDOG\]|\[STRESS\]' |
                Select-Object -Last 25 | ForEach-Object { $_.Line } | Out-File $ctxFile -Append -Encoding utf8
        }
        Write-Line "  context-> $ctxFile" 'Green'
    } catch { }

    # 4) Quick triage: show the UI-thread frames right in the console.
    try {
        if (Test-Path $stackFile) {
            $lines = Get-Content $stackFile
            $uiIdx = ($lines | Select-String -Pattern 'Thread.*0x' | Select-Object -First 1)
            Write-Line "  --- top managed frames (see file for all threads) ---" 'Cyan'
            $lines | Where-Object { $_ -match 'ConditioningControlPanel|System\.Windows|MS\.Internal|SkiaSharp|NAudio' } |
                Select-Object -First 12 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkCyan }
        }
    } catch { }

    "$stamp  pid=$procId  stack=$([IO.Path]::GetFileName($stackFile))  dump=$([IO.Path]::GetFileName($dumpFile))" |
        Out-File $manifest -Append -Encoding utf8
}

# --- Main hunt loop ----------------------------------------------------------------------------
Write-Host ""
Write-Host "==================== CCP HANG HUNT ====================" -ForegroundColor Magenta
Write-Line "exe        : $ExePath"
Write-Line "captures   : $OutDir"
Write-Line "stress     : spawn=$SpawnPerTick/tick tick=${TickMs}ms flashEvery=$FlashEvery toggleEvery=$ToggleEvery"
Write-Line "hang after : ${HangSeconds}s unresponsive   dump=$DumpType   maxCaptures=$MaxCaptures"
Write-Host "======================================================" -ForegroundColor Magenta
Write-Host "Leave this running. Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

# Push stress knobs to the child via env (read by App.StartHangStressMode).
$env:CCP_STRESS_SPAWN        = $SpawnPerTick
$env:CCP_STRESS_TICK_MS      = $TickMs
$env:CCP_STRESS_FLASH_EVERY  = $FlashEvery
$env:CCP_STRESS_TOGGLE_EVERY = $ToggleEvery

$captures  = 0
$run       = 0
$probeMs   = 700          # per-ping timeout
$sampleMs  = 500          # gap between pings
$needSamples = [int][math]::Ceiling(($HangSeconds * 1000.0) / $sampleMs)
$huntStart   = Get-Date
$deadline    = if ($MaxRuntimeMinutes -gt 0) { $huntStart.AddMinutes($MaxRuntimeMinutes) } else { $null }
function Past-Deadline { $deadline -ne $null -and (Get-Date) -gt $deadline }

try {
    while ($true) {
        if (Past-Deadline) {
            $mins = [int]((Get-Date) - $huntStart).TotalMinutes
            Write-Line "SURVIVED ${mins}min with $captures capture(s) - no new hang. (ran to MaxRuntimeMinutes)" 'Green'
            break
        }
        $run++
        Write-Line "launch #$run ..." 'White'
        $proc = Start-Process -FilePath $ExePath -ArgumentList '--stress' -PassThru
        Start-Sleep -Seconds 18  # heavy cold start: main window handle only appears ~15-20s in.
                                 # (Handle==0 is treated as alive anyway, so this just keeps logs clean.)

        $unresponsive = 0
        while (-not $proc.HasExited) {
            Start-Sleep -Milliseconds $sampleMs
            if (Past-Deadline) {
                Write-Line "reached MaxRuntimeMinutes with the app alive - stopping this instance" 'DarkGray'
                try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch {}
                break
            }
            try { $proc.Refresh() } catch {}
            if ($proc.HasExited) { break }

            $hwnd = $proc.MainWindowHandle
            $alive = [HangProbe]::IsResponsive($hwnd, [uint32]$probeMs)

            if ($alive) {
                if ($unresponsive -ge 2) { Write-Line "recovered after $unresponsive samples" 'DarkGray' }
                $unresponsive = 0
            }
            else {
                $unresponsive++
                if ($unresponsive -eq 2) { Write-Line "window not answering... ($unresponsive)" 'DarkYellow' }
                if ($unresponsive -ge $needSamples) {
                    Capture-Hang $proc.Id
                    $captures++
                    Write-Line "kill wedged pid $($proc.Id) and relaunch" 'DarkGray'
                    try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch {}
                    # kill the watchdog's dump-writer sibling if it spawned
                    Get-Process -Name 'ConditioningControlPanel' -ErrorAction SilentlyContinue |
                        Where-Object { $_.Id -ne $proc.Id } | Stop-Process -Force -ErrorAction SilentlyContinue
                    break
                }
            }
        }

        if ($proc.HasExited -and $unresponsive -lt $needSamples) {
            $code = try { $proc.ExitCode } catch { 'unknown' }
            if ($code -ne 0) {
                Write-Line "process EXITED code=$code (crash, not a hang) - logging + relaunching" 'Red'
                Capture-Hang $proc.Id 2>$null   # pid's gone; grabs the crash context from the log
            } else {
                Write-Line "process exited cleanly (code 0) - you closed it? relaunching" 'DarkGray'
            }
        }

        if ($MaxCaptures -gt 0 -and $captures -ge $MaxCaptures) {
            Write-Line "reached MaxCaptures=$MaxCaptures - done. Captures in: $OutDir" 'Magenta'
            break
        }
        Start-Sleep -Seconds 2
    }
}
finally {
    Write-Host ""
    Write-Line "HUNT ENDED. $captures capture(s). Open: $OutDir" 'Magenta'
    Get-Process -Name 'ConditioningControlPanel' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
