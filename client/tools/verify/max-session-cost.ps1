# Max-settings cost harness.
#
# client/port.txt: "Optimization claims require a MEASUREMENT, before and after, taken on the
# running product at maximum settings." This script IS that measurement. It starts the real
# product on an ISOLATED data root seeded to every module's clamp maximum, presses the shell's own
# START button with a real mouse click, and samples three numbers for a fixed window:
#
#   cpuPercentOfOneCore  process CPU time delta / wall delta x 100. 100 = one core saturated.
#   uiProbeMs            SendMessageTimeout(WM_NULL) round trip on the shell's hwnd. This is the
#                        message loop answering; a wedged UI thread is the owner's "could not
#                        close the app" and it is measured, not inferred.
#   surfaceChangeHz      how often the busiest of the app's own top-level window surfaces changes,
#                        read by blitting a 64x64 block out of each window's device context and
#                        hashing it. That is the rate at which a moving effect actually produces
#                        new content. The achieved sample rate is reported beside it so the
#                        Nyquist margin is visible rather than assumed.
#
# WHY THE SURFACE AND NOT THE SCREEN. Sampling the composited desktop was tried first and is
# unusable: one GetPixel against the screen DC with full-screen layered windows over it costs ~8 ms
# (measured: 7.4 samples/second for 16 points), which cannot resolve a 33-50 Hz cadence and burns
# 9% of a core doing it. Reading the app's own window DC costs microseconds. The price of that
# choice is stated plainly: it proves the app PRODUCED frames, not that the compositor showed
# them - screen-visible output stays a headed-capture claim.
#
# WHAT IT IS NOT. It is not a profiler and attributes nothing to a function. It is a single
# machine, a single display, a Debug build, and a desktop with whatever else the owner is running
# - which is why every run reports two samples and why a claim needs the same binary measured
# twice.
#
# CCP_DATA_ROOT IS SET ON THE CHILD PROCESS ONLY (ProcessStartInfo.Environment), never exported
# into this shell: exporting it process-wide makes the data-root isolation pin skip and the floor
# goes blind (client/CLAUDE.md).
param(
    [Parameter(Mandatory)][string]$Label,
    [int]$Seconds = 40,
    [int]$WarmSeconds = 10,
    # Real media. The port bundles no art (SpiralLibrary D86), so a spiral, some flash images and a
    # clip have to come from somewhere; the owner's library is the only real one on this machine.
    # Nothing about it is recorded except counts.
    [string]$MediaRoot = 'C:\Code\ccp media',
    [string]$OutDir = '',
    # The CONTROL. Runs the same session with the surface probe off, so its perturbation of the
    # CPU and message-loop numbers is a measurement rather than an assumption.
    [switch]$NoSurfaceProbe,
    # WHICH MODULES ARE ARMED. `all` is the performance contract (client/port.txt: every effect at
    # its highest value). `spiral` arms ONE moving surface and nothing else, which is the
    # low-noise instrument: with fifteen surfaces sharing one thread every module's cadence is a
    # function of every other module's, and a 12% run-to-run spread swallows anything smaller.
    # One surface alone answers "how long does ONE frame of this take" directly, in 1/Hz.
    [ValidateSet('all', 'spiral')][string]$Modules = 'all'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$verifyDir = $PSScriptRoot
$exe = Join-Path $verifyDir '..\..\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe'
if (-not (Test-Path $exe)) { Write-Output "FAIL: no build at $exe"; exit 1 }
if ($OutDir -eq '') { $OutDir = Join-Path $verifyDir 'artifacts' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ---------------------------------------------------------------------------------------------
# The real-desktop lease, the SAME file capture.ps1 takes (capture.ps1:496). The desktop is a
# singleton: a concurrent headed run in another lane would put its windows over this one's samples.
# ---------------------------------------------------------------------------------------------
$script:leasePath = Join-Path ([IO.Path]::GetTempPath()) 'ccp-real-desktop.lease'
$script:lease = $null
function Release-Lease {
    if ($null -ne $script:lease) { $script:lease.Dispose(); $script:lease = $null; Write-Output 'real-desktop lease released' }
}
function Take-Lease {
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt 300) {
        try {
            $script:lease = [IO.FileStream]::new($script:leasePath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
            Write-Output "real-desktop lease held by pid=$PID (waited $([math]::Round($deadline.Elapsed.TotalSeconds,1))s)"
            return
        }
        catch [IO.IOException] { Start-Sleep -Milliseconds 500 }
    }
    Write-Output 'FAIL: could not take the real-desktop lease within 300s'
    exit 1
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

public static class PerfProbe
{
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(
        IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr param);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect r);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern int GetGuiResources(IntPtr process, int flags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);

    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;

    // The UI probe. WM_NULL with SMTO_NORMAL (0) - deliberately NOT SMTO_ABORTIFHUNG, which
    // returns early on a hung window and would report the answer instead of the LATENCY.
    static double ProbeOnce(IntPtr hwnd, uint timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        IntPtr result;
        SendMessageTimeout(hwnd, 0x0000, IntPtr.Zero, IntPtr.Zero, 0, timeoutMs, out result);
        return sw.Elapsed.TotalMilliseconds;
    }

    static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0) return 0;
        var copy = new List<double>(values);
        copy.Sort();
        var index = (int)Math.Round(p * (copy.Count - 1));
        return copy[index];
    }

    static string N(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    public static string Sample(int pid, long hwndValue, int seconds, bool surfaceProbe)
    {
        var hwnd = new IntPtr(hwndValue);
        var proc = Process.GetProcessById(pid);
        timeBeginPeriod(1);
        try
        {
            var stop = new ManualResetEventSlim(false);

            // ---- cadence thread: a 64x64 block out of every top-level window this process owns.
            long samples = 0;
            var changesByWindow = new Dictionary<IntPtr, long>();
            var sizeByWindow = new Dictionary<IntPtr, string>();
            var cadence = new Thread(() =>
            {
                var screen = GetDC(IntPtr.Zero);
                var memory = CreateCompatibleDC(screen);
                var block = CreateCompatibleBitmap(screen, 64, 64);
                SelectObject(memory, block);
                ReleaseDC(IntPtr.Zero, screen);

                var windows = new List<IntPtr>();
                var hashes = new Dictionary<IntPtr, ulong>();
                var rescan = Stopwatch.StartNew();
                var pace = Stopwatch.StartNew();
                while (!stop.IsSet)
                {
                    if (windows.Count == 0 || rescan.ElapsedMilliseconds > 1000)
                    {
                        rescan.Restart();
                        windows.Clear();
                        EnumWindows((hwnd, _) =>
                        {
                            uint owner;
                            GetWindowThreadProcessId(hwnd, out owner);
                            if (owner == (uint)pid && IsWindowVisible(hwnd)) windows.Add(hwnd);
                            return true;
                        }, IntPtr.Zero);
                    }

                    foreach (var hwnd in windows)
                    {
                        Rect r;
                        if (!GetWindowRect(hwnd, out r)) continue;
                        var w = r.Right - r.Left; var h = r.Bottom - r.Top;
                        if (w < 64 || h < 64) continue;
                        var dc = GetDC(hwnd);
                        if (dc == IntPtr.Zero) continue;
                        var copied = BitBlt(memory, 0, 0, 64, 64, dc, (w / 2) - 32, (h / 2) - 32, 0x00CC0020);
                        ReleaseDC(hwnd, dc);
                        if (!copied) continue;
                        // NINE points, not sixty-four: the harness's own cost is a perturbation of
                        // the thing it measures, and at 64 points it spent 64% of a core.
                        ulong hash = 1469598103934665603UL;
                        for (var i = 0; i < 3; i++)
                            for (var j = 0; j < 3; j++)
                            {
                                hash ^= GetPixel(memory, i * 24, j * 24);
                                hash *= 1099511628211UL;
                            }
                        ulong previous;
                        if (hashes.TryGetValue(hwnd, out previous) && previous != hash)
                        {
                            long count;
                            changesByWindow.TryGetValue(hwnd, out count);
                            changesByWindow[hwnd] = count + 1;
                            sizeByWindow[hwnd] = w + "x" + h;
                        }
                        hashes[hwnd] = hash;
                    }

                    samples++;

                    // PACED, not free-running. The harness's own cost is a perturbation of the
                    // thing it measures and it reads the app's own device contexts, which GDI
                    // serialises against the app's blits. 150 Hz still resolves anything up to
                    // 75 Hz, which is above every cadence in this product.
                    var due = (samples * 1000L) / 150L;
                    var behind = due - pace.ElapsedMilliseconds;
                    if (behind > 0) Thread.Sleep((int)behind); else Thread.Sleep(1);
                }

                DeleteDC(memory);
                DeleteObject(block);
            });

            // ---- UI responsiveness thread.
            var probes = new List<double>();
            var ui = new Thread(() =>
            {
                while (!stop.IsSet)
                {
                    probes.Add(ProbeOnce(hwnd, 3000));
                    Thread.Sleep(100);
                }
            });

            // ---- process cost thread.
            var cpu = new List<double>();
            var gdi = new List<double>();
            var mem = new List<double>();
            var cost = new Thread(() =>
            {
                var lastCpu = proc.TotalProcessorTime;
                var sw = Stopwatch.StartNew();
                while (!stop.IsSet)
                {
                    Thread.Sleep(500);
                    proc.Refresh();
                    var now = proc.TotalProcessorTime;
                    var elapsed = sw.Elapsed;
                    sw.Restart();
                    cpu.Add((now - lastCpu).TotalMilliseconds / elapsed.TotalMilliseconds * 100.0);
                    lastCpu = now;
                    gdi.Add(GetGuiResources(proc.Handle, 0));
                    mem.Add(proc.PrivateMemorySize64 / 1048576.0);
                }
            });

            var harnessStart = Process.GetCurrentProcess().TotalProcessorTime;
            var wall = Stopwatch.StartNew();
            if (surfaceProbe) cadence.Start();
            ui.Start(); cost.Start();
            Thread.Sleep(seconds * 1000);
            stop.Set();
            if (surfaceProbe) cadence.Join();
            ui.Join(); cost.Join();
            var elapsedSeconds = wall.Elapsed.TotalSeconds;
            var self = Process.GetCurrentProcess();
            self.Refresh();
            var harnessCpu = (self.TotalProcessorTime - harnessStart).TotalMilliseconds / wall.Elapsed.TotalMilliseconds * 100.0;

            var cpuMean = 0.0; foreach (var v in cpu) cpuMean += v; if (cpu.Count > 0) cpuMean /= cpu.Count;
            var memMax = 0.0; foreach (var v in mem) if (v > memMax) memMax = v;
            var gdiMax = 0.0; foreach (var v in gdi) if (v > gdiMax) gdiMax = v;

            long busiest = 0;
            var busiestSize = "none";
            var surfaces = new List<string>();
            foreach (var pair in changesByWindow)
            {
                var size = sizeByWindow.ContainsKey(pair.Key) ? sizeByWindow[pair.Key] : "?";
                surfaces.Add("{\"window\":\"0x" + pair.Key.ToString("X") + "\",\"size\":\"" + size + "\",\"changeHz\":" + N(pair.Value / elapsedSeconds) + "}");
                if (pair.Value > busiest) { busiest = pair.Value; busiestSize = size; }
            }

            return "{"
                + "\"seconds\":" + N(elapsedSeconds)
                + ",\"exited\":" + (proc.HasExited ? "true" : "false")
                + ",\"cpuPercentOfOneCore\":{\"mean\":" + N(cpuMean) + ",\"p50\":" + N(Percentile(cpu, 0.5))
                    + ",\"p90\":" + N(Percentile(cpu, 0.9)) + ",\"max\":" + N(Percentile(cpu, 1.0)) + ",\"n\":" + cpu.Count + "}"
                + ",\"uiProbeMs\":{\"p50\":" + N(Percentile(probes, 0.5)) + ",\"p90\":" + N(Percentile(probes, 0.9))
                    + ",\"max\":" + N(Percentile(probes, 1.0)) + ",\"n\":" + probes.Count + "}"
                + ",\"surfaceChangeHz\":" + N(busiest / elapsedSeconds)
                + ",\"busiestSurface\":\"" + busiestSize + "\""
                + ",\"surfaces\":[" + string.Join(",", surfaces.ToArray()) + "]"
                + ",\"surfaceSamplesPerSecond\":" + N(samples / elapsedSeconds)
                + ",\"privateMemoryMbMax\":" + N(memMax)
                + ",\"gdiObjectsMax\":" + N(gdiMax)
                + ",\"harnessCpuPercentOfOneCore\":" + N(harnessCpu)
                + "}";
        }
        finally { timeEndPeriod(1); }
    }

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(200);
        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(500);
    }
}
'@

