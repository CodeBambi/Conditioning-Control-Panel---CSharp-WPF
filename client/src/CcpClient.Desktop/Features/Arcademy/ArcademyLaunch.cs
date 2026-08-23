using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// THE one place an Arcademy session is opened — the <see cref="Goon.GoonLaunch"/> /
/// <see cref="Intake.IntakeLaunch"/> pattern (one construction site, several callers, no second
/// launcher), and the place the door refuses.
///
/// <para><b>Order matters and is upstream's.</b> <c>ArcademyHostService.Launch</c> runs
/// idempotent re-focus FIRST (<c>:126-130</c>, so a live class is never stranded behind a gate
/// that only applies to opening a new one), then the DOOR (<c>:136-141</c>), and only then the T2
/// bar (<c>:146</c>) and AudioOnlySession (<c>:153-157</c>). This launcher reproduces the first
/// three. <b>The order is not cosmetic:</b> because the door answers first, a shipped build never
/// asks an entitlement authority about the Arcademy at all — no login is read, nothing is looked
/// up, and no account is adjudicated for a surface that is not being offered.</para>
///
/// <para><b>What "opens nothing" means concretely.</b> Under a shut door this method constructs
/// no <see cref="ArcademyParticipant"/>, so no loopback origin is bound, no port is listening, no
/// payload byte is reachable and no page exists to navigate to. The refusal is the first thing
/// that happens after the idempotency check, before any allocation.</para>
///
/// <para><b>The one gate that is NOT here is AudioOnlySession</b> (<c>:153-157</c>): this build
/// has no audio-only session for the Arcademy to be refused by — that is the same missing native
/// state slice 5 owns — so there is no flag to read and a hard-coded <c>false</c> would be a gate
/// pretending to have an input. <see cref="ArcademyProtocol.BuildInit"/> already projects
/// <c>audioOnlySession = false</c> for the same measured reason.</para>
///
/// <para><b>What this launcher does NOT do.</b> It opens the ORIGINS and hands back the url a host
/// window would navigate to; it shows no window, because the port has no Arcademy host window.
/// The Play-tab entry (<see cref="Views.Pages.PlayPage"/>) reaches THIS method and nothing else,
/// which is WPF's one-entry rule — <c>BtnStartArcademy_Click</c> is a try/catch around
/// <c>Launch()</c> and nothing more (<c>MainWindow/MainWindow.Lab.cs:302-321</c>).</para>
/// </summary>
public sealed class ArcademyLaunch
{
    private readonly ApplicationHost _host;
    private readonly Func<CancellationToken, Task<EntitlementOutcome>> _entitlement;
    private ArcademyParticipant? _participant;

