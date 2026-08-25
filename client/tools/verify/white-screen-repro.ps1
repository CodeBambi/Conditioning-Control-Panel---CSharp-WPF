# Full-white/black-screen reproduction harness.
#
# The owner reported a FULL WHITE SCREEN during a session twice, and nine passes of reading source
# had eliminated nine candidates without reproducing it. This script is the instrument that did.
# It starts the real product on an ISOLATED data root seeded to ONE module (or all of them), presses
# the shell's own START button with a real mouse click, and then samples the COMPOSITED desktop at
# ~4 Hz for a fixed window.
#
# WHAT IT MEASURES, AND WHY EACH READING IS THERE.
#
#   nearWhite   the fraction of the virtual screen whose EVERY channel is >= 240. This is the
#               owner's complaint expressed as a number: "full white screen" is nearWhite near 1.
#               white250 is the same count at >= 250, so a reader can see whether it is saturated
#               white or merely bright.
#   nearBlack   the same at <= 15, because he reported "black OR white" and they are one mechanism.
#   grid        twelve cells, so a 30 %-of-screen reading can be told from a corner blowout.
#
# WHEN nearWhite CROSSES THE THRESHOLD the tick is escalated into two more readings that answer
# different questions, and the difference between them is the whole diagnosis:
#
#   windows     every visible top-level window on the machine with class, rect, extended style,
#               DWM cloak and LAYERED ATTRIBUTES (GetLayeredWindowAttributes). The alpha byte is the
#               point: an overlay at LWA alpha 26 cannot flood and one at 255 can.
#   owner       WHO owns the white pixel. WindowFromPoint alone would be WRONG here - every overlay
#               this product places is WS_EX_TRANSPARENT and the hit test skips those by definition,
#               so it would name whatever is UNDERNEATH the white. The z-order stack is walked
#               instead (EnumWindows returns topmost-first) and the hit-test answer is reported
#               beside it as a separate fact. Each candidate's OWN buffer is then read twice -
#               GetWindowDC+BitBlt for the GDI surface, PrintWindow(PW_RENDERFULLCONTENT) for a
#               window that presents through DirectComposition - which is what separates "its buffer
#               is white" from "it is composited wrongly". Those are different defects.
#
# The child's stdout and stderr are captured BY PID through ProcessStartInfo redirection, never by
# process name: client/src/CcpClient.Desktop/Program.cs:323-330 installs panic hooks that log
# unhandled exceptions to stderr, and an earlier attempt matched the wrong process by name and
# produced no log at all.
#
# CCP_DATA_ROOT IS SET ON THE CHILD ONLY (ProcessStartInfo.Environment), never exported into this
# shell: exporting it process-wide makes the data-root isolation pin skip and the floor goes blind
# (client/CLAUDE.md).
#
# PRIVACY. The port bundles no art, so a spiral, some flash images and a clip have to come from the
# owner's own library. Nothing about it is recorded anywhere in this script's output except COUNTS.

