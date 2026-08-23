namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// The Arcademy payload-root probe and the answer to the tree's ONE serving trap. Same
/// payload-probe discipline as <see cref="Goon.GoonServingRoots"/> and
/// <see cref="Intake.IntakeServingRoots"/>: the tree rides a LINKED read-only glob in
/// <c>CcpClient.Desktop.csproj</c> that copies the bytes unmodified to <c>payload/arcademy</c>
/// beside the exe, <b>zero bytes are forked</b>, and a missing or incomplete tree is a TYPED
/// state rather than a crash or a silent substitute.
///
/// <para><b>THE TRAP: the arcademy tree does not stand alone.</b> The shell picks one spiral
/// per class out of DTRH's asset tree by RELATIVE url —
/// <c>arcademy/shell/shell.js:76</c> (<c>new URL('../../dtrh/assets/bubbles/effects/spirals/' +
/// file, import.meta.url)</c>) and <c>arcademy/games/the-deep-end/pressure.js:182</c>
/// (<c>SPIRAL_DIR: '../../../dtrh/assets/bubbles/effects/spirals/'</c>). Upstream answers that by
/// mapping the whole web ROOT to one origin (<c>ArcademyHostService.cs:167-171</c>:
/// <c>webRoot = …/Resources/web</c> → <c>ccp.game</c>, page at
/// <c>https://ccp.game/arcademy/index.html</c>), not by copying assets into the arcademy tree.
/// <b>This port answers it the same way and with the same server</b>: <see cref="WebRoot"/> —
/// the output <c>payload/</c> folder, which is already a faithful mirror of
/// <c>Resources/web</c> now that six linked globs land dtrh, intake, tunnel, goon, vendor and
/// arcademy side by side — is handed to <see cref="Dtrh.LoopbackServer"/> as BOTH of its page
/// roots, and the page is served at <c>/dtrh/arcademy/index.html</c>. From there
/// <c>../../dtrh/assets/…</c> resolves to <c>/dtrh/dtrh/assets/…</c>, whose <c>rel</c> under the
/// server's fixed <c>/dtrh/</c> route class is <c>dtrh/assets/…</c> — the real DTRH tree, served
/// read-only, byte-identical, from the copy that already exists.
/// </summary>
public static class ArcademyServingRoots
{
    /// <summary>Payload-root presence states (the payload-probe pattern).</summary>
    public enum ArcademyPayloadState
    {
        Present,
        Missing,
        Incomplete,
    }

    /// <summary>Probe outcome: state + observed file count + the RESOLVED root, so a boot
    /// transcript is self-evidencing about where the page is served from. MissingFile names the
    /// first required file whose absence typed the tree Incomplete.</summary>
    public sealed record ArcademyPayloadProbe(ArcademyPayloadState State, int FileCount, string Root, string? MissingFile = null);

    /// <summary>The web ROOT the page origin serves — the mirror of upstream's
    /// <c>Resources/web</c> (<c>ArcademyHostService.cs:167</c>). This is the serving root
    /// precisely because of the spiral borrow documented on this type.</summary>
    public static string WebRoot => Path.Combine(AppContext.BaseDirectory, "payload");

    /// <summary>The arcademy tree itself (what the glob copies, what the probe counts).</summary>
    public static string PayloadRoot => Path.Combine(WebRoot, "arcademy");

    /// <summary>The page's own route under the reused server's fixed <c>/dtrh/</c> route class.
    /// Upstream's start url is <c>https://ccp.game/arcademy/index.html</c>
    /// (<c>ArcademyHostService.cs:188</c>); the <c>arcademy/</c> path segment is load-bearing —
    /// drop it and every relative borrow above resolves one directory too high.</summary>
    public const string PageRoute = "arcademy/index.html";

    /// <summary>The borrowed DTRH spiral directory, as the page's own relative urls resolve it
    /// under <see cref="WebRoot"/>. Not a mapping this code performs — a fact about where the
    /// bytes must be for <c>shell.js:76</c> to 200.</summary>
    public const string BorrowedSpiralDirectory = "dtrh/assets/bubbles/effects/spirals";

    /// <summary>Files the page cannot boot without, each a verified entry point:
    /// <c>index.html</c> (the document), <c>boot.js</c> (its only module script,
    /// <c>index.html:51</c>), <c>bridge.js</c> (boot.js's transport import, <c>boot.js:27</c> —
    /// no bridge, no handshake) and <c>styles.css</c> (<c>index.html:15</c>).</summary>
    public static readonly IReadOnlyList<string> RequiredFiles =
        ["index.html", "boot.js", "bridge.js", "styles.css"];

    /// <summary>The upstream tree's file count at this packet's baseline (v6.8.4 —
    /// <c>client/docs/upstream-payload-inventory.json</c>, which records the v6.8.3 → v6.8.4
    /// growth from 88). Record data for the transcript; the guard that pins it lives in the
    /// test, so a drift is loud rather than silently absorbed here.</summary>
    public const int FileCountAtBaseline = 91;

    /// <summary>Non-fatal presence probe (<see cref="Goon.GoonServingRoots.Probe"/> parity).</summary>
    public static ArcademyPayloadProbe Probe(string? payloadRoot = null)
    {
        var root = payloadRoot ?? PayloadRoot;
        if (!Directory.Exists(root))
        {
            return new ArcademyPayloadProbe(ArcademyPayloadState.Missing, 0, root);
        }

        var count = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count();
        foreach (var required in RequiredFiles)
        {
            if (!File.Exists(Path.Combine(root, required.Replace('/', Path.DirectorySeparatorChar))))
            {
                return new ArcademyPayloadProbe(ArcademyPayloadState.Incomplete, count, root, required);
            }
        }

        return new ArcademyPayloadProbe(ArcademyPayloadState.Present, count, root);
    }
}