    /// <param name="host">The application host: diagnostics and the participant registry.</param>
    /// <param name="entitlement">The entitlement capability's resolve, as a function — the seam
    /// <c>Haptics.HapticParticipant</c> already takes (<c>CompositionRoot.cs:245</c>,
    /// <c>entitlement.ResolveAsync</c>). It is REQUIRED even though the shut door means it is
    /// never called: a launcher that could be built without one is a launcher that could hand out
    /// paid content the day the door opens.</param>
    public ArcademyLaunch(ApplicationHost host, Func<CancellationToken, Task<EntitlementOutcome>> entitlement)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(entitlement);
        _host = host;
        _entitlement = entitlement;
    }

    /// <summary>Where the participant's data directory lives. Null is the product default (the
    /// install's own data root through the data-root choke point); set only by tests.</summary>
    public string? DataDirectory { get; set; }

    /// <summary>How many times the launch gesture arrived (attempts, not sessions).</summary>
    public int AttendCount { get; private set; }

    /// <summary>The live session, or null. Never non-null while the door is shut.</summary>
    public ArcademyParticipant? Participant => _participant;

    /// <summary>Outcome of an attend gesture. Every path is typed; nothing throws.</summary>
    public abstract record ArcademyAttendOutcome
    {
        private ArcademyAttendOutcome() { }

        /// <summary>The door said no. Nothing was constructed, bound or shown — and no
        /// entitlement was read, because the door answers before the tier bar (<c>:136-146</c>).</summary>
        public sealed record Refused(ArcademyDoor.ArcademyDoorRefusal Refusal) : ArcademyAttendOutcome;

        /// <summary>The door was open and the TIER bar said no (<c>:146</c>). The decision is
        /// carried whole rather than flattened to a message: its two refusal cases are different
        /// types precisely so a surface cannot render "I could not tell" as "you are not a
        /// patron".</summary>
        public sealed record Gated(ArcademyGateDecision Decision) : ArcademyAttendOutcome;

        /// <summary>The origins are up; <paramref name="PageUrl"/> is what a host would navigate
        /// to. Unreachable in a shipped build while <see cref="ArcademyDoor.Available"/> is false.</summary>
        public sealed record Opened(ArcademyParticipant Participant, string PageUrl) : ArcademyAttendOutcome;

        /// <summary>A live session was re-presented rather than re-opened
        /// (<c>ArcademyHostService.cs:130</c>, <c>_host.FocusWeb()</c>).</summary>
        public sealed record AlreadyOpen(ArcademyParticipant Participant) : ArcademyAttendOutcome;

        /// <summary>The open flow threw. The exception is carried, never swallowed
        /// (the launch-fault lesson: a discarded task's exception is seen by nobody).</summary>
        public sealed record Faulted(Exception Exception) : ArcademyAttendOutcome;
    }

    /// <summary>What the tier bar last decided, or null — including on every shipped run, because
    /// the door refuses ahead of it and the bar is never reached.</summary>
    public ArcademyGateDecision? LastDecision { get; private set; }

    /// <summary>Attend the Arcademy: the ported launch path — idempotency, then the shut door,
    /// then the T2 bar, in upstream's order (<c>:126-146</c>). Never throws: a thrown open is the
    /// typed <see cref="ArcademyAttendOutcome.Faulted"/>, so a caller may discard the task.</summary>
    public async Task<ArcademyAttendOutcome> AttendAsync(CancellationToken cancellationToken = default)
    {
        AttendCount++;

        // 1. Idempotent, FIRST (:126-130): a live session is only ever re-presented.
        if (_participant is { } live)
        {
            _host.LogDiagnostic("arcademy: attend gesture on an already-open session — re-presented");
            return new ArcademyAttendOutcome.AlreadyOpen(live);
        }

        // 2. The door itself (:136-141), and SYNCHRONOUSLY, before the first await. Upstream keeps
        //    this refusal in the code path that actually opens the door — "the house rule is that
        //    the code path which actually opens the door has to be the one that can say no"
        //    (:133-138) — because the card is one edit away from visible. Same rule here: the
        //    refusal lives where the opening would happen, not in whatever UI hangs off it.
        if (!ArcademyDoor.Available)
        {
            _host.LogDiagnostic($"arcademy: attend refused: {ArcademyDoor.Refusal.Reason}");
            return new ArcademyAttendOutcome.Refused(ArcademyDoor.Refusal);
        }

        try
        {
            // 3. The T2 bar (:146). BEHIND the door, so nothing in a shipped build reads a login
            //    or asks an authority about this surface at all.
            EntitlementOutcome outcome;
            try
            {
                outcome = await _entitlement(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A throw HERE is still "could not tell", so it stays a refusal rather than a
                // fault: the question asked was about the entitlement and the honest answer is
                // that it could not be determined. Type name only — an exception message on this
                // path can carry a path or a bearer (the DtrhLaunch rule, same reason).
                outcome = new EntitlementOutcome.Unavailable(new EntitlementReason(
                    EntitlementReasonCodes.TierAuthorityFault,
                    "resolving the entitlement threw " + ex.GetType().Name));
            }

            var decision = ArcademyGate.Decide(outcome);
            LastDecision = decision;
            // Outcome CLASS plus reason CODE, never the detail and never anything token-derived
            // (the entitlement-logging discipline; EntitlementOutcome.Describe is that rendering).
            _host.LogDiagnostic($"arcademy: tier bar — entitlement {outcome.Describe()} -> {Classify(decision)}");
            if (decision is not ArcademyGateDecision.Proceed)
            {
                return new ArcademyAttendOutcome.Gated(decision);
            }

            var participant = new ArcademyParticipant(new LogSinkAdapter(_host), DataDirectory);
            var probe = participant.Start();
            _participant = participant;
            _host.LogDiagnostic($"arcademy: origins up (payload {probe.State}, {probe.FileCount} files)");
            return new ArcademyAttendOutcome.Opened(participant, participant.PageUrl());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller asked for the stop. Painting a failure for it would be the small lie the
            // entitlement capability's own remarks refuse.
            throw;
        }
        catch (Exception ex)
        {
            _host.LogDiagnostic($"arcademy: attend faulted ({ex.GetType().Name}: {ex.Message})");
            try { _participant?.Dispose(); } catch { /* best-effort */ }
            _participant = null;
            return new ArcademyAttendOutcome.Faulted(ex);
        }
    }

    /// <summary>Close a live session (idempotent). Nothing to close is not an error.</summary>
    public void Close()
    {
        var participant = _participant;
        _participant = null;
        participant?.Dispose();
    }

    /// <summary>Log-safe rendering of a gate decision: the decision CLASS and, where there is
    /// one, the reason CODE — never a message, never anything token-derived.</summary>
    private static string Classify(ArcademyGateDecision decision) => decision switch
    {
        ArcademyGateDecision.Proceed proceed => "proceed(" + proceed.Tier.ToString().ToLowerInvariant() + ")",
        ArcademyGateDecision.RefusedNotEntitled => "refused(not-entitled)",
        ArcademyGateDecision.RefusedUnverified unverified => "refused(unverified:" + unverified.ReasonCode + ")",
        _ => "refused(unknown-decision)",
    };

    /// <summary>Adapts the host's diagnostic log to the transport contracts' ILogSink
    /// (<c>GoonLaunch</c>'s adapter, same shape).</summary>
    private sealed class LogSinkAdapter(ApplicationHost host) : ILogSink
    {
        public void Log(string message) => host.LogDiagnostic(message);
    }
}
