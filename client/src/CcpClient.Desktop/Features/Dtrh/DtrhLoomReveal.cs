using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// SP-049: loom-reveal — the studio rack's 📂 shows the saved GIF in the OS file manager
/// (loomStudio.js:749; WPF LoomHostService.cs:108-117: <c>explorer.exe /select,"&lt;path&gt;"</c>).
/// The path comes ONLY from <see cref="DtrhLoom.GifPathFor"/> (slug whitelist + existence —
/// page strings never become paths). Linux has no /select equivalent: the folder opens
/// instead (recorded divergence; WPF is Windows-only). Presence-only logging — the path is
/// path-class content, never logged (§4.8 class).
/// </summary>
public static class DtrhLoomReveal
{
    /// <summary>The typed reveal outcome (never throws; tests assert the shape).</summary>
    public abstract record Outcome
    {
        private Outcome() { }

        /// <summary>The file manager was launched (the launch delegate accepted).</summary>
        public sealed record Revealed : Outcome
        {
            public static readonly Revealed Instance = new();
        }

        /// <summary>Bad slug or no such spiral — nothing launched (WPF: silent skip; greenfield: typed log).</summary>
        public sealed record Refused(string Reason) : Outcome;

        /// <summary>The OS launch itself failed (typed, never a crash).</summary>
        public sealed record LaunchFailed(string Detail) : Outcome;
    }

    /// <summary>Reveal one saved spiral. <paramref name="launch"/> is the OS seam
    /// (program + arguments → accepted); tests inject a recorder, never a process.</summary>
    public static Outcome Reveal(
        DtrhLoom loom,
        string? slug,
        Action<string> log,
        Func<string, string, bool>? launch = null)
    {
        var path = loom.GifPathFor(slug);
        if (path is null)
        {
            log("dtrh-loom: reveal refused (bad slug or no such spiral)"); // presence only
            return new Outcome.Refused("no-such-spiral");
        }

        launch ??= OsLaunch;
        try
        {
            // Windows: select the file in Explorer (WPF verbatim). Elsewhere (Linux):
            // open the containing folder — no /select equivalent (recorded divergence).
            var accepted = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? launch("explorer.exe", $"/select,\"{path}\"")
                : launch("xdg-open", $"\"{Path.GetDirectoryName(path)!}\"");
            if (!accepted)
            {
                log("dtrh-loom: reveal launch refused by the OS seam");
                return new Outcome.LaunchFailed("launch-refused");
            }

            log("dtrh-loom: reveal launched"); // presence only — never the path
            return Outcome.Revealed.Instance;
        }
        catch (Exception ex)
        {
            log($"dtrh-loom: reveal failed ({ex.GetType().Name})");
            return new Outcome.LaunchFailed(ex.GetType().Name);
        }
    }

    private static bool OsLaunch(string program, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = false });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
