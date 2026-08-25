using System.Text.RegularExpressions;
using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The Linux evidence record, kept honest by machine rather than by memory.
///
/// <para><b>What this file is NOT.</b> It is not Linux evidence. The evidence is ten real captures
/// of the running shell on a real X11 desktop under WSLg, checked by <c>CcpVerify</c> against
/// <c>client/tools/verify/checks.json</c>, every one shown scoring 0.000 on a real capture of the
/// opposite state. Nothing in a Windows test process establishes anything about Linux and no fact
/// here claims to.</para>
///
/// <para><b>What it IS.</b> The thing that rots between headed runs: the record of WHICH named
/// checks Linux reaches and, for every one it does not, the exact mechanism that stops it.
/// <c>client/port.txt</c> requires a named mechanism rather than "not supported yet", and a
/// document is exactly where that requirement decays silently — a check added to the manifest
/// simply never appears in it, and nobody notices for a month. These facts read BOTH files on every
/// floor run, so the manifest and the record cannot drift apart.</para>
///
/// <para><b>Honest limit.</b> Every fact here is LEXICAL. They prove the record classifies every
/// check, that its gate vocabulary is closed and explained, and that the surfaces it calls reached
/// are the surfaces the Linux harness can actually be asked for. They cannot prove a reading was
/// ever taken; only the captures do that.</para>
/// </summary>
public class LinuxEvidenceGateTests
{
    private const string Reached = "REACHED";
    private const string Gated = "GATED";
    private const string Undriven = "UNDRIVEN";

