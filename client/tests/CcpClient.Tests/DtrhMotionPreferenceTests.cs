using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-053: the reduced-motion inheritance probe SEAM (DtrhMotionPreference). These tests
/// pin the measurement PATH — the verdict mapping and the exact query literal the page's
/// own probe uses (payload shared/capability.js:35) — NEVER the OS inheritance itself
/// (that is the headed probe run's evidence, not a unit fact).
/// </summary>
public class DtrhMotionPreferenceTests
{
    [Fact]
    public void ProbeQuery_MatchesThePagesOwnProbe()
    {
        // The measurement path is the page's EXACT query (capability.js:35) — a
        // paraphrased query would measure a different feature.
        Assert.Equal("(prefers-reduced-motion: reduce)", DtrhMotionPreference.ProbeQuery);
    }

    [Theory]
    // OS animations OFF + engine reduced → the engine inherited the removed motion.
    [InlineData(false, true, DtrhMotionPreference.MotionInheritanceVerdict.Holds)]
    // OS animations ON + engine not reduced → inheritance holds (nothing to remove).
    [InlineData(true, false, DtrhMotionPreference.MotionInheritanceVerdict.Holds)]
    // OS OFF + engine NOT reduced → the betrayal direction (reduced-motion user gets 3D).
    [InlineData(false, false, DtrhMotionPreference.MotionInheritanceVerdict.Fails)]
    // OS ON + engine reduced → mismatch, conservative-safe (the OS can only remove motion).
    [InlineData(true, true, DtrhMotionPreference.MotionInheritanceVerdict.Fails)]
    public void Evaluate_KnownSides_MapsOsStateToVerdict(
        bool osAnimation, bool engineReduced, DtrhMotionPreference.MotionInheritanceVerdict expected)
    {
        Assert.Equal(expected, DtrhMotionPreference.Evaluate(osAnimation, engineReduced));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(null, false)]
    [InlineData(true, null)]
    [InlineData(false, null)]
    [InlineData(null, null)]
    public void Evaluate_UnknownSide_NeverDefaults(bool? osAnimation, bool? engineReduced)
    {
        // GET failure / named limit / silent engine → Unknown, never a defaulted boolean
        // (consult correction 3).
        Assert.Equal(DtrhMotionPreference.MotionInheritanceVerdict.Unknown,
            DtrhMotionPreference.Evaluate(osAnimation, engineReduced));
    }

    [Fact]
    public void ReadOsClientAreaAnimation_OnThisBox_ReturnsTheOsStateOrNull()
    {
        // Host-side read smoke fact: on Windows the GET answers a real boolean; on
        // non-Windows the named limit is null. Never throws, never defaults.
        var read = DtrhMotionPreference.ReadOsClientAreaAnimation();
        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(read);
        }
        else
        {
            Assert.Null(read);
        }
    }
}