# ---------------------------------------------------------------------------------------------
# The seeded profile. Every dial is written ABOVE its clamp and the document's own setter clamps
# it down, so this stays correct when a clamp moves (SpiralPresetDocument.cs:62-67 and the twelve
# documents beside it clamp on set, and PersistenceStore deserialises through those setters).
#
# TWO DELIBERATE DEVIATIONS, both recorded rather than hidden:
#   - the Intensity Ramp is OFF. Its whole job is to start modules BELOW their dials and climb
#     (Session/IntensityRampPresetDocument.cs), so enabling it would make the first minute LESS
#     than maximum, which is the opposite of the configuration port.txt names.
#   - every volume is 0. The mixing work is identical at 0 and at 100; the room is not.
# ---------------------------------------------------------------------------------------------
$dataRoot = Join-Path ([IO.Path]::GetTempPath()) ("ccp-perf-" + $Label + "-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$assets = Join-Path $dataRoot 'assets'
New-Item -ItemType Directory -Force -Path $dataRoot, $assets, (Join-Path $assets 'images'), (Join-Path $assets 'videos'), (Join-Path $assets 'spirals') | Out-Null

function Write-Doc([string]$name, [hashtable]$body) {
    $body['schemaVersion'] = 1
    $body | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $dataRoot $name) -Encoding UTF8
}

