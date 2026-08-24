using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// THE LINUX HARNESS MUST TAKE ITS RECTS FROM THE PROBE'S <c>@ window</c> FIELD, AND THE SHELL
/// MUST PUBLISH ONE. Both halves, together, because either alone is a rect aimed at nothing.
///
/// <para><b>Why this is a source guard and not a behavioural fact</b> — the same answer
/// <see cref="DiagnosticFooterGuardTests"/> and <c>PathPortabilityGuardTests</c> give, for the
/// same reason: the defect is INVISIBLE from the machine the tests run on. What makes
/// <c>@ screen</c> unusable on X11 is that its coordinate space MOVES during startup, and neither
/// Windows nor Avalonia's headless platform reproduces that. Measured on WSLg, three successive
/// readings of the same rail door in ONE run at
/// <c>AVALONIA_GLOBAL_SCALE_FACTOR=1.75</c>:</para>
///
/// <list type="number">
/// <item><description><c>175.0x44.0 DIP @ scale 1 @ screen 12,45</c> — before the scale factor
/// lands.</description></item>
/// <item><description><c>174.9x44.0 DIP @ scale 1.75 @ screen 21,79</c> — scale landed, Avalonia
/// still believes the window sits at 0,0, so this reads window-relative.</description></item>
/// <item><description><c>174.9x44.0 DIP @ scale 1.75 @ screen 37,116</c> — the window manager's
/// placement landed (root 16,37 plus 21,79), so this reads true root.</description></item>
/// </list>
///
/// <para><b>What that cost.</b> Both of the Linux harness's consumers want WINDOW-relative device
/// pixels — <c>xgetimage.py --crop</c> takes them directly and <c>xinput.py --click</c> adds the
/// window's root origin itself — so a script reading <c>@ screen</c> is correct only while the
/// middle reading happens to be the last one on stderr. Every scale-1 crop this harness ever took
/// landed correctly on exactly that coincidence. A crop off by the frame offset does not fail
/// loudly: it photographs plausible pixels of the wrong thing, which is how a rail-door check
/// scored 0.926 off pixels that were not a border and a click landed on the wrong door and
/// photographed the wrong page while scoring 0.982.</para>
///
/// <para><b>Both halves are pinned because a one-sided edit is silent.</b> Dropping <c>@ window</c>
/// from the shell makes <c>door_rect</c>'s pattern stop matching and the harness fails naming the
/// door — loud, recoverable. Switching the harness back to <c>@ screen</c> while the shell still
/// publishes both is the dangerous direction and has no symptom at all.</para>
/// </summary>
public class LayoutProbeWindowSpaceGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] ShellParts =
        ["client", "src", "CcpClient.Desktop", "Views", "MainWindow.axaml.cs"];
    private static readonly string[] HarnessParts = ["client", "tools", "verify", "capture-wslg.sh"];

    [Fact]
    public void TheLayoutProbePublishesWindowRelativePixels_AndTheLinuxHarnessIsWhatReadsThem()
    {
        var root = FindRepoRoot();
        var shell = File.ReadAllText(Path.Combine([root, .. ShellParts]));
        var harness = File.ReadAllText(Path.Combine([root, .. HarnessParts]));

        // 1. The shell publishes the door's origin in the window's own client space.
        Assert.Contains("@ window {inWindow.X},{inWindow.Y}", shell, StringComparison.Ordinal);

        // 2. It is a SUBTRACTION against the window's own PointToScreen, not a second, independent
        //    derivation. Two platform calls answering in two different spaces would put the door
        //    somewhere neither of them means.
        Assert.Contains("var inWindow = topLeft - clientOrigin;", shell, StringComparison.Ordinal);

        // 3. `@ screen` survives: it is what capture.ps1 aims SetCursorPos and CopyFromScreen at
        //    on Windows, where it has always meant one thing.
        Assert.Contains("@ screen {topLeft.X},{topLeft.Y}", shell, StringComparison.Ordinal);

        // 4. And the Linux harness derives its crop and its click from `@ window`. This is the half
        //    with no symptom when it regresses.
        Assert.Contains("@ window (-?[0-9]+),(-?[0-9]+)", harness, StringComparison.Ordinal);
        Assert.Contains("DOOR_X=${BASH_REMATCH[6]}", harness, StringComparison.Ordinal);
        Assert.Contains("DOOR_Y=${BASH_REMATCH[7]}", harness, StringComparison.Ordinal);
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

        throw new InvalidOperationException("repository root not found from " + AppContext.BaseDirectory);
    }
}
