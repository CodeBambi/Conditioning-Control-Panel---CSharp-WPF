using System;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>One sample of a playing leash video.</summary>
public readonly record struct LeashWatchSample(double CurrentSeconds, double DurationSeconds, bool Visible);

/// <summary>Why the runner went idle on its own (never for a <c>Cancel</c> or <c>Park</c> the host asked for).</summary>
public enum LeashTaskStop
{
    /// <summary>The thing behind the task stopped: the player ended the session, switched the
    /// bubbles off, or closed the task's video window.</summary>
    ActivityStopped,
    /// <summary>The task could not go on: the next lock card would not open (no phrases enabled).</summary>
    CouldNotContinue,
    /// <summary>The video never played (nothing playable within <see cref="LeashPlayability.Deadline"/>,
    /// a 404, an age wall) or the page reported a playback error.</summary>
    Unplayable,
}

/// <summary>The runner stopped by itself. <paramref name="Id"/> is the pid, or the aid for a
/// video assignment (<paramref name="Assignment"/> true).</summary>
public sealed record LeashTaskStopped(string Id, bool Assignment, LeashTaskStop Reason);

/// <summary>
/// The app features a gate task drives. <see cref="AppLeashTaskHost"/> is the real one; tests
/// hand in a fake. Nothing here may enable Strict Lock or touch the panic key.
/// </summary>
public interface ILeashTaskHost
{
    /// <summary>Lifetime count of finished lock cards (test cards never count).</summary>
    int LockCardsCompleted { get; }
    bool LockCardOpen { get; }
    /// <summary>Put one lock card up. False when it could not (no phrases enabled, no service).</summary>
    bool ShowLockCard();

    bool SessionRunning { get; }
    /// <summary>Start a leash session of <paramref name="minutes"/>: <see cref="PunishKind.Pink"/>
    /// wears the pink filter the whole way, <see cref="PunishKind.Detention"/> runs the player's
    /// own effects. False when it could not start.</summary>
    bool StartSession(PunishKind kind, int minutes);
    /// <summary>Stop the running session. Only ever asked for a session this runner started
    /// (<see cref="LeashTaskRunner.ParkAndStopItsSession"/>); a session the player started stays theirs.</summary>
    void StopSession();

    event Action? BubblePopped;
    /// <summary>Make sure bubbles are floating. True when this call started them (the runner
    /// then stops them when it is done).</summary>
    bool StartBubbles();
    void StopBubbles();
    /// <summary>True while bubbles float (the player's own or the task's).</summary>
    bool BubblesRunning { get; }

    /// <summary>Open the video. False when it cannot be opened here (a catalogue id with no file).</summary>
    bool OpenWatch(LeashWatch watch);
    /// <summary>The playing video, or null while it is not on screen yet.</summary>
    LeashWatchSample? SampleWatch(LeashWatch watch);
    /// <summary>A catalogue enhancement played through (its own completion, no sampling).</summary>
    event Action<LeashWatch>? WatchFinished;
    /// <summary>The page said the video cannot play (a failed load, a 404, a media error).</summary>
    event Action<LeashWatch>? WatchFailed;
    /// <summary>False once the watch's window is gone (closed by the player, not by <see cref="EndWatch"/>).
    /// True while it is up, and whenever this host cannot tell.</summary>
    bool WatchOpen { get; }
    /// <summary>True when the watch plays in the sampled cage (a Hypnotube page or a local file),
    /// so "nothing ever played" can be judged. False for the Deeper player.</summary>
    bool WatchCaged { get; }
    void EndWatch();
}

/// <summary>When a caged leash video counts as one that will not play. Pure.</summary>
public static class LeashPlayability
{
    /// <summary>No playing video by this long after the window opened = it will not play.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    /// <summary>A sample of a real, running video: a known length and a position past zero.</summary>
    public static bool Playing(LeashWatchSample s) =>
        double.IsFinite(s.DurationSeconds) && s.DurationSeconds > 0
        && double.IsFinite(s.CurrentSeconds) && s.CurrentSeconds > 0;

    public static bool GiveUp(DateTimeOffset openedAt, DateTimeOffset now, bool everPlayed) =>
        !everPlayed && now - openedAt >= Deadline;
}

