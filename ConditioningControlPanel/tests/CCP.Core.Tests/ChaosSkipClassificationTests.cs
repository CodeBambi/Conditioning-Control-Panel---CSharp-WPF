using ConditioningControlPanel.Core.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the WPF-parity boundary semantics of <see cref="ChaosSkipClassification.IsSkip"/>,
/// extracted from the inline DTRH watch-credit predicate (WPF DtrhHostService.cs:618-623):
/// a positive-duration clip watched under 90% is a skip; the 0.90 boundary is not; and a
/// non-positive duration is never a skip (Infinity/NaN division guarded away).
/// </summary>
public class ChaosSkipClassificationTests
{
    [Theory]
    // Under 90% of a positive-duration clip => skip.
    [InlineData(89.0, 100.0, true)]
    [InlineData(1.0, 100.0, true)]
    [InlineData(0.0, 100.0, true)]
    [InlineData(44.0, 50.0, true)]   // 0.88
    // The 0.90 boundary and above => NOT a skip (strict less-than).
    [InlineData(90.0, 100.0, false)]
    [InlineData(45.0, 50.0, false)]  // exactly 0.90
    [InlineData(99.0, 100.0, false)]
    [InlineData(100.0, 100.0, false)]
    // Over-watch => NOT a skip.
    [InlineData(200.0, 100.0, false)]
    // Non-positive duration guard => never a skip (no divide-by-zero / Infinity).
    [InlineData(50.0, 0.0, false)]
    [InlineData(0.0, 0.0, false)]
    [InlineData(50.0, -1.0, false)]
    public void IsSkip_MatchesWpfBoundarySemantics(double watchedSec, double durationSec, bool expected)
    {
        Assert.Equal(expected, ChaosSkipClassification.IsSkip(watchedSec, durationSec));
    }
}
