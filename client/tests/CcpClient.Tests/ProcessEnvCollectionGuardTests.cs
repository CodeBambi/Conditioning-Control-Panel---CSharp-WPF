using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-086: the <see cref="ProcessEnvCollection"/> membership convention, made mechanical.
///
/// <para>WHAT THIS BINDS. SP-062 established the only isolation this suite has against a
/// process-wide <c>CCP_DATA_ROOT</c> mutation: CO-LOCATION. The one in-suite mutator of the
/// variable lives in <see cref="DataRootOverrideEnvTests"/>, and every class that READS the
/// variable joins the same collection so xunit's intra-collection sequentiality serializes
/// them (the collection's own doc comment, DataRootOverrideTests.cs:116-120, is explicit that
/// <c>DisableParallelization</c> is a non-relied-upon hint on this runner and that
/// intra-collection sequentiality is the actual mechanism). SP-068's scheduling reshuffle then
/// exposed the pre-existing hole. Until this guard the convention was TEXT: the next class to
/// build a real <c>CompositionRoot</c> silently rejoined the racy default collection, and the
/// symptom arrived as an unrelated packet's scheduling change reddening a test it never
/// touched.</para>
///
/// <para>WHY A SOURCE WALK AND NOT REFLECTION. <c>[Collection]</c> is visible to reflection but
/// "this class constructs a CompositionRoot" is a property of a METHOD BODY, which reflection
/// cannot see without decoding IL — a reflection guard could only re-implement the hard-coded
/// single-class assertion that already exists at DataRootChokePointGuardTests.cs:64-70, which
/// is precisely what is not enough. And <c>CcpClient.HeadlessTests</c> is a separate assembly
/// that <c>CcpClient.Tests</c> does not reference, so its types are not loadable here. Only a
/// lexical repo-root walk sees both trees. Shape precedent: DataRootChokePointGuardTests /
/// HarnessEntryPointGuardTests / FloorWrapperGuardTests — repo-root walk, never skips, fails
/// closed on input it cannot parse, file:line violations.</para>
///
/// <para>THE ASSEMBLY BOUNDARY, AND WHY THE TWO HALVES DIFFER. xunit collections do not span
/// assemblies and <c>ProcessEnvCollection</c> is defined in <c>CcpClient.Tests</c>
/// (DataRootOverrideTests.cs:121-122), so a class in <c>CcpClient.HeadlessTests</c> CANNOT join
/// it. Membership is therefore not an available remedy over there, and the only rule that is
/// meaningful for that project is the stronger one: no mutation of the variable at all, because
/// there is no collection to serialize a mutation against.</para>
///
/// <para>HONESTY. This guard is LEXICAL. A class that reaches <c>DefaultSettingsPath()</c>
/// transitively through a call it does not name is invisible to it, and it binds only the
/// tokens enumerated below — a new door into the variable is invisible until someone adds its
/// name here. Membership is proven by an ATTRIBUTE, never by an executed demonstration that two
/// classes actually serialize; that executed proof is SP-062's probe, not this file.</para>
/// </summary>
public partial class ProcessEnvCollectionGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] TestsParts = ["client", "tests"];

    /// <summary>The project whose classes may join the collection (it defines it).</summary>
    private const string UnitProject = "CcpClient.Tests";

    /// <summary>The project that cannot join it, and therefore may not mutate at all.</summary>
    private const string HeadlessProject = "CcpClient.HeadlessTests";

    private const string CollectionName = "ProcessEnvCollection";

    private const string RedirectSeam = "SettingsPathFactory";

    /// <summary>
    /// Direct entry points INTO the data root. Naming one of these is reading the process
    /// environment: CompositionRoot.cs:108 and :127 are the two reads, and every other name
    /// here routes through DefaultSettingsPath() (DtrhProfileLock.cs:32-33/:37,
    /// ChaosTunnelService.cs:54-55). Held as class-level literals on purpose — the local
    /// sanitizer blanks string literals, so this guard never reports itself, and no
    /// env-predicate shape lands in a [Fact] body (SP-066 detector surface).
    /// </summary>
    private static readonly string[] DataRootEntryPoints =
    [
        "CompositionRoot.DefaultSettingsPath",
        "CompositionRoot.ActiveDataRootOverride",
        "DtrhProfileLock.DtrhDataRoot",
        "DtrhProfileLock.WebView2ProfileDir",
        "ChaosTunnelService.DataRoot",
    ];

    private const string EnvReader = "Environment.GetEnvironmentVariable";

    private const string EnvMutator = "Environment.SetEnvironmentVariable";

    private static readonly string[] EnvAccessors = [EnvReader, EnvMutator];

    /// <summary>
    /// The two ways a call site can name the variable: the product constant, or the raw value.
    /// Matched against the RAW argument text of an environment accessor, never against the
    /// whole file — a bare mention of the constant outside such an argument list touches
    /// nothing (CompositionRoot.cs:59 is a compile-time <c>const string</c>), and the raw
    /// spelling has to be read before sanitization or the form a headless author is most
    /// likely to reach for would be invisible to the no-mutator half.
    /// </summary>
    private static readonly string[] VariableSpellings = ["DataRootOverrideVariable", "CCP_DATA_ROOT"];

    /// <summary>Named positive control for the CONSTRUCTION half: five real
    /// <c>CompositionRoot</c> constructions, zero direct tokens (the class SP-068 fixed).</summary>
    private const string ConstructionControl = "CompositionRootValidationTests";

    /// <summary>Named positive control for the TOKEN half: the only class in the tree bound by
    /// a token and NOT by a construction (DataRootOverrideTests.cs:79/:91/:93; the file's one
    /// construction, :165, belongs to DataRootOverrideEnvTests). Delete the token list from the
    /// rule and this assertion is what reds.</summary>
    private const string TokenControl = "DataRootOverrideTests";

    /// <summary>Named NEGATIVE control for the sanitizer: HarnessEntryPointGuardTests.cs:74
    /// holds the string literal "new CompositionRoot". Blanking is load-bearing, not
    /// decorative — without it this class is reported and there is no honest fix for that
    /// report.</summary>
    private const string SanitizerControl = "HarnessEntryPointGuardTests";

    /// <summary>Named NEGATIVE control for this guard itself: it must stay OUT of the
    /// serialized collection (membership lengthens the serialized critical section), so it may
    /// never become bound by its own rule.</summary>
    private const string SelfControl = nameof(ProcessEnvCollectionGuardTests);

    private const string UnparseableRefusal =
        "this guard refuses to read input it cannot parse as a pass (precedent: FloorWrapperGuardTests.cs:92-101)";

    [GeneratedRegex(@"\bnew\s+CompositionRoot\b")]
    private static partial Regex RootConstruction();

    [GeneratedRegex(@"\b(?:class|struct|record)\s+(?:(?:class|struct)\s+)?(\w+)")]
    private static partial Regex TypeDecl();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])Collection(?:Attribute)?(?![A-Za-z0-9_])\s*[(<]")]
    private static partial Regex CollectionAttributeHead();

    [GeneratedRegex(@"^(?:nameof\s*\(\s*([\w.]+)\s*\)|typeof\s*\(\s*([\w.]+)\s*\)|""([\w.]+)""|([\w.]+))$")]
    private static partial Regex CollectionArgument();

    private sealed record SourceFile(string Relative, string Raw, string Sanitized, int[] LineStarts);

    private sealed record TypeSpan(string Name, int DeclIndex, int BodyStart, int BodyEnd);

    private sealed record Evidence(int Offset, string Reason);

    private sealed record EnvCallSite(int Offset, int ArgumentStart, int ArgumentEnd);

    [Fact]
    public void EveryDataRootReadingClass_CarriesTheProcessEnvCollectionAttribute()
    {
        var testsRoot = TestsRoot();
        var strays = SourcesOutsideTheKnownProjects(testsRoot);
        var violations = new List<string>();

        // Attribute membership aggregates over ALL declarations of a class name: partials are
        // real in this suite (HarnessEntryPointGuardTests.cs:18, FloorWrapperGuardTests.cs:32)
        // and the attribute may sit on any one of them.
        var carries = new Dictionary<string, bool>(StringComparer.Ordinal);
        var anchor = new Dictionary<string, string>(StringComparer.Ordinal);
        var bound = new SortedSet<string>(StringComparer.Ordinal);
        var reasons = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var path in EnumerateSources(Path.Combine(testsRoot, UnitProject)))
        {
            var file = Load(path);
            var spans = TypeSpans(file, violations);

            foreach (var span in spans)
            {
                var region = AttributeRegion(file, span);
                var carried = CarriesTheCollectionAttribute(region.Raw, region.Sanitized, out var problem);
                if (problem is not null)
                {
                    violations.Add($"{file.Relative}:{LineOf(file, span.DeclIndex)}: {span.Name}: {problem}");
                }

                carries[span.Name] = carries.GetValueOrDefault(span.Name) || carried;
                if (!anchor.ContainsKey(span.Name))
                {
                    anchor[span.Name] = $"{file.Relative}:{LineOf(file, span.DeclIndex)}";
                }
            }

            foreach (var evidence in DataRootEvidence(file, violations))
            {
                // Attributed to the OUTERMOST enclosing type: a token inside a nested private
                // helper still belongs to the outer test class, which is the only one that can
                // carry [Collection].
                var owner = Outermost(spans, evidence.Offset);
                var site = $"{file.Relative}:{LineOf(file, evidence.Offset)} ({evidence.Reason})";
                if (owner is null)
                {
                    violations.Add($"{site}: no enclosing type owns this data-root read, so no class can carry " +
                        $"[Collection(nameof({CollectionName}))] for it — {UnparseableRefusal}");
                    continue;
                }

                bound.Add(owner.Name);
                if (!reasons.TryGetValue(owner.Name, out var sites))
                {
                    sites = [];
                    reasons[owner.Name] = sites;
                }

                sites.Add(site);
            }
        }

        foreach (var name in bound)
        {
            if (carries.GetValueOrDefault(name))
            {
                continue;
            }

            violations.Add($"{anchor[name]}: {name} reads the data-root environment variable " +
                $"[{string.Join("; ", reasons[name])}] but does not carry [Collection(nameof({CollectionName}))]. " +
                "CCP_DATA_ROOT is PROCESS-WIDE and DataRootOverrideEnvTests mutates it in-suite; collection " +
                "membership IS the isolation (intra-collection sequentiality serializes this class against that " +
                "mutator — SP-062, re-applied in-lane by SP-068). Two correct fixes, in this order: give the " +
                "construction an explicit SettingsPathFactory pointing at a per-test temp path so it never reads " +
                "the variable at all (keeps the suite parallel), or — when the fact's subject IS the default path " +
                "or the override itself — add the attribute and accept the serialization. A suppression list is " +
                "not a fix: it is the convention as text again, which is the defect SP-086 closes.");
        }

        Assert.True(strays.Count == 0,
            $"source file(s) under client/tests outside the two project roots this guard knows — {UnitProject} " +
            $"(bound by the [Collection(nameof({CollectionName}))] membership rule) and {HeadlessProject} (bound " +
            "by the no-mutator rule, because collections do not span assemblies): " +
            string.Join(", ", strays) + Environment.NewLine +
            "A NEW test project under client/tests is invisible to BOTH halves, so this check fails closed on " +
            "purpose. EXTEND the guard to bind the new project — decide which of the two rules applies from " +
            "which assembly its ProcessEnvCollection would live in. Never delete this check.");
        Assert.NotEmpty(bound); // an empty BOUND set on this suite is a broken detector, not a clean tree
        Assert.True(bound.Contains(ConstructionControl),
            $"{ConstructionControl} must be detected as data-root-reading (it builds five real CompositionRoots, " +
            "none of which assigns SettingsPathFactory, and Validate reaches DefaultSettingsPath through " +
            "DefaultParticipants) — its absence means the CONSTRUCTION half of the rule stopped working");
        Assert.True(bound.Contains(TokenControl),
            $"{TokenControl} must be detected as data-root-reading (it names CompositionRoot.ActiveDataRootOverride " +
            "and CompositionRoot.DefaultSettingsPath and constructs nothing) — it is the only class the TOKEN half " +
            "of the rule binds and the construction half does not, so its absence means the token list stopped working");
        Assert.False(bound.Contains(SanitizerControl),
            $"{SanitizerControl} must NOT be detected: its \"new CompositionRoot\" is a string literal " +
            "(HarnessEntryPointGuardTests.cs:74) and the local sanitizer must blank it — reporting that class is a " +
            "false positive with no honest remedy");
        Assert.False(bound.Contains(SelfControl),
            $"{SelfControl} must NOT be detected: this guard has to stay in the default parallel collection, so it " +
            "may never become bound by its own rule (its detection tokens are string literals for exactly this reason)");
        Assert.True(violations.Count == 0,
            "ProcessEnvCollection membership guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void TheHeadlessAssembly_NeverMutatesTheDataRootVariable()
    {
        var headlessRoot = Path.Combine(TestsRoot(), HeadlessProject);
        var violations = new List<string>();
        var scanned = new List<string>();

        foreach (var path in EnumerateSources(headlessRoot))
        {
            scanned.Add(path);
            var file = Load(path);
            var spans = TypeSpans(file, violations);
            foreach (var site in EnvCallSites(file, EnvMutator, violations))
            {
                if (!NamesTheVariable(file.Raw[site.ArgumentStart..site.ArgumentEnd]))
                {
                    continue;
                }

                var owner = Outermost(spans, site.Offset);
                violations.Add($"{file.Relative}:{LineOf(file, site.Offset)}: {owner?.Name ?? "(no enclosing type)"} " +
                    $"mutates the data-root environment variable inside {HeadlessProject}. xunit collections do not " +
                    $"span assemblies and {CollectionName} is defined in {UnitProject} " +
                    "(DataRootOverrideTests.cs:121-122), so there is NO membership available here to serialize this " +
                    "mutation against any reader in either assembly — the rule for this project is therefore that " +
                    "the variable is never mutated at all. Redirect through SettingsPathFactory instead. If a " +
                    "genuine mutator is ever needed here the answer is a filed board row, never a second " +
                    $"{CollectionName} in this assembly.");
            }
        }

        Assert.NotEmpty(scanned); // zero scanned files is a broken walk, not a clean project
        Assert.True(violations.Count == 0,
            "headless data-root mutation guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    // ------------------------------------------------------------------ detection

    /// <summary>
    /// Every site in <paramref name="file"/> that makes its enclosing class a reader of the
    /// data-root variable: a <c>CompositionRoot</c> construction whose object initializer does
    /// NOT assign <see cref="RedirectSeam"/> (including the no-initializer form), a direct
    /// naming of one of <see cref="DataRootEntryPoints"/>, or an environment accessor whose
    /// argument list names the variable.
    /// </summary>
    private static List<Evidence> DataRootEvidence(SourceFile file, List<string> violations)
    {
        var found = new List<Evidence>();
        var text = file.Sanitized;

        foreach (Match match in RootConstruction().Matches(text))
        {
            var i = SkipWhitespace(text, match.Index + match.Length);
            if (i < text.Length && text[i] == '(')
            {
                var argumentEnd = MatchForward(text, i, '(', ')');
                if (argumentEnd < 0)
                {
                    violations.Add($"{file.Relative}:{LineOf(file, match.Index)}: the argument list of a " +
                        $"`new CompositionRoot` does not close — {UnparseableRefusal}");
                    continue;
                }

                i = SkipWhitespace(text, argumentEnd + 1);
            }

            var redirected = false;
            if (i < text.Length && text[i] == '{')
            {
                var initializerEnd = MatchForward(text, i, '{', '}');
                if (initializerEnd < 0)
                {
                    violations.Add($"{file.Relative}:{LineOf(file, match.Index)}: the object initializer of a " +
                        $"`new CompositionRoot` does not close — {UnparseableRefusal}");
                    continue;
                }

                redirected = text[i..initializerEnd].Contains(RedirectSeam, StringComparison.Ordinal);
            }

            if (!redirected)
            {
                found.Add(new Evidence(match.Index, $"constructs CompositionRoot without assigning {RedirectSeam}"));
            }
        }

        foreach (var token in DataRootEntryPoints)
        {
            var at = text.IndexOf(token, StringComparison.Ordinal);
            while (at >= 0)
            {
                found.Add(new Evidence(at, $"names {token}"));
                at = text.IndexOf(token, at + token.Length, StringComparison.Ordinal);
            }
        }

        foreach (var accessor in EnvAccessors)
        {
            foreach (var site in EnvCallSites(file, accessor, violations))
            {
                if (NamesTheVariable(file.Raw[site.ArgumentStart..site.ArgumentEnd]))
                {
                    found.Add(new Evidence(site.Offset, $"passes the data-root variable to {accessor}"));
                }
            }
        }

        return found;
    }

    /// <summary>Call sites of <paramref name="accessor"/>, located in the SANITIZED text (so a
    /// mention inside a comment or a literal is not a call) with their argument range, which
    /// the caller reads out of the RAW text at the same offsets.</summary>
    private static List<EnvCallSite> EnvCallSites(SourceFile file, string accessor, List<string> violations)
    {
        var sites = new List<EnvCallSite>();
        var text = file.Sanitized;
        var at = text.IndexOf(accessor, StringComparison.Ordinal);
        while (at >= 0)
        {
            var open = SkipWhitespace(text, at + accessor.Length);
            if (open < text.Length && text[open] == '(')
            {
                var close = MatchForward(text, open, '(', ')');
                if (close < 0)
                {
                    violations.Add($"{file.Relative}:{LineOf(file, at)}: the argument list of {accessor} does not " +
                        $"close — {UnparseableRefusal}");
                }
                else
                {
                    sites.Add(new EnvCallSite(at, open + 1, close));
                }
            }

            at = text.IndexOf(accessor, at + accessor.Length, StringComparison.Ordinal);
        }

        return sites;
    }

    private static bool NamesTheVariable(string rawArgumentList) =>
        VariableSpellings.Any(s => rawArgumentList.Contains(s, StringComparison.Ordinal));

    // ------------------------------------------------------------------ type structure

    /// <summary>Brace-matched type body spans. A bodyless declaration (<c>class X;</c> — the
    /// collection definition itself is one, DataRootOverrideTests.cs:122) owns no brace, so the
    /// next <c>{</c> belongs to a LATER type; that correction is load-bearing here.</summary>
    private static List<TypeSpan> TypeSpans(SourceFile file, List<string> violations)
    {
        var spans = new List<TypeSpan>();
        var text = file.Sanitized;
        foreach (Match match in TypeDecl().Matches(text))
        {
            var open = text.IndexOf('{', match.Index + match.Length);
            if (open < 0)
            {
                continue; // a declaration with no body anywhere after it
            }

            var between = text[(match.Index + match.Length)..open];
            if (between.Contains(';') || between.Contains('}'))
            {
                continue; // bodyless declaration
            }

            var close = MatchForward(text, open, '{', '}');
            if (close < 0)
            {
                violations.Add($"{file.Relative}:{LineOf(file, match.Index)}: the body of type " +
                    $"'{match.Groups[1].Value}' does not close — {UnparseableRefusal}");
                continue;
            }

            spans.Add(new TypeSpan(match.Groups[1].Value, match.Index, open, close));
        }

        return spans;
    }

    /// <summary>The outermost type whose body contains <paramref name="offset"/>. Spans are in
    /// declaration order, so the first container is the outermost one.</summary>
    private static TypeSpan? Outermost(List<TypeSpan> spans, int offset)
    {
        foreach (var span in spans)
        {
            if (offset > span.BodyStart && offset < span.BodyEnd)
            {
                return span;
            }
        }

        return null;
    }

    /// <summary>The attribute lists immediately preceding a type declaration, returned in both
    /// forms at IDENTICAL offsets: the STRUCTURE is bracket-matched on sanitized text (so a
    /// bracket inside a literal cannot move it) and the CONTENT is sliced out of raw text (so
    /// the <c>[Collection("ProcessEnvCollection")]</c> spelling stays readable). Comments were
    /// blanked to spaces, so the backward walk crosses a doc comment as whitespace.</summary>
    private static (string Raw, string Sanitized) AttributeRegion(SourceFile file, TypeSpan span)
    {
        var text = file.Sanitized;
        var i = span.DeclIndex - 1;

        // Cross the modifier words (public / sealed / partial / ...) that sit between the
        // attribute lists and the type keyword.
        while (i >= 0 && (char.IsWhiteSpace(text[i]) || char.IsLetterOrDigit(text[i]) || text[i] == '_'))
        {
            i--;
        }

        var raw = new StringBuilder();
        var sanitized = new StringBuilder();
        while (i >= 0 && text[i] == ']')
        {
            var open = MatchBackward(text, i);
            if (open < 0)
            {
                break;
            }

            raw.Insert(0, file.Raw[open..(i + 1)]);
            sanitized.Insert(0, text[open..(i + 1)]);
            i = open - 1;
            while (i >= 0 && char.IsWhiteSpace(text[i]))
            {
                i--;
            }
        }

        return (raw.ToString(), sanitized.ToString());
    }

    /// <summary>True when the attribute region names <see cref="CollectionName"/>. Accepted
    /// spellings are <c>nameof(X)</c>, <c>typeof(X)</c>, <c>"X"</c> and <c>[Collection&lt;X&gt;]</c>,
    /// and acceptance is keyed on the NAME, so <c>[Collection&lt;SomethingElse&gt;]</c> on a bound
    /// class is still a violation. An argument matching none of the four shapes does not parse
    /// and is reported through <paramref name="problem"/> rather than read as a pass.</summary>
    private static bool CarriesTheCollectionAttribute(string raw, string sanitized, out string? problem)
    {
        problem = null;
        var carries = false;
        foreach (Match head in CollectionAttributeHead().Matches(sanitized))
        {
            var openAt = head.Index + head.Length - 1;
            var open = sanitized[openAt];
            var end = MatchForward(sanitized, openAt, open, open == '(' ? ')' : '>');
            if (end < 0)
            {
                problem = $"a [Collection...] attribute does not close — {UnparseableRefusal}";
                return false;
            }

            var argument = raw[(openAt + 1)..end].Trim();
            var parsed = CollectionArgument().Match(argument);
            if (!parsed.Success)
            {
                problem = $"the [Collection(...)] argument '{argument}' does not parse into a collection name " +
                    "(accepted: nameof(X), typeof(X), \"X\", [Collection<X>]) — " + UnparseableRefusal;
                return false;
            }

            var name = parsed.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value;
            var lastDot = name.LastIndexOf('.');
            if (lastDot >= 0)
            {
                name = name[(lastDot + 1)..];
            }

            carries |= string.Equals(name, CollectionName, StringComparison.Ordinal);
        }

        return carries;
    }

    // ------------------------------------------------------------------ file plumbing

    private static SourceFile Load(string normalizedPath)
    {
        var raw = File.ReadAllText(normalizedPath);
        var sanitized = Sanitize(raw);
        return new SourceFile(Relative(normalizedPath), raw, sanitized, LineStarts(raw));
    }

    /// <summary>The two project roots this guard binds, plus a refusal for anything else. Kept
    /// out of the [Fact] bodies with the rest of the tree-existence plumbing so no fs-predicate
    /// shape lands in a fact (SP-066 detector surface).</summary>
    private static List<string> SourcesOutsideTheKnownProjects(string testsRoot)
    {
        var strays = new List<string>();
        foreach (var path in EnumerateSources(testsRoot))
        {
            if (!InProject(path, UnitProject) && !InProject(path, HeadlessProject))
            {
                strays.Add(Relative(path));
            }
        }

        return strays;
    }

    private static bool InProject(string normalizedPath, string project) =>
        normalizedPath.Contains($"/client/tests/{project}/", StringComparison.Ordinal);

    private static string TestsRoot()
    {
        var root = Path.Combine([FindRepoRoot(), .. TestsParts]);
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException(
                $"client/tests not found at {root} — the ProcessEnvCollection guard refuses to skip");
        }

        return root;
    }

    private static List<string> EnumerateSources(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                $"{directory} not found — the ProcessEnvCollection guard refuses to skip");
        }

        var sources = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.Contains("/obj/", StringComparison.Ordinal) ||
                normalized.Contains("/bin/", StringComparison.Ordinal))
            {
                continue; // generated sources (AssemblyInfo and friends) are not test code
            }

            sources.Add(normalized);
        }

        return sources;
    }

    private static string Relative(string normalizedPath)
    {
        var at = normalizedPath.IndexOf("/client/tests/", StringComparison.Ordinal);
        return at >= 0 ? normalizedPath[(at + 1)..] : normalizedPath;
    }

    private static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts.ToArray();
    }

    private static int LineOf(SourceFile file, int offset)
    {
        var index = Array.BinarySearch(file.LineStarts, offset);
        return index >= 0 ? index + 1 : ~index;
    }

    private static int SkipWhitespace(string text, int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            at++;
        }

        return at;
    }

    private static int MatchForward(string text, int openAt, char open, char close)
    {
        var depth = 0;
        for (var i = openAt; i < text.Length; i++)
        {
            if (text[i] == open)
            {
                depth++;
            }
            else if (text[i] == close && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int MatchBackward(string text, int closeAt)
    {
        var depth = 0;
        for (var i = closeAt; i >= 0; i--)
        {
            if (text[i] == ']')
            {
                depth++;
            }
            else if (text[i] == '[' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    // ------------------------------------------------------------------ sanitizer

    /// <summary>
    /// Blank comments and string/char literals: contents replaced with spaces, newlines AND
    /// OFFSETS preserved. Offset preservation is load-bearing — attribute structure is matched
    /// on this text and its content is then sliced out of the raw text at the same indices.
    /// Idiom: VacuousShapeDetector.cs:341-471. That file is out of this packet's scope and its
    /// blanker is private, so this is a local, smaller copy: blanking only, no shape analysis.
    /// </summary>
    private static string Sanitize(string source)
    {
        var buf = source.ToCharArray();
        var i = 0;
        while (i < buf.Length)
        {
            var start = i;
            var c = buf[i];
            if (c == '/' && i + 1 < buf.Length && buf[i + 1] == '/')
            {
                while (i < buf.Length && buf[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '/' && i + 1 < buf.Length && buf[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < buf.Length && !(buf[i] == '*' && buf[i + 1] == '/'))
                {
                    i++;
                }

                i = Math.Min(i + 2, buf.Length);
            }
            else if (c == '\'')
            {
                i++;
                while (i < buf.Length && buf[i] != '\'')
                {
                    i += buf[i] == '\\' ? 2 : 1;
                }

                i = Math.Min(i + 1, buf.Length);
            }
            else if (c == '"' || c == '$' || c == '@')
            {
                var end = EndOfLiteral(buf, i);
                if (end < 0)
                {
                    i = start + 1; // a bare @identifier or a stray $ — not a literal
                    continue;
                }

                i = end;
            }
            else
            {
                i++;
                continue;
            }

            Blank(buf, start, i);
        }

        return new string(buf);
    }

    /// <summary>Index just past the literal starting at <paramref name="at"/>, or -1 when the
    /// prefix is not a literal at all. Interpolation holes are tracked by brace depth so a
    /// quote inside a hole does not end the literal early.</summary>
    private static int EndOfLiteral(char[] buf, int at)
    {
        var i = at;
        var verbatim = false;
        var interpolated = false;
        while (i < buf.Length && (buf[i] == '$' || buf[i] == '@'))
        {
            verbatim |= buf[i] == '@';
            interpolated |= buf[i] == '$';
            i++;
        }

        var quotes = 0;
        while (i + quotes < buf.Length && buf[i + quotes] == '"')
        {
            quotes++;
        }

        if (quotes == 0)
        {
            return -1;
        }

        if (quotes >= 3)
        {
            i += quotes;
            while (i < buf.Length)
            {
                var run = 0;
                while (i + run < buf.Length && buf[i + run] == '"')
                {
                    run++;
                }

                if (run >= quotes)
                {
                    return Math.Min(i + run, buf.Length);
                }

                i += run > 0 ? run : 1;
            }

            return buf.Length;
        }

        if (quotes == 2)
        {
            return i + 2; // the empty literal
        }

        i++;
        var depth = 0;
        while (i < buf.Length)
        {
            var c = buf[i];
            if (!verbatim && c == '\\')
            {
                i += 2;
                continue;
            }

            if (verbatim && c == '"' && depth == 0 && i + 1 < buf.Length && buf[i + 1] == '"')
            {
                i += 2;
                continue;
            }

            if (interpolated && (c == '{' || c == '}'))
            {
                if (i + 1 < buf.Length && buf[i + 1] == c)
                {
                    i += 2; // an escaped brace
                    continue;
                }

                depth += c == '{' ? 1 : depth > 0 ? -1 : 0;
                i++;
                continue;
            }

            if (c == '"')
            {
                if (depth == 0)
                {
                    return i + 1;
                }

                var nested = EndOfLiteral(buf, i);
                i = nested < 0 ? i + 1 : nested;
                continue;
            }

            i++;
        }

        return buf.Length;
    }

    private static void Blank(char[] buf, int from, int to)
    {
        for (var k = from; k < to && k < buf.Length; k++)
        {
            if (buf[k] != '\n' && buf[k] != '\r')
            {
                buf[k] = ' ';
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the ProcessEnvCollection guard refuses to skip");
    }
}
