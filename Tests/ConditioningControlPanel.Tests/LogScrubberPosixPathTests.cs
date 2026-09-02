using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The POSIX/macOS home-path rule in <see cref="LogScrubber"/>. A crash report filed from a
/// non-Windows head used to carry "/home/&lt;name&gt;/..." through verbatim, so the rule was added
/// alongside the Windows one — but a home-path regex is exactly the kind that over- and
/// under-matches quietly, and a scrubber that misses one name in a report is a privacy bug that
/// nobody notices until the report is already attached to an issue.
///
/// <para>These pin the three boundary behaviours the rule stands or falls on:</para>
/// <list type="number">
/// <item>the delimiter before a path is matched zero-width, so two paths separated by a single
/// comma both redact instead of the first eating the second's boundary;</item>
/// <item>trailing punctuation is not part of a username, so "/home/root," is still recognised
/// as the deliberately preserved <c>root</c>;</item>
/// <item>the match is case-sensitive, so a lowercase HTTP route ("GET /users/alice") is left
/// alone rather than redacted and counted as a home directory.</item>
/// </list>
/// </summary>
public class LogScrubberPosixPathTests
{
    [Fact]
    public void PosixAndMacHomePaths_AreRedacted()
    {
        var (linux, linuxCounts) = LogScrubber.Scrub("crash while reading /home/alice/app/log.txt");
        Assert.Equal("crash while reading /home/<redacted>/app/log.txt", linux);
        Assert.Equal(1, linuxCounts.Paths);

        var (mac, macCounts) = LogScrubber.Scrub("crash while reading /Users/alice/Library/Logs/x.log");
        Assert.Equal("crash while reading /Users/<redacted>/Library/Logs/x.log", mac);
        Assert.Equal(1, macCounts.Paths);
    }

    [Fact]
    public void Root_IsPreserved_EvenBeforeTrailingPunctuation()
    {
        var (slash, slashCounts) = LogScrubber.Scrub("running from /home/root/app");
        Assert.Equal("running from /home/root/app", slash);
        Assert.Equal(0, slashCounts.Paths);

        // The comma is not part of the name, so what precedes it is still plain "root".
        var (comma, commaCounts) = LogScrubber.Scrub("ls /home/root, ok");
        Assert.Equal("ls /home/root, ok", comma);
        Assert.Equal(0, commaCounts.Paths);

        // ...but a name that merely starts with "root" is a real user and is redacted.
        var (rooter, rooterCounts) = LogScrubber.Scrub("home is /home/rooter/app");
        Assert.Equal("home is /home/<redacted>/app", rooter);
        Assert.Equal(1, rooterCounts.Paths);
    }

    [Fact]
    public void ConsecutiveCommaSeparatedPaths_BothRedact()
    {
        var (scrubbed, counts) = LogScrubber.Scrub("scanned /home/alice,/home/bob");

        Assert.Equal("scanned /home/<redacted>,/home/<redacted>", scrubbed);
        Assert.Equal(2, counts.Paths);
    }

    [Fact]
    public void LowercaseUsersRoute_IsNotAHomeDirectory()
    {
        var (route, routeCounts) = LogScrubber.Scrub("GET /users/alice HTTP/1.1");
        Assert.Equal("GET /users/alice HTTP/1.1", route);
        Assert.Equal(0, routeCounts.Paths);

        var (url, urlCounts) = LogScrubber.Scrub("fetching https://cclabs.app/users/alice/profile");
        Assert.Equal("fetching https://cclabs.app/users/alice/profile", url);
        Assert.Equal(0, urlCounts.Paths);
    }

    [Fact]
    public void WindowsPaths_AreUnaffectedByThePosixRule()
    {
        var (scrubbed, counts) = LogScrubber.Scrub(@"loading C:\Users\bob\AppData\Local\ConditioningControlPanel");

        Assert.Equal(@"loading C:\Users\<redacted>\AppData\Local\ConditioningControlPanel", scrubbed);
        // Redacted exactly once — the POSIX rule must not double-count the Windows shape.
        Assert.Equal(1, counts.Paths);
    }

    [Fact]
    public void MixedHeads_CountEveryRedactionOnce()
    {
        var (scrubbed, counts) = LogScrubber.Scrub(
            @"C:\Users\bob\logs and /home/alice/logs and /Users/carol/logs and /home/root/logs");

        Assert.Equal(
            @"C:\Users\<redacted>\logs and /home/<redacted>/logs and /Users/<redacted>/logs and /home/root/logs",
            scrubbed);
        Assert.Equal(3, counts.Paths);
    }
}