param(
    [Parameter(Mandatory)][ValidateSet(
        'flash', 'spiral', 'pinkfilter', 'subliminal', 'video', 'bouncing', 'bubblepop',
        'bubblecount', 'lockcard', 'popquiz', 'mindwipe', 'braindrain', 'ramp', 'all', 'owner', 'idle')]
    [string]$Mode,
    [int]$Seconds = 60,
    [double]$WhiteThreshold = 0.35,
    [string]$MediaRoot = 'C:\Code\ccp media',
    [switch]$NoMedia,
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$verifyDir = $PSScriptRoot
$exe = Join-Path $verifyDir '..\..\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe'
if (-not (Test-Path $exe)) { Write-Output "FAIL: no build at $exe"; exit 1 }
if ($OutDir -eq '') { $OutDir = Join-Path $verifyDir 'artifacts' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class WhiteProbe
{
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr param);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hwnd, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();

    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO { public BITMAPINFOHEADER h; public uint quad0; }

    const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    const int GWL_EXSTYLE = -20;

    static string N(double v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }

    static string Q(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ' || c > '~') b.Append(' ');
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    // A reusable TOP-DOWN 32bpp DIB section the caller blits into. A DIB section rather than a
    // compatible bitmap because its bits are directly addressable: one Marshal.Copy reads the whole
    // downsampled screen, where GetPixel against a screen DC with full-screen layered windows over it
    // costs about 8 ms PER POINT and cannot resolve anything.
    sealed class Surface : IDisposable
    {
        public IntPtr Dc, Bmp, Bits;
        public int W, H;
        public Surface(int w, int h)
        {
            W = w; H = h;
            var screen = GetDC(IntPtr.Zero);
            Dc = CreateCompatibleDC(screen);
            var bmi = new BITMAPINFO();
            bmi.h.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.h.biWidth = w;
            bmi.h.biHeight = -h;
            bmi.h.biPlanes = 1;
            bmi.h.biBitCount = 32;
            bmi.h.biCompression = 0;
            Bmp = CreateDIBSection(Dc, ref bmi, 0, out Bits, IntPtr.Zero, 0);
            SelectObject(Dc, Bmp);
            // COLORONCOLOR: nearest neighbour. Averaging would turn a thin white line into a grey
            // haze and a full white screen into the same number, which is the distinction this
            // whole script exists to make.
            SetStretchBltMode(Dc, 3);
            ReleaseDC(IntPtr.Zero, screen);
        }
        public void Dispose() { DeleteDC(Dc); DeleteObject(Bmp); }
    }

    static Surface _screen;
    static Surface _window;
    static byte[] _buf;

    public static void Init(int w, int h)
    {
        _screen = new Surface(w, h);
        _window = new Surface(64, 64);
        _buf = new byte[Math.Max(w * h, 64 * 64) * 4];
    }

    public static void Shutdown()
    {
        if (_screen != null) { _screen.Dispose(); _screen = null; }
        if (_window != null) { _window.Dispose(); _window = null; }
    }

    public struct Stats
    {
        public double NearWhite, White250, NearBlack, MeanR, MeanG, MeanB;
        public int MinAll, MaxAll;
        public string Grid;
        public int WhiteX, WhiteY;
    }

    static Stats Measure(Surface s, int originX, int originY, double scaleX, double scaleY)
    {
        GdiFlush();
        var n = s.W * s.H;
        Marshal.Copy(s.Bits, _buf, 0, n * 4);
        long nw = 0, w250 = 0, nb = 0, sr = 0, sg = 0, sb = 0;
        int minAll = 255, maxAll = 0, wx = -1, wy = -1;
        var cells = new long[12];
        var cellN = new long[12];
        for (var i = 0; i < n; i++)
        {
            int b = _buf[i * 4], g = _buf[i * 4 + 1], r = _buf[i * 4 + 2];
            sr += r; sg += g; sb += b;
            var lo = Math.Min(r, Math.Min(g, b));
            var hi = Math.Max(r, Math.Max(g, b));
            if (lo < minAll) minAll = lo;
            if (hi > maxAll) maxAll = hi;
            var px = i % s.W; var py = i / s.W;
            var cell = (py * 3 / s.H) * 4 + (px * 4 / s.W);
            cellN[cell]++;
            if (lo >= 240)
            {
                nw++; cells[cell]++;
                if (wx < 0) { wx = originX + (int)(px * scaleX); wy = originY + (int)(py * scaleY); }
            }
            if (lo >= 250) w250++;
            if (hi <= 15) nb++;
        }
        var grid = new StringBuilder("[");
        for (var c = 0; c < 12; c++)
        {
            if (c > 0) grid.Append(',');
            grid.Append(N(cellN[c] == 0 ? 0 : (double)cells[c] / cellN[c]));
        }
        grid.Append(']');
        return new Stats
        {
            NearWhite = (double)nw / n,
            White250 = (double)w250 / n,
            NearBlack = (double)nb / n,
            MeanR = (double)sr / n,
            MeanG = (double)sg / n,
            MeanB = (double)sb / n,
            MinAll = minAll,
            MaxAll = maxAll,
            Grid = grid.ToString(),
            WhiteX = wx,
            WhiteY = wy,
        };
    }

    public static string ScreenTick()
    {
        var vx = GetSystemMetrics(76); var vy = GetSystemMetrics(77);
        var vw = GetSystemMetrics(78); var vh = GetSystemMetrics(79);
        var screen = GetDC(IntPtr.Zero);
        // CAPTUREBLT: without it layered windows - which is EVERY overlay this product places - are
        // excluded from the read, and the harness would report a clean desktop through the defect.
        var ok = StretchBlt(_screen.Dc, 0, 0, _screen.W, _screen.H, screen, vx, vy, vw, vh, SRCCOPY | CAPTUREBLT);
        ReleaseDC(IntPtr.Zero, screen);
        if (!ok) return "{\"blit\":false}";
        var s = Measure(_screen, vx, vy, (double)vw / _screen.W, (double)vh / _screen.H);
        return "{\"blit\":true,\"nearWhite\":" + N(s.NearWhite) + ",\"white250\":" + N(s.White250)
            + ",\"nearBlack\":" + N(s.NearBlack)
            + ",\"mean\":[" + N(s.MeanR) + "," + N(s.MeanG) + "," + N(s.MeanB) + "]"
            + ",\"minChan\":" + s.MinAll + ",\"maxChan\":" + s.MaxAll
            + ",\"grid\":" + s.Grid
            + ",\"whitePoint\":[" + s.WhiteX + "," + s.WhiteY + "]}";
    }

    static string ProcName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch { return "?"; }
    }

    static string Describe(IntPtr hwnd)
    {
        uint pid; GetWindowThreadProcessId(hwnd, out pid);
        var cls = new StringBuilder(256); GetClassNameW(hwnd, cls, 256);
        var txt = new StringBuilder(256); GetWindowTextW(hwnd, txt, 256);
        RECT r; GetWindowRect(hwnd, out r);
        var ex = (ulong)(long)GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        uint key; byte alpha; uint flags;
        var layered = GetLayeredWindowAttributes(hwnd, out key, out alpha, out flags);
        int cloaked = 0; DwmGetWindowAttribute(hwnd, 14, out cloaked, 4);
        var title = txt.ToString();
        if (title.Length > 60) title = title.Substring(0, 60);
        return "{\"hwnd\":\"0x" + hwnd.ToString("X") + "\",\"pid\":" + pid + ",\"proc\":" + Q(ProcName(pid))
            + ",\"class\":" + Q(cls.ToString()) + ",\"title\":" + Q(title)
            + ",\"rect\":[" + r.Left + "," + r.Top + "," + (r.Right - r.Left) + "," + (r.Bottom - r.Top) + "]"
            + ",\"exStyle\":\"0x" + ex.ToString("X") + "\""
            + ",\"visible\":" + (IsWindowVisible(hwnd) ? "true" : "false")
            + ",\"cloaked\":" + cloaked
            + ",\"lwa\":" + (layered
                ? "{\"alpha\":" + alpha + ",\"colorKey\":\"0x" + key.ToString("X") + "\",\"flags\":" + flags + "}"
                : "null")
            + "}";
    }

    public static string Windows(int minArea)
    {
        var parts = new List<string>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            RECT r;
            if (!GetWindowRect(hwnd, out r)) return true;
            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area < minArea) return true;
            parts.Add(Describe(hwnd));
            return true;
        }, IntPtr.Zero);
        return "[" + string.Join(",", parts.ToArray()) + "]";
    }

    static string SurfaceStats(IntPtr hwnd, int x, int y)
    {
        var sb = new StringBuilder();
        RECT r; GetWindowRect(hwnd, out r);
        var w = r.Right - r.Left; var h = r.Bottom - r.Top;
        if (w < 64 || h < 64) return ",\"ownDc\":\"too-small\"";

        var lx = Math.Max(0, Math.Min(w - 64, x - r.Left - 32));
        var ly = Math.Max(0, Math.Min(h - 64, y - r.Top - 32));

        var dc = GetWindowDC(hwnd);
        var blit = dc != IntPtr.Zero && BitBlt(_window.Dc, 0, 0, 64, 64, dc, lx, ly, SRCCOPY);
        if (dc != IntPtr.Zero) ReleaseDC(hwnd, dc);
        if (blit)
        {
            var s = Measure(_window, 0, 0, 1, 1);
            sb.Append(",\"ownDc\":{\"nearWhite\":").Append(N(s.NearWhite))
              .Append(",\"mean\":[").Append(N(s.MeanR)).Append(',').Append(N(s.MeanG)).Append(',').Append(N(s.MeanB))
              .Append("],\"minChan\":").Append(s.MinAll).Append(",\"maxChan\":").Append(s.MaxAll).Append('}');
        }
        else sb.Append(",\"ownDc\":\"blit-failed\"");

        // PW_RENDERFULLCONTENT = 2. The WHOLE window into a 64x64 target, so this is a thumbnail of
        // the entire window rather than the sampled point, and it is the only reading that works for
        // a DirectComposition/GPU-presented child such as a WebView, whose window DC reads empty.
        if (PrintWindow(hwnd, _window.Dc, 2))
        {
            var s = Measure(_window, 0, 0, 1, 1);
            sb.Append(",\"ownPrint\":{\"nearWhite\":").Append(N(s.NearWhite))
              .Append(",\"mean\":[").Append(N(s.MeanR)).Append(',').Append(N(s.MeanG)).Append(',').Append(N(s.MeanB))
              .Append("],\"minChan\":").Append(s.MinAll).Append(",\"maxChan\":").Append(s.MaxAll).Append('}');
        }
        else sb.Append(",\"ownPrint\":\"failed\"");
        return sb.ToString();
    }

    public static string OwnerAt(int x, int y)
    {
        var sb = new StringBuilder();
        sb.Append("{\"point\":[").Append(x).Append(',').Append(y).Append(']');

        var p = new POINT { X = x, Y = y };
        var hit = WindowFromPoint(p);
        sb.Append(",\"hitTest\":").Append(hit == IntPtr.Zero ? "null" : Describe(hit));

        var stack = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (stack.Count >= 5) return false;
            if (!IsWindowVisible(hwnd)) return true;
            int cloaked; DwmGetWindowAttribute(hwnd, 14, out cloaked, 4);
            if (cloaked != 0) return true;
            RECT r;
            if (!GetWindowRect(hwnd, out r)) return true;
            if (x < r.Left || x >= r.Right || y < r.Top || y >= r.Bottom) return true;
            stack.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        sb.Append(",\"stack\":[");
        for (var i = 0; i < stack.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var d = Describe(stack[i]);
            sb.Append(d.Substring(0, d.Length - 1));
            sb.Append(SurfaceStats(stack[i], x, y));
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    public static Process Launch(string exe, string dataRoot, string outPath, string errPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["CCP_DATA_ROOT"] = dataRoot;
        var proc = Process.Start(psi);
        var outW = new System.IO.StreamWriter(outPath, false) { AutoFlush = true };
        var errW = new System.IO.StreamWriter(errPath, false) { AutoFlush = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outW) outW.WriteLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errW) errW.WriteLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        return proc;
    }

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(150);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(400);
    }
}
'@

