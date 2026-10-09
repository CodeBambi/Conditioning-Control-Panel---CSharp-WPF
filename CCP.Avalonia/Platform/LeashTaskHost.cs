// PORTED from WPF 7.1.5 Services/Leash/AppLeashTaskHost.cs + the App.xaml.cs / MainWindow.Leash.cs
// wiring: the real features a leash gate task drives, and the Core LeashTaskRunner on top of them.
// A video (Hypnotube, or a catalogue entry whose media is a local file) plays caged in
// LeashPunishWindow; a catalogue entry with no local file opens in the Deeper player. Nothing here
// enables Strict Lock, touches the panic key or asks for a strict lock card.
// Panic: a surface PREPENDED to PanicSurfaces.All (WPF: LeashOnPanicPress is the first thing a panic
// press does) parks the runner and closes the video window at once; the rest of the ladder then takes
// down lock cards and the engine. A cut (LeashHead.CutDone) or the leash ending cancels the task and
// closes the window.
// Head differences: no session-from-remote on this head (RemoteCommands refuses session verbs), so a
// pink / detention task cannot start a session (StartSession answers false and the runner reports it
// could not continue); the port's Deeper player raises no completion event, so a catalogue watch in
// it never finishes by itself; the video clock is sampled by a script poll (SampleWatch reads the last
// poll) instead of WPF's BrowserVideoTimeSource.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Platform;

internal sealed class LeashTaskHost : ILeashTaskHost, IDisposable
{
    /// <summary>Bubbles per minute while a bubble task runs and the player had them off.</summary>
    public const int BubblesPerMinute = 20;

    /// <summary>WPF App.LeashRunner. Null until <see cref="Start"/>. The main session sets
    /// <c>LeashLocator.Runner = () => LeashTaskHost.Runner</c> on merge.</summary>
    internal static LeashTaskRunner? Runner { get; private set; }

    internal static LeashTaskHost? Host { get; private set; }

    internal const string PanicSurfaceId = "leash-task";

    // ---- startup ------------------------------------------------------------------------------

    /// <summary>WPF App.xaml.cs LEASH: the host, the runner, assignment receipts, the cut and the
    /// panic hooks. Safe to call more than once (the first call wins).</summary>
    internal static LeashTaskRunner Start(LeashService? service)
    {
        if (Runner != null) return Runner;
        LeashHead.Seed();   // the runner's UI timer factory
        Host = new LeashTaskHost();
        Runner = new LeashTaskRunner(Host);
        if (service != null) Runner.AssignmentWatched += service.NoteAssignmentWatched;
        LeashHead.CutDone += () => OnUi(() => EndTask("cut"));
        LeashHead.LeashedChanged += on => { if (!on) OnUi(() => EndTask("leash ended")); };
        HookPanic();
        return Runner;
    }

    /// <summary>Puts the leash stop at the front of the panic registry (tray, panic key, blink stop and the
    /// safe word all run PanicSurfaces.StopAll). Idempotent.</summary>
    internal static void HookPanic()
    {
        // Right after "intake", which the registry keeps first on purpose (the mic chain ends before
        // anything can react), and before every other stop: WPF runs LeashOnPanicPress first.
        var all = PanicSurfaces.All.ToList();
        if (all.Any(s => s.Id == PanicSurfaceId)) return;
        var at = all.FindIndex(s => s.Id == "intake") + 1;   // 0 when there is no intake entry
        all.Insert(at, new PanicSurfaces.Surface(PanicSurfaceId, _ => OnPanicPress(panicRuns: true)));
        PanicSurfaces.All = all.ToArray();
    }

    /// <summary>WPF LeashOnPanicPress: the runner parks with its progress (nothing it drives comes
    /// back by itself), the video window closes. The punishment stays pending. With
    /// <paramref name="panicRuns"/> false (panic switched off or Lockdown holding the keys; panic
    /// always works on a leash) the leash's own session stops too.</summary>
    internal static void OnPanicPress(bool panicRuns) => OnPanicPressCore(panicRuns);

