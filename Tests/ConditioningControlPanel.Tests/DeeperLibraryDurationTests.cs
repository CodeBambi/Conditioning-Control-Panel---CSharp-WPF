using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A Discord user asked to see how long a video is from the Deeper library list, before
/// opening it. The cheapest reliable source is the enhancement file itself: the editor
/// writes <c>metadata.media_duration</c> on save, and the library reads it at index time.
/// Files from older builds carry no such field and must index as "unknown" (0, never
/// 0:00), leaving the hub's background probe to fill local media in later.
///
/// <para>Pinned here: the readout format the row shares with the mini timeline, the
/// file-carried read on both sides of the serializer, the "unknown stays unknown" rule for
/// remote and missing media, and the probe gate that keeps hypnotube / bambicloud rows off
/// the probe path.</para>
/// </summary>
public class DeeperLibraryDurationTests
{
    // =====================================================================================
    //  format
    // =====================================================================================

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(61.4, "1:01")]
    [InlineData(272.9, "4:32")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(7325, "2:02:05")]
    [InlineData(-5, "0:00")]
    [InlineData(double.NaN, "0:00")]
    public void Format_matches_the_mini_timeline_readout(double seconds, string expected)
    {
        Assert.Equal(expected, MediaDurationCache.Format(seconds));
    }

    // =====================================================================================
    //  file-carried duration
    // =====================================================================================

    private static string Fixture(string metadataExtra, string mediaSource = "https://hypnotube.com/video/abc") =>
        "{" +
        "\"$schema\":\"" + Enhancement.SchemaTag + "\"," +
        "\"version\":1," +
        "\"media_type\":\"video\"," +
        "\"media_source\":\"" + mediaSource.Replace("\\", "\\\\") + "\"," +
        "\"metadata\":{\"name\":\"Spiral\",\"creator\":\"Bambi\"" + metadataExtra + "}" +
        "}";

    [Fact]
    public void Load_reads_media_duration_from_the_file()
    {
        var enh = EnhancementSerializer.Load(Fixture(",\"media_duration\":272.5"));

        Assert.Equal(272.5, enh.Metadata.MediaDurationSeconds);
    }

    [Fact]
    public void Load_leaves_an_older_file_without_a_duration()
    {
        var enh = EnhancementSerializer.Load(Fixture(""));

        Assert.Null(enh.Metadata.MediaDurationSeconds);
    }

    [Fact]
    public void Save_writes_media_duration_only_when_known()
    {
        var known = EnhancementSerializer.Load(Fixture(",\"media_duration\":272.5"));
        var unknown = EnhancementSerializer.Load(Fixture(""));

        Assert.Contains("\"media_duration\": 272.5", EnhancementSerializer.Save(known));
        Assert.DoesNotContain("media_duration", EnhancementSerializer.Save(unknown));
    }

    [Fact]
    public void Save_round_trips_the_duration_through_an_older_file_shape()
    {
        var original = EnhancementSerializer.Load(Fixture(",\"media_duration\":90"));
        var again = EnhancementSerializer.Load(EnhancementSerializer.Save(original));

        Assert.Equal(90, again.Metadata.MediaDurationSeconds);
    }

    // =====================================================================================
    //  library entry projection
    // =====================================================================================

    [Fact]
    public void BuildEntry_takes_the_duration_the_file_carries()
    {
        var enh = EnhancementSerializer.Load(Fixture(",\"media_duration\":272.5"));

        var entry = EnhancementLibrary.BuildEntry(enh, @"C:\lib\spiral.ccpenh.json", new DateTime(2026, 9, 1));

        Assert.Equal(272.5, entry.DurationSeconds);
        Assert.Equal("Spiral", entry.Name);
        Assert.Equal("Bambi", entry.Creator);
        Assert.Equal(new DateTime(2026, 9, 1), entry.LastModified);
    }

    [Fact]
    public void BuildEntry_leaves_a_remote_source_unknown_when_the_file_is_silent()
    {
        var enh = EnhancementSerializer.Load(Fixture(""));

        var entry = EnhancementLibrary.BuildEntry(enh, @"C:\lib\spiral.ccpenh.json", DateTime.Now);

        Assert.Equal(0, entry.DurationSeconds);
    }

    [Fact]
    public void BuildEntry_leaves_a_missing_local_file_unknown()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ccp-no-such-" + Guid.NewGuid().ToString("N") + ".mp4");
        var enh = EnhancementSerializer.Load(Fixture("", missing));

        var entry = EnhancementLibrary.BuildEntry(enh, @"C:\lib\spiral.ccpenh.json", DateTime.Now);

        Assert.Equal(0, entry.DurationSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(double.NaN)]
    public void BuildEntry_treats_a_non_positive_file_value_as_unknown(double bad)
    {
        var enh = EnhancementSerializer.Load(Fixture(""));
        enh.Metadata.MediaDurationSeconds = bad;

        var entry = EnhancementLibrary.BuildEntry(enh, @"C:\lib\spiral.ccpenh.json", DateTime.Now);

        Assert.Equal(0, entry.DurationSeconds);
    }

    // =====================================================================================
    //  probe gate + cache key
    // =====================================================================================

    [Theory]
    [InlineData("https://hypnotube.com/video/abc")]
    [InlineData("http://bambicloud.com/x.mp4")]
    [InlineData("*")]
    [InlineData("spiral*")]
    [InlineData("")]
    [InlineData(null)]
    public void IsProbeable_rejects_remote_patterns_and_blanks(string? source)
    {
        Assert.False(MediaDurationCache.IsProbeable(source));
    }

    [Fact]
    public void IsProbeable_accepts_only_a_local_file_that_exists()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-dur-" + Guid.NewGuid().ToString("N") + ".mp4");
        Assert.False(MediaDurationCache.IsProbeable(path));
        File.WriteAllBytes(path, new byte[] { 0 });
        try
        {
            Assert.True(MediaDurationCache.IsProbeable(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void KeyFor_folds_case_and_pins_size_and_write_time()
    {
        var a = MediaDurationCache.KeyFor(@"C:\Media\Spiral.MP4", 1234, 5678);
        var b = MediaDurationCache.KeyFor(@"c:\media\spiral.mp4", 1234, 5678);
        var resized = MediaDurationCache.KeyFor(@"c:\media\spiral.mp4", 1235, 5678);
        var rewritten = MediaDurationCache.KeyFor(@"c:\media\spiral.mp4", 1234, 5679);

        Assert.Equal(a, b);
        Assert.NotEqual(a, resized);
        Assert.NotEqual(a, rewritten);
    }

    [Fact]
    public void ParseStore_keeps_positive_values_and_drops_the_rest()
    {
        var store = MediaDurationCache.ParseStore("{\"a|1|1\":12.5,\"b|1|1\":0,\"c|1|1\":-4,\"\":9}");

        Assert.Equal(new Dictionary<string, double> { ["a|1|1"] = 12.5 }, store);
    }

    [Fact]
    public void ParseStore_of_an_empty_document_is_an_empty_store()
    {
        Assert.Empty(MediaDurationCache.ParseStore("null"));
        Assert.Empty(MediaDurationCache.ParseStore("{}"));
    }
}