$MAX = 99999
$on = $Modules -eq 'all'
Write-Doc 'session_preset.json'        @{ flashEnabled = $on; flashesPerHour = $MAX; imagesPerFlash = $MAX }
Write-Doc 'session_visuals.json'       @{ imageScalePercent = $MAX; flashOpacityPercent = $MAX; flashDurationSeconds = $MAX }
Write-Doc 'session_spiral.json'        @{ enabled = $true; opacityPercent = $MAX; path = '' }
Write-Doc 'session_pinkfilter.json'    @{ enabled = $on; opacityPercent = $MAX }
Write-Doc 'session_subliminal.json'    @{ enabled = $on; perMinute = $MAX; durationFrames = $MAX; opacityPercent = $MAX }
Write-Doc 'session_video.json'         @{ enabled = $on; perHour = $MAX; maxSeconds = $MAX }
Write-Doc 'session_bubblepop.json'     @{ enabled = $on; perMinute = $MAX; sizePercent = $MAX; speedBoostPercent = $MAX }
Write-Doc 'session_bubblecount.json'   @{ enabled = $on; perHour = $MAX }
Write-Doc 'session_bouncing_text.json' @{ enabled = $on; speed = $MAX; sizePercent = $MAX; opacityPercent = $MAX }
Write-Doc 'session_lockcard.json'      @{ enabled = $on; perHour = $MAX; repeats = $MAX; strict = $false }
Write-Doc 'session_popquiz.json'       @{ enabled = $on; perHour = $MAX }
Write-Doc 'session_mindwipe.json'      @{ enabled = $on; perHour = $MAX; volumePercent = 0 }
Write-Doc 'session_braindrain.json'    @{ enabled = $on; intensityPercent = $MAX; highRefresh = $true; volumePercent = 0 }
Write-Doc 'session_ramp.json'          @{ enabled = $false }
Write-Doc 'audio.json'                 @{ masterVolume = 0; videoVolume = 0 }

