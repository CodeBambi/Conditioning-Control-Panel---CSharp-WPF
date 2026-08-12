using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-059 timing-discipline guard (T-15/T-16/third-occurrence encoding; SP-056/SP-057
/// guard lineage): a hard-coded wall-clock wait may appear in test code ONLY inside the
/// approved helper (<c>TestWait.cs</c>). Every other occurrence must carry an inline
/// <c>// wallclock-allow: &lt;reason&gt;</c> marker naming why it is class 3 (elapsed-time
/// subject / terminal hang tripwire / never-elapses instrument / negative-observation
/// settle) AND must be pinned below — a new wall-clock wait requires editing THIS guard,
/// which is the review friction that stops the class from returning. A pinned site that
/// disappears or changes also fails (stale pins cannot rot).
/// </summary>
public class TestTimingGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] TestsParts = ["client", "tests"];

    private static readonly string[] ForbiddenTokens =
    [
        "Environment.TickCount",
        "Thread.Sleep(",
        "Task.Delay(",
        "DateTime.UtcNow",
        "DateTime.Now",
        "DateTimeOffset.UtcNow",
        "DateTimeOffset.Now",
        "Stopwatch",
        ".WaitAsync(TimeSpan",
        ".Wait(TimeSpan",
        ".WaitOne(TimeSpan",
    ];

    /// <summary>Files exempt wholesale: the approved helper itself and this guard (which
    /// holds the tokens as string literals).</summary>
    private static readonly string[] ExemptFileNames = ["TestWait.cs", "TestTimingGuardTests.cs"];

    private const string Marker = "// wallclock-allow:";

    /// <summary>The named allowlist (pre-approach consult hardening — a bare marker would
    /// be a rubber stamp): (repo-relative path under client/tests, exact trimmed code
    /// before the marker, expected occurrence count). Unmarked token → fail. Marked but
    /// unpinned → fail. Pin count mismatch → fail.</summary>
    private static readonly (string Path, string Code, int Count)[] Pins =
    [
        ("CcpClient.Tests/AiMemoryPromptAssemblyTests.cs", "try { _serve.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }", 1),
        ("CcpClient.Tests/AiOperationContractTests.cs", "await Task.Delay(TimeSpan.FromMinutes(5), ct); // in-flight network/inference work", 1),
        ("CcpClient.Tests/AiProviderLab.cs", "await Task.Delay(1500);", 1),
        ("CcpClient.Tests/AiProviderLab.cs", "await Task.Delay(100);", 2),
        ("CcpClient.Tests/AiProviderLab.cs", "try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }", 1),
        ("CcpClient.Tests/AsyncLifecycleTests.cs", "await Task.Delay(Timeout.Infinite, token);", 1),
        ("CcpClient.Tests/AsyncLifecycleTests.cs", "await Task.Delay(Timeout.Infinite); // never observes the token", 1),
        ("CcpClient.Tests/AvatarAnimationEngineTests.cs", "await Task.Delay(150, TestContext.Current.CancellationToken);", 2),
        ("CcpClient.Tests/AvatarAnimationEngineTests.cs", "var outcome = await engine.Completion!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);", 2),
        ("CcpClient.Tests/AvatarAnimationEngineTests.cs", "var outcome = await participant.Completion!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);", 1),
        ("CcpClient.Tests/AvatarAnimationEngineTests.cs", "await participant.Completion!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);", 1),
        ("CcpClient.Tests/CapabilityTests.cs", "await Task.Delay(Timeout.Infinite, token); // observes cancellation", 1),
        ("CcpClient.Tests/DtrhInboxTests.cs", "var messages = await poll.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);", 2),
        ("CcpClient.Tests/DtrhLoopbackContractTests.cs", "var body = await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);", 1),
        ("CcpClient.Tests/TeardownFlushTests.cs", "releaseWrite.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);", 1),
        ("CcpClient.Tests/TeardownFlushTests.cs", "Assert.True(writeStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),", 1),
        ("CcpClient.HeadlessTests/AvatarTubeHeadlessTests.cs", "await Task.Delay(10); // let posted UI projections land", 1),
        ("CcpClient.HeadlessTests/AvatarTubeHeadlessTests.cs", "await Task.Delay(100);", 1),
        ("CcpClient.HeadlessTests/AvatarTubeHeadlessTests.cs", "var outcome = await participant.Completion!.WaitAsync(TimeSpan.FromSeconds(5));", 1),
    ];

    [Fact]
    public void NoWallClockWaitsOutsideTheApprovedHelper()
    {
        var testsRoot = Path.Combine([FindRepoRoot(), .. TestsParts]);
        Assert.True(Directory.Exists(testsRoot), $"client/tests not found at {testsRoot} — the timing guard refuses to skip");

        var violations = new List<string>();
        var seen = new Dictionary<(string Path, string Code), int>();
        foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (ExemptFileNames.Any(e => normalized.EndsWith("/" + e, StringComparison.Ordinal)))
            {
                continue; // the helper and this guard
            }

            var relative = normalized[(normalized.IndexOf("tests/", StringComparison.Ordinal) + "tests/".Length)..];
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!ForbiddenTokens.Any(t => line.Contains(t, StringComparison.Ordinal)))
                {
                    continue;
                }

                var markerAt = line.IndexOf(Marker, StringComparison.Ordinal);
                if (markerAt < 0)
                {
                    violations.Add($"{relative}:{i + 1}: unmarked wall-clock construct — use TestWait.Until/UntilSync or " +
                        "record the class-3 reason in a // wallclock-allow: marker AND pin it in TestTimingGuardTests (SP-059)");
                    continue;
                }

                if (line[(markerAt + Marker.Length)..].Trim().Length == 0)
                {
                    violations.Add($"{relative}:{i + 1}: the wallclock-allow marker carries no reason — name WHY this site is class 3");
                    continue;
                }

                var code = line[..markerAt].Trim();
                var key = (relative, code);
                if (!Pins.Any(p => p.Path == key.relative && p.Code == code))
                {
                    violations.Add($"{relative}:{i + 1}: marked wall-clock site is NOT PINNED in TestTimingGuardTests — " +
                        "a new wall-clock wait must be pinned here (that edit is the review gate): " + code);
                    continue;
                }

                seen[key] = seen.GetValueOrDefault(key) + 1;
            }
        }

        foreach (var pin in Pins)
        {
            var actual = seen.GetValueOrDefault((pin.Path, pin.Code));
            if (actual != pin.Count)
            {
                violations.Add($"stale pin: {pin.Path} \"{pin.Code}\" expected {pin.Count} occurrence(s), found {actual} — " +
                    "update or remove the pin (stale pins cannot rot)");
            }
        }

        Assert.True(violations.Count == 0,
            "timing-discipline guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the timing guard refuses to skip");
    }
}
