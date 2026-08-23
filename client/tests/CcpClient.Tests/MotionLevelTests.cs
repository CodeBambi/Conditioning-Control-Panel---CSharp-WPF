using System.Reflection;
using System.Text.Json.Nodes;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Motion;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The hosted-page motion rule: the Chromium <c>prefers-reduced-motion</c> answer is chosen by the
/// USER'S setting and never by the operating system
/// (<c>ConditioningControlPanel/Chaos/ChaosWebViewHost.cs:766-817</c>).
/// </summary>
public class MotionLevelTests
{
    [Theory]
    [InlineData(MotionLevel.Full, HostedMotion.NoReducedArgument)]
    // Reduced is the case the whole row turns on: a user who asked for calmer CHROME did not ask
    // for a hosted session that never moves (ChaosWebViewHost.cs:809-811).
    [InlineData(MotionLevel.Reduced, HostedMotion.NoReducedArgument)]
    [InlineData(MotionLevel.Off, HostedMotion.ReducedArgument)]
    public void OnlyOff_ForwardsReducedMotionToAHostedPage(MotionLevel level, string expected)
    {
        Assert.Equal(expected, HostedMotion.PrefersReducedMotionArgument(level));
    }

    [Fact]
    public void TheArgumentRule_TakesTheUsersLevelAndNothingElse()
    {
        // Structural, not an instance check: upstream's own note on this method is "what is NOT a
        // parameter: the OS animation flag" (ChaosWebViewHost.cs:809-810). A future edit that
        // threaded the OS state back in would have to change this signature to do it.
        var parameters = typeof(HostedMotion)
            .GetMethod(nameof(HostedMotion.PrefersReducedMotionArgument), BindingFlags.Public | BindingFlags.Static)!
            .GetParameters();

        Assert.Equal([typeof(MotionLevel)], parameters.Select(p => p.ParameterType).ToArray());
    }

    [Theory]
    // The one combination that is an override: the OS says animations are off and the user did
    // not ask for Off (ChaosWebViewHost.cs:782-800). Everything else is silent.
    [InlineData(MotionLevel.Full, false, true)]
    [InlineData(MotionLevel.Reduced, false, true)]
    [InlineData(MotionLevel.Off, false, false)]
    [InlineData(MotionLevel.Full, true, false)]
    [InlineData(MotionLevel.Reduced, true, false)]
    [InlineData(MotionLevel.Off, true, false)]
    // Unknown OS state (non-Windows, or a failed SPI GET) is not a disagreement.
    [InlineData(MotionLevel.Full, null, false)]
    [InlineData(MotionLevel.Off, null, false)]
    public void TheBreadcrumb_IsWrittenOnlyWhenTheAppActuallyOverruledTheOs(
        MotionLevel level, bool? osClientAreaAnimation, bool expected)
    {
        Assert.Equal(expected, HostedMotion.OverridesOsPreference(level, osClientAreaAnimation));
    }

    [Fact]
    public void FreshDocument_IsFull_SoTheAppsSettingGovernsFromFirstRun()
    {
        // WPF default (Models/AppSettings.cs:3954) and this row's owner decision.
        Assert.Equal(MotionLevel.Full, new MotionSettingsDocument().Level);
        Assert.Equal(MotionLevel.Full, HostedMotion.LevelOf(null));
        Assert.Equal(HostedMotion.NoReducedArgument, HostedMotion.PrefersReducedMotionArgument(new MotionSettingsDocument().Level));
    }

