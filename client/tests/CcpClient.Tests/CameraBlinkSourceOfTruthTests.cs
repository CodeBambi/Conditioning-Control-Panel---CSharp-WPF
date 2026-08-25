using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>UPSTREAM'S MODEL README CONTRADICTS UPSTREAM'S BLINK CODE, AND THE CODE WINS.</b> This guard
/// exists so the lane that eventually ports blink detection reads the right numbers off the right
/// file, instead of rediscovering the contradiction against a webcam.
///
/// <para><b>The two accounts.</b> <c>ConditioningControlPanel/Resources/Models/README.md:53-57</c>
/// describes blink as a 6-point EAR over <b>FaceMesh's</b> eyelid landmarks, firing on a
/// <b>50–400 ms</b> closed window with a <b>700 ms</b> cooldown. The code computes EAR over the
/// <b>iris model's</b> 71-point eye contour, with a <b>60–1500 ms</b> window and a <b>500 ms</b>
/// cooldown (<c>Services/Webcam/WebcamTrackingService.cs:181-193</c>). The code also records WHY it
/// moved: FaceMesh's eyelid landmarks <i>"barely move during mid-closed eyelids ... so EAR drops only
/// ~10% during real blinks — too little to reliably trigger threshold"</i>
/// (<c>Services/Webcam/WebcamTrackingService.cs:195-200</c>). The README describes the design that
/// was measured and abandoned.</para>
///
/// <para><b>Why this is a test and not a paragraph.</b> The port's standing rule is that code wins,
/// but a rule only helps somebody who already knows there is a conflict — and this one is invisible
/// from either file alone, because each is internally consistent and neither mentions the other. A
/// lane porting from the README would ship a window less than a third as wide, reject every
/// deliberate blink the calibration prompt asks users for
/// (<c>Services/Webcam/WebcamTrackingService.cs:175-180</c>: <i>"blink slowly and deliberately"</i>,
/// which is why 1.5 s is accepted as one blink), and have nothing in the failure to point at.</para>
///
/// <para><b>What this proves and what it does not.</b> It proves the two upstream files still say
/// what this port believes they say, so the trap is still live and the numbers below are still the
/// ones to port. It proves NOTHING about blink detection in this client: there is no gaze engine
/// here, no blink detector, no EAR, no landmark and no frame consumer
/// (<c>Camera/ICameraCaptureSource.cs</c> hands back a <c>bool</c>, never a pixel). When the gaze
/// slice lands, this guard's job is to be read once and then replaced by facts about real
/// code.</para>
///
/// <para><b>It reads the shipping WPF tree as read-only evidence</b>, the way
/// <c>EntitlementPrivacyTests</c> and <c>GradedRunAwardsTests</c> do. Nothing here writes to that
/// tree.</para>
/// </summary>
public sealed class CameraBlinkSourceOfTruthTests
{
    /// <summary>The numbers a blink port must use, because the CODE uses them. Named here so that a
    /// future lane's grep for "blink" under <c>client/</c> lands on the resolution rather than on the
    /// contradiction.</summary>
    private const int MinBlinkClosedMs = 60;

    private const int MaxBlinkClosedMs = 1500;

    private const int BlinkCooldownMs = 500;

    [Fact]
    public async Task TheModelREADMEAndTheBlinkCODEDisagree_AndTheCODEIsWhatAPortMustFollow()
    {
        var code = await ReadUpstreamAsync("Services", "Webcam", "WebcamTrackingService.cs");
        var readme = await ReadUpstreamAsync("Resources", "Models", "README.md");

        // ── The CODE's numbers. These are the ones to port. ────────────────────────────────────
        Assert.Contains($"MinBlinkClosedMs = {MinBlinkClosedMs};", code, StringComparison.Ordinal);
        Assert.Contains($"MaxBlinkClosedMs = {MaxBlinkClosedMs};", code, StringComparison.Ordinal);
        Assert.Contains($"BlinkCooldownMs = {BlinkCooldownMs};", code, StringComparison.Ordinal);

        // ── And the MODEL the code measures them over: the iris contour, not FaceMesh. ─────────
        Assert.Contains("EAR is computed against the IRIS MODEL's 71-point eye contour, NOT", code, StringComparison.Ordinal);
        Assert.Contains("FaceMesh's eyelid landmarks", code, StringComparison.Ordinal);

        // ── The README still tells the other story, so the trap is still live. ─────────────────
        // If any of these three reds, upstream has REPAIRED its README and this guard has done its
        // job: delete it, and record that the two accounts finally agree.
        Assert.Contains("on FaceMesh's eyelid landmarks", readme, StringComparison.Ordinal);
        Assert.Contains("50–400 ms closed window and 700 ms cooldown", readme, StringComparison.Ordinal);

        // ── Which makes the disagreement a fact rather than a reading. ─────────────────────────
        Assert.DoesNotContain($"{MinBlinkClosedMs}–{MaxBlinkClosedMs} ms", readme, StringComparison.Ordinal);
        Assert.DoesNotContain($"{BlinkCooldownMs} ms cooldown", readme, StringComparison.Ordinal);
    }

    /// <summary>Read a file from the shipping WPF tree. It THROWS when the tree is not where it
    /// should be, rather than skipping: a guard that quietly goes vacuous is worse than no
    /// guard.</summary>
    private static Task<string> ReadUpstreamAsync(params string[] parts) =>
        File.ReadAllTextAsync(
            Path.Combine([FindRepoRoot(), "ConditioningControlPanel", .. parts]),
            TestContext.Current.CancellationToken);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "client", "CcpClient.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found walking up from {AppContext.BaseDirectory} — the blink source-of-truth guard "
            + "refuses to skip");
    }
}
