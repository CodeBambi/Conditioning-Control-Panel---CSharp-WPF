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
            .Select(f => Regex.Replace(File.ReadAllText(f), @"//.*", "")).ToList(); // comments are not calls
        var defined = Directory.GetFiles(Path.Combine(head, "Views", "Windows"), "MainShellWindow*.cs")
            .SelectMany(f => Regex.Matches(Regex.Replace(File.ReadAllText(f), @"//.*", ""), @"void (Initialize\w+)\(").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.NotEmpty(defined);
        // A call is the name followed by "(" (or passed as a method group) anywhere but its own definition.
        var uncalled = defined.Where(name => all.Sum(src =>
            Regex.Matches(src, $@"\b{name}\b").Count - Regex.Matches(src, $@"void {name}\(").Count) == 0).ToList();
        Assert.Empty(uncalled);
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