    /// <summary>The gate's half of a panic press (panic runs, a task was live): the shell snoozes
    /// and hides its LeashGateCard. Set by MainShellWindow.InitializeLeash.</summary>
    internal static Action<bool, bool>? GatePanic { get; set; }

    private static void OnPanicPressCore(bool panicRuns)
    {
        var r = Runner;
        bool live = false;
        try
        {
            live = r?.IsRunning == true || LeashPunishWindow.Current != null;
            if (live)
                Serilog.Log.Information("Leash: panic press stops the running task (kept pending, panic runs={Runs})", panicRuns);
            if (panicRuns) r?.Park();
            else r?.ParkAndStopItsSession();
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash park failed: {E}", ex.Message); }
        LeashPunishWindow.CloseNow();
        // WPF: the gate stands back ten minutes (LeashUiRules.PanicSnooze) and hides.
        try { GatePanic?.Invoke(panicRuns, live); }
        catch (Exception ex) { Serilog.Log.Debug("Leash gate stand-back failed: {E}", ex.Message); }
    }

    /// <summary>WPF EndLeashTask: cancel whatever the runner drives and close its window. Idempotent.</summary>
    internal static void EndTask(string why)
    {
        try
        {
            if (Runner?.IsRunning == true || LeashPunishWindow.Current != null)
                Serilog.Log.Information("Leash: task ended ({Why})", why);
            Runner?.Cancel();
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash cancel failed: {E}", ex.Message); }
        LeashPunishWindow.CloseNow();
    }

    private static void OnUi(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }

    /// <summary>Test seam: forget the runner and the host (tests only).</summary>
    internal static void ResetForTests()
    {
        Host?.Dispose();
        Host = null;
        Runner = null;
    }

    // ---- the host -----------------------------------------------------------------------------

    private string? _watchPath;
    private bool _windowed;
    private LeashWatch? _watching;
    private int _lockCardsCompleted;
    private LeashWatchSample? _sample;
    private DispatcherTimer? _sampler;
    private bool _sampling;

    public event Action? BubblePopped;
    public event Action<LeashWatch>? WatchFinished;
    public event Action<LeashWatch>? WatchFailed;

    internal LeashTaskHost()
    {
        LeashPunishWindow.VideoEnded += OnWindowVideoEnded;
        LeashPunishWindow.PlaybackFailed += OnWindowPlaybackFailed;
        LockCardWindow.Completed += OnLockCardCompleted;
        CoreTubeEvents.BubblePopped += OnPopped;
    }

    private void OnWindowPlaybackFailed()
    {
        if (_watching is { } w && WatchCaged) WatchFailed?.Invoke(w);
    }

    private void OnWindowVideoEnded()
    {
        if (_watching is { } w) WatchFinished?.Invoke(w);
    }

    private void OnLockCardCompleted() => _lockCardsCompleted++;

    private void OnPopped() => BubblePopped?.Invoke();

    // ---- lock cards ----

    /// <summary>WPF reads Achievements TotalLockCardsCompleted; this head counts completed real cards
    /// itself (LockCardWindow.Completed). The runner only ever compares against its own baseline.</summary>
    public int LockCardsCompleted => _lockCardsCompleted;

    public bool LockCardOpen
    {
        get { try { return LockCardWindow.IsAnyOpen(); } catch { return false; } }
    }

    /// <summary>Never strict, never a test card. A card that cannot open (no phrase enabled) says
    /// so here, or the task would wait on it forever.</summary>
    public bool ShowLockCard()
    {
        try
        {
            if (global::ConditioningControlPanel.Services.LockCardScheduler.EnabledPhrases().Count == 0)
            {
                Serilog.Log.Information("Leash lock card: no phrases enabled");
                return false;
            }
            LockCardWindow.ShowNext(isTest: false);
            return true;
        }
        catch (Exception ex) { Serilog.Log.Warning("Leash lock card failed: {E}", ex.Message); return false; }
    }

    // ---- sessions ----

    public bool SessionRunning => CoreEngine.IsRunning;

    /// <summary>OWED on this head: WPF starts a leash_pink / leash_detention session through
    /// StartSessionFromRemote, which this head does not have. False = the runner reports it.</summary>
    public bool StartSession(PunishKind kind, int minutes)
    {
        Serilog.Log.Information("Leash session ({Kind}, {Min} min): no session-from-remote on this head", kind, minutes);
        return false;
    }

    /// <summary>Nothing to stop: this head never starts a leash session (see StartSession).</summary>
    public void StopSession() { }

    // ---- bubbles ----

    public bool StartBubbles()
    {
        try
        {
            if (Views.Overlays.BubbleOverlay.IsRunning) return false;
            if (MainShellWindow.Current is not { } host) return false;
            Views.Overlays.BubbleOverlay.Start(host, BubblesPerMinute);
            return Views.Overlays.BubbleOverlay.IsRunning;
        }
        catch (Exception ex) { Serilog.Log.Warning("Leash bubbles failed: {E}", ex.Message); return false; }
    }

    public bool BubblesRunning
    {
        get { try { return Views.Overlays.BubbleOverlay.IsRunning; } catch { return false; } }
    }

    public void StopBubbles()
    {
        try { Views.Overlays.BubbleOverlay.Stop(); }
        catch (Exception ex) { Serilog.Log.Debug("Leash bubbles stop failed: {E}", ex.Message); }
    }

    // ---- video ----

    /// <summary>Test seam: opens the Deeper player on a catalogue file (default: the shell's player).</summary>
    internal static Action<string> OpenDeeper { get; set; } = path => MainShellWindow.Current?.PlayDeeperLibraryEntry(path);

    public bool OpenWatch(LeashWatch watch)
    {
        if (!LeashGrammar.ValidWatch(watch)) return false;
        EndWatch();
        _watching = watch;
        try
        {
            var (holder, locked) = WhoAndWhy(watch);
            if (watch.Kind == "ht")
            {
                var url = LandingRules.HtUrl(watch.Id);
                var ok = url != null && LeashPunishWindow.OpenUrl(url, holder, locked);
                if (ok) StartSampler();
                return ok;
            }
            var path = CataloguePath(watch.Id);
            if (path == null) return false;
            _watchPath = path;
            if (LocalMediaFor(path) is { } media)
            {
                _windowed = true;
                var ok = LeashPunishWindow.OpenFile(media, holder, locked);
                if (ok) StartSampler();
                return ok;
            }
            OpenDeeper(path);
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Leash watch failed: {E}", ex.Message);
            return false;
        }
    }

    /// <summary>The holder's name for the window's head, and whether this watch is a pending
    /// punishment (locked: the window will not close) or a task (it closes normally).</summary>
    internal static (string Holder, bool Locked) WhoAndWhy(LeashWatch watch)
    {
        try
        {
            var me = LeashHead.Service?.Snapshot.Me;
            if (me == null) return ("", false);
            foreach (var p in me.Pending)
                if (p.Kind == PunishKind.Video && p.Watch != null && p.Watch.Kind == watch.Kind && p.Watch.Id == watch.Id)
                    return (me.Holder.Name, true);
            return (me.Holder.Name, false);
        }
        catch { return ("", false); }
    }

    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".m4v", ".mov", ".mkv" };

    /// <summary>The video a catalogue enhancement plays when it is a local file: its
    /// <c>mediaSource</c> when that names one, else a video beside it with the same base name.
    /// Null = let the Deeper player handle it.</summary>
    internal static string? LocalMediaFor(string enhancementPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(enhancementPath) ?? "";
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(enhancementPath));
            var src = (string?)(json["mediaSource"] ?? json["MediaSource"]);
            if (!string.IsNullOrWhiteSpace(src) && src != "*")
            {
                var full = Path.IsPathRooted(src) ? src : Path.Combine(dir, src);
                if (IsVideo(full) && File.Exists(full)) return full;
            }
            var stem = Path.GetFileName(enhancementPath);
            var cut = stem.IndexOf('.');
            if (cut > 0) stem = stem[..cut];
            foreach (var ext in VideoExtensions)
            {
                var sibling = Path.Combine(dir, stem + ext);
                if (File.Exists(sibling)) return sibling;
            }
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash local media lookup failed: {E}", ex.Message); }
        return null;
    }