# ---------------------------------------------------------------------------------------------
# The seeded profile. Outside 'owner' every dial is written ABOVE its clamp and the document's own
# setter clamps it down, so this stays correct when a clamp moves (SpiralPresetDocument.cs:62-67 and
# the twelve documents beside it clamp on set, and PersistenceStore deserialises through those
# setters). 'owner' is the owner's OWN shipping configuration, read out of
# %LOCALAPPDATA%\ConditioningControlPanel\settings.json - Flash on at ImageScale 100, opacity 100,
# duration 5 s and 5 simultaneous images, Mandatory Videos on, everything else off - with ONE
# deliberate deviation that is recorded rather than hidden: the FREQUENCIES are raised to their
# clamps. His are 10 flashes and 6 videos per HOUR, which fire nothing at all inside a 60 s
# observation; frequency changes how often a flash happens and nothing about what it looks like.
# ---------------------------------------------------------------------------------------------
$dataRoot = Join-Path ([IO.Path]::GetTempPath()) ("ccp-white-" + $Mode + "-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$assets = Join-Path $dataRoot 'assets'
New-Item -ItemType Directory -Force -Path $dataRoot, $assets,
    (Join-Path $assets 'images'), (Join-Path $assets 'videos'), (Join-Path $assets 'spirals') | Out-Null

function Write-Doc([string]$name, [hashtable]$body) {
    $body['schemaVersion'] = 1
    $body | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $dataRoot $name) -Encoding UTF8
}

