using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Services.Remix;

/// <summary>What the roll decided for this flash.</summary>
public enum JackpotRemixStep
{
    /// <summary>An ordinary flash.</summary>
    None,
    /// <summary>A built file is ready: show it and clear the carried roll.</summary>
    Play,
    /// <summary>The roll landed (or was carried) with nothing built: carry it and ask for ONE build.</summary>
    RequestBuild,
    /// <summary>Still carried, build already asked for: ordinary flash, no new request.</summary>
    Wait,
}

/// <summary>The roll's memory between flashes. Plain fields so tests can poke it.</summary>
public sealed class JackpotRemixRollState
{
    /// <summary>A hit that found no built file. Consumed by the next flash that finds one.</summary>
    public bool Carried;
    /// <summary>One build per carried roll: set with Carried, cleared with it.</summary>
    public bool BuildRequested;
    /// <summary>The seed of the roll being carried (the eight are a draw, not the first eight).</summary>
    public int Seed;
}

/// <summary>
/// The pure half of <see cref="JackpotRemixDirector"/>: eligibility, the 1-in-N roll, the carried
/// flag, the quiet-window and memory gates, source selection and the centred geometry. No App,
/// no disk, no WebView2, so JackpotRemixRollTests cover every rule.
/// </summary>
public static class JackpotRemixRoll
{
    public const int DefaultOdds = 100;
    /// <summary>DEBUG only: <c>CCP_JACKPOT_ODDS=N</c> makes it 1 in N (N=1 is every flash).</summary>
    public const string OddsEnvVar = "CCP_JACKPOT_ODDS";
    /// <summary>Skip the prebuild below this much free physical memory: a cold 8x12 MB build peaks near 1.5 GB.</summary>
    public const ulong MemoryFloorBytes = 1536UL * 1024 * 1024;
    /// <summary>The remix's shorter side as a fraction of the monitor's shorter side.</summary>
    public const double SizeFraction = 0.65;
    public const int EdgePadding = 24;

    public static int ResolveOdds(string? env)
        => int.TryParse(env, out var n) && n >= 1 ? n : DefaultOdds;

    public static bool Eligible(bool owned, bool enabled, MotionLevel level, int gifCount)
        => owned && enabled && level != MotionLevel.Off && gifCount > 0;

    /// <summary>1 in <paramref name="odds"/>; odds below 1 are read as 1.</summary>
    public static bool Hit(Random rng, int odds) => rng.Next(Math.Max(1, odds)) == 0;

    /// <summary>Advance the roll state for one flash. Ineligible flashes never touch the state.</summary>
    public static JackpotRemixStep Step(JackpotRemixRollState state, bool eligible, bool hit, bool hasBuiltFile, int seed)
    {
        if (!eligible) return JackpotRemixStep.None;
        if (!hit && !state.Carried) return JackpotRemixStep.None;
        if (hasBuiltFile)
        {
            state.Carried = false;
            state.BuildRequested = false;
            return JackpotRemixStep.Play;
        }
        if (state.Carried) return JackpotRemixStep.Wait;
        state.Carried = true;
        state.BuildRequested = true;
        state.Seed = seed;
        return JackpotRemixStep.RequestBuild;
    }

    /// <summary>The flash that consumed a file failed to load it: the roll is spent, not carried again.</summary>
    public static void Clear(JackpotRemixRollState state)
    {
        state.Carried = false;
        state.BuildRequested = false;
    }

    /// <summary>Quiet means nothing on screen that a build could stutter: no flash up, no video playing.</summary>
    public static bool Quiet(bool flashShowing, bool videoPlaying) => !flashShowing && !videoPlaying;

    public static bool MemoryPressure(ulong availablePhysicalBytes, ulong floorBytes = MemoryFloorBytes)
        => availablePhysicalBytes < floorBytes;

    /// <summary>
    /// Build now? Owned and enabled, nothing cached, nothing in flight, quiet, memory to spare.
    /// A carried roll wants the build too, but under the same gates: never during a video.
    /// </summary>
    public static bool ShouldBuild(bool eligible, bool cached, bool inFlight, bool quiet, bool memoryPressure)
        => eligible && !cached && !inFlight && quiet && !memoryPressure;

