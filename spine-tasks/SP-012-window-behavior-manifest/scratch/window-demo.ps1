# SP-012 Step 3 — dashboard window-behavior demonstrator (Windows, headed, observation-only).
# Executes the manifest §3 procedures against the ONE existing greenfield window.
# Zero product-code change; reads window properties via UIA + Win32, captures pixels,
# closes via WM_CLOSE (CloseMainWindow). Artifacts -> spine-tasks/SP-012-window-behavior-manifest/artifacts/
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$taskDir = Split-Path -Parent $PSScriptRoot
$shots = Join-Path $taskDir 'artifacts'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
$exe = Join-Path $PSScriptRoot '..\..\..\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe'
if (-not (Test-Path $exe)) { Write-Output "FAIL: app not built: $exe"; exit 1 }

$native = @'
using System;
using System.Runtime.InteropServices;
public class WinDemo {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder sb, int max);
    public struct RECT { public int Left, Top, Right, Bottom; }
    public const uint GW_OWNER = 4;
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const int WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000, WS_SYSMENU = 0x00080000;
    public const int WS_EX_TOPMOST = 0x00000008, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_APPWINDOW = 0x00040000;
    public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040, SWP_NOACTIVATE = 0x0010;
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    public const byte VK_MENU = 0x12, VK_TAB = 0x09, VK_ESCAPE = 0x1B;
    public const uint KEYUP = 0x0002;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
}
'@
Add-Type -TypeDefinition $native

