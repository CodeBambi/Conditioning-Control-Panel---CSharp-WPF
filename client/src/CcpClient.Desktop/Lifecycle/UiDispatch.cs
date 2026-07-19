using Avalonia.Threading;

namespace CcpClient.Desktop.Lifecycle;

/// <summary>
/// The one deliberate UI dispatch boundary (async-lifecycle-fault-contract §5).
/// Post-only by design: with no awaitable dispatch method, no operation can wait on the
/// UI thread, so the shutdown-deadlock class is eliminated by construction. Re-admitting
/// an awaitable dispatch requires a real consumer and must re-answer the deadlock
/// question (contract §5.1).
/// </summary>
public interface IUiDispatch
{
    /// <summary>
    /// Queues <paramref name="action"/> for the UI thread. The delegate must be harmless
    /// if it runs late or never (teardown blocks the UI thread): do the generation/liveness
    /// check inside the delegate (contract §5.5).
    /// </summary>
    void Post(Action action);
}

/// <summary>Production boundary: the real Avalonia UI thread. Used only from phase 4.</summary>
public sealed class AvaloniaUiDispatch : IUiDispatch
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}

/// <summary>
/// The late-bound holder (contract §5.2). Phases 1–3 run before Avalonia exists, so there
/// is no SynchronizationContext to capture at composition time; the boundary is bound in
/// phase 4 via <see cref="ApplicationHost.BindUiDispatch"/>. Capturing
/// SynchronizationContext.Current is banned. Posting before binding throws — an unbound
/// boundary is a composition/sequencing bug, not a runtime fault. Long-running owners
/// started in phase 3 must check <see cref="IsBound"/> and skip UI projection until bound
/// (contract §5.3).
/// </summary>
public sealed class UiDispatchBoundary
{
    private volatile IUiDispatch? _bound;

    public bool IsBound => _bound is not null;

    /// <summary>Phase 4 binding. One-shot: a second bind is a wiring bug.</summary>
    public void Bind(IUiDispatch dispatch)
    {
        if (Interlocked.CompareExchange(ref _bound, dispatch, null) is not null)
        {
            throw new InvalidOperationException("UI dispatch boundary is already bound.");
        }
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> when called before phase 4 binds (contract §5.3).</summary>
    public void Post(Action action)
    {
        var dispatch = _bound ?? throw new InvalidOperationException(
            "UI dispatch boundary is not bound: phase 4 (UserInterface) has not run.");
        dispatch.Post(action);
    }
}
