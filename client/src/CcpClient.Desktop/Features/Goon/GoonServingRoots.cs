namespace CcpClient.Desktop.Features.Goon;

/// <summary>
/// The Goon payload-root probe (the <see cref="Intake.IntakeServingRoots"/> shape,
/// payload-probe discipline). The goon tree rides the SAME copied-asset convention as dtrh/intake/
/// tunnel/vendor — a LINKED read-only glob in <c>CcpClient.Desktop.csproj</c> that copies the
/// bytes unmodified to <c>payload/goon</c> beside the exe. <b>Zero bytes are forked: the
/// legacy tree stays the single owner</b> (goon-game-census.md §5.3).
///
/// A missing or incomplete tree is a TYPED state the host window shows honestly — never a
/// crash, never a silent substitute tree, never a dev-worktree walk-up.
/// </summary>
public static class GoonServingRoots
{
    /// <summary>Payload-root presence states (the payload-probe pattern).</summary>
    public enum GoonPayloadState
    {
        Present,
        Missing,
        Incomplete,
    }

    /// <summary>The probe outcome: state + observed file count + the RESOLVED root (a boot
    /// transcript is self-evidencing about WHERE the page is served from). MissingFile names
    /// the first required file whose absence typed the tree Incomplete.</summary>
    public sealed record GoonPayloadProbe(GoonPayloadState State, int FileCount, string Root, string? MissingFile = null);

    /// <summary>Published-artifact location (beside the exe, via the linked glob).</summary>
    public static string PayloadRoot => Path.Combine(AppContext.BaseDirectory, "payload", "goon");

    /// <summary>The <c>/media/*</c> route's root. The goon page addresses every one of its own
    /// assets relatively (<c>./assets/…</c>, index.html), so this route is never exercised by
    /// the page; it is supplied because the §4 server class takes one.</summary>
    public static string MediaRoot => Path.Combine(PayloadRoot, "assets");

    /// <summary>Files the page cannot boot without, each verified as a real entry point:
    /// <c>index.html</c> (the document), <c>boot.js</c> (its only module script, index.html:96)
    /// and <c>bridge.js</c> (boot.js's transport import — no bridge, no handshake).</summary>
    public static readonly IReadOnlyList<string> RequiredFiles = ["index.html", "boot.js", "bridge.js"];

    /// <summary>The file count of the upstream tree at this packet's baseline (v6.8.0 /
    /// upstream-payload-inventory.json). Record data for the transcript — the guard that pins
    /// it lives in the test, so a drift is loud rather than silently absorbed here.</summary>
    public const int FileCountAtBaseline = 184;

    /// <summary>Non-fatal presence probe (IntakeServingRoots.Probe parity).</summary>
    public static GoonPayloadProbe Probe(string? payloadRoot = null)
    {
        var root = payloadRoot ?? PayloadRoot;
        if (!Directory.Exists(root))
        {
            return new GoonPayloadProbe(GoonPayloadState.Missing, 0, root);
        }

        var count = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count();
        foreach (var required in RequiredFiles)
        {
            if (!File.Exists(Path.Combine(root, required.Replace('/', Path.DirectorySeparatorChar))))
            {
                return new GoonPayloadProbe(GoonPayloadState.Incomplete, count, root, required);
            }
        }

        return new GoonPayloadProbe(GoonPayloadState.Present, count, root);
    }
}