function Rect-Str($r) { return "($($r.Left),$($r.Top)) $($($r.Right)-$($r.Left))x$($($r.Bottom)-$($r.Top))" }
function Capture-Full([string]$file) {
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
function Capture-Rect($r, [string]$file) {
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
Add-Type -AssemblyName System.Windows.Forms

Write-Output "=== SP-012 dashboard demonstrator (Windows headed) ==="
$proc = Start-Process -FilePath $exe -PassThru
try {
    # wait for main window handle
    $hwnd = [IntPtr]::Zero
    for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Milliseconds 500; $proc.Refresh(); if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $proc.MainWindowHandle; break } }
    if ($hwnd -eq [IntPtr]::Zero) { Write-Output "FAIL: no main window handle"; exit 1 }
    Start-Sleep -Seconds 2  # let startup phases finish rendering

    Write-Output "hwnd: $hwnd"

    # --- PROPERTY READS FIRST (before any raise contaminates topmost state) ---
    $style = [WinDemo]::GetWindowLong($hwnd, [WinDemo]::GWL_STYLE)
    $exstyle = [WinDemo]::GetWindowLong($hwnd, [WinDemo]::GWL_EXSTYLE)
    $owner = [WinDemo]::GetWindow($hwnd, [WinDemo]::GW_OWNER)
    $fg = [WinDemo]::GetForegroundWindow()
    $r = New-Object WinDemo+RECT
    [void][WinDemo]::GetWindowRect($hwnd, [ref]$r)

    Write-Output ("[owner] GW_OWNER handle: " + $owner + "  (0 = no owner => root/standalone)")
    Write-Output ("[modality] procedure defined, not demonstrable on this window (no owner/modal relationship exists)")
    Write-Output ("[activation] foreground window at observation: " + $fg + "  ours: " + $hwnd + "  => " + $(if ($fg -eq $hwnd) { 'ACTIVATED' } else { 'NOT foreground (opens unactivated — SP-007 observed pattern)' }))
    Write-Output ("[topmost] WS_EX_TOPMOST: " + $(if (($exstyle -band [WinDemo]::WS_EX_TOPMOST) -ne 0) { 'PRESENT' } else { 'absent' }))
    Write-Output ("[taskbar] WS_EX_APPWINDOW: " + $(if (($exstyle -band [WinDemo]::WS_EX_APPWINDOW) -ne 0) { 'PRESENT' } else { 'absent' }) + "; WS_EX_TOOLWINDOW: " + $(if (($exstyle -band [WinDemo]::WS_EX_TOOLWINDOW) -ne 0) { 'PRESENT' } else { 'absent' }))
    Write-Output ("[decorations] WS_CAPTION: " + $(if (($style -band [WinDemo]::WS_CAPTION) -eq [WinDemo]::WS_CAPTION) { 'PRESENT' } else { 'absent' }) + "; WS_THICKFRAME: " + $(if (($style -band [WinDemo]::WS_THICKFRAME) -ne 0) { 'PRESENT' } else { 'absent' }) + "; WS_MINIMIZEBOX: " + $(if (($style -band [WinDemo]::WS_MINIMIZEBOX) -ne 0) { 'PRESENT' } else { 'absent' }) + "; WS_MAXIMIZEBOX: " + $(if (($style -band [WinDemo]::WS_MAXIMIZEBOX) -ne 0) { 'PRESENT' } else { 'absent' }) + "; WS_SYSMENU: " + $(if (($style -band [WinDemo]::WS_SYSMENU) -ne 0) { 'PRESENT' } else { 'absent' }))
    Write-Output ("[placement] GetWindowRect: " + (Rect-Str $r))
    Write-Output ("[resize] WS_THICKFRAME " + $(if (($style -band [WinDemo]::WS_THICKFRAME) -ne 0) { 'PRESENT => resizable (style-level)' } else { 'absent => fixed-size' }))

    # UIA: window title + focused element
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($win) {
        Write-Output ("[uia] window title: '" + $win.Current.Name + "'")
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        if ($focused) { Write-Output ("[focus] UIA FocusedElement: '" + $focused.Current.Name + "' (" + $focused.Current.ControlType.ProgrammaticName + ")") } else { Write-Output "[focus] UIA FocusedElement: none" }
    }

    # taskbar button enumeration (Shell_TrayWnd)
    $taskbar = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Shell_TrayWnd')))
    $found = $false
    if ($taskbar) {
        $btns = $taskbar.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
        foreach ($b in $btns) { if ($b.Current.Name -like '*CCP*') { $found = $true; Write-Output ("[taskbar] taskbar button found: '" + $b.Current.Name + "'") } }
    }
    if (-not $found) { Write-Output "[taskbar] no CCP taskbar button found via UIA (recorded honestly)" }

    # raise for pixel + interaction observations (SP-007 harness pattern)
    [void][WinDemo]::SetWindowPos($hwnd, [WinDemo]::HWND_TOPMOST, 0, 0, 0, 0, [WinDemo]::SWP_NOMOVE -bor [WinDemo]::SWP_NOSIZE -bor [WinDemo]::SWP_SHOWWINDOW)
    Start-Sleep -Milliseconds 800
    $r2 = New-Object WinDemo+RECT
    [void][WinDemo]::GetWindowRect($hwnd, [ref]$r2)
    Capture-Rect $r2 (Join-Path $shots 'windows-dashboard-window.png')
    Write-Output ("[capture] windows-dashboard-window.png " + (Rect-Str $r2))

    # Alt-Tab switcher capture: hold ALT, press TAB, capture, ESC cancels, release ALT
    [WinDemo]::keybd_event([WinDemo]::VK_MENU, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 150
    [WinDemo]::keybd_event([WinDemo]::VK_TAB, 0, 0, [IntPtr]::Zero)
    [WinDemo]::keybd_event([WinDemo]::VK_TAB, 0, [WinDemo]::KEYUP, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    Capture-Full (Join-Path $shots 'windows-alttab-switcher.png')
    Write-Output "[alttab] switcher captured (windows-alttab-switcher.png) — presence asserted by review of capture"
    [WinDemo]::keybd_event([WinDemo]::VK_ESCAPE, 0, 0, [IntPtr]::Zero)
    [WinDemo]::keybd_event([WinDemo]::VK_ESCAPE, 0, [WinDemo]::KEYUP, [IntPtr]::Zero)
    [WinDemo]::keybd_event([WinDemo]::VK_MENU, 0, [WinDemo]::KEYUP, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 400

    # resize drag: right edge midpoint, +80px
    $rBefore = New-Object WinDemo+RECT
    [void][WinDemo]::GetWindowRect($hwnd, [ref]$rBefore)
    $ex = $rBefore.Right - 2; $ey = [int](($rBefore.Top + $rBefore.Bottom) / 2)
    [void][WinDemo]::SetCursorPos($ex, $ey)
    Start-Sleep -Milliseconds 200
    [WinDemo]::mouse_event([WinDemo]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    [void][WinDemo]::SetCursorPos($ex + 80, $ey)
    Start-Sleep -Milliseconds 250
    [WinDemo]::mouse_event([WinDemo]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 600
    $rAfter = New-Object WinDemo+RECT
    [void][WinDemo]::GetWindowRect($hwnd, [ref]$rAfter)
    $wBefore = $rBefore.Right - $rBefore.Left; $wAfter = $rAfter.Right - $rAfter.Left
    Write-Output ("[resize] drag right edge +80: width " + $wBefore + " -> " + $wAfter + " => " + $(if ($wAfter -gt $wBefore) { 'RESIZED (behavior-level)' } else { 'no size change (style-level evidence only — recorded honestly)' }))

    # close via WM_CLOSE (SP-010 pattern), record exit code
    [void]$proc.CloseMainWindow()
    $closed = $proc.WaitForExit(15000)
    Write-Output ("[close] CloseMainWindow: " + $(if ($closed) { "exit $($proc.ExitCode)" } else { 'did not exit in 15s' }))
}
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill() }
}
Write-Output "=== demonstrator done ==="
