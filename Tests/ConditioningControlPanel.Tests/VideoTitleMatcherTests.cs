using System;
using ConditioningControlPanel.Services.Commands;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1330: an AI-named video plays the closest local file by name, and an
/// unrelated name plays nothing (the #1325 rule).</summary>
public class VideoTitleMatcherTests
{
    private static readonly string[] Library =
    {
        @"C:\assets\videos\Bambi Bae.mp4",
        @"C:\assets\videos\loops\dumb_bimbo_brainwash.webm",
        @"C:\assets\videos\Bimbo Servitude Brainwash.mkv",
        @"C:\assets\videos\day-1.mp4",
        @"C:\assets\videos\day-2.mp4",
        @"C:\assets\videos\Bambi Uniform Bliss 1080p.mp4",
        @"C:\assets\videos\overload-46422.mp4",
    };

    [Theory]
    [InlineData("Bambi Bae", @"C:\assets\videos\Bambi Bae.mp4")]
    [InlineData("Dumb Bimbo Brainwash", @"C:\assets\videos\loops\dumb_bimbo_brainwash.webm")]
    [InlineData("Day 2", @"C:\assets\videos\day-2.mp4")]
    public void An_exact_title_matches(string query, string expected)
        => Assert.Equal(expected, VideoTitleMatcher.FindBest(query, Library));

    [Theory]
    [InlineData("bambi-bae.mp4")]
    [InlineData("videos/BAMBI_BAE.mp4")]
    [InlineData("Bambi... bae!")]
    [InlineData("BambiBae")]
    [InlineData("https://hypnotube.com/video/bambi-bae-113979.html")]
    public void Case_punctuation_and_separators_do_not_matter(string query)
        => Assert.Equal(@"C:\assets\videos\Bambi Bae.mp4", VideoTitleMatcher.FindBest(query, Library));

    [Fact]
    public void Quality_tags_and_ids_in_a_file_name_are_ignored()
    {
        Assert.Equal(@"C:\assets\videos\Bambi Uniform Bliss 1080p.mp4", VideoTitleMatcher.FindBest("Bambi Uniform Bliss", Library));
        Assert.Equal(@"C:\assets\videos\overload-46422.mp4", VideoTitleMatcher.FindBest("Overload", Library));
    }

    [Fact]
    public void A_partial_title_matches()
    {
        var lib = new[] { @"C:\v\Bambi Bae Full Version.mp4", @"C:\v\Something Else.mp4" };
        Assert.Equal(@"C:\v\Bambi Bae Full Version.mp4", VideoTitleMatcher.FindBest("Bambi Bae", lib));
        // The AI's title runs longer than the file name.
        Assert.Equal(@"C:\v\Bambi Bae Full Version.mp4", VideoTitleMatcher.FindBest("Bambi Bae Full Version by Somebody", lib));
    }

    [Fact]
    public void Reordered_words_and_a_one_letter_slip_still_match()
    {
        var lib = new[] { @"C:\v\Bambis Chastity Trainer.mp4" };
        Assert.Equal(lib[0], VideoTitleMatcher.FindBest("Chastity Trainer Bambi's", lib));
        Assert.Equal(lib[0], VideoTitleMatcher.FindBest("Bambi Chastity Trainer", lib));
    }

    [Theory]
    [InlineData("Bimbo Dreams Brainwash")]      // two words in common with two files
    [InlineData("Bambi Uniform Oblivion")]      // a different video in the same series
    [InlineData("Day 4")]                       // a different episode
    [InlineData("Naughty Bambi")]
    [InlineData("Mindlocked Cock Zombie")]
    [InlineData("Bambi Chastity Overload")]     // the short file name covers too little of it
    public void An_unrelated_name_matches_nothing(string query)
        => Assert.Null(VideoTitleMatcher.FindBest(query, Library));

    [Fact]
    public void A_generic_word_that_fits_two_files_is_ambiguous()
    {
        var lib = new[] { @"C:\v\Bambi Bae.mp4", @"C:\v\Bambi Slay.mp4" };
        Assert.Null(VideoTitleMatcher.FindBest("Bambi", lib));
    }

    [Fact]
    public void The_same_name_in_two_folders_is_not_ambiguous()
    {
        var lib = new[] { @"C:\v\a\Bambi Bae.mp4", @"C:\v\b\bambi_bae.webm" };
        Assert.Equal(lib[0], VideoTitleMatcher.FindBest("Bambi Bae", lib));
    }

    [Fact]
    public void An_empty_library_or_query_matches_nothing()
    {
        Assert.Null(VideoTitleMatcher.FindBest("Bambi Bae", Array.Empty<string>()));
        Assert.Null(VideoTitleMatcher.FindBest("", Library));
        Assert.Null(VideoTitleMatcher.FindBest("   ", Library));
        Assert.Null(VideoTitleMatcher.FindBest("...", Library));
        Assert.Null(VideoTitleMatcher.FindBest(new string?[] { null, "" }, Library));
    }

    [Fact]
    public void Path_is_tried_before_title()
    {
        Assert.Equal(@"C:\assets\videos\day-1.mp4",
            VideoTitleMatcher.FindBest(new[] { "day_1.mp4", "Bambi Bae" }, Library));
        // A path that matches nothing falls through to the title.
        Assert.Equal(@"C:\assets\videos\Bambi Bae.mp4",
            VideoTitleMatcher.FindBest(new[] { "made-up-file.mp4", "Bambi Bae" }, Library));
    }

    [Fact]
    public void Scores_sit_on_the_right_side_of_the_threshold()
    {
        Assert.Equal(1.0, VideoTitleMatcher.Score("Bambi Bae", "bambi_bae"), 3);
        Assert.True(VideoTitleMatcher.Score("Bambi Bae", "Bambi Bae Full Version") >= VideoTitleMatcher.Threshold);
        Assert.True(VideoTitleMatcher.Score("Dumb Bimbo Brainwash", "Bimbo Servitude Brainwash") < VideoTitleMatcher.Threshold);
        Assert.True(VideoTitleMatcher.Score("Bambi TikTok Good Girl Academy", "Bambi TikTok Good Girl Club") < VideoTitleMatcher.Threshold);
    }
}
