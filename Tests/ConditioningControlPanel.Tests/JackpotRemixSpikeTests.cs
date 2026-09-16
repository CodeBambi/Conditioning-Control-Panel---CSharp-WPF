#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Remix;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Jackpot Remix spike: runs the real hidden WebView2 builder against gifs on disk and writes
/// the numbers the verdict is judged on. DEBUG only, and skipped unless <c>CCP_REMIX_SPIKE=1</c>,
/// because it needs the WebView2 runtime, a desktop session and about a minute.
///
/// Inputs: every *.gif in <c>CCP_REMIX_INPUTS</c> (default C:\wt-br2\_evidence\fx\remix\inputs),
/// sorted by name. Outputs go to <c>CCP_REMIX_OUT</c> (default the evidence folder): the built
/// gifs, first / middle / last frames as PNG decoded through the SAME SkiaSharp helper
/// FlashService.LoadGifFrames calls, and <c>measurements-run.md</c> with one row per scenario.
/// </summary>
public class JackpotRemixSpikeTests
{
    private static readonly string Inputs = Environment.GetEnvironmentVariable("CCP_REMIX_INPUTS")
        ?? @"C:\wt-br2\_evidence\fx\remix\inputs";
    private static readonly string Out = Environment.GetEnvironmentVariable("CCP_REMIX_OUT")
        ?? @"C:\wt-br2\_evidence\fx\remix";

    private sealed record Scenario(string Name, string[] Files, double Scale, int Seed, bool Uncapped = false, bool Released = false, string? WantLayout = null);

    [Fact]
    public async Task Spike_builds_and_measures()
    {
        if (Environment.GetEnvironmentVariable("CCP_REMIX_SPIKE") != "1")
            Assert.Skip("Set CCP_REMIX_SPIKE=1 to run the WebView2 remix spike.");
        Assert.True(Directory.Exists(Inputs), "inputs folder missing: " + Inputs);
        var all = Directory.GetFiles(Inputs, "*.gif").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Assert.True(all.Length >= 1, "no gifs in " + Inputs);
        Directory.CreateDirectory(Out);

        var samples = all.Where(p => Path.GetFileName(p).StartsWith("sample-", StringComparison.Ordinal)).ToArray();
        var synth = all.Where(p => Path.GetFileName(p).StartsWith("synth-", StringComparison.Ordinal)).ToArray();
        var heavy = all.Where(p => Path.GetFileName(p).StartsWith("heavy-", StringComparison.Ordinal)).ToArray();
        var eight = samples.Concat(synth).Take(8).ToArray();
        // one seed per scenario, so the roll's layout draw is visible across the table
        var scenarios = new List<Scenario>
        {
            new("8-gifs", eight, 1, 4242),
            new("3-cycled", samples.Take(3).ToArray(), 1, 777),
            new("1-gif", samples.Take(1).ToArray(), 1, 31337),
        };
        // heavy: over the cap on purpose, built with the cap lifted, to show what the cap protects from
        if (heavy.Length > 0) scenarios.Add(new("8-heavy-uncapped", heavy.Take(8).ToArray(), 1, 9001, Uncapped: true));
        scenarios.Add(new("8-gifs-scale2", eight, 2, 2024));
        scenarios.Add(new("8-gifs-again", eight, 1, 1999));   // warm host: what a second build costs
        scenarios.Add(new("8-gifs-carousel", eight, 1, 100, WantLayout: "carousel"));   // a dear layout, worst-case encode
        scenarios.Add(new("8-gifs-released", eight, 1, 4242, Released: true));   // the app's default: host dropped after the build

        var pump = StartPump();
        var rows = new StringBuilder();
        rows.AppendLine("| scenario | sources | out | frames | bytes | fetch ms | decode ms | roll ms | encode ms | page ms | host ms | wv2 before MB | wv2 peak MB | wv2 after MB | layout (seed) | decodes via FlashService loader |");
        rows.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        JackpotRemixBuilder? b1 = null, b2 = null, b3 = null, b4 = null, b5 = null;
        try
        {
            foreach (var sc in scenarios)
            {
                var builder = sc.Released ? (b4 ??= Make(pump, 1, release: true))
                    : sc.WantLayout != null ? (b5 ??= Make(pump, 1, want: sc.WantLayout))
                    : sc.Uncapped ? (b3 ??= Make(pump, 1, long.MaxValue))
                    : sc.Scale > 1 ? (b2 ??= Make(pump, 2)) : (b1 ??= Make(pump, 1));
                var sampler = new TreeMemorySampler(() => builder.BrowserProcessId);
                sampler.Start();
                var result = await builder.BuildAsync(sc.Files, seed: sc.Seed, CancellationToken.None);
                await Task.Delay(1500);   // let a released host's processes go before the last sample
                sampler.Stop();
                if (sc.Released) Assert.Equal(0u, builder.BrowserProcessId);   // host gone after the build
                Assert.NotNull(result);
                if (sc.WantLayout != null) Assert.Equal(sc.WantLayout, result!.Layout);
                var r = result!;
                Assert.True(File.Exists(r.FilePath));
                var kept = Path.Combine(Out, sc.Name + ".gif");
                File.Copy(r.FilePath, kept, true);

                // FlashService.LoadGifFrames' exact call (decodeMax 2048 is the loader's ceiling).
                var decoded = AnimatedWebp.DecodeFrames(kept, 2048, maxFrames: 60, maxMemoryMb: 30.0);
                Assert.NotNull(decoded);
                var frames = decoded!.Value.Frames;
                Assert.True(frames.Count > 1, "loader saw a still");
                SavePng(frames[0], Path.Combine(Out, sc.Name + "-first.png"));
                SavePng(frames[frames.Count / 2], Path.Combine(Out, sc.Name + "-middle.png"));
                SavePng(frames[^1], Path.Combine(Out, sc.Name + "-last.png"));

                var t = r.Timings;
                rows.AppendLine($"| {sc.Name} | {sc.Files.Length} ({SrcBytes(sc.Files) / 1024} KB) | {r.Width}x{r.Height} | {r.Frames} | {r.Bytes:N0} | "
                    + $"{t.FetchMs:F0} | {t.DecodeMs:F0} | {t.RollMs:F0} | {t.EncodeMs:F0} | {t.PageTotalMs:F0} | {t.HostTotalMs:F0} | "
                    + $"{sampler.IdleMb:F0} | {sampler.PeakMb:F0} | {sampler.LastMb:F0} | {r.Layout} ({r.SeedUsed}) | yes, {frames.Count} frames at {frames[0].PixelWidth}x{frames[0].PixelHeight}, delay {decoded.Value.FrameDelay.TotalMilliseconds:F0} ms |");
            }
            // cache bound: two builds or more happened on b1, so at most two files remain
            var cacheDir = Path.Combine(Out, "cache");
            Assert.True(Directory.GetFiles(cacheDir, "remix-*.gif").Length <= JackpotRemixPlan.KeepFiles);
        }
        finally
        {
            b1?.Dispose();
            b2?.Dispose();
            b3?.Dispose();
            b4?.Dispose();
            b5?.Dispose();
            pump.InvokeShutdown();
            File.WriteAllText(Path.Combine(Out, "measurements-run.md"),
                "Run " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " on " + Environment.MachineName + "\n\n" + rows);
        }
    }

