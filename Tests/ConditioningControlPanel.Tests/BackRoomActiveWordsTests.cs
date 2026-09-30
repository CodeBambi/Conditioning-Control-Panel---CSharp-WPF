using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The slot reels deal the player's own words. A Regex Awareness keyword is a pattern, and one
/// reached a reel as <c>\bORGASM\w*\b</c> (support ticket, 2026-09-22). Patterns stay out.
/// </summary>
public class BackRoomActiveWordsTests
{
    private static KeywordTrigger Trigger(string keyword, KeywordMatchType type, bool enabled = true) =>
        new() { Keyword = keyword, MatchType = type, Enabled = enabled };

    [Fact]
    public void RegexKeywords_NeverReachTheReels()
    {
        var words = BackRoomMedia.WordsFrom(
            new Dictionary<string, bool> { ["DROP"] = true, ["OFF"] = false },
            new[]
            {
                Trigger("good girl", KeywordMatchType.PlainText),
                Trigger(@"\bORGASM\w*\b", KeywordMatchType.Regex),
                Trigger(@"\bTEAS(E|ES|ED|ING)\b", KeywordMatchType.Regex),
                Trigger("sleepy", KeywordMatchType.PlainText, enabled: false),
            });

        Assert.Equal(new[] { "DROP", "good girl" }, words);
    }

    [Fact]
    public void NothingActive_IsEmpty()
    {
        Assert.Empty(BackRoomMedia.WordsFrom(null, null));
    }
}
