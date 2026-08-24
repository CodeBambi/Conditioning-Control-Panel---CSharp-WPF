using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Source guard for the switch that makes Linux headed evidence possible at all, in the
/// established scan-the-source idiom (<see cref="PathPortabilityGuardTests"/>,
/// <c>DataRootChokePointGuardTests</c>).
///
/// <para>WHY A GUARD AND NOT JUST THE VACUITY GATE. The gate does catch this — a Linux capture
/// without software presentation is 836,000 pixels of one colour and <c>CcpVerify --vacuity</c>
/// refuses it at exit 3. But the gate only speaks when somebody runs the Linux leg by hand, and
/// the last time this exact condition was live it read as an unexplained black capture for weeks
/// before anyone separated "the app does not render" from "no capture route can see it". The
/// answer was the second one: Avalonia presenting through GL leaves the window's contents in a
/// GPU surface the X server does not track, so XGetImage on that drawable returns the window
/// background. Measured either side of the switch on the same binary and the same window —
/// 1 distinct colour without it, 3,083 with it.</para>
///
/// <para>THE CONTRACT HAS TWO ENDS AND EITHER ONE BREAKS IT, which is why one test asserts both:
/// the harness must ask for software presentation, and the product must still offer the opt-in
/// it asks for. Renaming the variable on the product side alone would leave a harness that reads
/// black and a gate that refuses, with nothing naming the cause.</para>
///
/// <para>This does NOT assert that software presentation is off by default; that is a separate
/// property and the opt-in's own conditional carries it. Comment lines are not scanned — both
/// files discuss the variable at length in prose, and a guard that fires on its own explanation
/// gets the explanation deleted.</para>
/// </summary>
public class LinuxCaptureHarnessGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private const string OptIn = "CCP_X11_SOFTWARE";

    [Fact]
    public void TheLinuxCaptureHarnessAsksForSoftwarePresentationAndTheProductStillOffersIt()
    {
        var root = FindRepoRoot();
        var harness = Path.Combine(root, "client", "tools", "verify", "capture-wslg.sh");
        var program = Path.Combine(root, "client", "src", "CcpClient.Desktop", "Program.cs");

        // No File.Exists precondition: reading a missing file throws with its path, which is a
        // louder failure than an assertion and carries no shape that could silence this test.
        var asks = Code(harness, "#")
            .Any(line => line.Contains($"export {OptIn}=1", StringComparison.Ordinal));
        Assert.True(asks,
            $"capture-wslg.sh no longer exports {OptIn}=1. Without it Avalonia presents through GL, "
            + "the window's contents live in a GPU surface the X server does not track, and every "
            + "Linux capture comes back as one colour — the defect that read as an unexplained black "
            + "screen for weeks. Re-add the export; do not make software presentation a product "
            + "default, because the fault is in what a capture can see, not in what a user sees.");

        var offered = Code(program, "//")
            .Any(line => line.Contains($"\"{OptIn}\"", StringComparison.Ordinal));
        Assert.True(offered,
            $"Program.cs no longer reads the {OptIn} environment variable, so the Linux capture "
            + "harness is asking for an opt-in that nothing honours and every capture it takes will "
            + "be refused as vacuous with no explanation of why.");
    }

    /// <summary>Lines that are behaviour rather than prose, for a file whose comments start with <paramref name="commentPrefix"/>.</summary>
    private static IEnumerable<string> Code(string path, string commentPrefix) =>
        File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith(commentPrefix, StringComparison.Ordinal));

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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the Linux capture guard refuses to skip");
    }
}
