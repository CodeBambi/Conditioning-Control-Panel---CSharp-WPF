using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// THE one place an Arcademy session is opened — the <see cref="Goon.GoonLaunch"/> /
/// <see cref="Intake.IntakeLaunch"/> pattern (one construction site, several callers, no second
/// launcher), and the place the door refuses.
///
/// <para><b>Order matters and is upstream's.</b> <c>ArcademyHostService.Launch</c> runs
/// idempotent re-focus FIRST (<c>:130</c>, so a live class is never stranded behind a gate that
/// only applies to opening a new one), then the DOOR (<c>:136-141</c>), and only then the T2 bar
/// (<c>:146</c>) and AudioOnlySession (<c>:153-157</c>). This launcher reproduces the first two.
/// The T2 and audio-only gates belong to slice 8 and are NOT here: adding them under a shut door
/// would be gates nothing can reach, and the door refuses ahead of both upstream anyway.</para>
///
/// <para><b>What "opens nothing" means concretely.</b> Under a shut door this method constructs
/// no <see cref="ArcademyParticipant"/>, so no loopback origin is bound, no port is listening, no
/// payload byte is reachable and no page exists to navigate to. The refusal is the first thing
/// that happens after the idempotency check, before any allocation.</para>
///
/// <para><b>What this launcher does NOT do yet.</b> It opens the ORIGINS and hands back the url a
/// host window would navigate to; it shows no window, because the window, the Play-tab entry and
/// the T2 gate are slice 8. This is the construction site that slice 8 attaches them to, and
/// until then the door is shut so the open path is unreachable in a shipped build.</para>
/// </summary>
public sealed class ArcademyLaunch
{
    private readonly ApplicationHost _host;
    private ArcademyParticipant? _participant;

    public ArcademyLaunch(ApplicationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
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

        /// <summary>The door said no. Nothing was constructed, bound or shown.</summary>
        public sealed record Refused(ArcademyDoor.ArcademyDoorRefusal Refusal) : ArcademyAttendOutcome;

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

    /// <summary>Attend the Arcademy: the ported launch path, refusing on the shut door.</summary>
    public ArcademyAttendOutcome Attend()
    {
        AttendCount++;

        // 1. Idempotent, FIRST (:126-130): a live session is only ever re-presented.
        if (_participant is { } live)
        {
            _host.LogDiagnostic("arcademy: attend gesture on an already-open session — re-presented");
            return new ArcademyAttendOutcome.AlreadyOpen(live);
        }

        // 2. The door itself (:136-141). Upstream keeps this refusal in the code path that
        //    actually opens the door — "the house rule is that the code path which actually opens
        //    the door has to be the one that can say no" (:133-138) — because the card is one edit
        //    away from visible. Same rule here: the refusal lives where the opening would happen,
        //    not in whatever UI a later slice hangs off it.
        if (!ArcademyDoor.Available)
        {
            _host.LogDiagnostic($"arcademy: attend refused: {ArcademyDoor.Refusal.Reason}");
            return new ArcademyAttendOutcome.Refused(ArcademyDoor.Refusal);
        }

        try
        {
            var participant = new ArcademyParticipant(new LogSinkAdapter(_host), DataDirectory);
            var probe = participant.Start();
            _participant = participant;
            _host.LogDiagnostic($"arcademy: origins up (payload {probe.State}, {probe.FileCount} files)");
            return new ArcademyAttendOutcome.Opened(participant, participant.PageUrl());
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

    /// <summary>Adapts the host's diagnostic log to the transport contracts' ILogSink
    /// (<c>GoonLaunch</c>'s adapter, same shape).</summary>
    private sealed class LogSinkAdapter(ApplicationHost host) : ILogSink
    {
        public void Log(string message) => host.LogDiagnostic(message);
    }
}
