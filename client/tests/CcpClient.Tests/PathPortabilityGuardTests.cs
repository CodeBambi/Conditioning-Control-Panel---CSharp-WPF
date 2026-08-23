using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Source guard for the "a Windows path separator reaches the user on Linux" class, in the
/// established scan-the-source-and-fail-with-file:line idiom
/// (<see cref="DataRootChokePointGuardTests"/>, <c>TestTimingGuardTests</c>,
/// <c>UnboundedWaitGuardTests</c>). This guard exists because the class is otherwise INVISIBLE
/// from Windows: every defect in it passes the Windows suite and fails only where nobody was
/// running. A guard that reads the source fires on the machine the code is written on, which is
/// the only place a regression in this class can be caught before it ships.
///
/// <para>Two rules, both chosen because they have ZERO legitimate uses in <c>client/src</c>
/// today and therefore need no suppression list. A guard that has to be waived at half its hits
/// teaches people to waive it.</para>
///
/// <list type="number">
/// <item><description>The framework's OS-varying invalid-filename set — NUL and '/' on Unix,
/// forty-one characters on Windows — so any name it sanitises is a DIFFERENT name on the two
/// platforms. Use <c>PortablePath.InvalidFileNameChars</c>, which is the Windows set
/// everywhere and therefore the WPF-observed answer everywhere.</description></item>
/// <item><description>A hardcoded drive-letter literal (<c>@"C:\…"</c> or <c>"C:\\…"</c>) —
/// there is no drive letter on Linux, and a path that assumes one is either a root that cannot
/// exist or a display string that leaks a machine's layout at a user.</description></item>
/// </list>
///
/// <para>Comment lines are not scanned. The rules are about what the program DOES, and a guard
/// that fires on the paragraph explaining it gets its explanation deleted — including this
/// one's, which names the banned member.</para>
///
/// <para>DELIBERATELY NOT GUARDED: a blanket ban on a literal backslash used as a separator
/// (<c>Split</c>, <c>Replace</c>, <c>Contains</c> on <c>'\'</c>). Every one of those in
/// <c>client/src</c> today is CORRECT — the two loopback servers reject a backslash in a URL
/// path on both platforms by design, the asset manifest rejects one in a manifest path for the
/// same reason, the DTRH media map normalises a relative path to '/' for a URL, and
/// <c>AtomicFileSystemProbe</c> un-escapes <c>\134</c> out of Linux's own <c>/proc/mounts</c>.
/// The rule would fire on five correct call sites and nothing else, and the waiver list would
/// BE the guard. Likewise a ban on <c>Path.GetFileName</c> inside display text: right about the
/// five sites this packet fixed, wrong about every path the process built itself, and evadable
/// by assigning to a local first — theatre rather than a guard.</para>
/// </summary>
public class PathPortabilityGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] SrcParts = ["client", "src"];

    /// <summary>Split so this guard's own source is not a hit on the tree it scans.</summary>
    private const string FrameworkInvalidSet = "Path.GetInvalid" + "FileNameChars";

    /// <summary>
    /// A drive letter opening a string literal — <c>@"C:\</c> or <c>"C:\\</c>. Anchored on the
    /// quote so a bare <c>Z:\</c> in prose is not a hit even on a code line.
    /// </summary>
    private static readonly Regex DriveLetterLiteral =
        new(@"@""[A-Za-z]:\\|""[A-Za-z]:\\\\", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void NoOsVaryingPathApiAndNoHardcodedDriveLetterInClientSrc()
    {
        var srcRoot = Path.Combine([FindRepoRoot(), .. SrcParts]);
        Assert.True(Directory.Exists(srcRoot),
            $"client/src not found at {srcRoot} — the path-portability guard refuses to skip");

        var violations = new List<string>();
        var scanned = 0;
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            scanned++;
            violations.AddRange(Violations(file.Replace('\\', '/'), File.ReadAllLines(file)));
        }

        // A scan that walked nothing would pass silently for ever.
        Assert.True(scanned > 100,
            $"only {scanned} source files scanned under {srcRoot} — the guard is not reading the tree");

        Assert.True(violations.Count == 0,
            "path-portability guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));

        // The guard's own teeth, proved rather than assumed. Both banned shapes are caught with a
        // file:line, and the two documentation shapes that genuinely live in this tree —
        // Win32TrayPresence's staging note ("never a Z:\ reference") and OverlayDisplays' device
        // name (\\.\DISPLAY1) — are not.
        var caught = Violations("f.cs",
        [
            @"        var p = @""C:\spirals\classic.gif"";",
            @"        var q = ""Z:\\staged\\rack.png"";",
            $"        var invalid = {FrameworkInvalidSet}();",
        ]).ToList();
        Assert.Equal(3, caught.Count);
        Assert.StartsWith("f.cs:1:", caught[0], StringComparison.Ordinal);
        Assert.StartsWith("f.cs:2:", caught[1], StringComparison.Ordinal);
        Assert.StartsWith("f.cs:3:", caught[2], StringComparison.Ordinal);

        Assert.Empty(Violations("f.cs",
        [
            @"        // overlay staging — never a Z:\ reference",
            @"        /// <param name=""DeviceName"">The OS's own name, e.g. \\.\DISPLAY1.</param>",
            $"        /// Not <c>{FrameworkInvalidSet}()</c>: it varies with the OS.",
            @"         * legacy block comment mentioning @""C:\Users\u""",
        ]));
    }

    private static IEnumerable<string> Violations(string file, IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith('*')
                || trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                continue; // prose, not behaviour
            }

            if (DriveLetterLiteral.IsMatch(line))
            {
                yield return $"{file}:{i + 1}: hardcoded drive-letter path literal — there is no drive letter "
                    + "on Linux. Build the path from the data root, and print a display name with "
                    + "PortablePath.FileName.";
            }

            if (line.Contains(FrameworkInvalidSet, StringComparison.Ordinal))
            {
                yield return $"{file}:{i + 1}: {FrameworkInvalidSet} is NUL and '/' on Linux and forty-one "
                    + "characters on Windows — the same name sanitises differently on the two platforms. "
                    + "Use PortablePath.InvalidFileNameChars.";
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found walking up from {AppContext.BaseDirectory} "
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the path-portability guard refuses to skip");
    }
}
