using System.Text.RegularExpressions;

namespace CcpClient.Tests;

/// <summary>
/// SP-066 (board row 49 part 1): the lexical VACUOUS-SHAPE detector. One exact surface,
/// shared by the raw inventory (produced through this class) and the pinned-ledger guard
/// (<c>VacuousShapeGuardTests</c>), so the enumeration and the guard can never drift.
///
/// WHAT THIS IS: a lexical scan over every <c>[Fact]</c>/<c>[Theory]</c> body in both
/// test projects. It classifies the shapes by which an assertion can be SILENCED:
/// an early <c>return;</c> before the first assertion, every assertion nested inside a
/// conditional or loop, no assertion token at all, a platform predicate, an environment
/// predicate, a filesystem-existence predicate, or a dynamic skip. A method carrying at
/// least one shape is a SITE and must be dispositioned in the committed ledger.
///
/// WHAT THIS CANNOT SEE (lexical detection of vacuity is provably incomplete):
///  * FALSE POSITIVE — a fact whose assertions live in a CALLED HELPER reads as
///    no-assertion (or all-nested) here while asserting plenty at runtime.
///  * FALSE NEGATIVE — a fact whose only assertions sit inside
///    <c>foreach (var x in collection)</c> over a POSSIBLY-EMPTY collection reads as
///    "assertions present" while being vacuous at runtime. That is the exact defect class
///    board row 49 names, and this detector does not detect it; the mitigation is the
///    framing-(c) <c>Assert.NotEmpty</c> pin before such loops, applied at disposition.
///  * String-literal internals are blanked before analysis, so code inside interpolated
///    holes is invisible (documented; no test here asserts inside an interpolation hole).
/// This is a SHAPE guard. It must never be read as proof of runtime non-vacuity.
/// </summary>
public static partial class VacuousShapeDetector
{
    // Shape identifiers — the committed ledger and the guard bind these exact strings.
    public const string EarlyReturn = "early-return";
    public const string AssertionsAllNested = "assertions-all-nested";
    public const string NoAssertion = "no-assertion";
    public const string PlatformPredicate = "platform-predicate";
    public const string EnvPredicate = "env-predicate";
    public const string FsPredicate = "fs-predicate";
    public const string DynamicSkip = "dynamic-skip";

