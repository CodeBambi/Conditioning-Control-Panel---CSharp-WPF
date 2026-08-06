using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// v6.6.4's installer deletes the bundled media and the packs bring it back, so the "has this mod
/// got its media?" probe decides whether a user gets their voice back. The old answer was
/// ".Any() over *.mp3", which ONE file surviving a half-merged install satisfied forever. These
/// pin the counting floor that replaced it.
/// </summary>
public class ContentMediaFloorTests
{
    private static IEnumerable<string> Media(int count, string ext = ".mp3")
        => Enumerable.Range(0, count).Select(i => $@"C:\content\Resources\sounds\clip{i}{ext}");

    // ---- what counts ----

    [Theory]
    [InlineData(".mp3")]
    [InlineData(".wav")]
    [InlineData(".ogg")]
    [InlineData(".m4a")]
    [InlineData(".png")]   // portraits break the mod just as visibly as silence does
    [InlineData(".PNG")]   // NTFS is case-insensitive; the probe must be too
    public void MediaExtensionsCount(string ext)
        => Assert.Equal(3, ReleaseContentService.CountMediaFiles(Media(3, ext)));

    [Theory]
    [InlineData(".json")]  // bark_rules.json / mantras.json STAY in the installer — never payload
    [InlineData(".txt")]
    [InlineData(".dll")]
    [InlineData("")]
    public void NonMediaDoesNot(string ext)
        => Assert.Equal(0, ReleaseContentService.CountMediaFiles(Media(3, ext)));

    [Fact]
    public void NullAndEmptyEntriesAreSkippedRatherThanThrowing()
        => Assert.Equal(1, ReleaseContentService.CountMediaFiles(
            new string[] { null!, "", "   ", @"C:\a\voice.mp3" }));

    [Fact]
    public void NullSequenceCountsZero()
        => Assert.Equal(0, ReleaseContentService.CountMediaFiles(null));

    // ---- the floor itself ----

    [Fact]
    public void OneSurvivingFileIsNotAnInstall()
    {
        // The exact v6.6.4 wedge: partial debris must read as missing so the pack re-fetches.
        Assert.True(ReleaseContentService.CountMediaFiles(Media(1)) < ReleaseContentService.MinModMediaFiles);
        Assert.True(ReleaseContentService.CountMediaFiles(Media(12)) < ReleaseContentService.MinModMediaFiles);
    }

    [Fact]
    public void AHealthyTreeClearsTheFloorWithRoomToSpare()
    {
        // Real trees: 390+ files extracted per built-in mod, 1,400-2,000 loose companion audio.
        Assert.True(ReleaseContentService.CountMediaFiles(Media(390), ReleaseContentService.MinModMediaFiles)
                    >= ReleaseContentService.MinModMediaFiles);
        Assert.True(ReleaseContentService.MinModMediaFiles < 390);
    }

    // ---- the short-circuit ----

    [Fact]
    public void CountingStopsAtTheFloor()
        => Assert.Equal(ReleaseContentService.MinModMediaFiles,
            ReleaseContentService.CountMediaFiles(Media(5000), ReleaseContentService.MinModMediaFiles));

    [Fact]
    public void ShortCircuitNeverInflatesAnUnderCount()
    {
        // The log line quotes this number to support, so a capped count must still be EXACT
        // whenever it lands below the cap — which is the only case that gets logged.
        for (var n = 0; n < ReleaseContentService.MinModMediaFiles; n++)
            Assert.Equal(n, ReleaseContentService.CountMediaFiles(Media(n), ReleaseContentService.MinModMediaFiles));
    }

    [Fact]
    public void NonPositiveStopAtCountsNothing()
    {
        // The two-shape probe passes `floor - count` as the remaining budget; a zero budget must
        // not walk thousands of paths for an answer it already has.
        Assert.Equal(0, ReleaseContentService.CountMediaFiles(Media(100), 0));
        Assert.Equal(0, ReleaseContentService.CountMediaFiles(Media(100), -1));
    }
}
