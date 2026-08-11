using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A merge that runs the drive out of space part-way is one of the two root causes behind the
/// failed pack installs that stranded v6.6.4 users — and the one that leaves half a payload behind.
/// These pin the arithmetic of the gate that now refuses the install before the download starts.
/// </summary>
public class ContentFreeSpaceTests
{
    [Fact]
    public void ThePackIsBudgetedThreeTimesOver()
    {
        // .partial zip + extract staging + merged copies all coexist at peak.
        Assert.Equal(300, ReleaseContentService.RequiredFreeBytes(100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnUnsizedManifestEntryIsNotJudged(long sizeBytes)
    {
        // No size to reason about means no grounds to block: proceed and let the IO speak.
        Assert.Equal(0, ReleaseContentService.RequiredFreeBytes(sizeBytes));
        Assert.True(ReleaseContentService.HasEnoughFreeSpace(sizeBytes, 0));
    }

    [Theory]
    [InlineData(100L, 299L, false)]
    [InlineData(100L, 300L, true)]   // exactly enough is enough
    [InlineData(100L, 301L, true)]
    [InlineData(380L * 1024 * 1024, 1024L * 1024 * 1024, false)]  // a 380 MB pack needs 1.14 GB
    [InlineData(380L * 1024 * 1024, 4L * 1024 * 1024 * 1024, true)]
    public void SpaceIsCheckedAgainstThePeak(long sizeBytes, long available, bool expected)
        => Assert.Equal(expected, ReleaseContentService.HasEnoughFreeSpace(sizeBytes, available));

    [Fact]
    public void AnAbsurdManifestSizeSaturatesInsteadOfWrappingNegative()
    {
        // A tampered/garbled size must not overflow into "plenty of room".
        Assert.Equal(long.MaxValue, ReleaseContentService.RequiredFreeBytes(long.MaxValue));
        Assert.False(ReleaseContentService.HasEnoughFreeSpace(long.MaxValue, long.MaxValue - 1));
    }
}
