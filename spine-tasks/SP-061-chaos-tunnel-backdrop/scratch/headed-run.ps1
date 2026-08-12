# SP-061 headed layering harness v2 (Windows). Lessons from v1 baked in:
#  - a bottom-sunk window sits below ALL non-topmost windows incl. other apps', so a raw
#    screen capture shows the user's desktop, not the tunnel. Tunnel-content proof is
#    PrintWindow(PW_RENDERFULLCONTENT) on the tunnel's own hwnd (probe: 1801 distinct
#    colors = the live three.js render). The screen capture answers "what is on TOP".
#  - the reverse case (tunnel visible with nothing above) needs a momentarily window-free
#    desktop: the harness minimizes other processes' visible windows (loud, restored after)
#    for phases A and D.
#  - z-walk cap raised 64 -> 512 (the topmost band sits above the whole non-topmost band).
#  - process exit code via System.Diagnostics.Process (Start-Process loses it on PS 5.1).
#  - DebugLogSink writes to STDERR; both streams fold into the polled log via events.
# Timed waits are HARNESS pacing (the timing guard covers client/tests/** only), all bounded.
$ErrorActionPreference = 'Stop'
$taskDir = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $taskDir '..\..\..')).Path
$evidence = Join-Path $taskDir 'evidence\wh'
$sandbox = Join-Path $evidence 'sandbox-root'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$exe = Join-Path $repo 'client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe'
$logFile = Join-Path $evidence 'run.log'
$transcript = Join-Path $evidence 'harness-transcript.txt'
$realProfile = Join-Path $env:APPDATA 'CcpClient'

function Say([string]$m) { $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss.fff'), $m; Write-Output $line; Add-Content -Path $transcript -Value $line -Encoding UTF8 }
function Fail([string]$m) { Say "FAIL: $m"; if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill() }; exit 1 }

foreach ($f in @($transcript, $logFile)) { if (Test-Path $f) { Remove-Item $f } }
if (Test-Path $sandbox) { Remove-Item $sandbox -Recurse -Force }

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$native = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public class TN {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lparam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int idx);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public struct RECT { public int Left, Top, Right, Bottom; }
    public static string TitleOf(IntPtr h) { var sb = new StringBuilder(512); GetWindowTextW(h, sb, 512); return sb.ToString(); }
    public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassNameW(h, sb, 256); return sb.ToString(); }
    public static string RectOf(IntPtr h) { RECT r; if (!GetWindowRect(h, out r)) return "(no rect)"; return string.Format("({0},{1})-({2},{3}) [{4}x{5}]", r.Left, r.Top, r.Right, r.Bottom, r.Right - r.Left, r.Bottom - r.Top); }
    public static IntPtr FindByTitle(uint pid, string frag) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint q; GetWindowThreadProcessId(h, out q);
            if (q == pid && IsWindowVisible(h) && TitleOf(h).Contains(frag)) found = h;
            return true;
        }, IntPtr.Zero);
        return found;
    }
    // Other processes' visible, titled, non-owned top-level windows (the desktop-polluting set).
    // Shell surfaces (Progman/WorkerW/Shell_TrayWnd) are NEVER touched.
    public static List<IntPtr> ForeignWindows(uint ownPid) {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint q; GetWindowThreadProcessId(h, out q);
            if (q != ownPid && IsWindowVisible(h)) {
                var cls = ClassOf(h);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return true;
                if (GetWindowLongPtr(h, -8) != IntPtr.Zero) return true; // GW_OWNER: owned tool windows left alone
                if (TitleOf(h).Length == 0) return true;
                list.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
'@
Add-Type -TypeDefinition $native -ReferencedAssemblies System.Drawing, System.Windows.Forms
# v2 lesson: the harness MUST run DPI-aware, or GetWindowRect (virtualized for a DPI-unaware
# caller) and CopyFromScreen (physical) disagree - run-1's mixed-space black patches.
# Called before ANY Forms/Drawing use; afterwards every coordinate below is physical pixels.
[TN]::SetProcessDPIAware() | Out-Null

function Wait-Log([string]$marker, [int]$timeoutSec, [int]$occurrence = 1) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $logFile) {
            $hits = @(Select-String -Path $logFile -Pattern ([regex]::Escape($marker)) -SimpleMatch:$false)
            if ($hits.Count -ge $occurrence) { Say "marker seen ($occurrence x): $marker"; return }
        }
        if ($script:proc -and $script:proc.HasExited) { Fail "app exited (code $($script:proc.ExitCode)) while waiting for marker: $marker" }
        Start-Sleep -Milliseconds 200
    }
    Fail "marker never appeared in ${timeoutSec}s: $marker"
}

