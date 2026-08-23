using CcpClient.Desktop.Session;

namespace CcpClient.HeadlessTests;

/// <summary>
/// Both clocks and the tick timer, moved by hand. The scripted run reads a wall clock AND a
/// monotonic one and compares them, so a seam with one clock could not express what it does;
/// this one moves them together, which is what "time passed" means.
///
/// <para>Shared by every headless fact that drives a scripted session, rather than copied into
/// each: two clocks that drifted apart would make two suites disagree about the same product.</para>
/// </summary>
internal sealed class ManualScriptedClock : IScriptedClock
{
    private readonly List<Entry> _timers = [];
    private DateTimeOffset _wall = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);
    private TimeSpan _monotonic = TimeSpan.Zero;

    public DateTimeOffset Now
    {
        get { lock (_timers) { return _wall; } }
    }

    public TimeSpan Monotonic
    {
        get { lock (_timers) { return _monotonic; } }
    }

    public IDisposable Schedule(TimeSpan due, Action fire)
    {
        Entry entry;
        lock (_timers)
        {
            entry = new Entry
            {
                Due = _monotonic + (due < TimeSpan.Zero ? TimeSpan.Zero : due),
                Fire = fire,
            };
            _timers.Add(entry);
        }

        return new CancelHandle(this, entry);
    }

    public void Advance(TimeSpan by)
    {
        lock (_timers)
        {
            _wall += by;
            _monotonic += by;
        }

        while (true)
        {
            Entry? next;
            lock (_timers)
            {
                next = _timers
                    .Where(t => !t.Cancelled && t.Due <= _monotonic)
                    .OrderBy(t => t.Due)
                    .FirstOrDefault();
                if (next is null)
                {
                    return;
                }

                _timers.Remove(next);
            }

            next.Fire();
        }
    }

    private sealed class Entry
    {
        public TimeSpan Due;

        public required Action Fire;

        public bool Cancelled;
    }

    private sealed class CancelHandle(ManualScriptedClock clock, Entry entry) : IDisposable
    {
        public void Dispose()
        {
            lock (clock._timers)
            {
                entry.Cancelled = true;
            }
        }
    }
}
