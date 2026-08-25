using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The recurrence guard for a defect class that has now turned the Linux leg red twice.</b>
///
/// <para>Both times the shape was the same: facts that read a Win32 mechanism landed green on
/// Windows, nobody ran the other platform, and the leg went red silently. On 2026-08-25 that was
/// fourteen failures — thirteen of them a keyed comparison of two absent handles
/// (<c>0 == 0</c> answering YES, closed by <see cref="PointerWindowProbe.SameWindow"/> and pinned
/// by the second fact here) and one of them something worse: a fact body called
/// <c>[DllImport("user32.dll")] IsWindow</c> directly, so off Windows it did not read WRONG, it
/// threw <c>DllNotFoundException</c> having never considered the platform at all.</para>
///
/// <para><b>What this guard is, exactly.</b> A lexical scan asserting that no <c>[Fact]</c> or
/// <c>[Theory]</c> body in either test project calls a P/Invoke declared anywhere in
/// <c>client/tests</c>. The convention it enforces is the one every probe in this suite already
/// follows — <c>PointerWindowProbe.WindowExists</c>, <c>GlyphWindowProbe.RectOf</c>,
/// <c>SurfaceTeardownObservations.Os.IsWindowHandle</c>, and now <c>PanicKeyTests.IsARealWindow</c>
/// — namely that the native call sits behind a helper whose first clause is
/// <c>OperatingSystem.IsWindows()</c>. Put the call in the helper and the platform question is
/// answered once, in the one place a reader looks for it; put it in the fact and the fact is one
/// edit away from a stack trace on a machine that was never considered.</para>
///
/// <para><b>What it does NOT catch, measured rather than supposed.</b> It catches one of those
/// fourteen. The other thirteen were <i>comparisons</i> between two handle-valued readings, and a
/// lexical ban on those is not available cheaply: with the fix in place the tree still holds ten
/// such comparisons, and four of them are legitimate — <c>ForegroundBeforeOpen ==
/// ForegroundAfterOpen</c> asks whether ONE reading moved, which is a different question from
/// "is this handle that window" and would be broken by <see cref="PointerWindowProbe.SameWindow"/>'s
/// non-zero clause. A ban would therefore need its own exemption ledger to stay honest, which is
/// larger than the defect. The second fact below is the cheap half that IS available: it pins the
/// helper's refusal, so the shared fix cannot be quietly un-fixed.</para>
///
/// <para>It also proves nothing about whether a fact is CORRECTLY keyed or gated — that judgement
/// is a human one, recorded in <c>client/tests/floor/vacuous-shape-ledger.json</c> and in
/// <c>floor.json</c>'s <c>allowedSkips</c> — and a green run here says nothing whatever about
/// X11 or Wayland behaviour.</para>
/// </summary>
public partial class WindowsOnlyFactGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] TestsParts = ["client", "tests"];

    /// <summary>A <c>[DllImport]</c> attribute, anything the marshalling attributes put between it
    /// and the declaration, then the declared name. Bounded at 400 characters so a runaway match
    /// can never swallow the next member.</summary>
    [GeneratedRegex(@"\[DllImport[\s\S]{0,400}?\bextern\s+[\w<>\[\]\?\.,\s]*?(\w+)\s*\(")]
    private static partial Regex ExternDeclaration();

    [Fact]
    public void NoFactCallsAPInvokeDirectly_BecauseThePlatformShortCircuitLivesInTheHelperAndNotInTheFact()
    {
        var declarations = PInvokeDeclarations();
        Assert.True(declarations.Count > 0,
            "the walk found no [DllImport] declaration anywhere in client/tests, so it is not reading the suite "
            + "it polices and a green here would mean nothing");

        var facts = VacuousShapeDetector.Bodies();
        Assert.NotEmpty(facts);

        var violations = new List<string>();
        foreach (var fact in facts)
        {
            foreach (var (name, declaredIn) in declarations)
            {
                // Bodies() hands back COMMENTS AND STRING LITERALS BLANKED, which is the whole
                // reason this reads the detector's parse rather than raw text: this suite names
                // USER32 entry points constantly in remarks and assertion messages, and a mention
                // is not a call. The lookbehind excludes member access, so a call through a guarded
                // helper (Os.IsWindow(...), probe.WindowExists(...)) is never this violation.
                if (!Regex.IsMatch(fact.Body, $@"(?<![\w.]){Regex.Escape(name)}\s*\("))
                {
                    continue;
                }

                violations.Add($"{fact.Path}:{fact.Line}: {fact.ClassName}.{fact.MethodName} calls the P/Invoke "
                    + $"{name} (declared in {declaredIn}) directly in its body. Off Windows that is not a wrong "
                    + "reading, it is a DllNotFoundException in a fact that never asked what platform it was on. "
                    + "Route it through a helper whose first clause is OperatingSystem.IsWindows(), the way every "
                    + "probe in this suite already does, and let the fact read the helper");
            }
        }

        Assert.True(violations.Count == 0,
            "a fact reaches native code without a platform short-circuit:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// <b>The shared fix for the other thirteen, pinned so it cannot be un-fixed.</b>
    /// <see cref="PointerWindowProbe.SameWindow"/> exists for one clause — a window identity
    /// question first requires there to BE a window — and deleting that clause restores the exact
    /// defect: every keyed real-desktop fact would again read <c>0 == 0</c> off Windows and answer
    /// YES about a surface that was never placed.
    /// </summary>
    [Fact]
    public void SameWindow_RefusesTwoAbsences_AndStillAnswersYESForARealPair()
    {
        Assert.False(PointerWindowProbe.SameWindow(0, 0),
            "two absent handles were reported as the SAME window. That is the reading that put thirteen facts red "
            + "on Linux on 2026-08-25 and, keyed the other way, reports a surface winning its own point on a "
            + "machine with no window manager at all");
        Assert.False(PointerWindowProbe.SameWindow(0, 0x2A));
        Assert.False(PointerWindowProbe.SameWindow(0x2A, 0));
        Assert.False(PointerWindowProbe.SameWindow(0x2A, 0x2B));

        Assert.True(PointerWindowProbe.SameWindow(0x2A, 0x2A),
            "a real handle compared with itself must still be the same window, or the non-vacuity clause has "
            + "eaten the comparison it was meant to qualify");
    }

    /// <summary>Every P/Invoke name declared under <c>client/tests</c>, with every file that
    /// declares it. Kept OUT of the fact body with the rest of the tree plumbing so no <c>fs-predicate</c>
    /// shape lands in a fact — the convention <c>SurfaceExitTests.ProductSourceRoot</c> follows. The
    /// name set is GLOBAL rather than per-file on purpose: an <c>internal</c> extern is callable
    /// from another file, and a guard that only looked in the declaring file would miss it. Line
    /// comments are dropped before matching so a commented-out declaration cannot invent a name;
    /// block comments are not, and a <c>[DllImport]</c> inside one would be a phantom — no such
    /// comment exists in this tree and a violation naming a phantom would say so in its text.
    /// </summary>
    private static List<(string Name, string DeclaredIn)> PInvokeDeclarations()
    {
        var testsRoot = Path.Combine([FindRepoRoot(), .. TestsParts]);
        Assert.True(Directory.Exists(testsRoot), $"client/tests not found at {testsRoot} — this guard refuses to skip");

        // Every declaring file, not the first one seen: several files declare their own
        // private IsWindow, and a message naming one of them as THE declaration sends the
        // reader to a file that has nothing to do with the violation.
        var byName = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal))
            .Where(f => !f.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal))
        {
            var text = string.Join('\n', File.ReadAllLines(file)
                .Select(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal) ? string.Empty : l));
            foreach (Match match in ExternDeclaration().Matches(text))
            {
                if (!byName.TryGetValue(match.Groups[1].Value, out var declaringFiles))
                {
                    declaringFiles = new SortedSet<string>(StringComparer.Ordinal);
                    byName[match.Groups[1].Value] = declaringFiles;
                }

                declaringFiles.Add(Path.GetFileName(file));
            }
        }

        return [.. byName.Select(pair => (pair.Key, string.Join(", ", pair.Value)))];
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine([directory.FullName, .. RepoAnchorParts])))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
