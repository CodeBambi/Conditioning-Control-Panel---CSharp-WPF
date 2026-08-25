using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The ONE tree-wide census under <c>client/docs/window-behavior-manifest.md</c> §8.3.
///
/// <para><b>Why one guard and not one per site.</b> Five of the eight sites already read the operating
/// system or the window manager back and refuse in type — <c>Overlay/Win32OverlayPresence.cs:610-623</c>,
/// <c>Glyph/Win32GlyphSurface.cs:638-652</c>, <c>Pointer/Win32PointerSurface.cs:445-468</c>,
/// <c>Input/Win32InputPresence.cs:362-368</c>, <c>Video/Win32VideoPresence.cs:175-182</c>. A per-surface
/// lexical guard restating any of those would be a SECOND authority that can drift from the first, and
/// a per-surface guard over the sites that declared nothing would freeze exactly the accident this
/// census exists to expose. So this file pins one thing only: <b>no native window exists in the tree
/// without a declared policy row</b>.</para>
///
/// <para><b>The document is the DATA, this file is the LOGIC</b> (<c>HapticSiteCensusTests</c>'s rule).
/// The needle set and the file universe live HERE and the site set is RE-DERIVED from the shipping
/// bytes of <c>client/src</c> on every run, so editing the manifest can never shrink the search. Every
/// occurrence of a needle is classified as a call site, a P/Invoke declaration, or prose; an occurrence
/// this file cannot classify is a hard FAILURE rather than a silent skip, because "the scanner did not
/// understand that line" and "there is no window there" are different facts.</para>
///
/// <para><b>What it deliberately does NOT pin.</b> Which extended-style flags a site may use. Freezing
/// those would make the manifest an authority over behaviour instead of a record of it, and would
/// freeze the undeclared sites in place. Nor does it assert anything about screen capture: no
/// <c>SetWindowDisplayAffinity</c> call exists anywhere in <c>client/src</c>, the capture boundary is
/// owner-reserved (<c>client/port.txt:34-35</c>), and capture visibility is per feature contract and
/// must never be inferred from input behaviour. The one capture rule here is anti-vacuity: a capture
/// value that is not <c>UNEXAMINED</c> must cite the <c>File.cs:line</c> where the policy is made.</para>
/// </summary>
public sealed class NativeWindowCensusTests
{
    /// <summary>Creates a Win32 top-level window.</summary>
    private const string CreateNeedle = "CreateWindowExW";

    /// <summary>Mutates the extended style of a window Avalonia created — the OTHER shape, which
    /// creates nothing and is invisible to a <see cref="CreateNeedle"/> sweep.</summary>
    private const string MutateNeedle = "AddWindowStylesCallback";

