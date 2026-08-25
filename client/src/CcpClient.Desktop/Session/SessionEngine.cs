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
///
/// <para><b>Amended:</b> the "not ported" above is now half true, and the half that changed is
/// named rather than quietly dropped. <see cref="ScriptedSessionRun"/> is slice 1 of that other
/// feature — the persisted model, phases on a clock, START/STOP with the settings snapshot, and
/// the clock-jump guard — and it OWNS this class from outside rather than living in it: still no
/// XP, no phases and no duration anywhere below this line. <b>It is now REACHABLE:</b> the
/// composition root builds one (<c>Lifecycle/CompositionRoot.cs:275</c> →
/// <c>Session/SessionParticipant.cs:620</c>), the shell resolves it off the host
/// (<c>Views/MainWindow.axaml.cs:112</c>) and the Studio door's rack starts, pauses and stops it
/// (<c>Views/Pages/StudioPage.axaml.cs</c>), so a user really can run one — and the rack they pick
/// from filters, orders and searches (<see cref="ScriptedSessionRack"/>). Its editor, custom and
/// imported sessions and the XP award remain unported.</para>
/// </summary>
public sealed class SessionEngine
{
    private readonly IReadOnlyList<ISessionEffect> _effects;
    private readonly PersistenceStore<SessionPresetDocument> _preset;
    private readonly Dictionary<string, CapabilityState> _armOutcomes = [];
    private readonly List<(string Id, CapabilityReason Reason)> _stopFailures = [];
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

    /// <summary>
    /// The modules that THREW on the way down during the last <see cref="Stop"/>, in rack order.
    /// Empty after a clean stop, and cleared at the start of every stop, so it always describes the
    /// most recent one.
    ///
    /// <para><b>It exists because the alternative was silence.</b> A disarm that throws leaves the
    /// module's schedule dead but may leave its surface on screen, and the port's standard is a typed
    /// outcome carrying a reason rather than a swallowed exception. The panic key's window procedure
    /// catches everything it is handed and cannot report — so if the failure is not readable HERE it
    /// is readable nowhere, which is exactly the state the emergency stop was in.</para>
    /// </summary>
    public IReadOnlyList<(string Id, CapabilityReason Reason)> StopFailures => _stopFailures;