# Media. Counts only: no file name from the owner's library reaches this repo or its output.
$spiralCount = 0; $imageCount = 0; $videoCount = 0
if (Test-Path $MediaRoot) {
    # Bounded and DETERMINISTIC: the same twenty-one files every run, so before and after are the
    # same workload. Under 8 MB keeps the copy off the measurement's critical path.
    $gifs = @(Get-ChildItem -Path $MediaRoot -Recurse -Filter *.gif -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Length -gt 500KB -and $_.Length -lt 8MB } | Sort-Object FullName | Select-Object -First 21)
    if ($gifs.Count -gt 0) {
        Copy-Item $gifs[0].FullName (Join-Path $assets 'spirals\spiral.gif'); $spiralCount = 1
        for ($i = 1; $i -lt $gifs.Count; $i++) {
            Copy-Item $gifs[$i].FullName (Join-Path $assets ("images\flash-$i.gif")); $imageCount++
        }
    }
    $clip = @(Get-ChildItem -Path $MediaRoot -Recurse -Filter *.mp4 -File -ErrorAction SilentlyContinue |
        Sort-Object Length | Select-Object -First 1)
    if ($clip.Count -gt 0) { Copy-Item $clip[0].FullName (Join-Path $assets 'videos\clip.mp4'); $videoCount = 1 }
}
Write-Output "seeded: $spiralCount spiral, $imageCount flash images, $videoCount video clip(s) into an isolated data root"