    public sealed record Site(string Path, int Line, string ClassName, string MethodName, IReadOnlyList<string> Shapes)
    {
        /// <summary>The ledger key. Line number is deliberately NOT part of the key: a
        /// moved test must stay covered (keyed on path + class + method only).</summary>
        public string Key => $"{Path}::{ClassName}.{MethodName}";
    }

    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] TestsParts = ["client", "tests"];

    [GeneratedRegex(@"\[(?:Fact|Theory)\b")]
    private static partial Regex FactAttribute();

    [GeneratedRegex(@"(?:public|internal|private|protected)\s+(?:static\s+)?(?:async\s+)?(?:void|Task|ValueTask)\b[\w<>\[\]\?,\. ]*?(\w+)\s*\(")]
    private static partial Regex MethodDecl();

    [GeneratedRegex(@"\bclass\s+(\w+)")]
    private static partial Regex ClassDecl();

    [GeneratedRegex(@"\bAssert\.(\w+)")]
    private static partial Regex AssertCall();

    [GeneratedRegex(@"\breturn\s*;")]
    private static partial Regex BareReturn();

    /// <summary>Scan both test projects under client/tests. Throws (never returns empty
    /// silently) when the tree is missing — the guard turns that into a failure.</summary>
    public static IReadOnlyList<Site> Scan()
    {
        var testsRoot = Path.Combine([FindRepoRoot(), .. TestsParts]);
        if (!Directory.Exists(testsRoot))
        {
            throw new InvalidOperationException(
                $"client/tests not found at {testsRoot} — the vacuous-shape detector refuses to skip");
        }

        var sites = new List<Site>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var normalized = file.Replace('\\', '/');
            var relative = normalized[(normalized.IndexOf("tests/", StringComparison.Ordinal) + "tests/".Length)..];
            var sanitized = Sanitize(File.ReadAllText(file));
            var lineStarts = LineStarts(sanitized);
            foreach (Match attr in FactAttribute().Matches(sanitized))
            {
                var site = AnalyzeMethod(relative, sanitized, lineStarts, attr.Index);
                if (site is null)
                {
                    throw new InvalidOperationException(
                        $"{relative}: [Fact]/[Theory] at offset {attr.Index} did not resolve to a method body — " +
                        "the detector refuses to go blind on a shape it cannot parse");
                }

                if (site.Shapes.Count == 0)
                {
                    continue;
                }

                if (!keys.Add(site.Key))
                {
                    throw new InvalidOperationException(
                        $"duplicate ledger key {site.Key} — method-name collisions break name-anchored coverage; " +
                        "rename one of the facts so the ledger stays keyed by name");
                }

                sites.Add(site);
            }
        }

        return sites;
    }

    private static Site? AnalyzeMethod(string relative, string text, int[] lineStarts, int attrIndex)
    {
        var decl = MethodDecl().Match(text, attrIndex);
        if (!decl.Success)
        {
            return null;
        }

        var methodName = decl.Groups[1].Value;
        // Enclosing type chain by BRACE RANGE (pre-approach consult correction: the last
        // textual class match can be a sibling nested type that already closed, misattributing
        // the fact to a fake). Only classes whose body span CONTAINS the attribute qualify,
        // innermost last, joined as Outer.Inner.
        var className = string.Join(".", EnclosingClasses(text, attrIndex));

        // Find the body: the first '{' after the parameter list, or an expression body.
        var i = decl.Index + decl.Length;
        var parenDepth = 1; // the '(' consumed by MethodDecl
        while (i < text.Length && parenDepth > 0)
        {
            if (text[i] == '(') parenDepth++;
            else if (text[i] == ')') parenDepth--;
            i++;
        }

        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        if (i >= text.Length) return null;

        int bodyStart, bodyEnd;
        if (text[i] == '{')
        {
            bodyStart = i;
            var depth = 0;
            while (i < text.Length)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) break;
                i++;
            }

            if (i >= text.Length) return null;
            bodyEnd = i;
        }
        else if (i + 1 < text.Length && text[i] == '=' && text[i + 1] == '>')
        {
            // Expression-bodied fact: body runs to the terminating ';' at paren depth 0.
            bodyStart = i + 2;
            var depth = 0;
            while (i < text.Length && !(text[i] == ';' && depth == 0))
            {
                if (text[i] is '(' or '[' or '{') depth++;
                else if (text[i] is ')' or ']' or '}') depth--;
                i++;
            }

            if (i >= text.Length) return null;
            bodyEnd = i;
        }
        else
        {
            return null; // attribute not followed by a parseable method body — fail closed above
        }

        var body = text[bodyStart..bodyEnd];
        var shapes = new List<string>();

        var assertions = new List<(int Offset, int Depth)>();
        var skipTokens = false;
        // depth 0 = a direct statement of the method body; >0 = inside a GUARDING block
        // (if/else/foreach/for/while/switch/lambda — the shapes that can silence an
        // assertion). try/finally/catch/using/lock/method/class/init braces do NOT guard:
        // an assertion inside try{} with cleanup in finally cannot be silenced by it.
        // The method body's own brace classifies non-guarding, so no base correction.
        foreach (Match m in AssertCall().Matches(body))
        {
            if (m.Groups[1].Value.StartsWith("Skip", StringComparison.Ordinal))
            {
                skipTokens = true; // Assert.Skip / SkipWhen / SkipUnless are skips, not assertions
                continue;
            }

            assertions.Add((m.Index, DepthAt(body, m.Index)));
        }

        var returns = BareReturn().Matches(body).Select(m => m.Index).ToArray();
        var firstAssertion = assertions.Count > 0 ? assertions[0].Offset : int.MaxValue;

        if (assertions.Count == 0)
        {
            shapes.Add(NoAssertion);
        }
        else if (assertions.All(a => a.Depth > 0))
        {
            shapes.Add(AssertionsAllNested);
        }

        if (returns.Any(r => r < firstAssertion))
        {
            shapes.Add(EarlyReturn);
        }

        if (body.Contains("OperatingSystem.Is", StringComparison.Ordinal) ||
            body.Contains("RuntimeInformation.IsOSPlatform", StringComparison.Ordinal))
        {
            shapes.Add(PlatformPredicate);
        }

        if (body.Contains("Environment.GetEnvironmentVariable", StringComparison.Ordinal))
        {
            shapes.Add(EnvPredicate);
        }

        if (body.Contains("File.Exists(", StringComparison.Ordinal) ||
            body.Contains("Directory.Exists(", StringComparison.Ordinal))
        {
            shapes.Add(FsPredicate);
        }

        if (skipTokens)
        {
            shapes.Add(DynamicSkip);
        }

        var line = Array.BinarySearch(lineStarts, decl.Index) is var li && li >= 0 ? li + 1 : ~li;
        return new Site(relative, line, className, methodName, shapes);
    }

    /// <summary>Names of every class whose brace-matched body contains <paramref name="offset"/>,
    /// outermost first. A class whose body closes before the offset (a sibling nested fake)
    /// is excluded — that was the misattribution bug the pre-approach consult caught.</summary>
    private static List<string> EnclosingClasses(string text, int offset)
    {
        var chain = new List<string>();
        foreach (Match c in ClassDecl().Matches(text))
        {
            if (c.Index >= offset) break;
            var open = text.IndexOf('{', c.Index + c.Length);
            if (open < 0 || open >= offset) continue; // attribute sits before this class body
            // A bodyless declaration (`class X;` — e.g. a CollectionDefinition) has no body
            // brace of its own; the next '{' belongs to a LATER type. If a ';' or '}' stands
            // between the declaration and the brace, this match does not own that brace.
            var between = text[(c.Index + c.Length)..open];
            if (between.Contains(';') || between.Contains('}')) continue;
            var depth = 0;
            var close = open;
            while (close < text.Length)
            {
                if (text[close] == '{') depth++;
                else if (text[close] == '}' && --depth == 0) break;
                close++;
            }

            if (offset < close)
            {
                chain.Add(c.Groups[1].Value);
            }
        }

        return chain;
    }

    /// <summary>Guarding depth at <paramref name="offset"/>: counts only braces whose block
    /// can SILENCE the statements inside (conditional/loop/lambda). try/finally/catch/using/
    /// lock/method/class/initializer braces are transparent to the count.</summary>
    private static int DepthAt(string body, int offset)
    {
        var stack = new Stack<bool>(); // true = guarding brace
        var depth = 0;
        for (var i = 0; i < offset; i++)
        {
            if (body[i] == '{')
            {
                var guarding = IsGuardingBrace(body, i);
                stack.Push(guarding);
                if (guarding) depth++;
            }
            else if (body[i] == '}' && stack.Count > 0)
            {
                if (stack.Pop()) depth--;
            }
        }

        return depth;
    }

    private static bool IsGuardingBrace(string text, int braceAt)
    {
        var j = braceAt - 1;
        while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
        if (j < 0) return false;

        if (j >= 1 && text[j] == '>' && text[j - 1] == '=') return true; // lambda body => { }

        // Word-terminated blocks without a parameter list: else / try / finally / do.
        if (char.IsLetter(text[j]))
        {
            var end = j + 1;
            while (j >= 0 && char.IsLetterOrDigit(text[j])) j--;
            var word = text[(j + 1)..end];
            return word is "else"; // try/finally/do and declarations do not guard
        }

        if (text[j] != ')') return false; // initializer / property / other

        // Find the keyword before the matching '(': if/foreach/for/while/switch guard;
        // using/lock/catch/fixed and method declarations do not.
        var parens = 1;
        var k = j - 1;
        while (k >= 0 && parens > 0)
        {
            if (text[k] == ')') parens++;
            else if (text[k] == '(') parens--;
            k--;
        }

        if (k < 0) return false;
        var wend = k + 1;
        while (wend > 0 && char.IsWhiteSpace(text[wend - 1])) wend--;
        var wstart = wend;
        while (wstart > 0 && char.IsLetterOrDigit(text[wstart - 1])) wstart--;
        var keyword = text[wstart..wend];
        return keyword is "if" or "foreach" or "for" or "while" or "switch";
    }

    /// <summary>Blank comments and string/char literals (contents replaced with spaces,
    /// newlines preserved) so brace-matching and token scans never see literal contents.
    /// Interpolated strings are blanked WHOLE, holes included — code inside an
    /// interpolation hole is invisible to this detector (documented class limitation).</summary>
    private static string Sanitize(string src)
    {
        var buf = src.ToCharArray();
        var i = 0;
        void Blank(int from, int to)
        {
            for (var k = from; k < to; k++)
            {
                if (buf[k] != '\n' && buf[k] != '\r') buf[k] = ' ';
            }
        }

        int CountRun(char c, int at)
        {
            var n = 0;
            while (at + n < buf.Length && buf[at + n] == c) n++;
            return n;
        }

        while (i < buf.Length)
        {
            var c = buf[i];
            if (c == '/' && i + 1 < buf.Length && buf[i + 1] == '/')
            {
                var start = i;
                while (i < buf.Length && buf[i] != '\n') i++;
                Blank(start, i);
            }
            else if (c == '/' && i + 1 < buf.Length && buf[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i + 1 < buf.Length && !(buf[i] == '*' && buf[i + 1] == '/')) i++;
                i = Math.Min(i + 2, buf.Length);
                Blank(start, i);
            }
            else if (c == '@' && i + 1 < buf.Length && buf[i + 1] == '"')
            {
                var start = i;
                i += 2;
                while (i < buf.Length)
                {
                    if (buf[i] == '"' && i + 1 < buf.Length && buf[i + 1] == '"') { i += 2; continue; }
                    if (buf[i] == '"') { i++; break; }
                    i++;
                }

                Blank(start, i);
            }
            else if (c == '$' || (c == '@' && i + 1 < buf.Length && buf[i + 1] == '$') || (c == '$' && i + 1 < buf.Length && buf[i + 1] == '@'))
            {
                // Interpolated (possibly verbatim/raw) string — blank whole, holes included.
                var start = i;
                var verbatim = false;
                while (i < buf.Length && (buf[i] == '$' || buf[i] == '@'))
                {
                    verbatim |= buf[i] == '@';
                    i++;
                }

                var quotes = CountRun('"', i);
                if (quotes == 0)
                {
                    continue; // a bare $ or @ identifier (e.g. @class) — not a string
                }

                if (quotes >= 3)
                {
                    i += quotes;
                    while (i < buf.Length && CountRun('"', i) < quotes) i++;
                    i += quotes;
                }
                else
                {
                    i += quotes;
                    while (i < buf.Length)
                    {
                        if (verbatim && buf[i] == '"' && i + 1 < buf.Length && buf[i + 1] == '"') { i += 2; continue; }
                        if (!verbatim && buf[i] == '\\') { i += 2; continue; }
                        if (buf[i] == '"') { i++; break; }
                        i++;
                    }
                }

                Blank(start, i);
            }
            else if (c == '"')
            {
                var quotes = CountRun('"', i);
                var start = i;
                if (quotes >= 3)
                {
                    i += quotes;
                    while (i < buf.Length && CountRun('"', i) < quotes) i++;
                    i += quotes;
                }
                else
                {
                    i++;
                    while (i < buf.Length && buf[i] != '"')
                    {
                        if (buf[i] == '\\') i++;
                        i++;
                    }

                    i = Math.Min(i + 1, buf.Length);
                }

                Blank(start, i);
            }
            else if (c == '\'')
            {
                var start = i;
                i++;
                while (i < buf.Length && buf[i] != '\'')
                {
                    if (buf[i] == '\\') i++;
                    i++;
                }

                i = Math.Min(i + 1, buf.Length);
                Blank(start, i);
            }
            else
            {
                i++;
            }
        }

        return new string(buf);
    }

    private static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') starts.Add(i + 1);
        }

        return starts.ToArray();
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the vacuous-shape detector refuses to skip");
    }
}
