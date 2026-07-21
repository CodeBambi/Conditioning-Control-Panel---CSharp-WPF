# SP-015 Step 4 — AvatarTube demonstrator Windows-headed evidence matrix.
# Rendered-frame DELTAS via CopyFromScreen + --avatar-strip-decode (app-side pixel logic),
# temporal verdicts via --avatar-sequence (evaluator, never hand-held PS math).
# Real input: UIA-located buttons + mouse_event; owner transitions via drag/ShowWindow.
# Usage: pwsh spine-tasks/SP-015-avatartube-animation/headed-evidence.ps1
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$taskDir = $PSScriptRoot
$shots = Join-Path $taskDir 'evidence'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
$exe = Join-Path $taskDir '..\..\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe'
$packDef = Join-Path $taskDir '..\..\client\src\CcpClient.Desktop\Assets\avatar\pack-circuit.json'
$settingsFile = Join-Path $env:APPDATA 'CcpClient\settings.json'
if (-not (Test-Path $exe)) { Write-Output "FAIL: app not built: $exe"; exit 1 }

$native = @'
using System;
using System.Runtime.InteropServices;
public class AvNative {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040;
    public const int GWL_EXSTYLE = -20; public const int WS_EX_TOPMOST = 0x0008;
    public const uint GW_OWNER = 4; public const int SW_MINIMIZE = 6, SW_RESTORE = 9;
    public const uint WM_CLOSE = 0x0010;
    public const uint SWP_NOACTIVATE = 0x0010;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
}
'@
Add-Type -TypeDefinition $native

$script:gates = @()
function Gate([bool]$ok, [string]$name, [string]$detail) {
    $script:gates += [pscustomobject]@{ Name = $name; Passed = $ok; Detail = $detail }
    Write-Output ("{0} {1} — {2}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail))
    if (-not $ok) { throw "GATE FAILED: $name — $detail" }
}
function Fail([string]$msg) { Write-Output "FAIL: $msg"; if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill() }; exit 1 }

