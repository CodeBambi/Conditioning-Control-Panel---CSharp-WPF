namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the intake payload-root probe (SP-048 discipline, intake variant). The intake
/// tree rides the SAME copied-asset convention as DTRH (SP-023 linked glob — added with
/// this packet's flagged serving glue; the legacy tree stays the READ-ONLY trust anchor)
/// and is served read-only from <c>payload/intake</c> beside the exe. A missing or
/// incomplete tree is a TYPED state (the window shows the honest surface) — never a crash,
/// never a silent substitute, never a dev-worktree walk-up (pre-approach consult ruling 2:
/// the walk-up is a Z:\-class smell, rejected).
/// </summary>
public static class IntakeServingRoots
{
    /// <summary>Payload-root presence states (SP-048 pattern).</summary>
    public enum IntakePayloadState
    {
        Present,
        Missing,
        Incomplete,
    }

    /// <summary>The probe outcome (state + observed file count + the RESOLVED root — a boot
    /// transcript is self-evidencing about WHERE the page is served from).</summary>
    public sealed record IntakePayloadProbe(IntakePayloadState State, int FileCount, string Root);

    /// <summary>Published-artifact location (beside the exe, via the linked glob).</summary>
    public static string PayloadRoot => Path.Combine(AppContext.BaseDirectory, "payload", "intake");

    /// <summary>Non-fatal presence probe (SP-048 ProbePayloadRoot parity).</summary>
    public static IntakePayloadProbe Probe(string? payloadRoot = null)
    {
        var root = payloadRoot ?? PayloadRoot;
        if (!Directory.Exists(root))
        {
            return new IntakePayloadProbe(IntakePayloadState.Missing, 0, root);
        }

        var count = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count();
        return File.Exists(Path.Combine(root, "index.html"))
            ? new IntakePayloadProbe(IntakePayloadState.Present, count, root)
            : new IntakePayloadProbe(IntakePayloadState.Incomplete, count, root);
    }
}
