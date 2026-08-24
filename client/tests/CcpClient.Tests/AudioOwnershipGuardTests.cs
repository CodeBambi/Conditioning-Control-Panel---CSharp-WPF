using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The audio device has exactly two owners in this process, and a window is not allowed to become
/// a third.
///
/// <para><b>Why this guard exists, and why it is LEXICAL.</b> The app-wide audio lift moved
/// <c>SoundArbitration</c> and its backend out of <c>Features/Dtrh/DtrhHostWindow.axaml.cs</c>,
/// which used to build both per window, and into <c>Audio/AudioParticipant.cs</c> at app lifetime.
/// The lane that did it said plainly what its own facts could not cover: the DTRH window's
/// CONSUMPTION is verified by reading rather than by a fact, because exercising it needs a real
/// host, <c>InitializeComponent()</c> and an <c>Opened</c> handler that boots a device — so a
/// window-local <c>new SoundFlowAudioBackend(...)</c> could come back and no behavioural fact would
/// notice. That is exactly the shape a source guard is for, and it is the same answer
/// <see cref="DiagnosticFooterGuardTests"/> and the path-portability rules give when a behavioural
/// inversion is not available.</para>
///
/// <para><b>It pins the count, not the absence.</b> Two constructions are legitimate and both are
/// named here: the app-wide arbitration in <c>AudioParticipant</c>, and
/// <c>AudioPresenceFactory</c>'s presence, which exists for a different job — it earns
/// <c>Available</c> from a WASAPI read-back, which the arbitration does not implement. Banning the
/// type outright would be wrong; letting a third appear silently is what this catches.</para>
///
/// <para><b>What it does NOT prove.</b> Nothing about the device. It reads source text, so it
/// cannot see a backend reached through a variable, a factory or reflection, and it says nothing
/// about whether Windows ever opened an endpoint. It closes one named regression route, and that
/// is the whole claim.</para>
/// </summary>
public class AudioOwnershipGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] SourceParts = ["client", "src", "CcpClient.Desktop"];

    /// <summary>The two files allowed to construct the backend, and why each one is.</summary>
    private static readonly (string File, string Why)[] Sanctioned =
    [
        (Path.Combine("Audio", "AudioParticipant.cs"),
            "the app-wide owner: one arbitration, one device, at app lifetime"),
        (Path.Combine("Audio", "AudioPresenceFactory.cs"),
            "the capability presence, which earns Available from a WASAPI read-back the arbitration does not implement"),
    ];

    [Fact]
    public void OnlyTheAudioOwnerAndTheCapabilityPresence_ConstructTheAudioBackend_NoWindowMayOwnOne()
    {
        var root = FindRepoRoot();
        var sourceRoot = Path.Combine([root, .. SourceParts]);

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            if (Sanctioned.Any(s => string.Equals(s.File, relative, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (File.ReadAllText(file).Contains("new SoundFlowAudioBackend", StringComparison.Ordinal))
            {
                offenders.Add(relative);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "a file outside the two sanctioned owners constructs the audio backend: "
            + string.Join(", ", offenders)
            + ". The app-wide lift exists so a WINDOW cannot own a device — DtrhHostWindow used to "
            + "build one per window, and the whole point of AudioParticipant is that it no longer "
            + "does. If a third owner is genuinely needed, add it to this guard's list WITH its "
            + "reason, so the next reader learns why three is correct.");

        // And the sanctioned two must still be the owners: a guard that passes because BOTH sites
        // were deleted would be reporting the wrong thing entirely.
        foreach (var (relative, why) in Sanctioned)
        {
            var text = File.ReadAllText(Path.Combine(sourceRoot, relative));
            Assert.True(
                text.Contains("new SoundFlowAudioBackend", StringComparison.Ordinal),
                $"{relative} no longer constructs the audio backend, and it is supposed to be {why}. "
                + "This guard counts owners; if it passes because there are none, it is measuring "
                + "nothing.");
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

        throw new InvalidOperationException("repository root not found from " + AppContext.BaseDirectory);
    }
}
