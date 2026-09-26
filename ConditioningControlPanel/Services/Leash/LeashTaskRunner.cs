using System;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>One sample of a playing leash video.</summary>
public readonly record struct LeashWatchSample(double CurrentSeconds, double DurationSeconds, bool Visible);

/// <summary>
/// The app features a gate task drives. <see cref="AppLeashTaskHost"/> is the real one; tests
/// hand in a fake. Nothing here may enable Strict Lock or touch the panic key.
/// </summary>
public interface ILeashTaskHost
{
    /// <summary>Lifetime count of finished lock cards (test cards never count).</summary>
    int LockCardsCompleted { get; }
    bool LockCardOpen { get; }
    /// <summary>Put one lock card up. False when it could not.</summary>
    bool ShowLockCard();

    bool SessionRunning { get; }
    /// <summary>Start a leash session of <paramref name="minutes"/>: <see cref="PunishKind.Pink"/>
    /// wears the pink filter the whole way, <see cref="PunishKind.Detention"/> runs the player's
    /// own effects. False when it could not start.</summary>
    bool StartSession(PunishKind kind, int minutes);

    event Action? BubblePopped;
    /// <summary>Make sure bubbles are floating. True when this call started them (the runner
    /// then stops them when it is done).</summary>
    bool StartBubbles();
    void StopBubbles();

    /// <summary>Open the video. False when it cannot be opened here (a catalogue id with no file).</summary>
    bool OpenWatch(LeashWatch watch);
    /// <summary>The playing video, or null while it is not on screen yet.</summary>
    LeashWatchSample? SampleWatch(LeashWatch watch);
    /// <summary>A catalogue enhancement played through (its own completion, no sampling).</summary>
    event Action<LeashWatch>? WatchFinished;
    void EndWatch();
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
/// <para>Merge seam: the UI lane's <c>Controls.Leash.ILeashTaskRunner</c> has exactly this public
/// shape; this class implements it once both lanes merge (one line on the declaration).</para>
/// </summary>
public sealed class LeashTaskRunner : ConditioningControlPanel.Controls.Leash.ILeashTaskRunner
{
    /// <summary>A session counts as done this close to its full length (start-up takes a moment).</summary>
    public static int SessionSlackSeconds(int totalSeconds) => Math.Max(15, totalSeconds * 3 / 100);

    /// <summary>A lock card is put up again at most this often while the task waits for the next.</summary>
    public static readonly TimeSpan LockCardRetry = TimeSpan.FromSeconds(3);

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
    private DateTimeOffset _lastTick;
    private DateTimeOffset _lastShow = DateTimeOffset.MinValue;
    private bool _startedBubbles;
    private int _lastDone = -1;

    public LeashTaskRunner(ILeashTaskHost host, Func<DateTimeOffset>? now = null)
    {
        _host = host;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _host.BubblePopped += OnBubblePopped;
        _host.WatchFinished += OnWatchFinished;
    }

    public bool IsRunning => _task != null || _watchAssignment != null;

    public string? RunningPid => _task?.Pid;

    /// <summary>(pid, done, total).</summary>
    public event Action<string, int, int>? Progress;

    /// <summary>The task is done; the gate host posts <c>complete</c>.</summary>
    public event Action<string>? Completed;

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
        if (!resume) Reset();
        _task = p;
        _lastTick = _now();
        bool ok;
        switch (p.Kind)
        {
            case PunishKind.Lines:
                if (!resume) _baseline = _host.LockCardsCompleted;
                ok = _host.LockCardOpen || ShowCard();
                break;
            case PunishKind.Pink:
            case PunishKind.Detention:
                ok = _host.SessionRunning || _host.StartSession(p.Kind, p.Size);
                break;
            case PunishKind.Bubbles:
                if (!resume) _startedBubbles = _host.StartBubbles();
                ok = true;
                break;
            case PunishKind.Video:
                ok = p.Watch != null && OpenWatch(p.Watch, resume, LeashVideoCap.Clamp(p.Size));
                break;
            default:
                ok = false;
                break;
        }
        if (!ok)
        {
            if (!resume) Reset();
            return false;
        }
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
        if (!resume) Reset();
        _watchAssignment = a;
        if (!OpenWatch(a.Watch, resume)) { if (!resume) Reset(); return false; }
        EnsureTimer();
        return true;
    }

    /// <summary>Stop tracking. Anything the runner started for bubbles is stopped; a session the
    /// player is in keeps running (it is theirs now).</summary>
    public void Cancel() => Reset();

    /// <summary>One step. The app's timer calls it every second; tests call it by hand.</summary>
    public void Tick()
    {
        var now = _now();
        var dt = Math.Clamp((now - _lastTick).TotalSeconds, 0, 2.5);
        _lastTick = now;

        if (_watchAssignment is { } a)
        {
            if (StepWatch(a.Watch!))
            {
                var aid = a.Aid;
                Reset();
                AssignmentWatched?.Invoke(aid);
            }
            return;
        }

        if (_task is not { } p) return;
        switch (p.Kind)
        {
            case PunishKind.Lines:
                _count = Math.Max(0, _host.LockCardsCompleted - _baseline);
                if (_count < p.Size && !_host.LockCardOpen && now - _lastShow >= LockCardRetry) ShowCard();
                break;
            case PunishKind.Pink:
            case PunishKind.Detention:
                if (_host.SessionRunning) _sessionSeconds += dt;
                break;
            case PunishKind.Video:
                StepWatch(p.Watch!);
                break;
        }
        Report();
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
            _watch = w;
            _meter = new LeashWatchMeter { CapSeconds = capMinutes > 0 ? capMinutes * 60 : null };
            _watchFinished = false;
        }
        return _host.OpenWatch(w);
    }

    /// <summary>True when the watch is done.</summary>
    private bool StepWatch(LeashWatch w)
    {
        if (_watchFinished) return true;
        if (_host.SampleWatch(w) is { } s) _meter?.Sample(s.CurrentSeconds, s.DurationSeconds, s.Visible);
        return _meter?.IsComplete == true;
    }

    private void OnBubblePopped()
    {
        if (_task?.Kind != PunishKind.Bubbles) return;
        _count++;
        Report();
    }

    private void OnWatchFinished(LeashWatch w)
    {
        if (_watch == null || w.Kind != _watch.Kind || w.Id != _watch.Id) return;
        _watchFinished = true;
        if (_watchAssignment != null) Tick();
        else Report();
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
            Reset();
            Completed?.Invoke(pid);
        }
    }

    private void Reset()
    {
        if (_startedBubbles) { try { _host.StopBubbles(); } catch { } }
        if (_watch != null) { try { _host.EndWatch(); } catch { } }
        _task = null;
        _watchAssignment = null;
        _watch = null;
        _meter = null;
        _watchFinished = false;
        _baseline = 0;
        _count = 0;
        _sessionSeconds = 0;
        _startedBubbles = false;
        _lastDone = -1;
        _lastShow = DateTimeOffset.MinValue;
        _timer?.Stop();
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