    /// <summary>One row of the record: a check name and the status column beside it.</summary>
    private sealed record Row(string Check, string Status, string Detail);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "client", "CcpClient.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not find the repository root from " + AppContext.BaseDirectory);
    }

    private static string RecordPath() =>
        Path.Combine(RepoRoot(), "client", "docs", "linux-evidence.md");

    private static string RecordText() => File.ReadAllText(RecordPath());

    private static string LinuxHarness() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "client", "tools", "verify", "capture-wslg.sh"));

    private static IReadOnlyList<ManifestCheck> ManifestChecks() =>
        [.. CheckManifest.Load(Path.Combine(RepoRoot(), "client", "tools", "verify", "checks.json"))];

    /// <summary>
    /// Every table row of the record, in file order. The shape is deliberately uniform across the
    /// three tables — check, status, detail — because a parser that has to know which table it is
    /// in is a parser that stops noticing a row filed under the wrong heading.
    /// </summary>
    private static IReadOnlyList<Row> Rows()
    {
        var pattern = new Regex(@"^\| `(?<check>[a-z0-9-]+)` \| (?<status>REACHED-[a-z]+|GATED: [a-z-]+|UNDRIVEN) \| (?<detail>.+?) \|\s*$",
            RegexOptions.Multiline);
        return [.. pattern.Matches(RecordText()).Select(m =>
            new Row(m.Groups["check"].Value, m.Groups["status"].Value, m.Groups["detail"].Value))];
    }

    /// <summary>
    /// EVERY named check is classified, and nothing is classified that is not a named check. This is
    /// the fact that makes the record survive: a check added to the manifest reds the floor here
    /// until somebody decides whether Linux reaches it, rather than being quietly absent from a
    /// document nobody re-reads.
    /// </summary>
    [Fact]
    public void EveryNamedCheckIsClassifiedExactlyOnce()
    {
        var manifest = ManifestChecks().Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var recorded = Rows().Select(r => r.Check).ToArray();

        Assert.Equal(recorded.Length, recorded.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(manifest, recorded.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The counts in the record's own headings match the rows underneath them. Written because the
    /// headings are the part a reader believes without checking, and they are also the part that
    /// goes stale the moment one row moves between sections.
    /// </summary>
    [Fact]
    public void EachHeadingCountMatchesItsRows()
    {
        var rows = Rows();
        var text = RecordText();
        var total = ManifestChecks().Count;

        // The three sections between them classify every check, asserted here rather than only
        // inside the loop below: a record whose tables all emptied would otherwise iterate three
        // headings of zero and agree with itself.
        Assert.Equal(total, rows.Count);

        foreach (var (heading, status) in new[]
                 {
                     ("Reached", Reached), ("Gated", Gated), ("Undriven", Undriven),
                 })
        {
            var counted = rows.Count(r => r.Status.StartsWith(status, StringComparison.Ordinal));
            Assert.Contains($"## {heading}: {counted} of {total}", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The gate vocabulary is CLOSED, and every mechanism it uses is explained in the record itself.
    /// <c>client/port.txt</c>'s rule is that an unavailable platform gate names a mechanism and why;
    /// an open vocabulary is how "not supported yet" gets back in wearing a hyphen.
    /// </summary>
    [Fact]
    public void EveryGateNamesAMechanismTheRecordExplains()
    {
        string[] allowed = ["ambiguous-accessible-name", "no-webkit-on-this-image"];
        var text = RecordText();
        var gates = Rows().Where(r => r.Status.StartsWith(Gated, StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(gates);
        foreach (var gate in gates)
        {
            var mechanism = gate.Status["GATED: ".Length..];
            Assert.Contains(mechanism, allowed);
            // The mechanism has its own bullet, not merely a mention in the table it labels.
            Assert.Contains($"- **`{mechanism}`** —", text, StringComparison.Ordinal);
        }

        foreach (var mechanism in allowed)
        {
            Assert.Contains(gates, g => g.Status.EndsWith(mechanism, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// No row's reason is a placeholder. A gate that says "not supported yet" is the exact shape
    /// <c>client/port.txt</c> forbids, and an UNDRIVEN row that says "TODO" is a gate pretending not
    /// to be one.
    /// </summary>
    [Fact]
    public void NoRowReasonIsAPlaceholder()
    {
        string[] placeholders = ["not supported yet", "TODO", "TBD", "later", "coming soon", "unsupported"];
        var rows = Rows();

        // Non-vacuous by construction: an empty record would pass a loop over nothing, and this is
        // exactly the fact whose whole value is that it ran.
        Assert.Equal(ManifestChecks().Count, rows.Count);

        foreach (var row in rows)
        {
            foreach (var placeholder in placeholders)
            {
                Assert.DoesNotContain(placeholder, row.Detail, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// THE RECORD AND THE HARNESS AGREE ON WHAT LINUX CAN BE ASKED FOR. Every surface/state pair
    /// whose checks the record calls REACHED must be a pair <c>capture-wslg.sh</c> accepts, and
    /// every pair it accepts must be one the record calls reached. Without this the record is a
    /// claim about a script rather than a claim CHECKED against it — and the failure mode is the
    /// expensive one: a surface silently dropped from the harness while the document still says
    /// Linux reaches it.
    /// </summary>
    [Fact]
    public void TheHarnessAcceptsExactlyThePairsTheRecordCallsReached()
    {
        var reached = Rows()
            .Where(r => r.Status.StartsWith(Reached, StringComparison.Ordinal))
            .Select(r => r.Check)
            .ToHashSet(StringComparer.Ordinal);

        var recordPairs = ManifestChecks()
            .Where(c => reached.Contains(c.Name))
            .Select(c => $"{c.Surface}/{c.State}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        // The harness declares its pairs in one `case` arm per line, which is the same
        // ValidateSet-by-pair shape capture.ps1 uses on Windows.
        var accepted = Regex.Matches(LinuxHarness(), @"^  (?<pairs>[a-z-]+/[a-z-]+(?:\|[a-z-]+/[a-z-]+)*)\) ;;",
                RegexOptions.Multiline)
            .SelectMany(m => m.Groups["pairs"].Value.Split('|'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(accepted);
        Assert.Equal(recordPairs, accepted);
    }

    /// <summary>
    /// The element route's own preconditions are still in the harness. LEXICAL, and it says so: this
    /// proves the accessibility bus is asked for BEFORE the app is launched and that the query is a
    /// READ rather than a write. The read matters beyond style — <c>org.a11y.Status.IsEnabled</c> is
    /// dconf-backed, so setting it would make a persistent change to the user's own desktop settings
    /// as a side effect of taking a screenshot, and the switch was measured NOT to be a precondition
    /// in Avalonia 12.1.1 anyway.
    /// </summary>
    [Fact]
    public void TheHarnessReadsTheAccessibilityBusRatherThanSwitchingAccessibilityOn()
    {
        var code = string.Join('\n', LinuxHarness().Split('\n').Where(l => !l.TrimStart().StartsWith('#')));

        var busQuery = code.IndexOf("org.a11y.Bus.GetAddress", StringComparison.Ordinal);
        var appLaunch = code.IndexOf("dotnet \"$DLL\"", StringComparison.Ordinal);
        Assert.True(busQuery >= 0, "the harness no longer asks for the accessibility bus at all");
        Assert.True(appLaunch >= 0, "the harness no longer launches the app");
        Assert.True(busQuery < appLaunch,
            "the accessibility bus must be reachable before the app starts, or it registers nothing");

        Assert.DoesNotContain("Properties.Set org.a11y.Status", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The AT-SPI route refuses an ambiguous selector rather than resolving it to the first match.
    /// LEXICAL. It exists because the temptation is real and the consequence is silent: two rack
    /// rows, two sliders or two buttons sharing an accessible name is the ordinary case on this
    /// tree — <c>Master volume</c> names a caption AND its slider — and "take the first one" would
    /// photograph whichever the tree walk happened to reach.
    /// </summary>
    [Fact]
    public void TheElementRouteRefusesAnAmbiguousSelector()
    {
        var tool = File.ReadAllText(Path.Combine(RepoRoot(), "client", "tools", "verify", "atspi.py"));
        Assert.Contains("if len(hits) > 1:", tool, StringComparison.Ordinal);
        Assert.Contains("A capture must name exactly one", tool, StringComparison.Ordinal);
    }
}