    private const string SectionHeading = "### 8.3 The eight sites";

    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] ManifestParts = ["client", "docs", "window-behavior-manifest.md"];
    private static readonly string[] SourceRootParts = ["client", "src"];

    /// <summary>Rows are cited relative to this, matching the manifest's own §8.3 convention.</summary>
    private const string CitationRoot = "CcpClient.Desktop/";

    /// <summary>The closed vocabulary of §8.2. Exactly three, and a fourth reds this guard.</summary>
    private static readonly string[] LegalProvenances = ["runtime-enforced", "upstream-cited", "UNEXAMINED"];

    private static readonly string[] LegalKinds = ["create", "ex-style"];

    private static readonly Regex CitationPattern = new(@"`([^`]+\.cs):(\d+)`", RegexOptions.Compiled);

    private readonly ITestOutputHelper _output;

    public NativeWindowCensusTests(ITestOutputHelper output) => _output = output;

    // ==================================================================
    // The census, against the real repository this test runs from.
    // ==================================================================

    [Fact]
    public void EveryNativeWindowInTheTreeCarriesADeclaredPolicyRow_AndTheSiteSetIsRederivedFromSourceNotFromTheDocument()
    {
        var scan = ScanSource(Path.Combine([FindRepoRoot(), .. SourceRootParts]));
        var rows = ParseRows(File.ReadAllText(Path.Combine([FindRepoRoot(), .. ManifestParts])));
        var (undeclared, phantom) = Compare(scan.Sites, rows);

        _output.WriteLine(Describe(scan, rows, undeclared, phantom));

        Assert.True(scan.Unclassified.Count == 0,
            "the sweep found an occurrence of a native-window needle it cannot classify as a call, a P/Invoke "
            + "declaration or prose, so it cannot say whether a window is there: "
            + string.Join("; ", scan.Unclassified));

        Assert.True(undeclared.Count == 0,
            "a native window exists in client/src with no policy row in window-behavior-manifest.md §8.3. Add a "
            + "row naming its input passthrough, its capture value (UNEXAMINED when the code declares none) and "
            + "one of the three §8.2 provenance tags: " + string.Join("; ", undeclared));

        Assert.True(phantom.Count == 0,
            "a §8.3 row cites a line that no longer holds its call — either the surface is gone or the citation "
            + "rotted as the file above it grew: " + string.Join("; ", phantom));
    }

    [Fact]
    public void TheProvenanceVocabularyIsClosed_NoFieldIsBlank_AndACaptureCLAIMMustCiteWhereItIsMade()
    {
        var rows = ParseRows(File.ReadAllText(Path.Combine([FindRepoRoot(), .. ManifestParts])));
        var complaints = Audit(rows);

        _output.WriteLine($"{rows.Count} declared site row(s); {complaints.Count} complaint(s)"
            + (complaints.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, complaints)));

        Assert.True(rows.Count > 0,
            $"no §8.3 row parsed at all — the '{SectionHeading}' section or its table is gone, and a census over "
            + "nothing passes trivially");

        Assert.True(complaints.Count == 0, string.Join(Environment.NewLine, complaints));
    }

    // ==================================================================
    // The census is proved to RED on an addition, against a synthetic tree.
    // Without this the guard above passes whether or not the comparer works.
    // ==================================================================

    /// <summary>One declared site and one undeclared intruder, as the §8.3 table would see them.</summary>
    private const string FixtureTable = SectionHeading + "\n\n"
        + "| ID | Site | Kind | Ex-styles at creation | Input passthrough | Capture | Provenance |\n"
        + "|---|---|---|---|---|---|---|\n"
        + "| S-01 | `Facade/Win32FacadeSurface.cs:2` | create | `WS_EX_LAYERED` | pass-through | `UNEXAMINED` | `runtime-enforced` |\n"
        + "\n## 9. after\n";

    [Fact]
    public void ANativeWindowADDEDWithNoRowIsREPORTED_AndOneWhoseRowRottedOffItsLineIsToo()
    {
        var scan = ScanTwoSiteFixture();
        Assert.Empty(scan.Unclassified);
        Assert.Equal(2, scan.Sites.Count);

        var (undeclared, phantom) = Compare(scan.Sites, ParseRows(FixtureTable));
        Assert.Equal("CcpClient.Desktop/Latecomer/Win32LatecomerSurface.cs:1 (create)", Assert.Single(undeclared));
        Assert.Empty(phantom);

        // And the other direction: a row whose cited LINE no longer holds the call is a phantom,
        // which is how a citation is stopped from rotting silently as the file above it grows.
        var rotted = FixtureTable.Replace("Win32FacadeSurface.cs:2", "Win32FacadeSurface.cs:97", StringComparison.Ordinal);
        var (undeclaredAfterRot, phantomAfterRot) = Compare(scan.Sites, ParseRows(rotted));
        Assert.Contains("CcpClient.Desktop/Facade/Win32FacadeSurface.cs:2 (create)", undeclaredAfterRot);
        Assert.Equal("Facade/Win32FacadeSurface.cs:97 (create)", Assert.Single(phantomAfterRot));
    }

    [Fact]
    public void AnOccurrenceTheScannerCannotClassifyFAILSRatherThanBeingSkipped()
    {
        var scan = ScanUnclassifiableFixture();

        Assert.Empty(scan.Sites);
        Assert.Equal($"Odd.cs:1: var name = nameof({CreateNeedle});", Assert.Single(scan.Unclassified));
    }

    private static SiteScan ScanTwoSiteFixture() => InATempTree(root =>
    {
        WriteSource(root, "CcpClient.Desktop/Facade/Win32FacadeSurface.cs",
        [
            $"// prose naming {CreateNeedle} while creating nothing",
            $"        _window = Win32FacadeInterop.{CreateNeedle}(",
        ]);

        WriteSource(root, "CcpClient.Desktop/Latecomer/Win32LatecomerSurface.cs",
        [
            $"        _window = Win32LatecomerInterop.{CreateNeedle}(",
        ]);

        return ScanSource(root);
    });

    private static SiteScan ScanUnclassifiableFixture() => InATempTree(root =>
    {
        WriteSource(root, "Odd.cs", [$"        var name = nameof({CreateNeedle});"]);
        return ScanSource(root);
    });

    /// <summary>
    /// Temp-tree plumbing, kept OUT of the <c>[Fact]</c> bodies with the rest of the fixture setup so
    /// neither an <c>fs-predicate</c> nor an <c>assertions-all-nested</c> shape lands in a fact — the
    /// same placement <c>ArcademyServingTests</c> and <c>SurfaceExitTests</c> use for the same reason.
    /// </summary>
    private static T InATempTree<T>(Func<string, T> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-window-census-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            return body(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void WriteSource(string root, string relativePath, string[] lines)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }

    // ==================================================================
    // Derivation from the shipping bytes.
    // ==================================================================

    private sealed record SiteScan(IReadOnlyList<string> Sites, IReadOnlyList<string> Unclassified, int FilesRead);

    /// <summary>
    /// Every needle occurrence under <paramref name="sourceRoot"/>, classified. A site is rendered
    /// exactly as <c>path:line (kind)</c> so the comparison below is over a citation and not over a
    /// file name — a call that moves down its own file is a citation that rotted.
    /// </summary>
    private static SiteScan ScanSource(string sourceRoot)
    {
        var sites = new List<string>();
        var unclassified = new List<string>();
        var filesRead = 0;

        var files = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal);

        foreach (var file in files)
        {
            filesRead++;
            var relative = Path.GetRelativePath(sourceRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                Classify(CreateNeedle, "create", relative, lines[i], i + 1, sites, unclassified);
                Classify(MutateNeedle, "ex-style", relative, lines[i], i + 1, sites, unclassified);
            }
        }

        return new SiteScan(sites, unclassified, filesRead);
    }

    private static void Classify(
        string needle,
        string kind,
        string relativePath,
        string line,
        int lineNumber,
        List<string> sites,
        List<string> unclassified)
    {
        if (!line.Contains(needle, StringComparison.Ordinal))
        {
            return;
        }

        var trimmed = line.TrimStart();

        // Prose: a doc comment or a plain comment mentioning the call.
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
        {
            return;
        }

        // The P/Invoke declaration itself creates nothing.
        if (line.Contains("extern", StringComparison.Ordinal))
        {
            return;
        }

        if (line.Contains($".{needle}(", StringComparison.Ordinal))
        {
            sites.Add($"{relativePath}:{lineNumber} ({kind})");
            return;
        }

        unclassified.Add($"{relativePath}:{lineNumber}: {trimmed}");
    }

    // ==================================================================
    // The manifest §8.3 table, read as data.
    // ==================================================================

    private sealed record Row(string Id, string Site, string Kind, string ExStyles, string Input, string Capture, string Provenance);

    private static IReadOnlyList<Row> ParseRows(string markdown)
    {
        var rows = new List<Row>();
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inside = false;

        foreach (var line in lines)
        {
            if (line.StartsWith(SectionHeading, StringComparison.Ordinal))
            {
                inside = true;
                continue;
            }

            if (!inside)
            {
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
            {
                break;
            }

            if (!line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Split('|');

            // A well-formed 7-column row splits into 9 parts (empty at each end). Anything else —
            // including a stray pipe inside a cell — is reported as a malformed row by Audit.
            if (cells.Length != 9)
            {
                rows.Add(new Row($"MALFORMED({cells.Length - 2} cells)", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));
                continue;
            }

            var cell = cells[1..8].Select(c => c.Trim()).ToArray();
            if (cell[0] is "ID" || cell[0].StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            var citation = CitationPattern.Match(cell[1]);
            rows.Add(new Row(
                cell[0],
                citation.Success ? $"{citation.Groups[1].Value}:{citation.Groups[2].Value}" : string.Empty,
                cell[2],
                cell[3],
                cell[4],
                cell[5],
                cell[6].Trim('`')));
        }

        return rows;
    }

    /// <summary>
    /// Derived sites versus declared rows, both directions. <b>Undeclared</b> is a native window in
    /// the tree with no row — the failure this census exists for. <b>Phantom</b> is a row pointing at
    /// a line that no longer holds its call, which is either a deleted surface or a rotted citation.
    /// </summary>
    private static (IReadOnlyList<string> Undeclared, IReadOnlyList<string> Phantom) Compare(
        IReadOnlyList<string> derivedSites,
        IReadOnlyList<Row> rows)
    {
        var declared = rows
            .Where(r => r.Site.Length > 0)
            .Select(r => $"{r.Site} ({r.Kind})")
            .ToHashSet(StringComparer.Ordinal);

        var derived = derivedSites
            .Select(s => s.StartsWith(CitationRoot, StringComparison.Ordinal) ? s[CitationRoot.Length..] : s)
            .ToHashSet(StringComparer.Ordinal);

        var undeclared = derivedSites
            .Where(s => !declared.Contains(s.StartsWith(CitationRoot, StringComparison.Ordinal) ? s[CitationRoot.Length..] : s))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var phantom = declared
            .Where(d => !derived.Contains(d))
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        return (undeclared, phantom);
    }

    /// <summary>The §8.2 vocabulary and the anti-vacuity rules, per row.</summary>
    private static IReadOnlyList<string> Audit(IReadOnlyList<Row> rows)
    {
        var complaints = new List<string>();

        foreach (var row in rows)
        {
            if (row.Id.StartsWith("MALFORMED", StringComparison.Ordinal))
            {
                complaints.Add($"a §8.3 table row is malformed — {row.Id}, expected 7 columns. A literal pipe inside "
                    + "a cell splits it; write flag unions with '+'");
                continue;
            }

            if (row.Site.Length == 0)
            {
                complaints.Add($"row {row.Id} names no `File.cs:line` site, so nothing can be re-derived for it");
            }

            if (!LegalKinds.Contains(row.Kind, StringComparer.Ordinal))
            {
                complaints.Add($"row {row.Id} has kind '{row.Kind}'; the legal kinds are {string.Join(" / ", LegalKinds)}");
            }

            if (!LegalProvenances.Contains(row.Provenance, StringComparer.Ordinal))
            {
                complaints.Add($"row {row.Id} has provenance '{row.Provenance}'; §8.2 closes that vocabulary at "
                    + $"{string.Join(" / ", LegalProvenances)} and a fourth value is a policy nobody agreed");
            }

            if (row.ExStyles.Length == 0 || row.Input.Length == 0 || row.Capture.Length == 0)
            {
                complaints.Add($"row {row.Id} leaves an ex-style, input-passthrough or capture cell empty; "
                    + "UNEXAMINED is the value for 'the code declares nothing', and a blank is not");
            }

            // Capture stays owner-reserved. Recording UNEXAMINED needs no citation; CLAIMING anything
            // else does, because a capture claim without a site is a boundary moved in prose.
            if (!row.Capture.Contains("UNEXAMINED", StringComparison.Ordinal)
                && !CitationPattern.IsMatch(row.Capture))
            {
                complaints.Add($"row {row.Id} states a capture value that is not UNEXAMINED and cites no "
                    + "`File.cs:line` for it. The capture boundary is owner-reserved (client/port.txt:34-35): "
                    + "a capture claim must point at the code that makes it");
            }
        }

        return complaints;
    }

    private static string Describe(
        SiteScan scan,
        IReadOnlyList<Row> rows,
        IReadOnlyList<string> undeclared,
        IReadOnlyList<string> phantom)
    {
        var report = new StringBuilder();
        report.AppendLine($"{scan.FilesRead} source file(s) swept; {scan.Sites.Count} native-window site(s) derived; "
            + $"{rows.Count} row(s) declared in §8.3");
        foreach (var site in scan.Sites)
        {
            report.AppendLine($"  derived: {site}");
        }

        foreach (var complaint in scan.Unclassified)
        {
            report.AppendLine($"  UNCLASSIFIED: {complaint}");
        }

        foreach (var site in undeclared)
        {
            report.AppendLine($"  UNDECLARED (a native window with no §8.3 policy row): {site}");
        }

        foreach (var site in phantom)
        {
            report.AppendLine($"  PHANTOM (a §8.3 row whose cited line holds no such call): {site}");
        }

        return report.ToString();
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
            $"repo root (the directory holding {string.Join('/', RepoAnchorParts)}) not found above "
            + $"{AppContext.BaseDirectory} — this guard fails rather than skips");
    }
}
