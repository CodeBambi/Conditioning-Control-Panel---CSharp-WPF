using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// Loom-reveal — the studio rack's 📂 shows the saved GIF in the OS file manager
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
            // Windows: select the file in Explorer. Elsewhere (Linux): open the containing
            // folder — no /select equivalent (recorded divergence).
            //
            // THE ARGUMENT IS NOT PRE-QUOTED, and that is the fix rather than a style choice.
            // This used to build `/select,"<path>"` as one already-quoted string and hand it to
            // the string-Arguments overload. The CRT re-parses those embedded quotes before
            // explorer ever sees them, and on a mismatch explorer does not report an error — it
            // SILENTLY OPENS THE DESKTOP instead, so the button looked like it did nothing.
            // Upstream hit this on media held on a different drive from the app and fixed it the
            // same way (Helpers/ExplorerLauncher.cs): one pre-tokenized argument, quoted by the
            // runtime's own Windows rules, so spaces and non-ASCII survive intact.
            //
            // A full path, because /select against a relative path resolves off the launching
            // process's working directory rather than the caller's intent.
            var full = Path.GetFullPath(path);
            var accepted = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? launch("explorer.exe", File.Exists(full)
                    ? $"/select,{full}"
                    // The file went away between the listing and the click (deleted, drive
                    // detached). Settling for its folder beats /select on a path that is gone,
                    // which is the desktop-fallback again by another route.
                    : Path.GetDirectoryName(full)!)
                : launch("xdg-open", Path.GetDirectoryName(full)!);
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

    /// <summary>Starts the program with the argument handed over as ONE pre-tokenized element.
    /// The string-Arguments overload would re-parse it, which is the defect above.</summary>
    private static bool OsLaunch(string program, string arguments)
    {
        try
        {
            var start = new ProcessStartInfo(program) { UseShellExecute = false };
            start.ArgumentList.Add(arguments);
            Process.Start(start);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