    /// <summary>
    /// The flash pool's gifs (extension only; stills and videos never go to the page), shuffled
    /// by <paramref name="seed"/>, over-cap files dropped, cycled to eight. Empty means no remix.
    /// </summary>
    public static IReadOnlyList<string> SelectSources(IEnumerable<string> pool, int seed, Func<string, long> sizeOf)
    {
        var gifs = (pool ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p)
                        && string.Equals(Path.GetExtension(p), ".gif", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rng = new Random(seed);
        for (int i = gifs.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (gifs[i], gifs[j]) = (gifs[j], gifs[i]);
        }
        var usable = JackpotRemixPlan.FilterBySize(gifs, sizeOf);
        return JackpotRemixPlan.Cycle(usable.Take(JackpotRemixPlan.Tiles).ToList());
    }

    /// <summary>
    /// Big and centred: the image's shorter side is <see cref="SizeFraction"/> of the monitor's
    /// shorter side, aspect kept, then clamped inside the padded monitor. Monitor and result in DIPs.
    /// </summary>
    public static (int X, int Y, int W, int H) CentredGeometry(int monX, int monY, int monW, int monH, int imgW, int imgH)
    {
        if (imgW <= 0 || imgH <= 0 || monW <= 0 || monH <= 0) return (monX, monY, Math.Max(1, monW), Math.Max(1, monH));
        double target = Math.Min(monW, monH) * SizeFraction;
        double scale = target / Math.Min(imgW, imgH);
        double maxW = Math.Max(1, monW - 2 * EdgePadding), maxH = Math.Max(1, monH - 2 * EdgePadding);
        scale = Math.Min(scale, Math.Min(maxW / imgW, maxH / imgH));
        int w = Math.Max(1, (int)Math.Round(imgW * scale)), h = Math.Max(1, (int)Math.Round(imgH * scale));
        return (monX + (monW - w) / 2, monY + (monH - h) / 2, w, h);
    }
}

/// <summary>
/// Owns the Jackpot Remix roll and its one-file cache for FlashService. Every flash asks
/// <see cref="TakeForFlash"/>; a roll that lands with nothing built is carried until a file is
/// ready. Builds run off the flash path through <see cref="JackpotRemixBuilder.Default"/>, only
/// when flashes are quiet (no flash up, no video) and memory is not tight, one at a time and
/// never in a loop (a failed build waits <see cref="RetryDelay"/> before another is allowed).
/// </summary>
internal sealed class JackpotRemixDirector : IDisposable
{
    public static readonly TimeSpan PollPeriod = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(10);

    private readonly Func<IReadOnlyList<string>> _gifPool;
    private readonly Func<bool> _flashShowing;
    private readonly Func<bool> _videoPlaying;
    private readonly Func<bool> _enabled;
    private readonly string _outputFolder;
    private readonly int _odds;
    private readonly Random _rng = new();
    private readonly object _gate = new();
    private readonly JackpotRemixRollState _state = new();
    private string? _cached;
    private bool _inFlight;
    private DateTime _nextBuildAllowedUtc = DateTime.MinValue;
    private Timer? _timer;
    private bool _disposed;

    public JackpotRemixDirector(Func<IReadOnlyList<string>> gifPool, Func<bool> flashShowing, Func<bool> videoPlaying,
        Func<bool> enabled, string outputFolder)
    {
        _gifPool = gifPool; _flashShowing = flashShowing; _videoPlaying = videoPlaying; _enabled = enabled;
        _outputFolder = outputFolder;
        _odds = JackpotRemixRoll.ResolveOdds(OddsOverride());
        _cached = NewestLeftover(outputFolder);
    }

    /// <summary>Start polling for a quiet window while flashes run; Stop ends it.</summary>
    public void Start()
    {
        lock (_gate) { _timer ??= new Timer(_ => Poll(), null, PollPeriod, PollPeriod); }
    }

    public void Stop()
    {
        Timer? t;
        lock (_gate) { t = _timer; _timer = null; }
        t?.Dispose();
    }

    private bool Eligible(int gifCount)
        => JackpotRemixRoll.Eligible(PrizeGrants.IsGranted(PrizeGrants.JackpotRemix), _enabled(), MotionFx.Level, gifCount)
           && !JackpotRemixBuilder.Default.Disabled;

