using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The "what was the app doing?" half of a hang report.
///
/// WHY THIS EXISTS (program-freeze wave, v6.8.2 — day-14 corner GIF, day-2 lock card, #984):
/// <see cref="UiHangWatchdog"/> already proves WHEN the dispatcher died and (via
/// <see cref="VideoDiag.UiMarkDescription"/>) names the native call it died inside — but only for
/// the handful of compositor/flash/overlay paths that carry a <c>UiScope</c>. Every one of the
/// program freezes reported "(idle)", which tells us only that the wedge is somewhere
/// uninstrumented. A stack (the minidump) without the feature state is half a diagnosis: knowing
/// the UI thread is parked in a bitmap lock does not say whether a corner GIF, a lock card or a
/// program day rollover put it there.
///
/// This class is that missing half: a passive, lock-free record of which features are live and what
/// the app last did, cheap enough to write on ordinary code paths and safe to READ from the
/// watchdog thread while the UI thread is wedged.
///
/// THREADING CONTRACT (must not regress the hang it diagnoses):
///   * <see cref="Note"/> / <see cref="Enter"/> / <see cref="Leave"/> are enqueue-only: one
///     interlocked increment and an array store, or one ConcurrentDictionary write. They never
///     block, never take a lock a reader could hold, never touch the disk and never touch WPF.
///     Safe from the UI thread, threadpool timers and native callback threads alike.
///   * <see cref="Describe"/> runs on the WATCHDOG thread while the UI is presumed dead. It must
///     therefore never take a lock the UI thread could be holding and never touch a
///     DispatcherObject. It reads only bools/ints/strings off already-constructed service
///     instances, each in its own try/catch, so one wedged or half-torn-down service cannot cost
///     us the rest of the report.
/// </summary>
public static class HangContext
{
    /// <summary>Breadcrumbs kept. Small on purpose: the last few actions are what matter.</summary>
    private const int RingSize = 24;

    private static readonly string?[] _ring = new string[RingSize];
    private static int _ringNext = -1;              // Interlocked.Increment => first slot is 0

    /// <summary>
    /// Process start, resolved once. Taken from the OS rather than from this type's static
    /// initializer: HangContext is initialized lazily on its first touch, which can be minutes into
    /// a session, and "uptime=13s" on an app that had been up for an hour would actively mislead the
    /// person reading the report. Falls back to the static-init moment if the OS call fails.
    /// </summary>
    private static readonly long _startTick = ResolveStartTick();

    private static long ResolveStartTick()
    {
        try
        {
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            var upMs = (DateTime.Now - p.StartTime).TotalMilliseconds;
            if (upMs >= 0 && upMs < TimeSpan.FromDays(30).TotalMilliseconds)
                return Environment.TickCount64 - (long)upMs;
        }
        catch { }
        return Environment.TickCount64;
    }

    /// <summary>Feature name -> TickCount64 when it went active. Removal = it went inactive.</summary>
    private static readonly ConcurrentDictionary<string, long> _active = new(StringComparer.Ordinal);

    /// <summary>Milliseconds since the process started (as seen by this class).</summary>
    public static long UptimeMs => Environment.TickCount64 - _startTick;

