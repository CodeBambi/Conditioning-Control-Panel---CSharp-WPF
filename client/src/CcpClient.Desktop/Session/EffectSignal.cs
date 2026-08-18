using Avalonia.Threading;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Session;

/// <summary>
/// Where a module's <see cref="ISessionEffect.Changed"/> is allowed to arrive, and the one place
/// that decides it.
///
/// <para><b>Why this exists (SP-101, hazard 2 from SP-098's review).</b> Before this, an effect
/// raised <c>Changed</c> on whatever thread moved its state, and every consumer carried its own
/// copy of the marshalling — <c>Dispatcher.UIThread.CheckAccess()</c>, then either the work inline
/// or a <c>Post</c>. Two consumers had that body
/// (<c>Views/MainWindow.axaml.cs</c>, <c>Views/Pages/StudioPage.axaml.cs</c>) and they agreed;
/// fifteen modules and their panels will not. A duty that every consumer must remember is a duty
/// one of them will forget, and the failure it produces — a control touched off the UI thread — is
/// intermittent and platform-shaped. So the duty moves to the PRODUCER: after this, a subscriber to
/// an effect's <c>Changed</c> may touch UI directly.</para>
///
/// <para><b>The rule, and why it is not simply "always post".</b> Inline when there is no UI to
/// marshal to, inline when the caller is ALREADY on the UI thread, posted otherwise. Always-post
/// would defer every synchronous gesture by a dispatcher turn — a click that starts a session would
/// return before the row's dot moved — which changes observable ordering for no safety gain. This
/// is exactly the rule the two hand-written consumers implemented; it is now written once.</para>
///
/// <para><b>Why the thread test is injected.</b> <see cref="UiDispatchBoundary"/> is post-only by
/// contract (async-lifecycle-fault-contract §5.1) and lives outside this packet's File Scope, so a
/// <c>CheckAccess</c> was not added to it. The default here is Avalonia's own, and it is consulted
/// only AFTER <see cref="UiDispatchBoundary.IsBound"/> is true — so a process with no Avalonia
/// runtime at all (the pure-logic test project, phases 1-3) never touches the dispatcher.</para>
/// </summary>
public sealed class EffectSignal
{
    private readonly UiDispatchBoundary _ui;
    private readonly Func<bool> _onSignalThread;

    /// <param name="ui">The late-bound UI dispatch boundary. Unbound means there is no UI yet, and
    /// every projection is skipped rather than faulted (contract §5.3).</param>
    /// <param name="onSignalThread">True when the calling thread is already the signal thread.
    /// Defaults to Avalonia's UI-thread check.</param>
    public EffectSignal(UiDispatchBoundary ui, Func<bool>? onSignalThread = null)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;
        _onSignalThread = onSignalThread ?? (static () => Dispatcher.UIThread.CheckAccess());
    }

    /// <summary>True once phase 4 has bound a UI thread to marshal onto.</summary>
    public bool IsBound => _ui.IsBound;

    /// <summary>
    /// Queue <paramref name="action"/> for the UI thread, or drop it when there is no UI
    /// (skip-until-bound, contract §5.3). Always a post, never inline: this is the route a
    /// PROJECTION takes, and a projection that touches a native window must be ordered behind
    /// whatever the UI thread is already doing rather than re-entering it.
    /// </summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_ui.IsBound)
        {
            return;
        }

        _ui.Post(action);
    }

    /// <summary>
    /// Raise a state-change notification on the signal thread. Inline when there is no UI or the
    /// caller is already on it; posted otherwise. Never dropped — unlike a projection, a state
    /// change with no UI still has non-UI subscribers (the engine forwards it) and losing it would
    /// leave them stale.
    /// </summary>
    public void Raise(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_ui.IsBound || _onSignalThread())
        {
            action();
            return;
        }

        _ui.Post(action);
    }
}