    /// <summary>
    /// Roll for this flash. Returns the built file to play, or null for an ordinary flash. A play
    /// takes the file out of the cache; the caller reports a failed load through <see cref="LoadFailed"/>.
    /// </summary>
    public string? TakeForFlash()
    {
        if (_disposed) return null;
        var pool = SafePool();
        JackpotRemixStep step;
        lock (_gate)
        {
            if (_cached != null && !File.Exists(_cached)) _cached = null;
            bool eligible = Eligible(pool.Count);
            bool hit = eligible && JackpotRemixRoll.Hit(_rng, _odds);
            step = JackpotRemixRoll.Step(_state, eligible, hit, _cached != null, _rng.Next());
            if (step != JackpotRemixStep.None)
                App.Logger?.Debug("JackpotRemix: {Step} (hit {Hit}, carried {Carried})", step, hit, _state.Carried);
            if (step == JackpotRemixStep.Play)
            {
                var path = _cached;
                _cached = null;
                return path;
            }
        }
        // A fresh carry asks for its one build now rather than on the next poll tick; Poll still
        // applies every gate (quiet, memory, retry delay), so this is a nudge, not a bypass.
        if (step == JackpotRemixStep.RequestBuild) _ = Task.Run(Poll);
        return null;
    }

    /// <summary>The played file is spent: drop it and keep the folder within the plan's bound.</summary>
    public void Consumed(string path)
    {
        try { File.Delete(path); } catch (Exception ex) { Diag.Swallowed(ex); }
        JackpotRemixPlan.Bound(_outputFolder);
    }

    /// <summary>The flash could not load the file: ordinary flash, roll spent, file gone.</summary>
    public void LoadFailed(string path)
    {
        lock (_gate) JackpotRemixRoll.Clear(_state);
        Consumed(path);
    }

    private void Poll()
    {
        if (_disposed) return;
        IReadOnlyList<string> pool;
        int seed;
        lock (_gate)
        {
            if (_inFlight || DateTime.UtcNow < _nextBuildAllowedUtc) return;
            if (_cached != null && !File.Exists(_cached)) _cached = null;
            pool = SafePool();
            bool quiet = JackpotRemixRoll.Quiet(Safe(_flashShowing), Safe(_videoPlaying));
            if (!JackpotRemixRoll.ShouldBuild(Eligible(pool.Count), _cached != null, _inFlight, quiet,
                    JackpotRemixRoll.MemoryPressure(AvailablePhysicalBytes())))
                return;
            seed = _state.Carried ? _state.Seed : _rng.Next();
            _inFlight = true;
        }
        _ = BuildAsync(pool, seed);
    }

    private async Task BuildAsync(IReadOnlyList<string> pool, int seed)
    {
        string? built = null;
        try
        {
            var sources = JackpotRemixRoll.SelectSources(pool, seed, SizeOf);
            if (sources.Count > 0)
            {
                var result = await JackpotRemixBuilder.Default.BuildAsync(sources, seed, CancellationToken.None).ConfigureAwait(false);
                built = result?.FilePath;
            }
        }
        catch (Exception ex) { App.Logger?.Warning("JackpotRemix: prebuild failed: {E}", ex.Message); }
        lock (_gate)
        {
            _inFlight = false;
            if (built != null) _cached = built;
            else _nextBuildAllowedUtc = DateTime.UtcNow + RetryDelay;
        }
    }

    private IReadOnlyList<string> SafePool()
    {
        try { return _gifPool() ?? Array.Empty<string>(); } catch (Exception ex) { Diag.Swallowed(ex); return Array.Empty<string>(); }
    }

    private static bool Safe(Func<bool> f)
    {
        try { return f(); } catch (Exception ex) { Diag.Swallowed(ex); return true; }
    }

    private static long SizeOf(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : -1; } catch { return -1; }
    }

    private static string? NewestLeftover(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return null;
            return new DirectoryInfo(folder).GetFiles("remix-*.gif").OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName).FirstOrDefault();
        }
        catch (Exception ex) { Diag.Swallowed(ex); return null; }
    }

    private static string? OddsOverride()
    {
#if DEBUG
        try { return Environment.GetEnvironmentVariable(JackpotRemixRoll.OddsEnvVar); } catch { return null; }
#else
        return null;
#endif
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }

    /* ---------------------------------------------------------- memory -----*/

    /// <summary>Free physical memory per GlobalMemoryStatusEx; ulong.MaxValue when the call fails (never a false gate).</summary>
    internal static ulong AvailablePhysicalBytes()
    {
        try
        {
            var s = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref s) ? s.ullAvailPhys : ulong.MaxValue;
        }
        catch { return ulong.MaxValue; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
