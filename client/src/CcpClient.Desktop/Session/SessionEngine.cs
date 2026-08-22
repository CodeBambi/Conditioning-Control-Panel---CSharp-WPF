using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The conditioning session: what WPF's <c>START</c> button drives.
///
/// <para><b>WHICH "session" this is.</b> WPF has two things by that name and they are not the
/// same. This is the <b>ENGINE</b> — <c>MainWindow/MainWindow.StartStop.cs:34</c>
/// (<c>BtnStart_Click</c>), <c>:159</c> (<c>StartEngine</c>), <c>:296</c>/<c>:302</c>
/// (<c>StopEngine</c>/<c>StopEngineCore</c>), and the app-global <c>App.IsEngineRunning</c>
/// flag at <c>:269</c>/<c>:387</c>. The other one is <c>Services/Session/SessionEngine.cs</c> —
/// a SCRIPTED session with phases, a duration, XP and a stop-confirmation dialog
/// (<c>MainWindow.StartStop.cs:52-88</c>) — which runs on TOP of the engine and is <b>not
/// ported</b>. Nothing here should ever grow XP or phases; that is a different feature with a
/// different name.</para>
///
/// <para><b>The ordering below is behaviour, not style.</b> WPF saves the dials before any
/// effect starts (<c>:161</c>), flips the running flag only AFTER the work has started
/// (<c>:268-269</c>), stops the work BEFORE it clears the flag (<c>:305</c> "Stop flash first",
/// <c>:385-387</c>), and guards stop against re-entry because a second panic press could race
/// teardown (<c>:292-296</c>). A session whose flag led the work would report itself running
/// with nothing running, which is precisely the "a flag is not a session" failure this spine
/// exists to avoid.</para>
///
/// <para>This class owns no threads, no timers and no cancellation of its own: every effect
/// carries an <see cref="Lifecycle.AsyncOperationOwner"/> and the work stops because that
/// generation is cancelled. So "stop really stops" is a property of the operation registry,
/// provable by draining it, not of a boolean here.</para>
/// </summary>
public sealed class SessionEngine
{
    private readonly IReadOnlyList<ISessionEffect> _effects;
    private readonly PersistenceStore<SessionPresetDocument> _preset;
    private readonly Dictionary<string, CapabilityState> _armOutcomes = [];
    private readonly EffectSignal? _signal;
    private bool _stopInProgress;

