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
    /// transcript is self-evidencing about WHERE the page is served from). MissingFile
    /// names the first required file whose absence typed the tree Incomplete.</summary>
    public sealed record IntakePayloadProbe(IntakePayloadState State, int FileCount, string Root, string? MissingFile = null);

    /// <summary>Published-artifact location (beside the exe, via the linked glob).</summary>
    public static string PayloadRoot => Path.Combine(AppContext.BaseDirectory, "payload", "intake");

    /// <summary>Files the page cannot boot without. SP-058: the v6.7.x delta added
    /// <c>core/accents.js</c> — <c>core/ai.js:24</c> imports it, so a missing accents module
    /// kills the whole ES-module graph; its absence is typed Incomplete HERE (never a silent
    /// 404 the payload masks as a dead page).</summary>
    public static readonly IReadOnlyList<string> RequiredFiles = ["index.html", "core/accents.js"];

    /// <summary>Non-fatal presence probe (SP-048 ProbePayloadRoot parity).</summary>
    public static IntakePayloadProbe Probe(string? payloadRoot = null)
    {
        var root = payloadRoot ?? PayloadRoot;
        if (!Directory.Exists(root))
        {
            return new IntakePayloadProbe(IntakePayloadState.Missing, 0, root);
        }

        var count = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count();
        foreach (var required in RequiredFiles)
        {
            if (!File.Exists(Path.Combine(root, required.Replace('/', Path.DirectorySeparatorChar))))
            {
                return new IntakePayloadProbe(IntakePayloadState.Incomplete, count, root, required);
            }
        }

        return new IntakePayloadProbe(IntakePayloadState.Present, count, root);
    }
}