    [Fact]
    public async Task TheHostedSurfacesReadTheUsersLevelOffTheStore_AndTheOsNeverChangesIt()
    {
        using var dir = new TempDir();
        var path = dir.Path(MotionSettingsDocument.FileName);
        var registry = new OperationRegistry();
        var log = new ListLogSink();
        var store = new PersistenceStore<MotionSettingsDocument>(
            registry.OwnerFor("MotionSettings"), log, path, MotionSettingsDocument.CurrentSchemaVersion);
        var host = new ApplicationHost(
            log, [store], new StartupTrace(), registry, new UiDispatchBoundary());
        await host.StartParticipantsAsync(TestContext.Current.CancellationToken);

        // What every hosted surface calls. The log delegate below is the breadcrumb's only sink;
        // whether it fires depends on THIS machine's animation flag, so the assertions here are on
        // the returned argument, which does not.
        var lines = new List<string>();
        Assert.Equal(HostedMotion.NoReducedArgument, HostedMotion.BrowserArgument(host, "test", lines.Add));

        store.Mutate(document => document.Level = MotionLevel.Reduced);
        Assert.Equal(MotionLevel.Reduced, HostedMotion.LevelOf(host));
        Assert.Equal(HostedMotion.NoReducedArgument, HostedMotion.BrowserArgument(host, "test", lines.Add));

        store.Mutate(document => document.Level = MotionLevel.Off);
        Assert.Equal(MotionLevel.Off, HostedMotion.LevelOf(host));
        Assert.Equal(HostedMotion.ReducedArgument, HostedMotion.BrowserArgument(host, "test", lines.Add));

        // Every breadcrumb that WAS written names the argument it is about, and none is written
        // for the Off call (that user asked for it; nothing was overruled).
        Assert.All(lines, line => Assert.Contains(HostedMotion.NoReducedArgument, line, StringComparison.Ordinal));

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task UnknownMembers_SurviveARoundTripThroughThisBuild()
    {
        using var dir = new TempDir();
        var path = dir.Path(MotionSettingsDocument.FileName);
        // A document written by a LATER build: a member this build has never heard of, beside the
        // one it owns. Persistence contract §6 — a round trip here must not eat it.
        File.WriteAllText(path, """
            {
              "schemaVersion": 1,
              "level": 2,
              "quietHours": { "from": "22:00", "to": "07:00" }
            }
            """);

        var registry = new OperationRegistry();
        var store = new PersistenceStore<MotionSettingsDocument>(
            registry.OwnerFor("MotionSettings"), new ListLogSink(), path,
            MotionSettingsDocument.CurrentSchemaVersion);
        await store.StartAsync(TestContext.Current.CancellationToken);

        Assert.IsType<LoadOutcome.Loaded>(store.LastLoadOutcome);
        Assert.Equal(MotionLevel.Off, store.Current.Level);

        store.Mutate(document => document.Level = MotionLevel.Reduced);
        Assert.IsType<OperationOutcome.Completed>(await store.SaveImmediate());

        var written = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(1, written["level"]!.GetValue<int>()); // the ordinal IS the persisted value
        Assert.Equal("22:00", written["quietHours"]!["from"]!.GetValue<string>());
        Assert.Equal(1, written["schemaVersion"]!.GetValue<int>());

        await store.StopAsync();
    }

    private sealed class ListLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccp-motion-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string Path(string fileName) => System.IO.Path.Combine(_root, fileName);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A temp directory a virus scanner still holds is not a test failure.
            }
        }
    }
}

/// <summary>
/// Motion-argument choke-point guard: the two Chromium switch literals may appear ONLY in the one
/// file that decides them, and every hosted WebView2 surface must reach that decision through
/// <see cref="HostedMotion.BrowserArgument"/>. Five surfaces with five copies of a rule is five
/// chances to drift, and this is what catches the sixth copy. Idiom:
/// <c>DataRootChokePointGuardTests</c> (repo-root walk, never skips, file:line violations).
/// </summary>
public class MotionArgumentChokePointGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] SrcParts = ["client", "src"];

    private static readonly string[] SwitchLiterals =
    [
        HostedMotion.ReducedArgument,
        HostedMotion.NoReducedArgument,
    ];

    private static readonly string[] AllowedSuffixes =
    [
        // The choke point itself.
        "Motion/MotionSettings.cs",
        // The startup-flag registry, which must classify every "--..." literal under client/src
        // (HarnessEntryPointGuardTests). Its copy is a classification, not a second decision.
        "Lifecycle/HarnessEntryPoints.cs",
    ];

    /// <summary>Every site that hands arguments to a hosted WebView2 environment.</summary>
    private static readonly string[] HostedSurfaces =
    [
        "CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs",
        "CcpClient.Desktop/Features/Dtrh/DtrhLoomWindow.axaml.cs",
        "CcpClient.Desktop/Features/Goon/GoonHostWindow.axaml.cs",
        "CcpClient.Desktop/Features/Intake/IntakeHostWindow.axaml.cs",
        "CcpClient.Desktop/Features/Chaos/ChaosTunnelWindow.cs",
    ];

    [Fact]
    public void EveryHostedSurface_RoutesThroughTheOneMotionArgumentHelper()
    {
        var srcRoot = Path.Combine([FindRepoRoot(), .. SrcParts]);
        Assert.True(Directory.Exists(srcRoot), $"client/src not found at {srcRoot} — the motion choke-point guard refuses to skip");

        // (1) The literals live in one place.
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (AllowedSuffixes.Any(s => normalized.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var literal in SwitchLiterals)
                {
                    if (lines[i].Contains(literal, StringComparison.Ordinal))
                    {
                        violations.Add($"{normalized}:{i + 1}: \"{literal}\" outside {AllowedSuffixes[0]} — "
                            + "the prefers-reduced-motion answer is decided in exactly one place; "
                            + "call HostedMotion.BrowserArgument instead");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            "motion-argument choke-point guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));

        // (2) Every hosted surface actually calls it. A surface that quietly stopped would leave
        // its pages at the OS answer again — the exact regression this row closed.
        foreach (var surface in HostedSurfaces)
        {
            var path = Path.Combine([srcRoot, .. surface.Split('/')]);
            Assert.True(File.Exists(path), $"{surface} not found at {path} — the motion choke-point guard refuses to skip");
            var text = File.ReadAllText(path);
            Assert.True(
                text.Contains("HostedMotion.BrowserArgument(", StringComparison.Ordinal),
                $"{surface} sets WebView2 browser arguments without calling HostedMotion.BrowserArgument — "
                + "its hosted pages would take Chromium's OS-derived prefers-reduced-motion answer");
            Assert.True(
                text.Contains("AdditionalBrowserArguments", StringComparison.Ordinal),
                $"{surface} no longer sets AdditionalBrowserArguments — this guard's surface list is stale");
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the motion choke-point guard refuses to skip");
    }
}
