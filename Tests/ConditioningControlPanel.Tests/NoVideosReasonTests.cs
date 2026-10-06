using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1352: the length filter excluded all 14 enabled videos and the dialog said "add files".
/// </summary>
public class NoVideosReasonTests
{
    [Theory]
    [InlineData(14, 0, true)]   // the report: every enabled video filtered out by length
    [InlineData(0, 0, false)]   // truly empty: keep the "add files" text
    [InlineData(14, 3, false)]  // the filter left some, the pool is not its fault
    [InlineData(-1, -1, false)] // the funnel has not run yet
    public void Blames_the_length_filter_only_when_it_emptied_the_pool(int enabled, int afterDuration, bool expected)
        => Assert.Equal(expected, NoVideosReason.LengthFilterEmptied(enabled, afterDuration));

    [Theory]
    [InlineData(52, 170, "52s - 2m 50s")]
    [InlineData(0, 120, "0s - 2m")]
    [InlineData(45, 0, "45s - ∞")]
    public void Range_reads_like_the_panel_sliders(int min, int max, string expected)
        => Assert.Equal(expected, NoVideosReason.FormatRange(min, max));
}
