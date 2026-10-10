using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// P04/P11/P31, oracle CHECKPOINT B test 8 (programs 3a, docs/avalonia-decisions.md): App startup builds
/// the full writing ProgramService (timers, startup repair + rollover), seeds the capability gate before
/// it and the verifier fan-out after it (WPF App.xaml.cs:413), hands it the session runner through the
/// one ProgramEngineBridge, and disposes it on exit (the flush). The service lives in App.Programs.cs
/// (StartPrograms), called from App.axaml.cs right after the gate is seeded.
/// </summary>
public sealed class ProgramServiceStartupTests
{
    [Fact]
    public void AppStartupBuildsTheWritingProgramsSeedsTheVerifierAndDisposesIt()
    {
        var app = Source("App.axaml.cs");
        var programs = Source("App.Programs.cs");

        Assert.Matches(@"CoreProgram\.TaskAvailableProvider\s*=\s*Platform\.ProgramCapabilities\.IsAvailable;\s*" +
                       @"StartPrograms\(\);", app);
        Assert.Matches(@"Programs\s*=\s*new\s+ProgramService\(\);", programs);
        Assert.Matches(@"CoreQuests\.TrackProgramVerifierProvider\s*=\s*\(category,\s*amount\)\s*=>\s*Programs\?\.TrackVerifier\(category,\s*amount\)", programs);
        // One attach path: the bridge (SessionRunner.Stopped), never a second completion hook beside it.
        Assert.Contains("Programs.AttachSessionRunner(runner)", programs);
        Assert.DoesNotContain("OnEngineSessionCompleted", programs);
        Assert.Contains("Programs?.Dispose()", app + programs);
        Assert.DoesNotContain("CreateReadOnly", app + programs);
    }

    private static string Source(string file) => Regex.Replace(   // P23: comments and string literals never count
        File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", file)),
        @"//[^\n]*|/\*.*?\*/|@?""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