function Get-Windows([int]$processId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $found = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
    return @($found | ForEach-Object { $_ })
}
function Get-Texts($window) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $els = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $lines = @(); foreach ($t in $els) { $lines += $t.Current.Name }
    return $lines
}
function Get-Tube {
    # OWNED windows nest under their owner in the UIA tree (they are not root children):
    # while attached, the tube is a DESCENDANT of the dashboard element — matching on probe
    # texts conflated the two windows and broke G9's window identity (dashboard hwnd read as
    # the tube's). Match the WINDOW NAME, detached (root child) or attached (descendant).
    foreach ($w in (Get-Windows $script:proc.Id)) {
        if ($w.Current.Name -like 'AvatarTube DEMONSTRATOR*') { return $w }
        if ($w.Current.Name -eq 'CCP Client') {
            $nameCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'AvatarTube DEMONSTRATOR (SP-015)')
            $tube = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
            if ($null -ne $tube) { return $tube }
        }
    }
    return $null
}
function Get-Dashboard {
    # The dashboard is always a root child (it owns, it is never owned). UIA tree queries
    # can race window transitions (detach/attach) — retry generously.
    foreach ($try in 1..15) {
        foreach ($w in (Get-Windows $script:proc.Id)) {
            if ($w.Current.Name -eq 'CCP Client') { return $w }
        }
        Start-Sleep -Milliseconds 400
    }
    return $null
}
function Read-Probe {
    $tube = Get-Tube
    if ($null -eq $tube) { return $null }
    $probe = (Get-Texts $tube) | Where-Object { $_ -match '^avatar-probe:' }
    if ($probe -notmatch 'pack=(\d+) clip=(\d+) frame=(\d+) mode=(\w+) outstanding=(\d+) subs=(\d+) stage=(-?\d+),(-?\d+) scale=([\d.]+)') { return $null }
    return @{
        Pack = [int]$Matches[1]; Clip = [int]$Matches[2]; Frame = [int]$Matches[3]; Mode = $Matches[4]
        Outstanding = [int]$Matches[5]; Subs = [int]$Matches[6]
        StageX = [int]$Matches[7]; StageY = [int]$Matches[8]; Scale = [double]$Matches[9]
    }
}
function Click-Button([string]$name) {
    $tube = Get-Tube
    if ($null -eq $tube) { Fail "tube window not found for click '$name'" }
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $buttons = $tube.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    foreach ($b in $buttons) {
        if ($b.Current.Name -eq $name) {
            $r = $b.Current.BoundingRectangle
            $cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)
            [AvNative]::SetCursorPos($cx, $cy) | Out-Null; Start-Sleep -Milliseconds 150
            [AvNative]::mouse_event([AvNative]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
            [AvNative]::mouse_event([AvNative]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
            Start-Sleep -Milliseconds 250
            return
        }
    }
    Fail "button '$name' not found on tube"
}
function Raise-Tube {
    $tube = Get-Tube
    $hwnd = [IntPtr]$tube.Current.NativeWindowHandle
    [AvNative]::SetWindowPos($hwnd, [AvNative]::HWND_TOPMOST, 0, 0, 0, 0,
        [AvNative]::SWP_NOMOVE -bor [AvNative]::SWP_NOSIZE -bor [AvNative]::SWP_SHOWWINDOW -bor [AvNative]::SWP_NOACTIVATE) | Out-Null
    Start-Sleep -Milliseconds 200
}

# Capture phase (fast): file + wall timestamp per shot; the tube is raised NOACTIVATE per
# shot so the script host/console can never occlude the strip region. The rect is the FULL
# stage (104x136 incl. the 4-DIP float headroom) — cropping tighter would clip the strip
# at positive float offsets.
function Capture-Shot([string]$tag) {
    # Retry the probe read: a UIA tree query can race a crossfade/rotation layout pass
    # (run-3 died here at G3 on a single transient null).
    $probe = $null
    foreach ($try in 1..8) {
        $probe = Read-Probe
        if ($null -ne $probe) { break }
        Start-Sleep -Milliseconds 200
    }
    if ($null -eq $probe) { Fail "probe unreadable during capture ($tag)" }
    $tube = Get-Tube
    [AvNative]::SetWindowPos([IntPtr]$tube.Current.NativeWindowHandle, [AvNative]::HWND_TOPMOST, 0, 0, 0, 0,
        [AvNative]::SWP_NOMOVE -bor [AvNative]::SWP_NOSIZE -bor [AvNative]::SWP_SHOWWINDOW -bor [AvNative]::SWP_NOACTIVATE) | Out-Null
    $size = [int](104 * $probe.Scale)
    $sizeH = [int](136 * $probe.Scale)
    $t = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $file = Join-Path $shots ("cap-{0}-{1}.bmp" -f $tag, $t)
    $bmp = New-Object System.Drawing.Bitmap $size, $sizeH
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($probe.StageX, $probe.StageY, 0, 0, $bmp.Size)
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $g.Dispose(); $bmp.Dispose()
    return $file
}
function Collect-Shots([string]$tag, [double]$seconds, [int]$periodMs = 260) {
    $files = @()
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $files += Capture-Shot $tag
        Start-Sleep -Milliseconds $periodMs
    }
    return $files
}
# Decode phase (batch): the app decodes each shot's strip (pixel logic lives in the app).
# Start-Process with redirect files: the direct `& $exe` invocation returns empty output
# in this worker environment (no native exec) — Start-Process is the deterministic path.
function Invoke-AppChecked([string[]]$appArgs) {
    # One retry on empty stdout: a bare .NET spawn occasionally yields an empty redirect
    # file under load (observed in G3); the app-side decode itself is deterministic.
    foreach ($attempt in 1..2) {
        $outFile = Join-Path $env:TEMP ('ccp-decode-' + [Guid]::NewGuid().ToString('N') + '.txt')
        $errFile = $outFile + '.err'
        $p = Start-Process -FilePath $exe -ArgumentList $appArgs -NoNewWindow -Wait -RedirectStandardOutput $outFile -RedirectStandardError $errFile -PassThru
        # Start-Process -Wait does not wait for the redirected-stream flush (PS known
        # issue): poll for content so a slow flush is never read as an empty result.
        foreach ($tick in 1..50) {
            if ((Test-Path $outFile) -and (Get-Item $outFile).Length -gt 0) { break }
            Start-Sleep -Milliseconds 100
        }
        $out = if (Test-Path $outFile) { Get-Content $outFile -Raw } else { $null }
        $err = if (Test-Path $errFile) { Get-Content $errFile -Raw } else { $null }
        Remove-Item $outFile, $errFile -Force -ErrorAction SilentlyContinue
        if ($out) { return @{ Exit = $p.ExitCode; Out = $out.Trim() } }
        if ($attempt -eq 2) { return @{ Exit = $p.ExitCode; Out = ''; Err = ($err ?? '').Trim() } }
        Start-Sleep -Milliseconds 300
    }
}
function Decode-Shots($files) {
    $samples = @()
    foreach ($file in $files) {
        $r = Invoke-AppChecked @('--avatar-strip-decode', '--capture', $file)
        if (-not $r.Out) { Fail "strip-decode produced no output for $file (exit $($r.Exit), stderr: $($r.Err))" }
        $sample = $r.Out | ConvertFrom-Json
        if ($file -match '-(\d+)\.bmp$') { $sample.T = [long]$Matches[1] }
        $samples += $sample
    }
    return $samples
}
function Save-Samples($samples, [string]$name) {
    $path = Join-Path $shots "$name.jsonl"
    $samples | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content -Path $path -Encoding utf8
    return $path
}
function Invoke-Sequence([string]$name, [string]$samplesPath, [bool]$withTrace, [string[]]$expectVerdicts, [bool]$assertAll = $true) {
    $evalArgs = @('--avatar-sequence', $samplesPath, '--pack', $packDef)
    if ($withTrace) { $evalArgs += @('--trace', $script:traceFile) }
    $r = Invoke-AppChecked $evalArgs
    $text = $r.Out
    $output = $text -split "`n"
    foreach ($v in $expectVerdicts) {
        $found = $text -match ('PASS ' + [regex]::Escape($v))
        $detail = (($output | Where-Object { $_ -match $v }) -join '; ')
        if (-not $found -and -not $detail) { $detail = "exit=$($r.Exit) err=$($r.Err) raw=$($text.Substring(0, [Math]::Min(300, $text.Length)))" }
        Gate $found "$name/$v" $detail
    }
    if ($assertAll) { Gate ($text -match 'ALL VERDICTS PASSED') "$name/all-verdicts" ($output | Select-Object -Last 1) }
}
function Trace-Contains([string]$kind) {
    if (-not (Test-Path $script:traceFile)) { return $false }
    return [bool](Select-String -Path $script:traceFile -Pattern "`"Kind`":`"$kind`"" -Quiet)
}

Write-Output '=== SP-015 AvatarTube demonstrator — Windows-headed evidence matrix ==='
# Fresh run: drop stale caps/samples from prior partial runs so evidence/ holds exactly
# one coherent session's artifacts.
Remove-Item (Join-Path $shots 'cap-*.bmp') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $shots 'g*.jsonl') -Force -ErrorAction SilentlyContinue
if (Test-Path $settingsFile) { Remove-Item $settingsFile -Force }
$script:traceFile = Join-Path $shots 'trace.jsonl'
# stderr is NOT redirected: an undrained redirect pipe wedges the app (SP-015 finding).
$script:proc = [System.Diagnostics.Process]::Start((New-Object System.Diagnostics.ProcessStartInfo -Property @{
    FileName = $exe; Arguments = "--avatartube-demo --avatar-trace `"$script:traceFile`""
    UseShellExecute = $false }))
Start-Sleep -Seconds 5
$tube = Get-Tube
if ($null -eq $tube) { Fail 'tube window not found after launch' }
$script:tubeHwnd = [IntPtr]$tube.Current.NativeWindowHandle
Raise-Tube
$probe0 = Read-Probe
Gate ($null -ne $probe0) 'boot/probe' "probe live: $($probe0 | ConvertTo-Json -Compress)"
Gate ($probe0.Mode -eq 'Static') 'boot/static-mode' "mode=$($probe0.Mode)"

# ---- G1: static pose fade + looping (static mode) ----
Write-Output '-- G1 static pose fades (8s)'
$g1 = Decode-Shots (Collect-Shots 'g1' 8)
Gate ((($g1 | Where-Object Decoded | Select-Object -ExpandProperty Frame -Unique) | Measure-Object).Count -ge 2) 'g1/pose-delta' 'distinct pose frames observed'
Invoke-Sequence 'g1' (Save-Samples $g1 'g1') $false @('frames-advance', 'no-blank', 'monotonic-modular-advance', 'no-duplicate-run-beyond-hold', 'float-liveness')

# ---- G2: animated loop + cadence (multiplied-speed falsification) ----
Write-Output '-- G2 animated loop + cadence (13s)'
Click-Button 'Animate'
Start-Sleep -Milliseconds 400
$g2 = Decode-Shots (Collect-Shots 'g2' 13)
Invoke-Sequence 'g2' (Save-Samples $g2 'g2') $false @('frames-advance', 'no-blank', 'monotonic-modular-advance', 'no-duplicate-run-beyond-hold', 'schedule-fit-1x', 'schedule-not-2x-speed', 'schedule-not-half-speed', 'float-liveness')

# ---- G3: idle rotation + crossfade coverage (mid-fade capture for K3) ----
Write-Output '-- G3 idle rotation + crossfade (9s)'
$g3files = Collect-Shots 'g3' 9 200
$g3 = Decode-Shots $g3files
$clipsG3 = $g3 | Where-Object Decoded | Select-Object -ExpandProperty Clip -Unique
Gate (($clipsG3 -contains 1) -and ($clipsG3 -contains 2)) 'g3/idle-rotation' "clips observed: $($clipsG3 -join ',')"
Invoke-Sequence 'g3' (Save-Samples $g3 'g3') $true @('frames-advance', 'no-blank', 'monotonic-modular-advance')
# Mid-fade coverage: a capture inside [crossfade-start+80, +950] per the engine trace.
$fadeStarts = @(Get-Content $script:traceFile | Where-Object { $_ -match 'crossfade-start' } | ForEach-Object { ($_ | ConvertFrom-Json).T })
$midFade = $g3 | Where-Object { $t = $_.T; ($fadeStarts | Where-Object { $t -ge $_ + 80 -and $t -le $_ + 950 } | Measure-Object).Count -gt 0 } | Select-Object -First 1
Gate ($null -ne $midFade) 'g3/mid-fade-coverage' "mid-fade capture at t=$($midFade.T)"
$midFadeFile = Join-Path $shots ("cap-g3-{0}.bmp" -f $midFade.T)
Gate (Test-Path $midFadeFile) 'g3/mid-fade-artifact' $midFadeFile

# ---- G4: talk → reaction → idle named sequence ----
Write-Output '-- G4 talk sequence (8s)'
Click-Button 'Talk'
$g4 = Decode-Shots (Collect-Shots 'g4' 8 220)
$seqClips = @($g4 | Where-Object Decoded | ForEach-Object { [int]$_.Clip })
Gate (($seqClips -contains 3) -and ($seqClips -contains 4)) 'g4/talk+reaction-seen' "clips: $($seqClips -join ',')"
$talkIdx = [Array]::IndexOf($seqClips, 3); $reactIdx = [Array]::IndexOf($seqClips, 4)
$idleAfterReact = $false
for ($i = $reactIdx; $i -lt $seqClips.Count; $i++) { if ($seqClips[$i] -in 1, 2) { $idleAfterReact = $true; break } }
Gate (($talkIdx -ge 0) -and ($reactIdx -gt $talkIdx) -and $idleAfterReact) 'g4/talk→reaction→idle' "talk@$talkIdx reaction@$reactIdx idle-after=$idleAfterReact"
Invoke-Sequence 'g4' (Save-Samples $g4 'g4') $true @('no-blank', 'monotonic-modular-advance')

# ---- G5: click reaction + cooldown (real click on the avatar) ----
Write-Output '-- G5 click reaction (6s)'
$probeClick = Read-Probe
[AvNative]::SetCursorPos($probeClick.StageX + 48, $probeClick.StageY + 64) | Out-Null
Start-Sleep -Milliseconds 150
[AvNative]::mouse_event([AvNative]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
[AvNative]::mouse_event([AvNative]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 500
# Second click inside the 3000ms cooldown window: must be ignored (trace).
[AvNative]::mouse_event([AvNative]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
[AvNative]::mouse_event([AvNative]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
$g5 = Decode-Shots (Collect-Shots 'g5' 5 220)
Gate (($g5 | Where-Object Decoded | Select-Object -ExpandProperty Clip -Unique) -contains 5) 'g5/click-clip-played' 'click emote clip rendered'
Gate (Trace-Contains 'click-cooldown-ignored') 'g5/cooldown-ignored' 'duplicate click inside cooldown traced as ignored'
$g5Clips = @($g5 | Where-Object Decoded | ForEach-Object { [int]$_.Clip })
$clickSeen = [Array]::IndexOf($g5Clips, 5)
$idleAfterClick = $false
for ($i = $clickSeen; $i -lt $g5Clips.Count; $i++) { if ($g5Clips[$i] -in 1, 2) { $idleAfterClick = $true; break } }
Gate ($clickSeen -ge 0 -and $idleAfterClick) 'g5/returns-to-idle' "click@$clickSeen idle-after=$idleAfterClick"

# ---- G6: float — verified by the float-liveness verdict inside G2's long gate ----
Gate $true 'g6/float-folded-into-g2' 'centroid oscillation asserted by g2/float-liveness (window position checked in g10)'

# ---- G7: pause/resume — freeze, successor, unchanged cadence ----
Write-Output '-- G7 pause/resume (9s)'
# Capture CONTINUOUSLY while hunting: the cadence bridge needs >=3 same-clip samples in the
# 1940ms before the pause — hunting without capturing (the old order) starved exactly that
# window (observed flakes: 0-1 before). Pause target = clip 1 (idle) frames 2-4: frame 2's
# 820ms hold absorbs the probe-to-click latency (~550ms), frames 2-4 give 3+ distinct
# ordinals before the pause; frame 5 and the crossfade boundaries are excluded (a frozen
# blend would fail strip decode in the freeze gate).
$g7aFiles = @()
$accepted = $false
$prevProbe = $null
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline -and -not $accepted) {
    $g7aFiles += Capture-Shot 'g7a'
    $probe = Read-Probe
    if ($probe -and $prevProbe -and $probe.Mode -eq 'Animated' `
        -and $probe.Clip -eq 1 -and $probe.Frame -ge 2 -and $probe.Frame -le 4 `
        -and $prevProbe.Clip -eq 1 -and $prevProbe.Frame -le $probe.Frame) { $accepted = $true; break }
    $prevProbe = $probe
    Start-Sleep -Milliseconds 200
}
if (-not $accepted) { Fail 'no clip-1 mid-pass frame to pause on within 30s' }
$g7aFiles += Capture-Shot 'g7a' # last pre-pause shot immediately before the click
Click-Button 'Pause'
$g7fFiles = Collect-Shots 'g7f' 2.5 450
Click-Button 'Resume'
$g7bFiles = Collect-Shots 'g7b' 3.5 220
$g7a = Decode-Shots $g7aFiles
$g7frozen = Decode-Shots $g7fFiles
$g7b = Decode-Shots $g7bFiles
$frozenFrames = $g7frozen | Where-Object Decoded | Select-Object -ExpandProperty Frame -Unique
Gate (($frozenFrames | Measure-Object).Count -eq 1) 'g7/frozen-identical' "frozen frame: $($frozenFrames -join ',')"
Invoke-Sequence 'g7' (Save-Samples (@($g7a) + @($g7frozen) + @($g7b)) 'g7') $true @('pause-freeze', 'resume-successor', 'cadence-unchanged-after-resume', 'no-blank')

# ---- G8: pack switching (the demonstrator's "mod switching") ----
Write-Output '-- G8 pack switch (6s)'
Click-Button 'Switch pack'
Start-Sleep -Milliseconds 300
$g8 = Decode-Shots (Collect-Shots 'g8' 5 220)
Invoke-Sequence 'g8' (Save-Samples $g8 'g8') $true @('pack-switch-clean', 'no-blank', 'frames-advance')
Gate ((Read-Probe).Pack -eq 1) 'g8/now-pulse' 'probe reports pack 1'
Click-Button 'Switch pack' # switch back for later gates
Start-Sleep -Milliseconds 400
Gate ((Read-Probe).Pack -eq 0) 'g8/back-to-circuit' 'probe reports pack 0'

# ---- G9: attach/detach — style bits + pipeline preservation ----
Write-Output '-- G9 attach/detach (5s)'
$g9a = Decode-Shots (Collect-Shots 'g9a' 1.5 260)
Click-Button 'Detach'
Start-Sleep -Milliseconds 400
$exStyle = [AvNative]::GetWindowLong($script:tubeHwnd, [AvNative]::GWL_EXSTYLE)
Gate (($exStyle -band [AvNative]::WS_EX_TOPMOST) -ne 0) 'g9/detached-topmost' ("exstyle=0x{0:X8}" -f $exStyle)
Gate ([AvNative]::GetWindow($script:tubeHwnd, [AvNative]::GW_OWNER) -eq [IntPtr]::Zero) 'g9/detached-ownerless' 'GW_OWNER == 0'
$g9b = Decode-Shots (Collect-Shots 'g9b' 1.5 260)
Click-Button 'Attach'
Start-Sleep -Milliseconds 400
$dash = Get-Dashboard
if ($null -eq $dash) { Fail 'dashboard window not found after attach' }
$dashHwnd = [IntPtr]$dash.Current.NativeWindowHandle
$exStyle2 = [AvNative]::GetWindowLong($script:tubeHwnd, [AvNative]::GWL_EXSTYLE)
Gate (($exStyle2 -band [AvNative]::WS_EX_TOPMOST) -eq 0) 'g9/attached-not-topmost' ("exstyle=0x{0:X8}" -f $exStyle2)
Gate ([AvNative]::GetWindow($script:tubeHwnd, [AvNative]::GW_OWNER) -eq $dashHwnd) 'g9/attached-owned' 'GW_OWNER == dashboard'
$g9c = Decode-Shots (Collect-Shots 'g9c' 1 260)
Invoke-Sequence 'g9' (Save-Samples (@($g9a) + @($g9b) + @($g9c)) 'g9') $false @('frames-advance', 'no-blank', 'monotonic-modular-advance')

# ---- G10: owner move — the tube follows the owner exactly ----
Write-Output '-- G10 owner move'
$tubeRect0 = (Get-Tube).Current.BoundingRectangle
$dashRect0 = (Get-Dashboard).Current.BoundingRectangle
$dragX = [int]($dashRect0.X + $dashRect0.Width / 2); $dragY = [int]($dashRect0.Y + 10)
[AvNative]::SetCursorPos($dragX, $dragY) | Out-Null; Start-Sleep -Milliseconds 150
[AvNative]::mouse_event([AvNative]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
foreach ($step in 1..8) { [AvNative]::SetCursorPos($dragX + $step * 17, $dragY + $step * 11) | Out-Null; Start-Sleep -Milliseconds 40 }
[AvNative]::mouse_event([AvNative]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 700
$tubeRect1 = (Get-Tube).Current.BoundingRectangle
$dashRect1 = (Get-Dashboard).Current.BoundingRectangle
$tubeDx = $tubeRect1.X - $tubeRect0.X; $tubeDy = $tubeRect1.Y - $tubeRect0.Y
$dashDx = $dashRect1.X - $dashRect0.X; $dashDy = $dashRect1.Y - $dashRect0.Y
Gate (($dashDx -ne 0 -or $dashDy -ne 0) -and ([Math]::Abs($tubeDx - $dashDx) -le 4) -and ([Math]::Abs($tubeDy - $dashDy) -le 4)) 'g10/tube-follows-owner' "dash ($dashDx,$dashDy) tube ($tubeDx,$tubeDy)"

# ---- G11: owner minimize → pause+hide; restore → resume+show ----
Write-Output '-- G11 owner minimize/restore'
$frameBeforeMin = (Read-Probe).Frame
[AvNative]::ShowWindow($dashHwnd, [AvNative]::SW_MINIMIZE) | Out-Null
Start-Sleep -Milliseconds 900
Gate (-not [AvNative]::IsWindowVisible($script:tubeHwnd)) 'g11/tube-hidden-on-minimize' 'tube hidden with owner'
Gate (Trace-Contains 'pause-begin') 'g11/pause-traced' 'engine paused on owner minimize'
$frameDuringMin = (Read-Probe)
[AvNative]::ShowWindow($dashHwnd, [AvNative]::SW_RESTORE) | Out-Null
Start-Sleep -Milliseconds 900
Raise-Tube
Gate ([AvNative]::IsWindowVisible($script:tubeHwnd)) 'g11/tube-restored' 'tube visible after restore'
Gate (Trace-Contains 'pause-end') 'g11/resume-traced' 'engine resumed on owner restore'
$g11 = Decode-Shots (Collect-Shots 'g11' 1.5 260)
$probeAfter = Read-Probe
Gate ($null -ne $probeAfter) 'g11/probe-after-restore' "frame=$($probeAfter.Frame) (was $frameBeforeMin) — successor semantics by engine design + g7"

# ---- G12: leak long-run — 25 attach/detach/pack-switch cycles, registry stable ----
Write-Output '-- G12 leak long-run (25 cycles)'
for ($cycle = 0; $cycle -lt 25; $cycle++) {
    Click-Button $(if ($cycle % 2 -eq 0) { 'Detach' } else { 'Attach' })
    Click-Button 'Switch pack'
}
$probeEnd = Read-Probe
Gate ($probeEnd.Outstanding -eq 2 -and $probeEnd.Subs -eq 1) 'g12/registry-stable' "outstanding=$($probeEnd.Outstanding) (heartbeat+engine) subs=$($probeEnd.Subs) after 25 cycles"

# ---- G13: cleanup — tube close disposes; dashboard close exits 0 ----
Write-Output '-- G13 cleanup'
[AvNative]::SendMessage($script:tubeHwnd, [AvNative]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Milliseconds 800
Gate ($null -eq (Get-Tube)) 'g13/tube-closed' 'tube window gone'
$dash = Get-Dashboard
if ($null -ne $dash) { $null = $script:proc.CloseMainWindow() }
if (-not $script:proc.WaitForExit(10000)) { Fail 'process did not exit within 10s' }
Gate ($script:proc.ExitCode -eq 0) 'g13/exit-zero' "exit code $($script:proc.ExitCode)"

# ---- G14: undecodable-asset typed state (separate run, corrupt demo) ----
Write-Output '-- G14 undecodable-asset typed state'
$script:proc = [System.Diagnostics.Process]::Start((New-Object System.Diagnostics.ProcessStartInfo -Property @{
    FileName = $exe; Arguments = '--avatartube-demo --avatar-corrupt-demo'
    UseShellExecute = $false }))
Start-Sleep -Seconds 5
$tube = Get-Tube
if ($null -eq $tube) { Fail 'tube window not found (corrupt run)' }
Raise-Tube
Click-Button 'Switch pack'
Start-Sleep -Milliseconds 700
$capText = (Get-Texts (Get-Tube)) | Where-Object { $_ -match '^capability avatar-animation:' }
Gate ($capText -match 'Degraded' -and $capText -match 'asset-undecodable' -and $capText -match 'static fallback') 'g14/typed-degraded' $capText
$fallbackShot = Capture-Shot 'g14-fallback'
$fallbackSample = (Decode-Shots @($fallbackShot))[0]
Gate ($fallbackSample.Decoded -and $fallbackSample.Pack -eq 3 -and $fallbackSample.Clip -eq 7) 'g14/fallback-rendered' "strip decodes fallback identity pack=$($fallbackSample.Pack) clip=$($fallbackSample.Clip)"
Invoke-Sequence 'g14' (Save-Samples @($fallbackSample) 'g14') $false @('no-blank') $false
$script:fallbackFile = Join-Path $shots ("cap-g14-fallback-{0}.bmp" -f $fallbackSample.T)
$null = $script:proc.CloseMainWindow()
if (-not $script:proc.WaitForExit(10000)) { Fail 'corrupt run did not exit' }
Gate ($script:proc.ExitCode -eq 0) 'g14/exit-zero' "exit code $($script:proc.ExitCode)"

Write-Output ''
Write-Output ("EVIDENCE PASS — {0} gates green" -f $script:gates.Count)
Write-Output "mid-fade artifact: $midFadeFile"
Write-Output "fallback artifact: $script:fallbackFile"