Take-Lease
$script:proc = $null
function Cleanup([string]$why) {
    if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill(); $script:proc.WaitForExit(10000) }
    Release-Lease
    Remove-Item -Recurse -Force $dataRoot -ErrorAction SilentlyContinue
    if ($why -ne '') { Write-Output "FAIL: $why"; exit 1 }
}

$psi = New-Object Diagnostics.ProcessStartInfo
$psi.FileName = (Resolve-Path $exe).Path
$psi.UseShellExecute = $false
$psi.Environment['CCP_DATA_ROOT'] = $dataRoot
$script:proc = [Diagnostics.Process]::Start($psi)
Write-Output "launched pid=$($script:proc.Id) with an isolated data root"

$deadline = [Diagnostics.Stopwatch]::StartNew()
$window = $null
while ($deadline.Elapsed.TotalSeconds -lt 90 -and $null -eq $window) {
    Start-Sleep -Milliseconds 500
    if ($script:proc.HasExited) { Cleanup "the app exited during startup with code $($script:proc.ExitCode)" }
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:proc.Id)
    $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}
if ($null -eq $window) { Cleanup 'no main window appeared within 90s' }
$hwnd = [int64]$window.Current.NativeWindowHandle
Write-Output "main window 0x$('{0:X}' -f $hwnd) up after $([math]::Round($deadline.Elapsed.TotalSeconds,1))s"

# The START button, found by the AutomationId the shell markup declares
# (Views/MainWindow.axaml:440), pressed with a real mouse click.
$startCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SessionStartButton')
$start = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $startCond)
if ($null -eq $start) { Cleanup 'no SessionStartButton in the shell' }
$r = $start.Current.BoundingRectangle
[PerfProbe]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))

$statusCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SessionStatusText')
$status = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCond)
$statusText = if ($null -ne $status) { $status.Current.Name } else { '' }
Write-Output "status after the click: '$statusText'"
if ($statusText -notmatch 'running') { Cleanup "the shell does not report a running session after the click (status '$statusText')" }

Write-Output "warming $WarmSeconds s, then sampling $Seconds s"
Start-Sleep -Seconds $WarmSeconds
$json = [PerfProbe]::Sample($script:proc.Id, $hwnd, $Seconds, -not $NoSurfaceProbe)

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outFile = Join-Path $OutDir "max-session-cost-$Label-$stamp.json"
$record = "{`"label`":`"$Label`",`"modules`":`"$Modules`",`"media`":{`"spirals`":$spiralCount,`"images`":$imageCount,`"videos`":$videoCount},`"sample`":$json}"
$record | Set-Content -Path $outFile -Encoding UTF8
Write-Output $record
Write-Output "wrote $outFile"

Cleanup ''
Get-Process -Name 'CcpClient*' -ErrorAction SilentlyContinue | ForEach-Object { Write-Output "residual process $($_.Id)"; $_.Kill() }
Write-Output 'done'