    /// <param name="effects">The rack's modules, in WPF's rack order.</param>
    /// <param name="preset">The persisted preset the session saves before it starts anything.</param>
    /// <param name="signal">Where <see cref="Changed"/> is delivered. The engine raises its
    /// OWN notifications on START, STOP and quick-toggle, and those are raised from the caller's
    /// thread — teardown's, on the stop path — so they need the same marshalling the modules' do.
    /// Omitting it raises inline, which is what a caller with no UI at all wants.</param>
    public SessionEngine(
        IReadOnlyList<ISessionEffect> effects,
        PersistenceStore<SessionPresetDocument> preset,
        EffectSignal? signal = null)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(preset);
        _effects = effects;
        _preset = preset;
        _signal = signal;
        foreach (var effect in _effects)
        {
            // Forwarded INLINE on purpose: a module's Changed has already been through the signal,
            // so this runs on the signal thread already and a second hop would only defer it.
            effect.Changed += () => Changed?.Invoke();
        }
    }

    /// <summary>The rack's modules, in WPF's rack order (<c>StudioTabView.xaml.cs:482-497</c>).</summary>
    public IReadOnlyList<ISessionEffect> Effects => _effects;

    private void RaiseChanged()
    {
        if (_signal is null)
        {
            Changed?.Invoke();
            return;
        }

        _signal.Raise(() => Changed?.Invoke());
    }

    /// <summary>
    /// What each module said the last time it was armed, by module id. Empty before the
    /// first START.
    ///
    /// <para>This is the reason <see cref="ISessionEffect.Arm"/> stopped returning <c>void</c>: a
    /// session that arms fifteen modules of which three are switched off and one has no audio device
    /// is a session with a hole in it, and until now nothing in the port could name which hole. The
    /// states are kept VERBATIM — a reason code and its detail, never summarised into a boolean —
    /// because the detail is what a user reads and a bug report quotes.</para>
    /// </summary>
    public IReadOnlyDictionary<string, CapabilityState> ArmOutcomes => _armOutcomes;

    /// <summary>The modules that did not take the last START — the dial-off ones, and any that could
    /// not run here. Order is the rack's.</summary>
    public IReadOnlyList<(string Id, CapabilityReason Reason)> ArmRefusals =>
        [.. _effects
            .Where(e => _armOutcomes.TryGetValue(e.Id, out var state) && state is CapabilityState.Unavailable)
            .Select(e => (e.Id, ((CapabilityState.Unavailable)_armOutcomes[e.Id]).Reason))];

    /// <summary>WPF's <c>_isRunning</c>/<c>App.IsEngineRunning</c> (<c>MainWindow.StartStop.cs:268-269</c>).</summary>
    public bool Running { get; private set; }

    /// <summary>Raised when the session's own state, or any effect's, moves.</summary>
    public event Action? Changed;

    /// <summary>The persisted preset every effect reads its dials out of.</summary>
    public SessionPresetDocument Preset => _preset.Current;

    /// <summary>
    /// The ONE button. WPF's <c>BtnStart_Click</c> is a single control that reads
    /// <c>_isRunning</c> and branches (<c>MainWindow.StartStop.cs:50,105</c>) — never two
    /// buttons, and never a control that is disabled in one of its two states.
    /// </summary>
    public void Toggle()
    {
        if (Running)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    /// <summary>
    /// WPF <c>StartEngine</c> (<c>MainWindow.StartStop.cs:159-292</c>), in its order:
    /// persist the dials first (<c>:161</c> <c>SaveSettings()</c>), arm every effect
    /// (<c>:178</c> — flash first, which is registration order here), and only then flip the
    /// flag (<c>:268-269</c>).
    /// </summary>
    /// <returns>False when a session was already running (WPF's <c>if (_isRunning)</c> branch
    /// never reaches <c>StartEngine</c>).</returns>
    public bool Start()
    {
        if (Running)
        {
            return false;
        }

        // WPF saves synchronously before starting anything. The port's store save is an owned
        // operation, discarded here exactly as the landed toggle path discards it
        // (MainWindowViewModel.Toggle): it is never awaited on the UI thread, and teardown's
        // reserved pre-drain flush is what guarantees it reaches disk.
        _ = _preset.Save();

        foreach (var effect in _effects)
        {
            // The outcome is RECORDED rather than discarded: a module that armed nothing is a fact
            // about this session, and dropping it here would put the typed refusal back where it
            // was before — expressible and unobserved.
            _armOutcomes[effect.Id] = effect.Arm();
        }

        Running = true;
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// WPF <c>StopEngine</c>/<c>StopEngineCore</c> (<c>MainWindow.StartStop.cs:292-410</c>):
    /// the re-entrancy guard first (<c>:292-296</c>), the work stopped before the flag
    /// (<c>:305</c>, <c>:385-387</c>).
    /// </summary>
    /// <returns>False when nothing was running, or when a stop was already in progress.</returns>
    public bool Stop()
    {
        // WPF's guard exists because its stop body pumps the dispatcher, so a second panic
        // press could re-enter and race teardown (:292-296). The port's body does not pump,
        // but the guard is kept: it is the reason a double-press cannot produce two teardowns,
        // and a spine that drops it invites the same defect back when an effect one day does
        // something slow on its way down.
        if (_stopInProgress)
        {
            return false;
        }

        _stopInProgress = true;
        try
        {
            if (!Running)
            {
                return false;
            }

            foreach (var effect in _effects)
            {
                effect.Disarm();
            }

            Running = false;
            RaiseChanged();
            return true;
        }
        finally
        {
            _stopInProgress = false;
        }
    }

    /// <summary>
    /// The rack row's second gesture: right-click quick-toggles the module
    /// (<c>StudioTabView.xaml.cs:657-660</c> -&gt; <c>MainWindow.Presets.cs:1241-1266</c>).
    /// WPF's body for a rack key, in its order: flip the persisted flag, then — <b>only if the
    /// engine is running</b> — start or stop the live service, then save
    /// (<c>:1250</c>, <c>:1264</c>).
    ///
    /// <para>An unknown id is a silent no-op, WPF's <c>default: return</c> (<c>:1259</c>): the
    /// gesture is simply not handled for a row that has no toggle (<c>:659</c> — "Rows with no
    /// Toggle fall through unhandled"), never swallowed by a fake one.</para>
    /// </summary>
    /// <returns>True when a real module took the gesture.</returns>
    public bool QuickToggle(string? effectId)
    {
        var effect = _effects.FirstOrDefault(e => string.Equals(e.Id, effectId, StringComparison.Ordinal));
        if (effect is null)
        {
            return false;
        }

        var on = !effect.Enabled;
        effect.SetEnabled(on);
        if (Running)
        {
            if (on)
            {
                _armOutcomes[effect.Id] = effect.Arm();
            }
            else
            {
                effect.Disarm();
                _armOutcomes[effect.Id] = new CapabilityState.Unavailable(new CapabilityReason(
                    EffectReasonCodes.EffectDialOff,
                    $"the '{effect.Id}' module was switched off mid-session by the rack's quick-toggle"));
            }
        }

        _ = _preset.Save();
        RaiseChanged();
        return true;
    }
}
