using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The guards on the zero-execution census (the zero-execution census row on <c>client/docs/task-board.md</c>).
///
/// <para>WHAT THIS DOES NOT DO. It sets NO threshold, NO target and NO gate on the census count.
/// The board row asks which shipped types have ZERO executed lines, not a percentage, and a
/// tolerance sized to a number just observed is exactly the size of the defect it will next hide.
/// Nothing here reds because the number moved; the number is free to move in either direction.</para>
///
/// <para>WHAT IT DOES BIND, AND WHY THAT IS THE RISK. The census's exclusion rule is BIGGER than
/// its answer — on the run that produced the committed document, 2622 of 3946 report entries were
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
/// <see cref="CensusGenerator_HoldsNoShapeLiteralOfItsOwn"/> reads <c>census.mjs</c> as text.
/// Precedent for reading a tool's source: AllowedSkipsBanGuardTests.</para>
///
/// <para>WHAT THAT GUARD ACTUALLY ENFORCES, STATED EXACTLY BECAUSE IT USED TO OVERSTATE ITSELF. It
/// forbids a NAMED list of literals in the generator's non-comment, non-document-emitting lines:
/// the four generated-shape names, and the <c>obj/</c> path fragments that would reinstate the
/// rejected R1 path clause. It does NOT prove the generator applies no other filter. A hand-rolled
/// predicate mentioning none of those literals — <c>if (name.includes("&lt;")) continue;</c> inside
/// <c>accumulate</c>, which is precisely the rejected R2 widening — would pass this guard, the JSON
/// fixture guard and <c>--self-check</c> alike, because none of the three observes
/// <c>accumulate</c>; all three observe <c>classify</c>. Review confirmed no such widening exists
/// today. The honest statement of this guard's reach: it closes the literal-reuse route and NAMES
/// the route it leaves open, rather than claiming a completeness it cannot deliver.</para>
///
/// <para>WHY THE CROSS-VALIDATION NO LONGER TOUCHES THE COMMITTED DOCUMENT. Until this
/// packet, <c>census.mjs</c>'s hand-rolled ECMA-335 reader was cross-validated by recomputing two
/// of its numbers here by reflection and comparing them to SCALARS STORED IN
/// <c>execution-census.md</c>. The cross-validation was the only check on that reader anywhere in
/// the port; the STORED half was a chokepoint. Adding one ordinary <c>public sealed class</c> to
/// <c>client/src</c> made reflection say 885 while the document said 884, so every future product
/// packet reddened this file, and the only remedy — regenerating the census — is closed to lanes
/// for the same reason <c>floor.json</c> is. Measured, not argued: with a throwaway shipped type
/// present this class reported <c>Failed: 1, Passed: 9</c>, failing at
/// <c>Assert.Equal() ... Expected: 885 / Actual: 884</c>.</para>
///
/// <para>So both halves now run at RUNTIME.
/// <see cref="MetadataReader_AndReflection_SeeTheSameShippedTypes"/> asks <c>census.mjs</c> for its
/// own reading of the ASSEMBLY THIS TEST HAS LOADED (<c>--metadata-json --dll</c>, a mode that runs
/// no coverage and writes no document) and compares it, name by name and kind by kind, against
/// ordinary reflection over the identical file. A new shipped type moves BOTH sides; a wrong reader
/// moves ONE. Nothing about it reads the census, so nothing about it chokepoints.</para>
///
/// <para>WHAT WAS TRADED, SAID PLAINLY. No fact here notices any more that the committed census's
/// SCALARS have stopped describing the tree — that comparison IS the chokepoint. It moved into the
/// tool as <c>census.mjs --check-stale</c>, run at a land by the orchestrator, where regenerating is
/// something the runner may actually do. It is not merely asserted to exist: both of its outcomes
/// are pinned below (<see cref="StaleCheck_RedsWhenTheDocumentsScalarsStopDescribingTheAssembly"/>,
/// <see cref="StaleCheck_IsQuietWhenTheDocumentDescribesTheAssembly"/>) against SYNTHETIC documents
/// in the temp directory, never against the committed one.</para>
///
/// <para>HONESTY. Every fact here but three is pure logic over committed files.
/// Those three shell out to <c>node</c> — a new precedent for <c>client/tests/**</c>, justified
/// because both tier-1 gates and this tool are themselves node scripts, so node is already a hard
/// requirement of this tree. If node is absent they FAIL rather than skip. None of these facts runs
/// coverage or starts a test host, and none proves the census's ANSWER is correct — only that the
/// rule is the one written down, that the generator holds no second rule, that its metadata reader
/// agrees with an independent mechanism, and that the committed document is internally consistent,
/// deterministically ordered and free of machine identity. Whether a named type is really dead is a
/// question for a reader, not for this file.</para>
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
        // The generated-shape names, plus the obj/ path fragments that would reinstate the REJECTED
        // R1 path clause — the one that would have discarded DtrhLoom's [GeneratedRegex] half.
        foreach (var forbidden in new[] { "XamlClosure", "DisplayClass", "<>c", "CompiledAvaloniaXaml", "/obj/", "\\\\obj\\\\" })
        {
            if (code.Contains(forbidden, StringComparison.Ordinal))
            {
                violations.Add($"  census.mjs contains the literal \"{forbidden}\" in code — every shape belongs " +
                    "in shipped-type-rule.json, and a path-based exclusion is the rejected R1 clause returning");
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

        // `\r?$` and not `$`: the census is GENERATED with LF and CHECKED OUT with CRLF on an
        // autocrlf machine (`git ls-files --eol` reports i/lf w/crlf), so a bare `$` matches in the
        // worktree that wrote the file and fails in every fresh checkout — including this land.
        // The line ending is a property of the checkout, never of the census, so tolerating it
        // removes an environmental dependency rather than weakening the guard.
        var listed = Regex.Matches(census, @"^## The (\d+) shipped types with zero executed lines\r?$", RegexOptions.Multiline);
        Assert.True(listed.Count == 1, "the census must carry exactly one zero-execution list heading");
        Assert.Equal(zero, int.Parse(listed[0].Groups[1].Value));

        // The stated total must equal the rows actually rendered. Without this, a marker turned into
        // a filter inside the render loop would shorten the list while the headline kept its number.
        Assert.Equal(zero, ZeroListRows(census).Count);

        // The two relations the old reflection anchor carried, restated over STORED scalars
        // only. They are arithmetic INSIDE the document, so they do not move when a lane adds a
        // shipped type — which is why they survive here while the live comparison did not. What
        // they still catch: a universe quietly shrunk against its own metadata table cannot hide,
        // because it has to surface as a larger "no source line maps to it".
        var authoredShape = ScalarRow(census, AuthoredRowLabel);
        var invisible = ScalarRow(census, "**INVISIBLE rather than zero**");
        var noMethodBody = ScalarRow(census, NoMethodBodyRowLabel);
        var noSourceMapped = ScalarRow(census, "— has a method body, but no source line maps to it");
        Assert.Equal(invisible, authoredShape - universe);
        Assert.Equal(invisible, noMethodBody + noSourceMapped);
    }

    /// <summary>
    /// THE READER IS CROSS-VALIDATED AGAINST REFLECTION, AT RUNTIME, ON BOTH SIDES.
    ///
    /// <para>WHAT STILL FAILS IF THE READER STARTS MISCOUNTING, in one checkable sentence: if
    /// <c>census.mjs</c>'s ECMA-335 walk goes wrong in any way that changes which type definitions
    /// it reports, what kind each one is, or which of the C2/C3-SURVIVING ones carry a method body
    /// — a wrong row width, a wrong heap-index size, a dropped or duplicated TypeDef row, a
    /// <c>methodList</c> range off by one, a mis-decoded <c>extends</c> coded index — then its own
    /// output stops matching what <see cref="Assembly.GetTypes"/>, <see cref="Type.IsInterface"/>
    /// and <see cref="MethodBase.GetMethodBody"/> report for THE IDENTICAL FILE, and this fact
    /// fails naming the exact names that differ.</para>
    ///
    /// <para>THAT CLAUSE IS SCOPED, AND BOTH GAPS ARE NAMED. "Carry a method body" is asserted only
    /// over the C2/C3-surviving subset, because comparison 4 reads <c>noMethodBody</c>, which
    /// <c>census.mjs</c> has ALREADY narrowed to <c>authored.filter(t =&gt; !t.hasIl)</c>. So a walk
    /// defect flipping <c>hasIl</c> on an EXCLUDED TypeDef changes the reader's emitted output and
    /// reds nothing here. Its consequence for the census is nil — <c>hasIl</c> has exactly one
    /// consumer, that filter — and closing it would be a fifth multiset over every row; it is named
    /// rather than closed. The second gap is <c>ns</c>, which drives the census's namespace headings
    /// and its nested/top-level split and is compared by nothing here. Neither gap was covered by
    /// the anchor this replaces either.</para>
    ///
    /// <para>Both sides are live, so a packet adding an ordinary shipped type moves both and this
    /// stays green; a reader defect moves one and it reds. That is the whole difference from the
    /// anchor it replaces, which compared the live side to a number stored in a document.</para>
    ///
    /// <para>MULTISETS, NOT SETS, AND NOT COUNTS. 295 of this assembly's 1325 TypeDef simple names
    /// repeat, because nested types reuse simple names — a set comparison would read 1030 == 1030
    /// and miss a dropped duplicate row, and a count comparison would miss a row dropped and another
    /// invented. NAMES ONLY, never namespace-qualified: metadata's <c>TypeNamespace</c> is empty for
    /// a nested type while reflection reports the enclosing namespace, so qualifying would compare
    /// two different questions. Kind is compared beside the name because it drives the census's
    /// interfaces/enums/structs/classes row, which nothing else observes.</para>
    ///
    /// <para>Deliberately NOT a threshold: nothing here says the assembly must be big.</para>
    /// </summary>
    [Fact]
    public async Task MetadataReader_AndReflection_SeeTheSameShippedTypes()
    {
        var rule = ReadRule();
        var assembly = typeof(CcpClient.Desktop.Haptics.HapticGate).Assembly;
        var reading = await ReadAssemblyMetadataAsync(assembly);
        var types = assembly.GetTypes();
        Assert.NotEmpty(types);

        // 1. Every type definition. The module pseudo-type is the one TypeDef row reflection never
        // returns, so it is the one name removed — and it is removed by NAME, so a reader that
        // stopped emitting it would surface here rather than be silently forgiven.
        AssertSameMultiset("every type definition",
            reading.Rows.Select(r => r.Name).Where(n => n != ModulePseudoType),
            types.Select(t => t.Name));

        // 2. ...and each one's kind, which the census reports and nothing else checks.
        AssertSameMultiset("every type definition, with its kind",
            reading.Rows.Where(r => r.Name != ModulePseudoType).Select(r => $"{r.Name} [{r.Kind}]"),
            types.Select(t => $"{t.Name} [{KindOf(t)}]"));

        // 3. The C2/C3-surviving subset — the census's "authored name shape" row. The same clauses,
        // applied to simple names exactly as census.mjs applies them to the metadata: a simple name
        // carries no namespace, so "not excluded" is the test, never "kept" (a bare name never
        // starts with the shipped namespace root and would read as flagged).
        AssertSameMultiset("the C2/C3-surviving subset",
            reading.Authored,
            types.Where(t => !Classify(rule, "CcpClient.Desktop", t.Name).StartsWith("excluded-", StringComparison.Ordinal))
                .Select(t => t.Name));

        // 4. "No method body at all": every declared method and constructor is abstract or extern,
        // so nothing can be instrumented and the type is INVISIBLE rather than zero. This is the
        // subset that exercises the reader's MethodDef walk rather than its TypeDef walk.
        AssertSameMultiset("the no-method-body subset",
            reading.NoMethodBody,
            types.Where(t => !Classify(rule, "CcpClient.Desktop", t.Name).StartsWith("excluded-", StringComparison.Ordinal))
                .Where(HasNoMethodBody)
                .Select(t => t.Name));
    }

    /// <summary>
    /// The two implementations of the written rule, applied to the REAL assembly's whole name
    /// population instead of to the twenty fixtures they otherwise only ever meet on.
    ///
    /// <para><see cref="ShippedTypeRule_ClassifiesEveryFixtureAsDeclared"/> proves the .NET side
    /// reproduces the rule on names somebody chose; this proves the two agree on the names the
    /// compiler actually emitted, which is the population the census is computed over.</para>
    /// </summary>
    [Fact]
    public async Task TheRuleClassifiesTheRealAssembly_IdenticallyInBothImplementations()
    {
        var rule = ReadRule();
        var assembly = typeof(CcpClient.Desktop.Haptics.HapticGate).Assembly;
        var reading = await ReadAssemblyMetadataAsync(assembly);

        // Not vacuous on an empty reading: the row count is pinned to reflection's, +1 for the
        // module pseudo-type. An empty or truncated JSON would fail HERE rather than pass a loop
        // that never ran (VacuousShapeDetector.cs:21-24 names that exact false negative).
        Assert.NotEmpty(reading.Rows);
        Assert.Equal(assembly.GetTypes().Length + 1, reading.Rows.Count);

        var disagreements = reading.Rows
            .Select(r => (r.Name, Mine: Classify(rule, "CcpClient.Desktop", r.Name), Theirs: r.Verdict))
            .Where(x => x.Mine != x.Theirs)
            .Select(x => $"  {x.Name}\n    census.mjs says {x.Theirs}, this file's implementation says {x.Mine}")
            .ToList();

        Assert.True(disagreements.Count == 0,
            $"the rule is applied differently in the two languages on {disagreements.Count} of " +
            $"{reading.Rows.Count} real type names:" + Environment.NewLine +
            string.Join(Environment.NewLine, disagreements.Take(20)));
    }

    /// <summary>
    /// THE DRIFT CHECK BITES. Bump one scalar by one in a synthetic census and
    /// <c>--check-stale</c> must exit non-zero and NAME the row it disbelieves.
    ///
    /// <para>Synthetic, in the temp directory: the committed census is never read or written here,
    /// which is exactly what stops this from being the chokepoint it replaces.</para>
    /// </summary>
    [Fact]
    public async Task StaleCheck_RedsWhenTheDocumentsScalarsStopDescribingTheAssembly()
    {
        var assembly = typeof(CcpClient.Desktop.Haptics.HapticGate).Assembly;
        var reading = await ReadAssemblyMetadataAsync(assembly);
        var drifted = TempCensusPath();
        try
        {
            await File.WriteAllTextAsync(drifted, SyntheticCensus(reading.Scalars, AuthoredRowLabel, +1),
                TestContext.Current.CancellationToken);
            var run = await RunCensusToolAsync("--check-stale", "--dll", assembly.Location, "--census", drifted);

            Assert.NotEqual(0, run.ExitCode);
            Assert.Contains("STALE ROW", run.StdErr, StringComparison.Ordinal);
            Assert.Contains(AuthoredRowLabel, run.StdErr, StringComparison.Ordinal);
            Assert.Contains(reading.Scalars[AuthoredRowLabel].ToString(), run.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(drifted);
        }
    }

    /// <summary>
    /// ...and it is quiet when the document does describe the assembly — otherwise the fact above
    /// would pass against a checker that reds unconditionally.
    ///
    /// <para>It also pins that a quiet exit ENUMERATES WHAT IT DID NOT CHECK. The coverage-derived
    /// rows and the embedded suite run table are stale by construction the moment a test moves
    /// (the zero-execution census row on <c>client/docs/task-board.md</c>), so a future orchestrator reading silence as "the
    /// census is current" would be misled in the reassuring direction.</para>
    /// </summary>
    [Fact]
    public async Task StaleCheck_IsQuietWhenTheDocumentDescribesTheAssembly()
    {
        var assembly = typeof(CcpClient.Desktop.Haptics.HapticGate).Assembly;
        var reading = await ReadAssemblyMetadataAsync(assembly);
        var current = TempCensusPath();
        try
        {
            await File.WriteAllTextAsync(current, SyntheticCensus(reading.Scalars, AuthoredRowLabel, 0),
                TestContext.Current.CancellationToken);
            var run = await RunCensusToolAsync("--check-stale", "--dll", assembly.Location, "--census", current);

            Assert.Equal(0, run.ExitCode);
            Assert.DoesNotContain("STALE ROW", run.StdErr, StringComparison.Ordinal);
            Assert.Contains("NOT CHECKED", run.StdOut, StringComparison.Ordinal);
            Assert.Contains("run table", run.StdOut, StringComparison.Ordinal);

            // ...and it names when the BINARY it read was written. The checker's own input can be
            // stale: a leftover Debug build makes every verdict wrong, in the loud direction, over a
            // document that describes the source tree perfectly. Pinned rather than trusted, because
            // a diagnostic nobody asserts is a diagnostic that quietly stops being printed.
            Assert.Contains("last written", run.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(current);
        }
    }

    /// <summary>The type rows of the zero-execution list only — never the tables above it.</summary>
    private static List<string> ZeroListRows(string census)
    {
        // `\r?$` for the same reason as the sibling anchor above: LF in the index, CRLF in the
        // worktree on this machine.
        var heading = Regex.Match(census, @"^## The \d+ shipped types with zero executed lines\r?$", RegexOptions.Multiline);
        Assert.True(heading.Success, "the census carries no zero-execution list heading");
        return Regex.Matches(census[heading.Index..], @"^\| `([^`]+)`[^|]*\| \d+ \| ", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();
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

    // ------------------------------------------------------------------ the reader, run live

    /// <summary>The one TypeDef row <see cref="Assembly.GetTypes"/> never returns.</summary>
    private const string ModulePseudoType = "<Module>";

    /// <summary>The two census row labels this file names in more than one place. Spelled once so a
    /// renamed row cannot leave one guard reading a row that no longer exists while another passes;
    /// <c>census.mjs</c> holds the same two strings as constants for the same reason.</summary>
    private const string AuthoredRowLabel = "of those, authored name shape (would survive C2/C3)";

    private const string NoMethodBodyRowLabel =
        "— no method body at all: interfaces without default members, enums, abstract-only";

    private sealed record MetadataRow(string Name, string Kind, bool HasIl, string Verdict);

    private sealed record AssemblyReading(
        IReadOnlyList<MetadataRow> Rows,
        IReadOnlyList<string> Authored,
        IReadOnlyList<string> NoMethodBody,
        IReadOnlyDictionary<string, int> Scalars);

    private sealed record ToolRun(int ExitCode, string StdOut, string StdErr);

    /// <summary>
    /// The census generator's OWN reading of an assembly, in the mode that runs no coverage and
    /// writes no document. The assembly handed in is the one this test process has LOADED, so both
    /// mechanisms read the identical bytes rather than two builds that merely ought to match.
    /// </summary>
    private static async Task<AssemblyReading> ReadAssemblyMetadataAsync(Assembly assembly)
    {
        Assert.False(string.IsNullOrEmpty(assembly.Location),
            "the shipped assembly reports no file location — the cross-validation has nothing to read");

        var run = await RunCensusToolAsync("--metadata-json", "--dll", assembly.Location);
        Assert.True(run.ExitCode == 0,
            $"census.mjs --metadata-json exited {run.ExitCode}: {run.StdErr}");

        using var json = JsonDocument.Parse(run.StdOut);
        var root = json.RootElement;
        var rows = root.GetProperty("rows").EnumerateArray()
            .Select(r => new MetadataRow(
                r.GetProperty("name").GetString()!,
                r.GetProperty("kind").GetString()!,
                r.GetProperty("hasIl").GetBoolean(),
                r.GetProperty("verdict").GetString()!))
            .ToList();

        return new AssemblyReading(
            rows,
            [.. root.GetProperty("authored").EnumerateArray().Select(e => e.GetString()!)],
            [.. root.GetProperty("noMethodBody").EnumerateArray().Select(e => e.GetString()!)],
            root.GetProperty("scalars").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32()));
    }

    /// <summary>
    /// Runs <c>census.mjs</c> and returns its exit code and streams. Both streams are read
    /// concurrently with the wait: the metadata JSON is far larger than a pipe buffer, and reading
    /// after the exit would deadlock. The wait itself goes through the approved helper.
    /// </summary>
    private static async Task<ToolRun> RunCensusToolAsync(params string[] args)
    {
        var generator = Path.Combine([FindRepoRoot(), .. GeneratorParts]);
        Assert.True(File.Exists(generator), $"census.mjs is missing at {generator}");

        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = FindRepoRoot(),
        };
        start.ArgumentList.Add(generator);
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Deliberately a FAILURE and not a skip: both tier-1 gates are node scripts, so a tree
            // that cannot run node cannot run its own gates, and allowedSkips is not a quarantine.
            throw new InvalidOperationException(
                "could not start `node` to cross-validate the census reader — node is a hard " +
                "requirement of this tree (check-warnings.mjs, check-floor.mjs, census.mjs) and " +
                "this guard refuses to skip", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await TestWait.Until(process.WaitForExitAsync(), $"census.mjs {args[0]} to exit");
            }
            catch
            {
                // A wedged node must not OUTLIVE the fact that started it. The window expiring is
                // already a failure and stays one — this only stops the failure from leaving a
                // process behind to hold the assembly open and confuse the next run.
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It exited between the window expiring and the kill; nothing to clean up.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // The OS refused the kill. Reporting THAT instead of the timeout would bury the
                    // real failure, so the original exception wins.
                }

                throw;
            }

            return new ToolRun(process.ExitCode, await stdout, await stderr);
        }
    }

    /// <summary>
    /// Fails naming the exact names that differ, with their multiplicities. A count-only message
    /// would leave the next reader to rediscover which row the reader lost.
    /// </summary>
    private static void AssertSameMultiset(string subject, IEnumerable<string> fromReader, IEnumerable<string> fromReflection)
    {
        var reader = fromReader.GroupBy(n => n, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var reflection = fromReflection.GroupBy(n => n, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var differences = reader.Keys.Union(reflection.Keys, StringComparer.Ordinal)
            .Where(name => reader.GetValueOrDefault(name) != reflection.GetValueOrDefault(name))
            .Order(StringComparer.Ordinal)
            .Select(name => $"  {name}: census.mjs {reader.GetValueOrDefault(name)}, reflection {reflection.GetValueOrDefault(name)}")
            .ToList();

        Assert.True(differences.Count == 0,
            $"census.mjs's ECMA-335 reader and .NET reflection disagree about {subject} on " +
            $"{differences.Count} name(s) of the SAME file — one of the two mechanisms is wrong:" +
            Environment.NewLine + string.Join(Environment.NewLine, differences.Take(25)));
    }

    /// <summary>Reflection's answer to the question census.mjs answers from the TypeDef's
    /// <c>Flags</c> and <c>Extends</c> columns.</summary>
    private static string KindOf(Type type) =>
        type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : "class";

    /// <summary>Reflection's answer to census.mjs's <c>hasIl</c>: no declared method or constructor
    /// carries a body, so no instruction of this type can ever be instrumented.</summary>
    private static bool HasNoMethodBody(Type type)
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return type.GetMethods(Declared).All(m => m.GetMethodBody() is null)
            && type.GetConstructors(Declared).All(c => c.GetMethodBody() is null);
    }

    private static string TempCensusPath() =>
        Path.Combine(Path.GetTempPath(), $"ccp-sp124-census-{Guid.NewGuid():N}.md");

    /// <summary>
    /// The smallest document <c>--check-stale</c> can read: the three metadata rows, with
    /// <paramref name="drift"/> added to <paramref name="driftRow"/>. Built from the reading the
    /// test just took, so the "current" case cannot be made to pass by a stale literal.
    /// </summary>
    private static string SyntheticCensus(IReadOnlyDictionary<string, int> scalars, string driftRow, int drift)
    {
        string[] lines = ["| | |", "|---|---|",
            .. scalars.Select(s => $"| {s.Key} | {s.Value + (s.Key == driftRow ? drift : 0)} |"), string.Empty];
        return string.Join(Environment.NewLine, lines);
    }

    // ------------------------------------------------------------------ the document, read

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