/// <summary>
/// Starts and tracks one gate task at a time (CONTRACT "The gate"): N lock cards, a pink session of
/// N minutes, N bubbles popped, a detention session of N minutes, a video watched through. Raises
/// <see cref="Progress"/>(pid, done, total) and <see cref="Completed"/>(pid) on the UI thread. It
/// never posts <c>complete</c>: the gate host does, on <see cref="Completed"/>. Never runs a
/// Chaster punishment (that one books itself).
///
/// <para>Units: lines and bubbles count items, sessions count whole minutes, a video counts percent
/// (0..100).</para>
///
/// <para>No limbo (2026-09-28): when the thing behind a task stops (the session ended, the bubbles
/// went off, the window closed, the next lock card would not open, the video never played) the
/// runner goes idle by itself and says so through <see cref="Stopped"/>; the task stays pending and
/// the gate comes back. A stop, and a <see cref="Park"/> (panic), keep the progress: a later
/// <see cref="Start"/> of the same punishment carries on from there. <see cref="Cancel"/> forgets it.</para>
/// </summary>
public sealed class LeashTaskRunner : ConditioningControlPanel.Controls.Leash.ILeashTaskRunner
{
    /// <summary>A session counts as done this close to its full length (start-up takes a moment).</summary>
    public static int SessionSlackSeconds(int totalSeconds) => Math.Max(15, totalSeconds * 3 / 100);

    /// <summary>A lock card is put up again at most this often while the task waits for the next.</summary>
    public static readonly TimeSpan LockCardRetry = TimeSpan.FromSeconds(3);

    /// <summary>How long a started session or bubbles may take to show up before their absence
    /// counts as "stopped".</summary>
    public static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(10);

    private readonly ILeashTaskHost _host;
    private readonly Func<DateTimeOffset> _now;
    private DispatcherTimer? _timer;

    private Punishment? _task;
    private Assignment? _watchAssignment;
    private LeashWatch? _watch;
    private LeashWatchMeter? _meter;
    private bool _watchFinished;
    private int _baseline;
    private int _count;
    private double _sessionSeconds;
    private bool _startedSession;
    private DateTimeOffset _lastTick;
    private DateTimeOffset _lastShow = DateTimeOffset.MinValue;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _watchOpenedAt;
    private bool _everPlayed;
    private bool _sawActivity;
    private bool _startedBubbles;
    private int _lastDone = -1;
    private bool _resetting;

    /// <summary>Progress kept for a punishment that was parked or stopped (one at a time).</summary>
    private sealed record Kept(string Pid, int Count, double SessionSeconds, LeashWatchMeter? Meter, bool StartedSession = false);
    private Kept? _kept;

    public LeashTaskRunner(ILeashTaskHost host, Func<DateTimeOffset>? now = null)
    {
        _host = host;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _host.BubblePopped += OnBubblePopped;
        _host.WatchFinished += OnWatchFinished;
        _host.WatchFailed += OnWatchFailed;
    }

    public bool IsRunning => _task != null || _watchAssignment != null;

    public string? RunningPid => _task?.Pid;

    /// <summary>What the running punishment asks for, or null (no task, or a video assignment).</summary>
    public PunishKind? RunningKind => _task?.Kind;

    /// <summary>The open video assignment being watched, or null.</summary>
    public string? RunningAid => _watchAssignment?.Aid;

    /// <summary>(pid, done, total).</summary>
    public event Action<string, int, int>? Progress;

    /// <summary>The task is done; the gate host posts <c>complete</c>.</summary>
    public event Action<string>? Completed;

    /// <summary>The runner went idle by itself; the task (or assignment) is still open.</summary>
    public event Action<LeashTaskStopped>? Stopped;

    /// <inheritdoc />
    public bool LastCompletionCapped { get; private set; }

    /// <summary>A video assignment's verified watch finished (aid). The app hands it to
    /// <c>LeashService.NoteAssignmentWatched</c>.</summary>
    public event Action<string>? AssignmentWatched;

    /// <summary>Start (or resume) a gate task. False when it cannot start now: a Chaster kind, a
    /// different task already running, or the feature would not start.</summary>
    public bool Start(Punishment p)
    {
        if (p == null || p.Kind == PunishKind.Chaster) return false;
        if (_watchAssignment != null) return false;
        if (_task != null && _task.Pid != p.Pid) return false;
        var resume = _task?.Pid == p.Pid;
        if (!resume) Reset(keep: false);
        var kept = !resume && _kept?.Pid == p.Pid ? _kept : null;
        _task = p;
        _lastTick = _now();
        _startedAt = _lastTick;
        _sawActivity = false;
        bool ok;
        switch (p.Kind)
        {
            case PunishKind.Lines:
                if (!resume)
                {
                    _count = kept?.Count ?? 0;
                    _baseline = _host.LockCardsCompleted - _count;
                }
                ok = _host.LockCardOpen || ShowCard();
                break;
            case PunishKind.Pink:
            case PunishKind.Detention:
                if (kept != null) _sessionSeconds = kept.SessionSeconds;
                if (_host.SessionRunning)
                {
                    // adopted, not started: the player's own session stays theirs, unless this is the
                    // session the runner started before a park
                    if (!resume) _startedSession = kept?.StartedSession == true;
                    ok = true;
                }
                else
                {
                    ok = _host.StartSession(p.Kind, p.Size);
                    _startedSession = ok;
                }
                break;
            case PunishKind.Bubbles:
                if (!resume)
                {
                    _count = kept?.Count ?? 0;
                    _startedBubbles = _host.StartBubbles();
                }
                ok = true;
                break;
            case PunishKind.Video:
                if (kept?.Meter != null) _meter = kept.Meter;
                ok = p.Watch != null && OpenWatch(p.Watch, resume || kept?.Meter != null, LeashVideoCap.Clamp(p.Size));
                break;
            default:
                ok = false;
                break;
        }
        if (!ok)
        {
            if (!resume) Reset(keep: kept != null);
            return false;
        }
        _kept = null;
        EnsureTimer();
        Report();
        return true;
    }