$script:minimized = @()
function Minimize-Others([uint32]$ownPid) {
    $script:minimized = @()
    foreach ($h in [TN]::ForeignWindows($ownPid)) {
        $script:minimized += $h
        [TN]::ShowWindow($h, 6) | Out-Null  # SW_MINIMIZE
        Say ("minimized (harness, restored later): '" + [TN]::TitleOf($h) + "'")
    }
}
function Restore-Others() {
    foreach ($h in $script:minimized) { [TN]::ShowWindow($h, 9) | Out-Null }  # SW_RESTORE
    if ($script:minimized.Count -gt 0) { Say ("restored " + $script:minimized.Count + " foreign window(s)") }
    $script:minimized = @()
}

function Print-WindowShot([IntPtr]$hwnd, [string]$name) {
    $r = New-Object TN+RECT
    [TN]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    $ok = [TN]::PrintWindow($hwnd, $hdc, 2)  # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($hdc); $g.Dispose()
    $colors = @{}
    $luma = 0.0; $n = 0
    for ($x = 0; $x -lt $w; $x += 7) { for ($y = 0; $y -lt $h; $y += 7) {
        $p = $bmp.GetPixel($x, $y); $colors[$p.ToArgb()] = $true
        $luma += (0.299 * $p.R + 0.587 * $p.G + 0.114 * $p.B); $n++
    } }
    $png = Join-Path $evidence "$name.png"
    $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Say ("PrintWindow($name) ok=$ok distinct=$($colors.Count) meanLuma=" + [math]::Round($luma / [math]::Max($n, 1), 1) + " -> $png")
}