    private static bool IsVideo(string path) =>
        Array.IndexOf(VideoExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

    /// <summary>The local file for a catalogue id, the same lookup a friend's watch uses.</summary>
    private static string? CataloguePath(string catalogueId)
    {
        var subs = CoreSettings.Current?.DeeperSubmissions;
        if (subs == null) return null;
        foreach (var kv in subs)
            if (string.Equals(kv.Value?.CatalogueId, catalogueId, StringComparison.Ordinal) && File.Exists(kv.Key))
                return kv.Key;
        return null;
    }

    /// <summary>The page's main video, as "current,duration" (WPF BrowserVideoTimeSource reads the same).</summary>
    internal const string SampleScript =
        "(function(){var b=null,s=-1;document.querySelectorAll('video').forEach(function(x){var r=x.getBoundingClientRect();"
        + "var k=(r.width*r.height)+(isFinite(x.duration)?x.duration*1000:0);if(k>s){b=x;s=k;}});"
        + "return b?(b.currentTime+','+b.duration):'';})()";

    private void StartSampler()
    {
        _sample = null;
        _sampler?.Stop();
        _sampler = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sampler.Tick += async (_, _) =>
        {
            if (_sampling) return;
            var view = LeashPunishWindow.Current?.View;
            if (view == null) { _sample = null; return; }
            _sampling = true;
            try { _sample = ParseSample(await view.InvokeScriptAsync(SampleScript), LeashPunishWindow.Watchable); }
            catch { _sample = null; }
            finally { _sampling = false; }
        };
        _sampler.Start();
    }

    /// <summary>"12.5,300" (a script result may arrive JSON-quoted) to a sample; null when no video.</summary>
    internal static LeashWatchSample? ParseSample(string? raw, bool visible)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim().Trim('"');
        var parts = t.Split(',');
        if (parts.Length != 2) return null;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var cur)) return null;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dur)) dur = double.NaN;
        return new LeashWatchSample(cur, dur, visible);
    }

    public LeashWatchSample? SampleWatch(LeashWatch watch)
    {
        // A catalogue entry in the Deeper player finishes by its own event (none on this head yet).
        if (watch.Kind != "ht" && !_windowed) return null;
        if (LeashPunishWindow.Current == null) return null;
        return _sample is { } s ? s with { Visible = LeashPunishWindow.Watchable } : null;
    }

    /// <summary>A caged watch is open while its window is; the Deeper player cannot be told apart
    /// from the player's own use of it, so that one always reads open.</summary>
    public bool WatchOpen => _watching != null && (!WatchCaged || LeashPunishWindow.Current != null);

    public bool WatchCaged => _watching is { } w && (w.Kind == "ht" || _windowed);

    public void EndWatch()
    {
        _watching = null;
        _watchPath = null;
        _windowed = false;
        _sampler?.Stop();
        _sampler = null;
        _sample = null;
        try { LeashPunishWindow.CloseNow(); } catch { }
    }

    public void Dispose()
    {
        EndWatch();
        LeashPunishWindow.VideoEnded -= OnWindowVideoEnded;
        LeashPunishWindow.PlaybackFailed -= OnWindowPlaybackFailed;
        LockCardWindow.Completed -= OnLockCardCompleted;
        CoreTubeEvents.BubblePopped -= OnPopped;
    }
}