    /// <summary>Open a video assignment and watch it for real. One thing at a time.</summary>
    public bool StartAssignmentWatch(Assignment a)
    {
        if (a == null || a.Kind != AssignKind.Video || a.Watch == null || a.Status != AssignStatus.Open) return false;
        if (_task != null) return false;
        var resume = _watchAssignment?.Aid == a.Aid;
        if (_watchAssignment != null && !resume) return false;
        if (!resume) Reset(keep: false);
        _watchAssignment = a;
        _lastTick = _now();
        _startedAt = _lastTick;
        if (!OpenWatch(a.Watch, resume)) { if (!resume) Reset(keep: false); return false; }
        EnsureTimer();
        return true;
    }

    /// <summary>Stop tracking and forget the progress (a cut, the leash ended, the task went
    /// away). Anything the runner started for bubbles is stopped; a session the player is in
    /// keeps running (it is theirs now).</summary>
    public void Cancel()
    {
        Reset(keep: false);
        _kept = null;
    }

    /// <summary>Stop tracking but keep the progress for this punishment (a panic press): the
    /// window closes, bubbles the task started stop, and nothing comes back by itself.</summary>
    public void Park() => Reset(keep: true);

    /// <summary>A panic press while the panic itself is switched off (owner, 2026-09-30): park, and
    /// stop the session only when this runner started it. A session the player started keeps running.</summary>
    public void ParkAndStopItsSession()
    {
        var mine = _startedSession && _host.SessionRunning;
        Reset(keep: true);
        if (mine) { try { _host.StopSession(); } catch { } }
    }

    /// <summary>True while the running task's session is one this runner started.</summary>
    public bool StartedItsSession => _startedSession;

    /// <summary>One step. The app's timer calls it every second; tests call it by hand.</summary>
    public void Tick()
    {
        var now = _now();
        var dt = Math.Clamp((now - _lastTick).TotalSeconds, 0, 2.5);
        _lastTick = now;

        if (_watchAssignment is { } a)
        {
            if (StepWatch(a.Watch!, now) is { } stop) { StopSelf(a.Aid, true, stop); return; }
            if (_meter?.IsComplete == true || _watchFinished)
            {
                var aid = a.Aid;
                Reset(keep: false);
                AssignmentWatched?.Invoke(aid);
            }
            return;
        }

        if (_task is not { } p) return;
        LeashTaskStop? stopped = null;
        switch (p.Kind)
        {
            case PunishKind.Lines:
                _count = Math.Max(0, _host.LockCardsCompleted - _baseline);
                if (_count < p.Size && !_host.LockCardOpen && now - _lastShow >= LockCardRetry && !ShowCard())
                    stopped = LeashTaskStop.CouldNotContinue;
                break;
            case PunishKind.Pink:
            case PunishKind.Detention:
                if (_host.SessionRunning) { _sessionSeconds += dt; _sawActivity = true; }
                else if (Gone(now)) stopped = LeashTaskStop.ActivityStopped;
                break;
            case PunishKind.Bubbles:
                if (_host.BubblesRunning) _sawActivity = true;
                else if (Gone(now)) stopped = LeashTaskStop.ActivityStopped;
                break;
            case PunishKind.Video:
                stopped = StepWatch(p.Watch!, now);
                break;
        }
        // Count what was done up to now first: the last card or minute may have just landed.
        Report();
        if (stopped is { } why && _task?.Pid == p.Pid) StopSelf(p.Pid, false, why);
    }

    /// <summary>True when the activity was seen and is gone, or never showed up within the grace.</summary>
    private bool Gone(DateTimeOffset now) => _sawActivity || now - _startedAt >= StartGrace;

    private void StopSelf(string id, bool assignment, LeashTaskStop why)
    {
        Reset(keep: !assignment);
        try { Stopped?.Invoke(new LeashTaskStopped(id, assignment, why)); }
        catch (Exception ex) { App.Logger?.Debug("Leash stop handler failed: {E}", ex.Message); }
    }

