using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace CcpSpike.VideoHandoff;

/// <summary>
/// Windows browser→native handoff layer (Step 3). SP-011-pattern Avalonia host with the
/// SP-011-admitted Avalonia.Controls.WebView 12.0.1 (WebView2 on Windows). Per matrix row:
/// page-side DISCOVERY (InvokeScript on the live DOM) → TRANSFER to the native decoder →
/// decode-event-verified playback or a typed limitation. Presentation stays out of scope.
/// </summary>
public static class Browser
{
    public static Task<int> RunAsync(Lab lab, string scratch)
    {
        // Avalonia/WebView2 need an STA thread; async Main continuations are thread-pool (MTA).
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure(() => new BrowserApp(lab, scratch))
                    .UsePlatformDetect()
                    .StartWithClassicDesktopLifetime(Array.Empty<string>());
                tcs.TrySetResult(_code);
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static int _code = 3;

    public static void Finish(int code)
    {
        _code = code;
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lt)
            lt.Shutdown(code);
    }
}

public sealed class BrowserApp : Application
{
    private readonly Lab _lab;
    private readonly string _scratch;

    public BrowserApp(Lab lab, string scratch) { _lab = lab; _scratch = scratch; }

    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        base.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            new BrowserWindow(_lab, _scratch).Show();
        base.OnFrameworkInitializationCompleted();
    }
}

public sealed class BrowserWindow : Window
{
    private readonly Lab _lab;
    private readonly NativeWebView _web;
    private readonly TaskCompletionSource _navTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _pass, _fail;

    public BrowserWindow(Lab lab, string scratch)
    {
        _lab = lab;
        Title = "SP-018 video handoff spike";
        Width = 900; Height = 640;
        _web = new NativeWebView();
        Content = _web;

        _web.EnvironmentRequested += (_, args) =>
        {
            args.EnableDevTools = false;
            if (args is WindowsWebView2EnvironmentRequestedEventArgs wv2)
            {
                wv2.UserDataFolder = Path.Combine(scratch, "wv2-profile");
                SpikeLog.Line("browser", $"WebView2 env UserDataFolder={wv2.UserDataFolder}");
            }
        };
        _web.AdapterCreated += (_, _) => SpikeLog.Line("browser", $"AdapterCreated info={SafeAdapterInfo()}");
        _web.NavigationCompleted += (_, e) =>
        {
            SpikeLog.Line("browser", $"NavigationCompleted success={e.IsSuccess}");
            _navTcs.TrySetResult();
        };

        Opened += async (_, _) =>
        {
            try { await RunScenarios(); }
            catch (Exception ex)
            {
                SpikeLog.Line("browser", $"runner threw {ex.GetType().Name}: {ex.Message}");
                _fail++;
            }
            SpikeLog.Line("browser", $"browser-matrix done pass={_pass} fail={_fail}");
            Browser.Finish(_fail == 0 ? 0 : 1);
        };
    }

    private string SafeAdapterInfo()
    {
        try { return _web.AdapterInfo?.ToString() ?? "(null)"; }
        catch (Exception ex) { return $"(AdapterInfo threw {ex.GetType().Name})"; }
    }