$MAX = 99999
function On([string]$m) { return ($Mode -eq 'all') -or ($Mode -eq $m) }

if ($Mode -eq 'owner') {
    Write-Doc 'session_preset.json'        @{ flashEnabled = $true; flashesPerHour = $MAX; imagesPerFlash = 5 }
    Write-Doc 'session_visuals.json'       @{ imageScalePercent = 100; flashOpacityPercent = 100; flashDurationSeconds = 5 }
    Write-Doc 'session_spiral.json'        @{ enabled = $false; opacityPercent = 10; path = '' }
    Write-Doc 'session_pinkfilter.json'    @{ enabled = $false; opacityPercent = 10 }
    Write-Doc 'session_subliminal.json'    @{ enabled = $false; perMinute = 5; durationFrames = 2; opacityPercent = 80 }
    Write-Doc 'session_video.json'         @{ enabled = $true; perHour = $MAX; maxSeconds = 0 }
    Write-Doc 'session_bubblepop.json'     @{ enabled = $false }
    Write-Doc 'session_bubblecount.json'   @{ enabled = $false }
    Write-Doc 'session_bouncing_text.json' @{ enabled = $false }
    Write-Doc 'session_lockcard.json'      @{ enabled = $false }
    Write-Doc 'session_popquiz.json'       @{ enabled = $false }
    Write-Doc 'session_mindwipe.json'      @{ enabled = $false }
    Write-Doc 'session_braindrain.json'    @{ enabled = $false }
    Write-Doc 'session_ramp.json'          @{ enabled = $false }
}
else {
    Write-Doc 'session_preset.json'        @{ flashEnabled = (On 'flash'); flashesPerHour = $MAX; imagesPerFlash = $MAX }
    Write-Doc 'session_visuals.json'       @{ imageScalePercent = $MAX; flashOpacityPercent = $MAX; flashDurationSeconds = $MAX }
    Write-Doc 'session_spiral.json'        @{ enabled = (On 'spiral'); opacityPercent = $MAX; path = '' }
    Write-Doc 'session_pinkfilter.json'    @{ enabled = (On 'pinkfilter'); opacityPercent = $MAX }
    Write-Doc 'session_subliminal.json'    @{ enabled = (On 'subliminal'); perMinute = $MAX; durationFrames = $MAX; opacityPercent = $MAX }
    Write-Doc 'session_video.json'         @{ enabled = (On 'video'); perHour = $MAX; maxSeconds = $MAX }
    Write-Doc 'session_bubblepop.json'     @{ enabled = (On 'bubblepop'); perMinute = $MAX; sizePercent = $MAX; speedBoostPercent = $MAX }
    Write-Doc 'session_bubblecount.json'   @{ enabled = (On 'bubblecount'); perHour = $MAX }
    Write-Doc 'session_bouncing_text.json' @{ enabled = (On 'bouncing'); speed = $MAX; sizePercent = $MAX; opacityPercent = $MAX }
    Write-Doc 'session_lockcard.json'      @{ enabled = (On 'lockcard'); perHour = $MAX; repeats = $MAX; strict = $false }
    Write-Doc 'session_popquiz.json'       @{ enabled = (On 'popquiz'); perHour = $MAX }
    Write-Doc 'session_mindwipe.json'      @{ enabled = (On 'mindwipe'); perHour = $MAX; volumePercent = 0 }
    Write-Doc 'session_braindrain.json'    @{ enabled = (On 'braindrain'); intensityPercent = $MAX; highRefresh = $true; volumePercent = 0 }
    Write-Doc 'session_ramp.json'          @{ enabled = (On 'ramp') }
}
Write-Doc 'audio.json' @{ masterVolume = 0; videoVolume = 0 }