    private bool ShowCard()
    {
        _lastShow = _now();
        return _host.ShowLockCard();
    }

    private bool OpenWatch(LeashWatch w, bool resume, int capMinutes = 0)
    {
        if (!resume || _meter == null)
        {
            _meter = new LeashWatchMeter { CapSeconds = capMinutes > 0 ? capMinutes * 60 : null };
            _watchFinished = false;
        }
        _watch = w;
        _watchOpenedAt = _now();
        _everPlayed = false;
        return _host.OpenWatch(w);
    }

    /// <summary>One sample. Null = carry on; a value = the watch cannot go on.</summary>
    private LeashTaskStop? StepWatch(LeashWatch w, DateTimeOffset now)
    {
        if (_watchFinished) return null;
        if (_host.SampleWatch(w) is { } s)
        {
            if (LeashPlayability.Playing(s)) _everPlayed = true;
            _meter?.Sample(s.CurrentSeconds, s.DurationSeconds, s.Visible);
        }
        if (_meter?.IsComplete == true) return null;
        if (!_host.WatchOpen) return LeashTaskStop.ActivityStopped;
        if (_host.WatchCaged && LeashPlayability.GiveUp(_watchOpenedAt, now, _everPlayed)) return LeashTaskStop.Unplayable;
        return null;
    }

    private void OnBubblePopped()
    {
        if (_task?.Kind != PunishKind.Bubbles) return;
        _count++;
        _sawActivity = true;
        Report();
    }

    private void OnWatchFinished(LeashWatch w)
    {
        if (_watch == null || w.Kind != _watch.Kind || w.Id != _watch.Id) return;
        _watchFinished = true;
        if (_watchAssignment != null) Tick();
        else Report();
    }

    private void OnWatchFailed(LeashWatch w)
    {
        if (_watch == null || w.Kind != _watch.Kind || w.Id != _watch.Id || _watchFinished || _everPlayed) return;
        if (_watchAssignment is { } a) StopSelf(a.Aid, true, LeashTaskStop.Unplayable);
        else if (_task is { } p) StopSelf(p.Pid, false, LeashTaskStop.Unplayable);
    }

    private (int Done, int Total) Measure(Punishment p) => p.Kind switch
    {
        PunishKind.Lines or PunishKind.Bubbles => (Math.Min(_count, p.Size), p.Size),
        PunishKind.Pink or PunishKind.Detention => SessionMeasure(p.Size),
        PunishKind.Video => (_watchFinished ? 100 : _meter?.IsComplete == true ? 100 : Math.Min(99, _meter?.Percent ?? 0), 100),
        _ => (0, 1),
    };

    private (int, int) SessionMeasure(int minutes)
    {
        var total = minutes * 60;
        if (_sessionSeconds >= total - SessionSlackSeconds(total)) return (minutes, minutes);
        return (Math.Min(minutes - 1, (int)(_sessionSeconds / 60)), minutes);
    }

    private void Report()
    {
        if (_task is not { } p) return;
        var (done, total) = Measure(p);
        if (done != _lastDone)
        {
            _lastDone = done;
            Progress?.Invoke(p.Pid, done, total);
        }
        if (done >= total)
        {
            var pid = p.Pid;
            LastCompletionCapped = p.Kind == PunishKind.Video && _meter?.CapReached == true;
            Reset(keep: false);
            if (_kept?.Pid == pid) _kept = null;
            Completed?.Invoke(pid);
        }
    }

    private void Reset(bool keep)
    {
        // EndWatch closes the window, which can call back in (a closed window, a failed page).
        if (_resetting) return;
        _resetting = true;
        try
        {
            if (keep && _task is { } p) _kept = new Kept(p.Pid, _count, _sessionSeconds, _meter, _startedSession);
            var endWatch = _watch != null;
            var stopBubbles = _startedBubbles;
            _task = null;
            _watchAssignment = null;
            _watch = null;
            _meter = null;
            _watchFinished = false;
            _baseline = 0;
            _count = 0;
            _sessionSeconds = 0;
            _startedSession = false;
            _startedBubbles = false;
            _sawActivity = false;
            _everPlayed = false;
            _lastDone = -1;
            _lastShow = DateTimeOffset.MinValue;
            _timer?.Stop();
            if (stopBubbles) { try { _host.StopBubbles(); } catch { } }
            if (endWatch) { try { _host.EndWatch(); } catch { } }
        }
        finally { _resetting = false; }
    }

    private void EnsureTimer()
    {
        if (System.Windows.Application.Current == null) return;
        if (_timer == null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) =>
            {
                try { Tick(); }
                catch (Exception ex) { App.Logger?.Debug("Leash task tick failed: {E}", ex.Message); }
            };
        }
        _timer.Start();
    }
}