    private async Task RunScenarios()
    {
        using var probe = new ProbeLease();

        // B1 — target-site shape (owned loopback page): discover <video> src → transfer → decode events.
        await Scenario("B1-site-direct", async () =>
        {
            var d = await Discover(_lab.Url("/page/site.html"));
            Require(d.Protocol == "http:" && d.Src.Contains("/media/clip.mp4", StringComparison.Ordinal),
                $"unexpected discovery {d.Src}");
            var r = await probe.RunAsync(d.Src);
            Require(r.Outcome == ProbeOutcome.Success, $"decode {r.Outcome}");
            return $"discovered+transferred {d.Src} → {r.VideoTrack} frames={r.FramesDecoded} end={r.EndReached}";
        });

        // B2 — expiring signed URL, valid: discovery carries the FULL signed URL → decode events.
        await Scenario("B2-site-signed-valid", async () =>
        {
            var d = await Discover(_lab.Url("/page/site-signed.html?ttl=300"));
            Require(d.Src.Contains("/signed/clip.mp4", StringComparison.Ordinal), $"unexpected {d.Src}");
            var r = await probe.RunAsync(d.Src);
            Require(r.Outcome == ProbeOutcome.Success, $"decode {r.Outcome}");
            return "valid signed URL transferred → decode-verified";
        });

        // B3 — expiring signed URL, expired: typed limitation at transfer, no retry-storm.
        await Scenario("B3-site-signed-expired", async () =>
        {
            var d = await Discover(_lab.Url("/page/site-signed.html?ttl=-60"));
            var r = await probe.RunAsync(d.Src);
            Require(r.Outcome == ProbeOutcome.SourceExpired, $"expected SourceExpired got {r.Outcome}");
            return "expired signed URL → typed source-expired (one decoder open, no retry)";
        });

        // B4 — cookie-gated: direct decoder open fails at the gate (negative control);
        // host-owned credential transfers via relay → decode events.
        await Scenario("B4-site-cookie", async () =>
        {
            var d = await Discover(_lab.Url("/page/site-gated.html?kind=cookie"));
            Require(d.Src.Contains("/gated-cookie/", StringComparison.Ordinal), $"unexpected {d.Src}");
            var direct = await probe.RunAsync(d.Src);
            Require(direct.Outcome == ProbeOutcome.AuthRequired, $"negative control {direct.Outcome}");
            var relay = await probe.RunAsync(d.Src.Replace("/gated-cookie/", "/relay/cookie/", StringComparison.Ordinal));
            Require(relay.Outcome == ProbeOutcome.Success, $"relay {relay.Outcome}");
            return "direct=auth-required (control); relay-mediated=decode-verified (proxy-mediated auth, pending-owner)";
        });

        // B5 — custom-header-gated: same shape.
        await Scenario("B5-site-header", async () =>
        {
            var d = await Discover(_lab.Url("/page/site-gated.html?kind=header"));
            Require(d.Src.Contains("/gated-header/", StringComparison.Ordinal), $"unexpected {d.Src}");
            var direct = await probe.RunAsync(d.Src);
            Require(direct.Outcome == ProbeOutcome.AuthRequired, $"negative control {direct.Outcome}");
            var relay = await probe.RunAsync(d.Src.Replace("/gated-header/", "/relay/header/", StringComparison.Ordinal));
            Require(relay.Outcome == ProbeOutcome.Success, $"relay {relay.Outcome}");
            return "direct=auth-required (control); relay-mediated=decode-verified (proxy-mediated auth, pending-owner)";
        });

        // B6 — blob:/MSE: detection is DOM-observed (protocol read off the live element);
        // typed blob-untransferable, NEVER browser-fullscreen/capture fallback.
        await Scenario("B6-blob-mse", async () =>
        {
            var d = await Discover(_lab.Url("/page/blob.html"), waitFor: "blob-src-set");
            Require(d.Protocol == "blob:", $"expected blob: got {d.Protocol} ({d.Src})");
            var mse = d.Logs.FirstOrDefault(l => l.StartsWith("mse-", StringComparison.Ordinal)) ?? "mse-outcome-absent";
            SpikeLog.Line("browser", $"B6 typed-limitation blob-untransferable detection=protocol:{d.Protocol} page-log=[{string.Join(" | ", d.Logs)}] — no decoder attempt, no capture/mirror fallback");
            return $"blob: detected on live DOM → typed blob-untransferable; {mse}";
        });

        // B7 — DRM: EME usage observed (requestMediaKeySystemAccess resolved) → typed
        // drm-detected limitation; no bypass/key-extraction/capture attempted (asserted).
        await Scenario("B7-drm-eme", async () =>
        {
            var d = await Discover(_lab.Url("/page/drm.html"), waitFor: "eme-");
            var granted = d.Logs.Any(l => l.StartsWith("eme-keysystem-access-granted", StringComparison.Ordinal));
            Require(granted, $"EME usage not observed: [{string.Join(" | ", d.Logs)}]");
            SpikeLog.Line("browser", $"B7 typed-limitation drm-detected evidence=EME-usage page-log=[{string.Join(" | ", d.Logs)}] — NO bypass/key-extraction/capture attempted");
            return "EME signaling detected → typed drm-detected (no bypass attempted, asserted)";
        });
    }

    private async Task Scenario(string id, Func<Task<string>> run)
    {
        try
        {
            var detail = await run();
            _pass++;
            SpikeLog.Line("browser", $"row {id} pass=True detail={detail}");
        }
        catch (Exception ex)
        {
            _fail++;
            SpikeLog.Line("browser", $"row {id} pass=False detail={ex.Message}");
        }
    }

    private sealed record Discovery(string Src, string Protocol, string[] Logs);

    private async Task<Discovery> Discover(string pageUrl, string? waitFor = null)
    {
        // Fresh nav gate per page.
        var nav = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, WebViewNavigationCompletedEventArgs e) => nav.TrySetResult();
        _web.NavigationCompleted += Handler;
        try
        {
            SpikeLog.Line("browser", $"navigate {Redact.Scrub(pageUrl)}");
            _web.Navigate(new Uri(pageUrl));
            var done = await Task.WhenAny(nav.Task, Task.Delay(15000));
            if (done != nav.Task) throw new InvalidOperationException("navigation timeout");

            const string script = """
                (function(){
                  var v = document.querySelector('video');
                  var src = v ? (v.currentSrc || v.src) : null;
                  var proto = null;
                  try { proto = src ? new URL(src).protocol : null; } catch(e) {}
                  var logs = Array.from(document.querySelectorAll('.spike-log')).map(function(d){ return d.textContent; });
                  return JSON.stringify({src: src, protocol: proto, logs: logs});
                })()
                """;

            // Poll for async pages (blob fetch / EME resolution) up to ~7.5s.
            for (var attempt = 0; ; attempt++)
            {
                var raw = await _web.InvokeScript(script);
                SpikeLog.Line("browser", $"discovery raw={Redact.Scrub(raw ?? "null")}");
                // InvokeScript returns the result JSON-encoded; our script returns a JSON string (double-encoded).
                var inner = JsonSerializer.Deserialize<string>(raw ?? "null")
                    ?? throw new InvalidOperationException("empty discovery result");
                using var doc = JsonDocument.Parse(inner);
                var src = doc.RootElement.GetProperty("src").GetString() ?? "";
                var proto = doc.RootElement.GetProperty("protocol").GetString() ?? "";
                var logs = doc.RootElement.GetProperty("logs").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                if (waitFor is null || logs.Any(l => l.Contains(waitFor, StringComparison.Ordinal)) || attempt >= 30)
                    return new Discovery(src, proto, logs);
                await Task.Delay(250);
            }
        }
        finally
        {
            _web.NavigationCompleted -= Handler;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>Probe lifetime wrapper so the browser runner reads cleanly.</summary>
    private sealed class ProbeLease : IDisposable
    {
        private readonly Probe _probe = new();
        public Task<ProbeReport> RunAsync(string url) => _probe.RunAsync(url);
        public void Dispose() { } // Finding V3: no libvlc teardown — process hard-exits after flush.
    }
}
