using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// P04/P11/P31, oracle CHECKPOINT B test 8 (programs 3a, docs/avalonia-decisions.md): App startup builds
/// the full writing ProgramService (timers, startup repair + rollover), seeds the capability gate before
/// it and the verifier fan-out after it (WPF App.xaml.cs:413), and disposes it on exit (the flush).
/// </summary>
public sealed class ProgramServiceStartupTests
{
    [Fact]
    public void AppStartupBuildsTheWritingProgramsSeedsTheVerifierAndDisposesIt()
    {
        var src = Regex.Replace(   // P23: comments and string literals never count
            File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs")),
            @"//[^\n]*|/\*.*?\*/|@?""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);

        Assert.Matches(@"CoreProgram\.TaskAvailableProvider\s*=\s*Platform\.ProgramCapabilities\.IsAvailable;\s*" +
                       @"Programs\s*=\s*new\s+Services\.Program\.ProgramService\(\);", src);
        Assert.Matches(@"CoreQuests\.TrackProgramVerifierProvider\s*=\s*\(category,\s*amount\)\s*=>\s*Programs\?\.TrackVerifier\(category,\s*amount\)", src);
        Assert.Contains("Programs?.Dispose()", src);
        Assert.DoesNotContain("CreateReadOnly", src);
    }

    /// <summary>Programs 3b (oracle programs-roadmap-seed test 6): ritual photos file through the shell's roadmap
    /// (WPF App.xaml.cs:447), seeded lazily - startup never touches MainShellWindow.Roadmap itself, so
    /// DisposeRoadmapIfCreated still only disposes a roadmap someone used.</summary>
    [Fact]
    public void AppStartupSeedsTheRoadmapProviderLazily()
    {
        var src = Regex.Replace(
            File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs")),
            @"//[^\n]*|/\*.*?\*/|@?""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);
        Assert.Matches(@"CoreProgram\.RoadmapProvider\s*=\s*\(\)\s*=>\s*Views\.Windows\.MainShellWindow\.Roadmap;", src);
        Assert.Single(Regex.Matches(src, @"MainShellWindow\.Roadmap\b"));
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