    private static JackpotRemixBuilder Make(Dispatcher d, double scale, long cap = JackpotRemixPlan.MaxSourceBytes,
        bool release = false, string? want = null)
        => new(new JackpotRemixBuilder.Options
        {
            WebRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web"),
            AssetsRoot = Inputs,
            UserDataFolder = Path.Combine(Out, "browser_data_remix"),
            OutputFolder = Path.Combine(Out, "cache"),
            Dispatcher = d,
            Scale = scale,
            MaxSourceBytes = cap,
            BuildTimeout = TimeSpan.FromMinutes(5),
            // warm hosts on purpose (the app default releases): the spike wants the residual number
            ReleaseHostAfterBuild = release,
            WantLayout = want,
        });

    private static long SrcBytes(IEnumerable<string> files) => files.Sum(f => new FileInfo(f).Length);

    private static void SavePng(BitmapSource frame, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(frame));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>A live STA dispatcher for the hidden window; the builder marshals onto it.</summary>
    private static Dispatcher StartPump()
    {
        Dispatcher? d = null;
        var ready = new ManualResetEventSlim();
        var th = new Thread(() =>
        {
            d = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(d));
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "remix-spike-sta" };
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        ready.Wait();
        return d!;
    }

    /// <summary>Peak private bytes over the WebView2 browser process and every descendant, sampled
    /// every 50 ms; the first sample after the page is up is the idle figure.</summary>
    private sealed class TreeMemorySampler
    {
        private readonly Func<uint> _root;
        private Thread? _th;
        private volatile bool _run;
        public double PeakMb { get; private set; }
        public double IdleMb { get; private set; } = -1;
        /// <summary>The last sample: what the tree still holds once the build is over (0 = host gone).</summary>
        public double LastMb { get; private set; }
        public TreeMemorySampler(Func<uint> root) => _root = root;

        public void Start()
        {
            _run = true;
            _th = new Thread(() =>
            {
                while (_run)
                {
                    var root = _root();
                    var mb = root == 0 ? 0 : TreeBytes(root) / (1024.0 * 1024.0);
                    if (root != 0)
                    {
                        if (IdleMb < 0) IdleMb = mb;
                        if (mb > PeakMb) PeakMb = mb;
                    }
                    LastMb = mb;
                    Thread.Sleep(50);
                }
            }) { IsBackground = true };
            _th.Start();
        }

        public void Stop() { _run = false; _th?.Join(2000); }

        private static long TreeBytes(uint root)
        {
            var parents = Parents();
            var tree = new HashSet<uint> { root };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var (pid, ppid) in parents)
                    if (tree.Contains(ppid) && tree.Add(pid)) grew = true;
            }
            long sum = 0;
            foreach (var pid in tree)
            {
                try { using var p = Process.GetProcessById((int)pid); sum += p.PrivateMemorySize64; }
                catch { /* gone between snapshot and read */ }
            }
            return sum;
        }

        private static List<(uint Pid, uint Ppid)> Parents()
        {
            var list = new List<(uint, uint)>();
            var snap = CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
            try
            {
                var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                if (Process32FirstW(snap, ref e))
                    do { list.Add((e.th32ProcessID, e.th32ParentProcessID)); } while (Process32NextW(snap, ref e));
            }
            finally { CloseHandle(snap); }
            return list;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize; public uint cntUsage; public uint th32ProcessID; public IntPtr th32DefaultHeapID;
            public uint th32ModuleID; public uint cntThreads; public uint th32ParentProcessID; public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }
        [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    }
}
#endif