function Capture-Phase([string]$name, [uint32]$pid2) {
    Say "=== capture $name ==="
    $fg = [TN]::GetForegroundWindow()
    Say ("foreground: '" + [TN]::TitleOf($fg) + "' hwnd=" + $fg)
    $tunnel = [TN]::FindByTitle($pid2, 'Chaos Tunnel')
    $video = [TN]::FindByTitle($pid2, 'DTRH video')
    $dash = [TN]::FindByTitle($pid2, 'CCP Client')
    Say ("tunnel rect: " + $(if ($tunnel -ne [IntPtr]::Zero) { [TN]::RectOf($tunnel) } else { '(absent)' }))
    Say ("video  rect: " + $(if ($video -ne [IntPtr]::Zero) { [TN]::RectOf($video) } else { '(absent)' }))
    Say ("dash   rect: " + $(if ($dash -ne [IntPtr]::Zero) { [TN]::RectOf($dash) } else { '(absent)' }))
    if ($tunnel -ne [IntPtr]::Zero) {
        # z-walk upward from the tunnel (GW_HWNDPREV=3), 512-bound (the WPF guard's bound)
        $aboveVideo = $false; $aboveDash = $false; $count = 0
        $h = $tunnel
        for ($i = 0; $i -lt 512; $i++) {
            $h = [TN]::GetWindow($h, 3)
            if ($h -eq [IntPtr]::Zero) { break }
            $count++
            if ($video -ne [IntPtr]::Zero -and $h -eq $video) { $aboveVideo = $true }
            if ($dash -ne [IntPtr]::Zero -and $h -eq $dash) { $aboveDash = $true }
        }
        Say ("z-walk from tunnel: walked=$count video-above-tunnel=$aboveVideo dashboard-above-tunnel=$aboveDash")
    }
    if ($tunnel -ne [IntPtr]::Zero) { Print-WindowShot $tunnel "$name-tunnel" }
    # screen capture (what is ON TOP, per region)
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bmp.Size)
    $g.Dispose()
    $png = Join-Path $evidence "$name-screen.png"
    $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    function Patch-Stats([int]$cx, [int]$cy) {
        $colors = @{}; $luma = 0.0; $n = 0
        for ($x = $cx - 60; $x -lt $cx + 60; $x += 3) { for ($y = $cy - 60; $y -lt $cy + 60; $y += 3) {
            if ($x -lt 0 -or $y -lt 0 -or $x -ge $bmp.Width -or $y -ge $bmp.Height) { continue }
            $p = $bmp.GetPixel($x, $y); $colors[$p.ToArgb()] = $true
            $luma += (0.299 * $p.R + 0.587 * $p.G + 0.114 * $p.B); $n++
        } }
        return "distinct=$($colors.Count) meanLuma=" + [math]::Round($luma / [math]::Max($n, 1), 1)
    }
    if ($video -ne [IntPtr]::Zero) {
        $r = New-Object TN+RECT
        [TN]::GetWindowRect($video, [ref]$r) | Out-Null
        $ix = [int](($r.Left + $r.Right) / 2); $iy = [int](($r.Top + $r.Bottom) / 2) + 40
        Say ("screen patch INSIDE-video @($ix,$iy): " + (Patch-Stats $ix $iy))
        $ox = [math]::Min($bounds.Width - 120, [math]::Max(120, $r.Right + 150))
        $oy = [math]::Min($bounds.Height - 120, [math]::Max(120, $r.Bottom + 100))
        Say ("screen patch OUTSIDE-video @($ox,$oy): " + (Patch-Stats $ox $oy))
    } else {
        $ix = [int]($bounds.Width / 2); $iy = [int]($bounds.Height / 2)
        Say ("screen patch CENTER @($ix,$iy): " + (Patch-Stats $ix $iy))
        Say ("screen patch CORNER @(1500,900): " + (Patch-Stats 1500 900))
    }
    Say ("screen capture saved: $png ({0} bytes)" -f (Get-Item $png).Length)
    $bmp.Dispose()
}

function Profile-Manifest([string]$root, [string]$out) {
    $lines = @()
    if (Test-Path $root) {
        Get-ChildItem -Recurse -File $root | ForEach-Object {
            $rel = $_.FullName.Substring($root.Length + 1)
            $relHash = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($rel))).Replace('-', '').ToLower()
            $bytes = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($_.FullName))).Replace('-', '').ToLower()
            $lines += "$relHash $bytes"
        }
    }
    $lines | Sort-Object | Set-Content -Path $out -Encoding UTF8
    Say ("profile manifest: " + $lines.Count + " files -> $out")
}

# ---- display convention (loud fallback, SP-057/SP-058 precedent) ----
$screens = [System.Windows.Forms.Screen]::AllScreens | ForEach-Object { "$($_.DeviceName) $($_.Bounds) primary=$($_.Primary)" }
Say "displays: $($screens -join ' ; ')"
if ($screens -notmatch 'DISPLAY3') {
    Say "DISPLAY3 (-2576,1091) 2560x1440 ABSENT on this machine - LOUD FALLBACK to the primary display named above (never a faked rect; the standing laptop posture named in the packet)"
}

# ---- pre-run real-profile manifest ----
Profile-Manifest $realProfile (Join-Path $evidence 'profile-pre.txt')

