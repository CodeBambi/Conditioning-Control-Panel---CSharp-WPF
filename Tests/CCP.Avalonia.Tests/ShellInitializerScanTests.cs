using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>PLAYBOOK P04: a shell feature whose Initialize* is never called is dead even with green tests
/// (port-social's header ticket). Every Initialize* a MainShellWindow partial defines must be called somewhere.</summary>
public sealed class ShellInitializerScanTests
{
    [Fact]
    public void EveryShellInitializerIsCalled()
    {
        var head = Path.Combine(RepoRoot(), "CCP.Avalonia");
        var all = Directory.GetFiles(head, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            // Diagnostic runners (--nav-check, --smoke, --render-*, probes) re-invoke wiring; they are not the app calling it.
            .Where(f => !Regex.IsMatch(Path.GetFileName(f), @"^(\w+Check|\w+Probe|HeadlessSmoke|RenderProof)\.cs$"))
            .Select(f => Code(File.ReadAllText(f))).ToList();
        var defined = Directory.GetFiles(Path.Combine(head, "Views", "Windows"), "MainShellWindow*.cs")
            .SelectMany(f => Regex.Matches(Code(File.ReadAllText(f)), Definition(@"(Initialize\w+)")).Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.NotEmpty(defined);
        // A call is any other mention in code (a call or a method group), never its own definition.
        var uncalled = defined.Where(name => all.Sum(src =>
            Regex.Matches(src, $@"\b{name}\b").Count - Regex.Matches(src, Definition(name)).Count) == 0).ToList();
        Assert.Empty(uncalled);
    }

    /// <summary>A method definition whatever it returns (void, Task, Task&lt;T&gt;, bool), async or not.</summary>
    private static string Definition(string name) => $@"\b(?:void|bool|Task(?:<[^>()]+>)?)\s+{name}\s*\(";

    /// <summary>Source with comments and string literals ($"", @"", plain, chars) blanked: a logged name is not a call.</summary>
    private static string Code(string src) =>
        Regex.Replace(src, @"//[^\n]*|/\*.*?\*/|'(?:[^'\\]|\\.)'|[$@]{0,2}""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