    /// <summary>
    /// Record one breadcrumb ("what just happened"). Cheap enough for feature start/stop, day
    /// rollovers and dialog shows; NOT for per-frame paths (use <see cref="VideoDiag.UiScope"/>
    /// there, which is two volatile writes and no allocation).
    /// </summary>
    public static void Note(string what)
    {
        try
        {
            if (string.IsNullOrEmpty(what)) return;
            int slot = (int)((uint)Interlocked.Increment(ref _ringNext) % RingSize);
            _ring[slot] = string.Concat(
                DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), " ", what);
        }
        catch { /* diagnostics must never throw into a feature path */ }
    }

    /// <summary>Mark a feature as live. Idempotent; the first Enter wins the timestamp.</summary>
    public static void Enter(string feature)
    {
        try
        {
            if (string.IsNullOrEmpty(feature)) return;
            _active.TryAdd(feature, Environment.TickCount64);
            Note("+ " + feature);
        }
        catch { }
    }

    /// <summary>Mark a feature as no longer live. Safe to call without a matching Enter.</summary>
    public static void Leave(string feature)
    {
        try
        {
            if (string.IsNullOrEmpty(feature)) return;
            if (_active.TryRemove(feature, out _)) Note("- " + feature);
        }
        catch { }
    }

    /// <summary>
    /// <c>using var _ = HangContext.Scope("LockCard.Show");</c> — Enter on construction, Leave on
    /// dispose. Use for a bounded operation; use Enter/Leave directly for a feature that outlives
    /// one call.
    /// </summary>
    public static FeatureScope Scope(string feature) => new(feature);

    public readonly struct FeatureScope : IDisposable
    {
        private readonly string _feature;
        internal FeatureScope(string feature) { _feature = feature; Enter(feature); }
        public void Dispose() => Leave(_feature);
    }

    /// <summary>
    /// The full state block for a hang report. Never throws, never blocks, safe from the watchdog
    /// thread while the UI thread is wedged. Multi-line.
    /// </summary>
    public static string Describe()
    {
        var sb = new StringBuilder(1024);
        try
        {
            sb.Append("uptime=").Append((UptimeMs / 1000.0).ToString("F0", CultureInfo.InvariantCulture)).AppendLine("s");
            sb.Append("lastUiMark=").AppendLine(Safe(() => VideoDiag.UiMarkDescription));
            sb.Append("uiStallMs=").AppendLine(Safe(() => VideoDiag.UiStallMs.ToString(CultureInfo.InvariantCulture)));
            // Input starved while uiStallMs stays small = the thread is BUSY above Input priority,
            // not blocked. That distinction is the whole diagnosis for a "frozen but still
            // repainting" report (ccp-bugs #984/#993/#996/#1001).
            sb.Append("uiInputStallMs=").AppendLine(Safe(() => VideoDiag.UiInputStallMs.ToString(CultureInfo.InvariantCulture)));
            sb.AppendLine("state:");
            foreach (var line in DescribeServices()) sb.Append("  ").AppendLine(line);
            sb.Append("activeFeatures: ").AppendLine(DescribeActive());
            sb.AppendLine("recent:");
            foreach (var crumb in RecentBreadcrumbs()) sb.Append("  ").AppendLine(crumb);
        }
        catch (Exception ex)
        {
            try { sb.AppendLine("(hang context failed: " + ex.Message + ")"); } catch { }
        }
        return sb.ToString();
    }

    /// <summary>One-line form for the Serilog hang line (the log tail a bug report attaches).</summary>
    public static string DescribeCompact()
    {
        try
        {
            return string.Concat(
                "uptime=", (UptimeMs / 1000).ToString(CultureInfo.InvariantCulture), "s",
                " active=[", DescribeActive(), "]",
                " last=", LastBreadcrumb());
        }
        catch { return "(unavailable)"; }
    }

    private static string DescribeActive()
    {
        try
        {
            if (_active.IsEmpty) return "(none)";
            var sb = new StringBuilder();
            long now = Environment.TickCount64;
            foreach (var kv in _active)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kv.Key).Append('(').Append((now - kv.Value) / 1000).Append("s)");
                if (sb.Length > 600) { sb.Append(", ..."); break; }
            }
            return sb.ToString();
        }
        catch { return "(unavailable)"; }
    }

    private static string LastBreadcrumb()
    {
        try
        {
            int next = Volatile.Read(ref _ringNext);
            if (next < 0) return "(none)";
            return _ring[(int)((uint)next % RingSize)] ?? "(none)";
        }
        catch { return "(unavailable)"; }
    }

    /// <summary>Breadcrumbs oldest-first. Tolerates a torn ring (a slot rewritten mid-read).</summary>
    private static string[] RecentBreadcrumbs()
    {
        try
        {
            int next = Volatile.Read(ref _ringNext);
            if (next < 0) return new[] { "(none)" };
            int count = Math.Min(RingSize, next + 1);
            var outp = new string[count];
            for (int i = 0; i < count; i++)
            {
                int idx = (int)((uint)(next - (count - 1 - i)) % RingSize);
                outp[i] = _ring[idx] ?? "(?)";
            }
            return outp;
        }
        catch { return new[] { "(unavailable)" }; }
    }

    /// <summary>
    /// Passive read of the services whose state actually distinguishes the reported freezes from
    /// each other. Every probe is independently try/caught: a service that is null (not yet
    /// constructed, or torn down) or whose getter throws costs one line, not the report.
    /// Deliberately reads plain fields only — no dispatcher marshalling, no locks, no I/O.
    /// </summary>
    private static string[] DescribeServices()
    {
        var lines = new System.Collections.Generic.List<string>(12);

        lines.Add("sessionRunning=" + Safe(() => App.IsSessionRunning.ToString()));

        // Program: the day number is the single most valuable field in these reports — "day 14"
        // and "day 2" are the whole shape of the complaint.
        lines.Add("program=" + Safe(() =>
        {
            var enrollment = App.Programs?.ActiveEnrollment;
            if (enrollment == null) return "(none enrolled)";
            return string.Concat(enrollment.ProgramId, " day=", enrollment.CurrentDay.ToString(CultureInfo.InvariantCulture));
        }));

        lines.Add("lockCardRunning=" + Safe(() => App.LockCard?.IsRunning.ToString() ?? "(null)"));
        lines.Add("cornerGifWindows=" + Safe(() => App.CornerGif?.ActiveWindowCount ?? "(null)"));
        lines.Add("compositor=" + Safe(() => App.Compositor == null ? "(off)" : "on"));

        return lines.ToArray();
    }

    private static string Safe(Func<string> probe)
    {
        try { return probe() ?? "(null)"; }
        catch (Exception ex) { return "(probe failed: " + ex.GetType().Name + ")"; }
    }
}
