using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The shell must not render its diagnostic footer to a user in a shipped build.
///
/// <para><b>Why this is a SOURCE guard and not a behavioural fact.</b> The footer's visibility is
/// decided by <c>DiagnosticFooterPolicy.Rendered</c>, which is <c>#if DEBUG</c>. Every test run in
/// this repository IS a Debug build, so a behavioural assertion here could only ever observe the
/// visible arm — it could never fail against the defect it exists to prevent. That is the same
/// condition <c>PathPortabilityGuardTests</c> is in, and it gets the same answer: assert on the
/// source, where the gate either exists or does not.</para>
///
/// <para><b>What it is protecting against, measured rather than imagined.</b> The footer rendered
/// unconditionally on every page and shipped that way. It was found by taking a headed capture and
/// reading the picture — the window carried <c>route: companion</c> and <c>layout-probe: door
/// studio 174.9x44.0 DIP @ scale 1.75 @ screen 121,223</c> across the bottom. No fact in this
/// suite caught it in all that time, because every one of them asserts on the PROBE rather than on
/// whether somebody should be looking at it.</para>
///
/// <para><b>It deliberately does NOT ban the probe.</b> The channel is content-free, UIA-readable
/// on Windows, and logged once on first layout so it is stderr-readable on Linux — which is the
/// only way this port has ever read its rail geometry on a platform where every screen capture
/// comes back black. A guard that removed the probe would take the Linux leg's evidence with it.
/// So this pins the GATE, not the absence of the channel.</para>
/// </summary>
public class DiagnosticFooterGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] PolicyParts =
        ["client", "src", "CcpClient.Desktop", "Views", "DiagnosticFooterPolicy.cs"];
    private static readonly string[] ShellCodeParts =
        ["client", "src", "CcpClient.Desktop", "Views", "MainWindow.axaml.cs"];
    private static readonly string[] ShellMarkupParts =
        ["client", "src", "CcpClient.Desktop", "Views", "MainWindow.axaml"];

    [Fact]
    public void TheShellsDiagnosticFooter_IsGatedOutOfAShippedBuild_AndTheProbeChannelSurvives()
    {
        var root = FindRepoRoot();
        var policy = File.ReadAllText(Path.Combine([root, .. PolicyParts]));
        var shell = File.ReadAllText(Path.Combine([root, .. ShellCodeParts]));
        var markup = File.ReadAllText(Path.Combine([root, .. ShellMarkupParts]));

        // 1. The policy is a real conditional, not a constant somebody flipped to true.
        Assert.True(
            policy.Contains("#if DEBUG", StringComparison.Ordinal)
            && policy.Contains("#else", StringComparison.Ordinal),
            "DiagnosticFooterPolicy no longer carries an #if DEBUG / #else pair, so 'Rendered' is now the same "
            + "answer in every configuration. The footer is instrumentation; a shipped build must not draw it.");

        // 2. The shell asks the policy. A literal here would be the defect returning with a name on it.
        Assert.True(
            shell.Contains("DiagnosticFooter.IsVisible = DiagnosticFooterPolicy.Rendered;", StringComparison.Ordinal),
            "MainWindow no longer sets DiagnosticFooter.IsVisible from DiagnosticFooterPolicy.Rendered. It rendered "
            + "unconditionally once and shipped that way; the gate is what stops that recurring, and assigning a "
            + "literal would pass every behavioural test in this suite while restoring the defect exactly.");

        // 3. The markup must not re-assert visibility and win the gate back.
        Assert.DoesNotContain("x:Name=\"DiagnosticFooter\" DockPanel.Dock=\"Bottom\" Classes=\"footer\" IsVisible",
            markup, StringComparison.Ordinal);

        // 4. THE CHANNEL ITSELF MUST SURVIVE. This is the half that stops the guard being "obeyed" by
        //    deleting the probe: the Linux leg has no other evidence of the rail's geometry, because
        //    every screen capture on that platform comes back black.
        Assert.True(
            shell.Contains("layout-probe: door", StringComparison.Ordinal),
            "the layout probe line is gone from the shell. It is the ONLY reading this port has of its rail "
            + "geometry on Linux, where every capture is blank — gating the footer must never mean deleting the "
            + "channel.");
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
