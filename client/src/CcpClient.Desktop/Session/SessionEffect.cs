using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Session;

/// <summary>
/// What a rack row's dot is allowed to say. THREE states, not two, and the third one exists
/// because WPF's own copy and WPF's own mechanism disagree about what the dot means.
///
/// <para>The Studio onboarding card promises: <i>"The dot on each row is live: at a glance you
/// can see everything that is currently running."</i> (quoted in
/// <c>client/docs/wpf-surface-reachability.md</c> §8.3). The mechanism reads the persisted
/// ENABLE FLAG — <c>Add("flash", …, () =&gt; App.Settings?.Current?.FlashEnabled)</c>
/// (<c>Views/Tabs/StudioTabView.xaml.cs:484-485</c>) — which says "armed", not "running". The
/// two coincide only while the engine runs, because <c>StartEngine</c> gates each service on
/// its flag and the quick-toggle starts/stops the live service
/// (<c>MainWindow/MainWindow.Presets.cs:1250</c>).</para>
///
/// <para>Rather than pick one and be wrong about the other, the port says both. A dot that
/// could only be lit or unlit would have to claim a stopped-but-armed effect is "running" or
/// that an armed one is "off"; the capability contract bans exactly that kind of confident
/// half-truth.</para>
/// </summary>
public enum EffectDotState
{
    /// <summary>The module's own dial is off. Nothing will happen, session or no session.</summary>
    Off,

    /// <summary>Armed — the dial is on — but no session owns it, so nothing is scheduled.</summary>
    Armed,

    /// <summary>Really running: an owned operation is live and the effect's work is scheduled.</summary>
    Live,
}

/// <summary>
/// One effect module under the conditioning session. This is the spine's contract, and the
/// other fourteen rack modules (<c>wpf-surface-reachability.md</c> §8.3) implement THIS —
/// which is why it is deliberately small: a stable identity, a display title, one persisted
/// dial, a truthful dot, and an arm/disarm pair with an owned completion behind it.
///
/// <para>Implementations must be idempotent (the lifecycle contract's §5.3 participant rule at effect
/// granularity): arming an armed effect starts no second generation, and disarming a
/// never-armed effect is a no-op.</para>
/// </summary>
public interface ISessionEffect
{
    /// <summary>
    /// The stable dispatch identity (A-004) — WPF's own rack key, e.g. <c>"flash"</c>
    /// (<c>StudioTabView.xaml.cs:484</c>, and the same key
    /// <c>MainWindow.Presets.cs:1250</c> switches on). Never a display string, never localized.
    /// </summary>
    string Id { get; }

    /// <summary>Display text. Mutating it must never affect dispatch.</summary>
    string Title { get; }

    /// <summary>The persisted on/off dial for this module.</summary>
    bool Enabled { get; }

    /// <summary>What the row's dot is entitled to show right now. Derived, never a stored bool.</summary>
    EffectDotState Dot { get; }

    /// <summary>
    /// The armed schedule's owned completion (async-lifecycle-fault-contract §1): it exists
    /// from the first arm and terminates with a typed outcome when the generation is
    /// cancelled. Null before the effect has ever been armed.
    /// </summary>
    Task<OperationOutcome>? Completion { get; }

    /// <summary>
    /// Raised when the dot, the counters or the dial move.
    ///
    /// <para><b>Delivered on the UI thread whenever one exists</b>. It used to be raised on
    /// whatever thread moved the state, which pushed the marshalling onto every consumer; two
    /// consumers carried the same hand-written <c>CheckAccess</c>-or-<c>Post</c> body and agreed,
    /// and the fifteenth would not have. The producer now owns it — see
    /// <see cref="EffectSignal"/>. Before phase 4 binds a UI thread there is nothing to marshal to
    /// and the event is raised inline.</para>
    /// </summary>
    event Action? Changed;

    /// <summary>
    /// Writes the persisted dial. Separate from <see cref="Arm"/> because WPF's quick-toggle
    /// writes the flag whether or not anything is running and then starts/stops only if the
    /// engine is up (<c>MainWindow.Presets.cs:1250</c>).
    /// </summary>
    void SetEnabled(bool enabled);

    /// <summary>
    /// Take ownership of a live generation and schedule the work. Called by the session on
    /// START, and by the quick-toggle when a module is switched on mid-session.
    ///
    /// <para><b>It returns a typed outcome, and that is not decoration.</b> This method
    /// returned <c>void</c> through the first two modules, which meant "this module took the session and
    /// is paced" and "this module did nothing at all" were literally the same observation. Two
    /// modules whose only precondition is a persisted flag survive that; the modules still queued —
    /// the ones that need an audio device, a webcam, a display server — cannot, because a module
    /// that must refuse has nowhere to say so and the session would report itself running with a
    /// silent hole in it. Both ported modules already produce two different states here (an armed
    /// schedule, and a dial that is off), and Subliminals produces a third
    /// (<see cref="CapabilityState.Degraded"/>: paced, but with no phrase to show).</para>
    /// </summary>
    /// <returns>What the module can honestly say about the session it was just handed. Never null,
    /// and never a bare boolean: the reason code is what a caller reports and a bug report quotes.</returns>
    CapabilityState Arm();

    /// <summary>
    /// Stop the work. Called by the session on STOP, and by the quick-toggle when a module is
    /// switched off mid-session. After this returns, nothing this effect scheduled may fire.
    /// </summary>
    void Disarm();
}
