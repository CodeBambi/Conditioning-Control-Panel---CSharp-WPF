using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// M0 spike driver for the DtRH browser-game port (launched via <c>--dtrh-spike</c>).
/// THROWAWAY: proves the virtual-host pipeline before M1 — (a) Range-seek on a real video from
/// the user's assets through https://ccp.assets/, (b) cross-origin media → WebGL texture,
/// (c) an input-receiving fullscreen WebView2 window with a topmost "payload" window landing
/// above it and focus returning after it closes. Results land in logs/dtrh-spike.json and the
/// app log; the app shuts down when done so the harness can read the file.
/// </summary>
internal static class DtrhSpike
{
    private static ChaosWebViewHost? _host;
    private static readonly List<object> _results = new();
    private static bool _pagePass;

    public static void Run()
    {
        try
        {
            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            var assetsRoot = App.EffectiveAssetsPath;
            App.Logger?.Information("DtrhSpike: webRoot={W} assets={A}", webRoot, assetsRoot);

            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = "https://ccp.game/dtrh/spike.html",
                PrimaryHost = "ccp.game",
                Mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
                {
                    ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                    ("ccp.assets", assetsRoot, CoreWebView2HostResourceAccessKind.Allow),
                },
                UserDataFolderName = "browser_data_dtrh",
                InputEnabled = true,
                LogTag = "DtrhSpike",
                OnReady = OnPageReady,
                OnMessage = OnPageMessage,
                OnProcessFailed = kind => Record("process-failed", false, kind.ToString()),
            });
            _host.Show();

            // Safety net: if the page never completes, still write results + exit.
            var overall = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            overall.Tick += (_, _) => { overall.Stop(); Record("overall-timeout", false, "spike did not finish in 60s"); Finish(); };
            overall.Start();
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "DtrhSpike.Run failed");
            Record("spike-boot", false, ex.Message);
            Finish();
        }
    }

    private static void OnPageReady()
    {
        try
        {
            var assetsRoot = App.EffectiveAssetsPath;
            string? image = FirstFile(Path.Combine(assetsRoot, "images"), new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" });
            // Prefer the LARGEST browser-decodable video so Range-seek is a real test.
            string? video = LargestFile(Path.Combine(assetsRoot, "videos"), new[] { ".mp4", ".webm", ".m4v" });
            Record("asset-discovery", image != null || video != null,
                $"image={(image == null ? "none" : Path.GetFileName(image))} video={(video == null ? "none" : Path.GetFileName(video) + " " + new FileInfo(video).Length / (1024 * 1024) + "MB")}");

            _host?.Post(new
            {
                type = "spike-run",
                image = image == null ? null : ToAssetUrl(assetsRoot, image),
                video = video == null ? null : ToAssetUrl(assetsRoot, video),
            });
        }
        catch (Exception ex) { Record("spike-run-post", false, ex.Message); Finish(); }
    }

    private static void OnPageMessage(JObject o)
    {
        switch ((string?)o["type"])
        {
            case "spike-result":
                Record((string?)o["name"] ?? "?", (bool?)o["ok"] ?? false, (string?)o["detail"] ?? "");
                break;
            case "spike-done":
                _pagePass = (bool?)o["pass"] ?? false;
                App.Logger?.Information("DtrhSpike: page tests done (pass={P})", _pagePass);
                RunZOrderAndFocusTest();
                break;
            case "spike-pointer":
                App.Logger?.Information("DtrhSpike: pointer hit page at {X},{Y}", o["x"], o["y"]);
                break;
        }
    }

    /// <summary>Spike (c): a topmost window born AFTER the game surface must land above it
    /// (that is exactly how flash/video payload windows will cover the game), and closing it
    /// plus FocusWeb() must put Win32 foreground focus back on the game window.</summary>
    private static void RunZOrderAndFocusTest()
    {
        try
        {
            var gameWin = _host?.Window;
            if (gameWin == null) { Record("zorder-payload-above", false, "no host window"); Finish(); return; }
            var gameH = new WindowInteropHelper(gameWin).Handle;

            var payload = new Window
            {
                WindowStyle = WindowStyle.None,
                Topmost = true,   // payload windows are born topmost LATER -> must stack above the game
                ShowInTaskbar = false,
                Background = Brushes.HotPink,
                Left = 200, Top = 200, Width = 400, Height = 300,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            payload.Show();
            var payH = new WindowInteropHelper(payload).Handle;

            bool above = IsAboveInZOrder(payH, gameH);
            Record("zorder-payload-above", above, above ? "payload window stacks above the game surface" : "payload landed BELOW the game surface");

            // Live smoke: fire a real flash payload over the game (visual only, best effort).
            try { App.Flash?.TriggerFlashOnce(amount: 1, duration: 1200, suppressHaptic: true); } catch { }

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                try { payload.Close(); } catch { }
                _host?.FocusWeb();
                var t2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                t2.Tick += (_, _) =>
                {
                    t2.Stop();
                    var fg = GetForegroundWindow();
                    bool focused = fg == gameH;
                    Record("focus-return", focused, focused ? "foreground back on game surface after payload close" : $"foreground is 0x{fg.ToInt64():X}, expected game window");
                    Finish();
                };
                t2.Start();
            };
            t.Start();
        }
        catch (Exception ex) { Record("zorder-focus-test", false, ex.Message); Finish(); }
    }

    private static void Record(string name, bool ok, string detail)
    {
        _results.Add(new { name, ok, detail });
        if (ok) App.Logger?.Information("DtrhSpike PASS {Name}: {Detail}", name, detail);
        else App.Logger?.Warning("DtrhSpike FAIL {Name}: {Detail}", name, detail);
    }

    private static bool _finished;
    private static void Finish()
    {
        if (_finished) return;
        _finished = true;
        try
        {
            var outPath = Path.Combine(AppContext.BaseDirectory, "logs", "dtrh-spike.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, JsonConvert.SerializeObject(new
            {
                at = DateTime.Now.ToString("s"),
                pagePass = _pagePass,
                results = _results,
            }, Formatting.Indented));
            App.Logger?.Information("DtrhSpike: results written to {P}", outPath);
        }
        catch (Exception ex) { App.Logger?.Error(ex, "DtrhSpike.Finish write failed"); }
        try { _host?.Dispose(); } catch { }
        // Spike launch is headless-ish: shut the app down so the harness can read the file.
        try { Application.Current?.Shutdown(); } catch { }
    }

    // -------- helpers --------

    private static string? FirstFile(string dir, string[] exts) =>
        !Directory.Exists(dir) ? null :
        Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()));

    private static string? LargestFile(string dir, string[] exts) =>
        !Directory.Exists(dir) ? null :
        Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
            .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();

    private static string ToAssetUrl(string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var escaped = string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
        return "https://ccp.assets/" + escaped;
    }

    private static bool IsAboveInZOrder(IntPtr candidate, IntPtr reference)
    {
        // Walk upward (toward the top) from reference; if we meet candidate, it is above.
        var h = reference;
        for (int i = 0; i < 512; i++)
        {
            h = GetWindow(h, GW_HWNDPREV);
            if (h == IntPtr.Zero) return false;
            if (h == candidate) return true;
        }
        return false;
    }

    private const uint GW_HWNDPREV = 3;
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
