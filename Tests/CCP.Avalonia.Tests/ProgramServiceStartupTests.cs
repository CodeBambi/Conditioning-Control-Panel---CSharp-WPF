using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// P04/P11 for programs CHECKPOINT A (docs/avalonia-decisions.md 2026-10-09): App startup builds
/// ProgramService load-only, disposes it on exit, and never builds the writing instance or seeds
/// the mutating TrackProgramVerifierProvider before the run panel exists.
/// </summary>
public sealed class ProgramServiceStartupTests
{
    [Fact]
    public void AppStartupBuildsProgramsReadOnlyAndDisposesThem()
    {
        var src = Regex.Replace(   // P23: comments and string literals never count
            File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs")),
            @"//[^\n]*|/\*.*?\*/|@?""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);

        Assert.Matches(@"Programs\s*=\s*Services\.Program\.ProgramService\.CreateReadOnly\(\)", src);
        Assert.Contains("Programs?.Dispose()", src);
        Assert.DoesNotMatch(@"new\s+(?:[\w.]+\.)?ProgramService\s*\(", src);
        Assert.DoesNotContain("TrackProgramVerifierProvider", src);
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