# Media. Bounded and DETERMINISTIC - the same twenty-one files every run, so two runs are the same
# workload. COUNTS ONLY reach the output.
$spiralCount = 0; $imageCount = 0; $videoCount = 0
if (-not $NoMedia -and (Test-Path $MediaRoot)) {
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
# The installed spiral wins when it is there: its properties are known (2400x1600, 32 frames, fully
# opaque, 24.6 % near-white), which makes the spiral arm a controlled input rather than a lottery.
$installed = Join-Path $env:APPDATA 'CcpClient\assets\spirals\spiral.gif'
if (Test-Path $installed) { Copy-Item $installed (Join-Path $assets 'spirals\spiral.gif') -Force; $spiralCount = 1 }

# ---------------------------------------------------------------------------------------------
# The real-desktop lease, the SAME file capture.ps1 and max-session-cost.ps1 take. The desktop is a
# singleton: a concurrent headed run in another lane would put its windows into these samples, and a
# harness that reports another lane's window as the owner of the white is worse than no harness.
# ---------------------------------------------------------------------------------------------
$script:leasePath = Join-Path ([IO.Path]::GetTempPath()) 'ccp-real-desktop.lease'
$script:lease = $null
function Release-Lease {
    if ($null -ne $script:lease) { $script:lease.Dispose(); $script:lease = $null; Write-Output 'real-desktop lease released' }
}
$wait = [Diagnostics.Stopwatch]::StartNew()
while ($wait.Elapsed.TotalSeconds -lt 300 -and $null -eq $script:lease) {
    try { $script:lease = [IO.FileStream]::new($script:leasePath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None) }
    catch [IO.IOException] { Start-Sleep -Milliseconds 500 }
}
if ($null -eq $script:lease) { Write-Output 'FAIL: could not take the real-desktop lease within 300s'; exit 1 }
Write-Output "real-desktop lease held by pid=$PID"

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$base = Join-Path $OutDir "white-screen-$Mode-$stamp"
$logPath = "$base.jsonl"
$errPath = "$base.stderr.txt"
$outPath = "$base.stdout.txt"
$script:proc = $null
$log = [IO.StreamWriter]::new($logPath, $false)
$log.AutoFlush = $true

function Emit([string]$json) { $log.WriteLine($json) }

# Finally-style guarantee: the child is killed BY THE PID THIS SCRIPT CAPTURED, never by a name
# sweep. A name sweep on this machine would also match the owner's own shipping product.
function Cleanup {
    if ($script:proc -and -not $script:proc.HasExited) {
        try { $script:proc.Kill($true) } catch { }
        $script:proc.WaitForExit(10000) | Out-Null
    }
    [WhiteProbe]::Shutdown()
    Release-Lease
    $log.Dispose()
    Remove-Item -Recurse -Force $dataRoot -ErrorAction SilentlyContinue
}

try {
    [WhiteProbe]::Init(192, 108)
    Emit ("{""event"":""seed"",""profile"":""$Mode"",""media"":{""spirals"":$spiralCount,""images"":$imageCount,""videos"":$videoCount}}")

    $script:proc = [WhiteProbe]::Launch((Resolve-Path $exe).Path, $dataRoot, $outPath, $errPath)
    $childPid = $script:proc.Id
    Write-Output "launched pid=$childPid profile=$Mode with an isolated data root"
    Emit ("{""event"":""launch"",""pid"":$childPid}")

    $deadline = [Diagnostics.Stopwatch]::StartNew()
    $window = $null
    while ($deadline.Elapsed.TotalSeconds -lt 90 -and $null -eq $window) {
        Start-Sleep -Milliseconds 500
        if ($script:proc.HasExited) {
            Emit ("{""event"":""exit"",""phase"":""startup"",""code"":$($script:proc.ExitCode)}")
            Write-Output "EXITED during startup, code $($script:proc.ExitCode) - see $errPath"
            Cleanup; exit 2
        }
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $childPid)
        $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    }
    if ($null -eq $window) { Emit '{"event":"fail","why":"no main window in 90s"}'; Cleanup; exit 1 }
    $hwnd = [int64]$window.Current.NativeWindowHandle
    Emit ("{""event"":""shell-up"",""hwnd"":""0x$('{0:X}' -f $hwnd)"",""seconds"":$([math]::Round($deadline.Elapsed.TotalSeconds,2))}")
    # The BASELINE: the same reading with the app up and no session running, so every number below
    # is measured against this desktop rather than against an assumption about it.
    Emit ("{""event"":""baseline"",""screen"":$([WhiteProbe]::ScreenTick())}")

    if ($Mode -ne 'idle') {
        $startCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SessionStartButton')
        $start = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $startCond)
        if ($null -eq $start) { Emit '{"event":"fail","why":"no SessionStartButton"}'; Cleanup; exit 1 }
        $r = $start.Current.BoundingRectangle
        [WhiteProbe]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))

        $statusCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SessionStatusText')
        $status = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCond)
        $statusText = if ($null -ne $status) { $status.Current.Name } else { '' }
        Emit ("{""event"":""started"",""status"":""$($statusText -replace '"','')""}")
        Write-Output "status after the click: '$statusText'"
        if ($statusText -notmatch 'running') { Write-Output 'FAIL: the shell does not report a running session'; Cleanup; exit 1 }
    }

    $run = [Diagnostics.Stopwatch]::StartNew()
    $tick = 0
    $peak = 0.0
    $escalations = 0
    while ($run.Elapsed.TotalSeconds -lt $Seconds) {
        if ($script:proc.HasExited) {
            Emit ("{""event"":""exit"",""phase"":""observation"",""atSeconds"":$([math]::Round($run.Elapsed.TotalSeconds,2)),""code"":$($script:proc.ExitCode)}")
            Write-Output "EXITED at $([math]::Round($run.Elapsed.TotalSeconds,1))s, code $($script:proc.ExitCode) - see $errPath"
            break
        }
        $s = [WhiteProbe]::ScreenTick()
        $tick++
        $parsed = $s | ConvertFrom-Json
        if ($parsed.nearWhite -gt $peak) { $peak = $parsed.nearWhite }
        $line = "{""t"":$([math]::Round($run.Elapsed.TotalSeconds,2)),""screen"":$s"
        # Bounded escalation: the census and the per-window reads are expensive enough to perturb the
        # thing being measured, and six of them are plenty to name an owner.
        if ($parsed.nearWhite -ge $WhiteThreshold -and $escalations -lt 6) {
            $escalations++
            $wp = $parsed.whitePoint
            $line += ",""windows"":$([WhiteProbe]::Windows(40000))"
            $line += ",""owner"":$([WhiteProbe]::OwnerAt([int]$wp[0], [int]$wp[1]))"
        }
        $line += '}'
        Emit $line
        Start-Sleep -Milliseconds 200
    }

    $peakText = $peak.ToString([cultureinfo]::InvariantCulture)
    Emit ("{""event"":""done"",""ticks"":$tick,""peakNearWhite"":$peakText,""escalations"":$escalations,""exited"":$(if($script:proc.HasExited){'true'}else{'false'})}")
    Write-Output "ticks=$tick peakNearWhite=$peakText escalations=$escalations exited=$($script:proc.HasExited)"
}
finally {
    Cleanup
    Write-Output "log: $logPath"
    Write-Output "stderr: $errPath"
}
