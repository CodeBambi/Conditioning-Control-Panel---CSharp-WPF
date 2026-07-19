using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features;

/// <summary>
/// Demonstrator feature <c>demo.status-ticker</c> (SP-007; A-004 stable identity). It is
/// explicitly a DEMONSTRATOR — not a product feature, named after no real WPF feature, and
/// the first real feature card supersedes it in a later dashboard row. The toggle
/// starts/cancels a REAL SP-004 owned operation (owner, cancellation generation, typed
/// terminal outcome); the card's ring reflects <see cref="IsOperationLive"/> — the operation
/// authority — never the persisted flag alone. The flag round-trips through the SP-005
/// store. Construction starts nothing (SP-003 §4.4); the phase-3 start applies the
/// restored flag (restore-then-start: the store starts earlier in registration order, so
/// its load has completed before this participant reads it).
/// </summary>
public sealed class StatusTickerParticipant : IBackgroundParticipant
{
    /// <summary>The stable feature identity (A-004): dispatch never uses display text.</summary>
    public const string FeatureId = "demo.status-ticker";

    private readonly AsyncOperationOwner _owner;
    private readonly UiDispatchBoundary _ui;
    private readonly PersistenceStore<DemoSettings> _store;
    private readonly TimeSpan _interval;
    private int _tickCount;
    private bool _enabled;

    public StatusTickerParticipant(
        AsyncOperationOwner owner,
        UiDispatchBoundary ui,
        PersistenceStore<DemoSettings> store,
        TimeSpan? interval = null)
    {
        _owner = owner;
        _ui = ui;
        _store = store;
        _interval = interval ?? TimeSpan.FromMilliseconds(500);
    }

    public string Name => "StatusTicker";

    public bool Running { get; private set; }

    public int TickCount => Volatile.Read(ref _tickCount);

    /// <summary>The tick loop's owned completion (async contract §1): completes with the typed terminal outcome.</summary>
    public Task<OperationOutcome>? Completion { get; private set; }

    /// <summary>Set by the window's view-model (phase 4). Invoked only on the UI thread, inside a boundary post.</summary>
    public Action<string>? TickReporter { get; set; }

    /// <summary>
    /// The ring authority (async contract §3/§5): true only while the owned operation's
    /// generation is live. A bool set by the toggle handler would rebuild the
    /// persisted-flag lie one level up — the ring derives from the owner, not the flag.
    /// </summary>
    public bool IsOperationLive => Running && _enabled && _owner.IsLive(_owner.Generation);

    /// <summary>
    /// Phase-3 start: applies the restored flag. The store started earlier in registration
    /// order, so <see cref="PersistenceStore{TModel}.Current"/> already reflects the
    /// persisted document — restore-then-start is the registration order, nothing else.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Running)
        {
            return Task.CompletedTask;
        }

        Running = true;
        if (_store.Current.StatusTickerEnabled)
        {
            SetEnabled(enabled: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>Idempotent stop (SP-003 §5.3): cancels the operation generation.</summary>
    public Task StopAsync()
    {
        if (!Running)
        {
            return Task.CompletedTask;
        }

        Running = false;
        _enabled = false;
        _owner.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Idempotent enable/disable: enabling an already-enabled ticker is a no-op — never a
    /// second generation racing the first (re-entrant double-toggle guard).
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        if (!Running)
        {
            throw new InvalidOperationException("demo.status-ticker cannot be toggled before its phase-3 start.");
        }

        if (_enabled == enabled)
        {
            return;
        }

        _enabled = enabled;
        if (enabled)
        {
            var generation = _owner.Begin();
            Completion = _owner.RunAsync("status-tick", token => TickLoopAsync(generation, token));
        }
        else
        {
            _owner.Cancel();
        }
    }

    private async Task<OperationOutcome> TickLoopAsync(int generation, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var tick = Interlocked.Increment(ref _tickCount);
            // Skip-until-bound (async contract §5.3): the operation may start in phase 3
            // (restored flag) before the boundary binds in phase 4; projection is skipped,
            // never faulted, until then.
            if (_ui.IsBound && _owner.IsLive(generation))
            {
                var text = $"demo.status-ticker: tick {tick}";
                _ui.Post(() =>
                {
                    // Stale check inside the delegate on the UI thread (contract §5.5).
                    if (_owner.IsLive(generation))
                    {
                        TickReporter?.Invoke(text);
                    }
                });
            }

            await Task.Delay(_interval, token).ConfigureAwait(false);
        }

        // Typed terminal outcome: observing the token at the loop check is Cancelled too —
        // identical semantics to the OCE path RunAsync maps (async contract §2).
        return token.IsCancellationRequested
            ? OperationOutcome.Cancelled.Instance
            : OperationOutcome.Completed.Instance;
    }
}