# ---- launch (CCP_DATA_ROOT isolation, SP-057) ----
Say "launching: --tunnel-demo --tunnel-drive topmost-show,tunnel-close,tunnel-show,topmost-hide,finish (CCP_DATA_ROOT=$sandbox)"
$env:CCP_DATA_ROOT = $sandbox
$psi = New-Object System.Diagnostics.ProcessStartInfo($exe, '--tunnel-demo --tunnel-drive topmost-show,tunnel-close,tunnel-show,topmost-hide,finish')
$psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false
$psi.StandardOutputEncoding = [Text.Encoding]::UTF8; $psi.StandardErrorEncoding = [Text.Encoding]::UTF8
$script:proc = [System.Diagnostics.Process]::Start($psi)
$action = { if ($EventArgs.Data -ne $null) { try { Add-Content -Path $Event.MessageData -Value $EventArgs.Data -Encoding UTF8 } catch {} } }
Register-ObjectEvent -InputObject $script:proc -EventName ErrorDataReceived -Action $action -MessageData $logFile | Out-Null
Register-ObjectEvent -InputObject $script:proc -EventName OutputDataReceived -Action $action -MessageData $logFile | Out-Null
$script:proc.BeginErrorReadLine()
$script:proc.BeginOutputReadLine()
Remove-Item Env:CCP_DATA_ROOT

# ---- Phase A: tunnel rendering, desktop cleared (reverse case) ----
Wait-Log 'chaos-tunnel: page ready' 60
Wait-Log 'tunnel-drive: page ready' 20
Start-Sleep -Seconds 3  # curtain fade (0.9s) + descent ramp; steps run on a 10s cadence
# Foreign windows stay minimized through ALL FOUR phases (restored after D): the occlusion
# frames (B/C) then show tunnel colors AROUND the video rect - the strongest framing.
Minimize-Others $script:proc.Id
Start-Sleep -Milliseconds 1500
Capture-Phase 'A-tunnel-only' $script:proc.Id

# ---- Phase B: real Topmost surface over the tunnel ----
Wait-Log 'tunnel-drive: topmost surface shown' 40
Start-Sleep -Seconds 1
Capture-Phase 'B-topmost-over-tunnel' $script:proc.Id

# ---- Phase C: tunnel closed + re-shown UNDER the live Topmost surface ----
Wait-Log 'tunnel-drive: tunnel re-shown' 40
Wait-Log 'chaos-tunnel: page ready' 60 -occurrence 2
Start-Sleep -Seconds 3
Capture-Phase 'C-tunnel-reshown-under-topmost' $script:proc.Id

# ---- Phase D: topmost hidden - tunnel visible again (desktop still cleared) ----
Wait-Log 'tunnel-drive: topmost surface hidden' 40
Start-Sleep -Milliseconds 1500
Capture-Phase 'D-topmost-hidden' $script:proc.Id
Restore-Others
# The dashboard's activation (it inherits foreground when the foreign windows minimize)
# raises it above the tunnel; the z-guard demotes it back within one 1500ms cadence (the
# ported timer semantics - WPF's no-flash WndProc hook is the deliberately-unported delta).
# Surface the correction line so the guard's live function is IN the transcript.
$demoteLines = @(Select-String -Path $logFile -Pattern 'z-guard: dashboard was above the tunnel' -SimpleMatch)
Say ("z-guard demote events in app log: " + $demoteLines.Count)
foreach ($dl in $demoteLines) { Say ("  app log line " + $dl.LineNumber + ": " + $dl.Line) }

# ---- graceful finish -> exit 0 ----
Wait-Log 'shutting down the lifetime' 60
if (-not $script:proc.WaitForExit(30000)) { Fail 'process did not exit within 30s of shutdown' }
$exitCode = $script:proc.ExitCode
Say "EXIT=$exitCode"
if ($exitCode -ne 0) { Fail "non-zero exit: $exitCode" }

# ---- post-run profile byte-identity ----
Profile-Manifest $realProfile (Join-Path $evidence 'profile-post.txt')
$diff = Compare-Object (Get-Content (Join-Path $evidence 'profile-pre.txt')) (Get-Content (Join-Path $evidence 'profile-post.txt'))
if ($diff) { Say "PROFILE DIFF DETECTED:"; $diff | ForEach-Object { Say $_.InputObject }; Fail 'real profile changed during the headed run' }
Say 'profile byte-identity: IDENTICAL (pre == post)'
Say 'HARNESS COMPLETE'