    /// <summary>
    /// What a user is told when the last stop did not fully take, or <c>null</c> when it did.
    ///
    /// <para>It lives on the ENGINE rather than on the shell because the engine is what knows: which
    /// modules broke, and — more importantly — exactly how much the stop is still entitled to claim.
    /// Every module's schedule really is dead, so the sentence says nothing more is scheduled; a
    /// surface whose withdrawal threw may still be up, so the sentence says that too and does not
    /// round it off to "stopped". Rounding it off is the failure this whole path exists to prevent.</para>
    /// </summary>
    /// <param name="gesture">What the user just did, in their words ("STOP", "the emergency stop").</param>
    /// <param name="panicGesture">The emergency chord IF this process really holds it, otherwise
    /// null — the way out named must be one that exists.</param>
    public string? StopFailureNotice(string gesture, string? panicGesture)
    {
        if (_stopFailures.Count == 0)
        {
            return null;
        }

        var wayOut = panicGesture is null
            ? "Closing the main window exits the application."
            : $"Pressing {panicGesture} twice exits the application.";

        return $"{gesture} could not fully stop {string.Join(", ", _stopFailures.Select(f => f.Id))}. "
            + $"Nothing more is scheduled, but anything those modules had on screen may still be up. {wayOut}";
    }

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
            //
            // ONE MODULE MAY NOT TAKE THE RACK DOWN WITH IT. Arm() is not bookkeeping — it creates
            // native windows, opens decoders and claims audio devices — so it can throw, and an
            // unguarded throw here meant every module after it in rack order was never armed while
            // the exception left through whatever pressed START (a click handler, or a scheduler
            // tick on a pool thread with nobody watching). The failure is typed instead, into the
            // same channel every other refusal uses, so the rack can SAY which module broke.
            try
            {
                _armOutcomes[effect.Id] = effect.Arm();
            }
            catch (Exception ex)
            {
                _armOutcomes[effect.Id] = new CapabilityState.Unavailable(new CapabilityReason(
                    EffectReasonCodes.EffectArmFailed,
                    $"the '{effect.Id}' module threw while the session was starting it "
                    + $"({ex.GetType().Name}: {ex.Message}); the session started without it"));
            }
        }

        Running = true;
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// WPF <c>StopEngine</c>/<c>StopEngineCore</c> (<c>MainWindow.StartStop.cs:292-410</c>):
    /// the re-entrancy guard first (<c>:292-296</c>), the work stopped before the flag
    /// (<c>:305</c>, <c>:385-387</c>).
    ///
    /// <para><b>Every module gets its disarm attempted, whatever the module before it did.</b>
    /// This loop used to be bare, and the emergency stop's own review is what found what that
    /// costs: <see cref="ISessionEffect.Disarm"/> runs native window teardown, decoders and audio,
    /// so one module throwing meant every module AFTER it in rack order was never disarmed and kept
    /// its surfaces on screen — while the throw travelled up into
    /// <c>Input/Win32PanicKey.cs</c>'s window procedure, which catches and is deliberately silent.
    /// The user was left under the surfaces with no log line and no toast, and pressing the panic
    /// chord again re-entered the same throwing branch for as long as he cared to press it.</para>
    ///
    /// <para><b><see cref="Running"/> goes false even when a module threw, deliberately.</b> The
    /// alternative — keep the flag true while a module is "still live" — reads as the honest one and
    /// is not: it re-arms nothing, it keeps the shell's ONE control captioned <c>STOP</c>, and it
    /// keeps the panic ladder's stop rung in front of the exit rung, so the flag a failed stop
    /// corrupts becomes the reason the user can never leave. What the flag means here is that this
    /// engine owns a session, and after this loop it owns none: every module was asked, and every
    /// module's generation is cancelled whatever its release did
    /// (<see cref="OwnedSessionEffect.Disarm"/> cancels in a <c>finally</c>), so nothing any module
    /// scheduled can still fire. What may survive is a SURFACE whose withdrawal threw halfway — and
    /// a boolean cannot say that, which is why <see cref="StopFailures"/> exists and why the shell
    /// puts it in front of the user instead of leaving it in a dictionary.</para>
    /// </summary>
    /// <returns>False when nothing was running, or when a stop was already in progress. True when a
    /// stop really ran — including one where a module threw, because "nothing was running" and "a
    /// module broke on the way down" are different facts and <see cref="StopFailures"/> is where the
    /// second one is told.</returns>
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

            _stopFailures.Clear();
            foreach (var effect in _effects)
            {
                try
                {
                    effect.Disarm();
                }
                catch (Exception ex)
                {
                    var reason = new CapabilityReason(
                        EffectReasonCodes.EffectDisarmFailed,
                        $"the '{effect.Id}' module threw while the session was stopping it "
                        + $"({ex.GetType().Name}: {ex.Message}); its schedule was cancelled anyway, but "
                        + "anything it had on screen may still be up");
                    _stopFailures.Add((effect.Id, reason));

                    // Into the SAME channel every other module refusal uses, so the rack row that
                    // showed "armed" a moment ago now shows why it is not.
                    _armOutcomes[effect.Id] = new CapabilityState.Unavailable(reason);
                }
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
            // Guarded for the reason Start and Stop are: this is a mouse gesture on a rack row, and
            // a module that throws here would take the exception out through the row's click
            // handler with the persisted dial already written. The typed outcome is the row's own
            // channel, so the dot and the refusal text say what happened instead.
            try
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
            catch (Exception ex)
            {
                _armOutcomes[effect.Id] = new CapabilityState.Unavailable(new CapabilityReason(
                    on ? EffectReasonCodes.EffectArmFailed : EffectReasonCodes.EffectDisarmFailed,
                    $"the '{effect.Id}' module threw when the rack's quick-toggle switched it "
                    + $"{(on ? "on" : "off")} mid-session ({ex.GetType().Name}: {ex.Message})"));
            }
        }

        _ = _preset.Save();
        RaiseChanged();
        return true;
    }
}
