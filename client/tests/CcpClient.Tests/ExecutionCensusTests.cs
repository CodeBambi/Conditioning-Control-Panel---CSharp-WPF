using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-121: the guards on the zero-execution census (<c>client/docs/task-board.md:32</c>).
///
/// <para>WHAT THIS DOES NOT DO. It sets NO threshold, NO target and NO gate on the census count.
/// The board row asks which shipped types have ZERO executed lines, not a percentage, and a
/// tolerance sized to a number just observed is exactly the size of the defect it will next hide.
/// Nothing here reds because the number moved; the number is free to move in either direction.</para>
///
/// <para>WHAT IT DOES BIND, AND WHY THAT IS THE RISK. The census's exclusion rule is BIGGER than
/// its answer — on the run that produced the committed document, 2619 of 3943 report entries were
/// excluded to leave 649 shipped types. So the census is only as honest as the rule, and the
/// tempting failure is to widen an exclusion until the list looks short, which is
/// <c>allowedSkips</c>-as-quarantine wearing a new hat. The ANTI-WIDENING GUARD is
/// <see cref="ShippedTypeRule_ClassifiesEveryFixtureAsDeclared"/>: it applies the rule's own
/// patterns, in .NET <see cref="Regex"/>, to a fixture table holding both compiler-generated shapes
/// and the three AUTHORED generic types. The obvious wider rule — "exclude any name containing
/// '&lt;'" — is three characters shorter and reds here, because it eats
/// <c>PersistenceStore&lt;TModel&gt;</c>, <c>OrphanSafePlayerFactory&lt;TPlayer&gt;</c> and
/// <c>PacedSessionEffect&lt;TFiring&gt;</c>.</para>
///
/// <para>WHY THE RULE IS READ FROM JSON AND THE GENERATOR IS READ AS TEXT. The patterns live in
/// <c>shipped-type-rule.json</c> so that this test and <c>census.mjs</c> apply the SAME rule in two
/// languages. That alone is defeated by one edit: inline a pattern in the generator and the JSON
/// guard stays green while the census widens. So
/// <see cref="CensusGenerator_HoldsNoShapeLiteralOfItsOwn"/> reads <c>census.mjs</c> as text and
/// refuses any shape literal in it. Precedent for reading a tool's source: FloorWrapperGuardTests,
/// AllowedSkipsBanGuardTests.</para>
///
/// <para>HONESTY. These are pure-logic facts over committed files. They never run coverage, never
/// start a test host, and prove nothing about whether the census's number is correct — only that
/// the rule is the one written down, that the generator holds no second rule, and that the
/// committed document is internally consistent, deterministically ordered and free of machine
/// identity. Whether a named type is really dead is a question for a reader, not for this file.</para>
/// </summary>
public sealed class ExecutionCensusTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] RuleParts = ["client", "tools", "coverage", "shipped-type-rule.json"];
    private static readonly string[] GeneratorParts = ["client", "tools", "coverage", "census.mjs"];
    private static readonly string[] CensusParts = ["client", "docs", "execution-census.md"];

    // ------------------------------------------------------------------ the rule

    /// <summary>
    /// THE ANTI-WIDENING GUARD. Every fixture in the rule file, classified by this independent
    /// .NET implementation of the rule's own clauses, must land exactly where the rule says.
    /// </summary>
    [Fact]
    public void ShippedTypeRule_ClassifiesEveryFixtureAsDeclared()
    {
        var rule = ReadRule();
        var fixtures = rule.RootElement.GetProperty("fixtures").EnumerateArray().ToList();
        Assert.True(fixtures.Count >= 15,
            $"the rule declares only {fixtures.Count} fixtures — too few to bind a rule that removes " +
            "more entries than the census keeps");

        var violations = new List<string>();
        foreach (var fixture in fixtures)
        {
            var name = fixture.GetProperty("name").GetString()!;
            var package = fixture.GetProperty("package").GetString()!;
            var expected = fixture.GetProperty("expect").GetString()!;
            var actual = Classify(rule, package, name);
            if (actual != expected)
            {
                violations.Add($"  {name}\n    declared {expected}, rule produces {actual}" +
                    $"\n    ({fixture.GetProperty("why").GetString()})");
            }
        }

        Assert.True(violations.Count == 0,
            "shipped-type rule violations — the rule no longer classifies its own fixtures:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The widening this packet was warned about, executed rather than described. "Exclude any name
    /// containing '&lt;'" is the tidier rule that removes three more entries; this fact pins that it
    /// would eat three types somebody wrote, so the shorter list is a hidden answer and not a
    /// cleaner one.
    /// </summary>
    [Fact]
    public void TheTemptingWiderRule_WouldEatThreeAuthoredGenericTypes()
    {
        var rule = ReadRule();
        string[] authoredGenerics =
        [
            "CcpClient.Desktop.Persistence.PersistenceStore<TModel>",
            "CcpClient.Desktop.Audio.OrphanSafePlayerFactory<TPlayer>",
            "CcpClient.Desktop.Session.PacedSessionEffect<TFiring>",
        ];

        foreach (var name in authoredGenerics)
        {
            Assert.Equal("kept", Classify(rule, "CcpClient.Desktop", name));
            Assert.Contains('<', name); // the wider rule's predicate, stated so the trap is visible
        }

        // And the rule must still catch the generated shapes that share the character.
        Assert.Equal("excluded-C2", Classify(rule, "CcpClient.Desktop",
            "CcpClient.Desktop.Persistence.PersistenceStore.<FlushAsync>d__47<TModel>"));
    }

    /// <summary>
    /// C3's boundary. <c>XamlClosure_2</c> is Avalonia's; <c>XamlClosure_Registry</c> would be
    /// somebody's. An unanchored pattern cannot tell them apart, so the anchors are the clause.
    /// </summary>
    [Fact]
    public void XamlClosureClause_IsAnchoredAtBothEnds()
    {
        var rule = ReadRule();
        Assert.Equal("excluded-C3", Classify(rule, "CcpClient.Desktop", "CcpClient.Desktop.Views.MainWindow.XamlClosure_2"));
        Assert.Equal("kept", Classify(rule, "CcpClient.Desktop", "CcpClient.Desktop.Views.XamlClosure_Registry"));
        Assert.Equal("kept", Classify(rule, "CcpClient.Desktop", "CcpClient.Desktop.Views.MainWindow.XamlClosure_2Extras"));
    }

    /// <summary>
    /// The valve must be EXECUTED code, not a branch nothing reaches. Anything compiled but never
    /// executed is unexecuted, and that rule applies to the census's own tooling first.
    /// </summary>
    [Fact]
    public void UnclassifiedValve_KeepsAndFlags_RatherThanDropping()
    {
        var rule = ReadRule();
        Assert.Equal("flagged-V1", Classify(rule, "CcpClient.Desktop", "CompiledAvaloniaXaml.XamlIlContext"));
        Assert.Equal("flagged-V1", Classify(rule, "CcpClient.Desktop", "CompiledAvaloniaXaml.!AvaloniaResources"));

        // "flagged" must never be spelled the same as "excluded": a valve that drops is not a valve.
        var valve = rule.RootElement.GetProperty("valve");
        Assert.Contains("KEEP", valve.GetProperty("action").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The generator may hold no rule of its own. If <c>census.mjs</c> inlined a shape literal, the
    /// JSON guard above would stay green while the census silently widened.
    /// </summary>
    [Fact]
    public void CensusGenerator_HoldsNoShapeLiteralOfItsOwn()
    {
        var generator = File.ReadAllText(Path.Combine([FindRepoRoot(), .. GeneratorParts]));

        // Two kinds of line may name a shape without being a second rule: a comment, and a line
        // that emits document prose (`w(...)`). The census is REQUIRED to explain the shapes it
        // excluded, and that explanation is text, not classification. Everything else is code.
        var code = string.Join('\n', generator.Split('\n')
            .Select(l => l.TrimStart())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("w(", StringComparison.Ordinal)));

        var violations = new List<string>();
        foreach (var forbidden in new[] { "XamlClosure", "DisplayClass", "<>c", "CompiledAvaloniaXaml" })
        {
            if (code.Contains(forbidden, StringComparison.Ordinal))
            {
                violations.Add($"  census.mjs contains the shape literal \"{forbidden}\" in code — every shape " +
                    "belongs in shipped-type-rule.json, or the cross-language guard goes blind");
            }
        }

        // The one shape the generator is allowed to know is the compiler's own '<' prefix, and only
        // inside declaringType(), where attribution has to find a generated entry's declaring type.
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        Assert.Contains("shipped-type-rule.json", generator, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the document

    /// <summary>
    /// The committed census must add up: the universe is exactly what the clauses removed plus what
    /// they kept, and the kept types are exactly the executed plus the zero-execution ones. This is
    /// arithmetic, never a tolerance.
    /// </summary>
    [Fact]
    public void Census_IsInternallyConsistent()
    {
        var census = ReadCensus();
        var zero = ScalarRow(census, "shipped types with ZERO executed lines");
        var executed = ScalarRow(census, "shipped types with at least one executed line");
        var universe = ScalarRow(census, "census universe (shipped types reaching this census)");

        Assert.Equal(universe, zero + executed);

        var nested = ScalarRow(census, "of the zero-execution types, nested");
        var topLevel = ScalarRow(census, "of the zero-execution types, top-level");
        Assert.Equal(zero, nested + topLevel);

        var listed = Regex.Matches(census, @"^## The (\d+) shipped types with zero executed lines$", RegexOptions.Multiline);
        Assert.True(listed.Count == 1, "the census must carry exactly one zero-execution list heading");
        Assert.Equal(zero, int.Parse(listed[0].Groups[1].Value));
    }

    /// <summary>
    /// Every clause declared in the rule must appear in the census's own clause table with a removed
    /// count, so a clause cannot be widened in code without its written defence moving with it.
    /// </summary>
    [Fact]
    public void Census_AndRule_DeclareTheSameClauses()
    {
        var rule = ReadRule();
        var census = ReadCensus();

        // Not nested: an empty clause list would otherwise make every assertion below vacuous, and
        // a rule with no clauses is the widest rule there is.
        var clauses = rule.RootElement.GetProperty("clauses").EnumerateArray().ToList();
        var rejected = rule.RootElement.GetProperty("rejectedClauses").EnumerateArray().ToList();
        Assert.Equal(3, clauses.Count);
        Assert.Equal(2, rejected.Count);

        foreach (var clause in clauses)
        {
            var id = clause.GetProperty("id").GetString()!;
            Assert.True(Regex.IsMatch(census, $@"^\| \*\*{Regex.Escape(id)}\*\* \|.*\| \d+ \|", RegexOptions.Multiline),
                $"census carries no row with a removed count for clause {id}");
        }

        foreach (var refusal in rejected)
        {
            Assert.Contains(refusal.GetProperty("wouldHaveBeen").GetString()!, census, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The census must diff cleanly next wave: ordinal sort within each namespace section, no
    /// duplicate rows, and nothing that changes between two identical runs on two machines.
    /// </summary>
    [Fact]
    public void Census_IsDeterministicAndCarriesNoMachineIdentity()
    {
        var census = ReadCensus();

        foreach (var pattern in new[] { @"[A-Za-z]:\\", @"\d{4}-\d{2}-\d{2}", @"\bMicha\b", @"\bDuration\b" })
        {
            Assert.False(Regex.IsMatch(census, pattern),
                $"census contains machine identity or a timestamp matching /{pattern}/ — it would diff dirty every wave");
        }

        var rows = Regex.Matches(census, @"^\| `([^`]+)`[^|]*\| \d+ \| ([^|]+)\|", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();
        Assert.True(rows.Count > 0, "the census lists no type rows at all");
        Assert.Equal(rows.Count, rows.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The census must keep saying what it cannot see. Each limit below was measured, and a census
    /// that dropped one would read as complete when a whole category never entered it.
    /// </summary>
    [Fact]
    public void Census_StatesItsOwnBlindSpots()
    {
        var census = ReadCensus();

        foreach (var limit in new[]
        {
            "INVISIBLE rather than zero",          // types with no source-mapped IL never enter the universe
            "Executed is not tested",              // an incidental constructor counts as executed
            "dead HALF of a live partial class",   // type-level census, method-level question
            "OS-CONDITIONED",                      // the Linux legs never run on a Windows generation
        })
        {
            Assert.Contains(limit, census, StringComparison.Ordinal);
        }

        // No threshold may ever appear. The row asks ZERO, not a target.
        Assert.Contains("no threshold", census, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ the rule, applied

    /// <summary>
    /// An independent .NET implementation of the rule declared in <c>shipped-type-rule.json</c>.
    /// Deliberately NOT shared with <c>census.mjs</c>: two implementations of one written rule is
    /// the mechanism, and factoring them together would restore the single point of edit this guard
    /// exists to remove.
    /// </summary>
    private static string Classify(JsonDocument rule, string package, string name)
    {
        var segments = name.Split('.');
        foreach (var clause in rule.RootElement.GetProperty("clauses").EnumerateArray())
        {
            var id = clause.GetProperty("id").GetString()!;
            switch (clause.GetProperty("kind").GetString())
            {
                case "keepOnlyPackage":
                    if (package != clause.GetProperty("package").GetString()) return $"excluded-{id}";
                    break;
                case "anySegmentMatches":
                    if (segments.Any(s => Regex.IsMatch(s, clause.GetProperty("pattern").GetString()!))) return $"excluded-{id}";
                    break;
                case "finalSegmentMatches":
                    if (Regex.IsMatch(segments[^1], clause.GetProperty("pattern").GetString()!)) return $"excluded-{id}";
                    break;
                default:
                    throw new InvalidOperationException($"clause {id} declares an unknown kind — the guard refuses to skip");
            }
        }

        var root = rule.RootElement.GetProperty("shippedNamespaceRoot").GetString()!;
        return name.StartsWith(root, StringComparison.Ordinal)
            ? "kept"
            : $"flagged-{rule.RootElement.GetProperty("valve").GetProperty("id").GetString()}";
    }

    private static int ScalarRow(string census, string label)
    {
        var match = Regex.Match(census, $@"^\| (?:\*\*)?{Regex.Escape(label)}(?:\*\*)? \| (?:\*\*)?(\d+)(?:\*\*)? \|",
            RegexOptions.Multiline);
        Assert.True(match.Success, $"census carries no row labelled \"{label}\" — the guard refuses to go blind");
        return int.Parse(match.Groups[1].Value);
    }

    private static JsonDocument ReadRule() => JsonDocument.Parse(File.ReadAllText(Path.Combine([FindRepoRoot(), .. RuleParts])));

    private static string ReadCensus()
    {
        var path = Path.Combine([FindRepoRoot(), .. CensusParts]);
        Assert.True(File.Exists(path), $"the committed census is missing at {path} — regenerate with census.mjs");
        return File.ReadAllText(path);
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
            $"repo root not found walking up from {AppContext.BaseDirectory} " +
            $"(anchor: {string.Join('/', RepoAnchorParts)}) — the execution-census guard refuses to skip");
    }
}
