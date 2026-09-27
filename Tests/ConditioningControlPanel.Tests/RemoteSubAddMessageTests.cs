using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Why adding a subreddit did not work, in one vocabulary.
///
/// <para>Support, 2026-09-19: "App won't let me add the category! Is the criteria for 'media'
/// pretty restrictive?" There is no criteria - the feed is Scrolller's, and Scrolller either
/// hosts a community or it does not. The old line ("r/X doesn't exist or has no media") fused
/// that with a transport failure and named neither, so the one refusal worth retrying and the one
/// that never will be read identically.</para>
/// </summary>
public class RemoteSubAddMessageTests
{
    // ---- classification ----------------------------------------------------------------

    /// <summary>
    /// The load-bearing pair. A null Error with Ok false is ScrolllerSource's real "not on
    /// scrolller" answer (it returns exactly that after asking twice); "offline" is the wire.
    /// Only the first is a verdict about the NAME, which is also why only the first is persisted.
    /// </summary>
    [Fact]
    public void ANullErrorIsAVerdictAndOfflineIsNot()
    {
        Assert.Equal(RemoteSubAddOutcome.NotCarried, RemoteSubAddMessages.Classify(false, null));
        Assert.Equal(RemoteSubAddOutcome.Unreachable, RemoteSubAddMessages.Classify(false, "offline"));
    }

    [Theory]
    [InlineData(true, null, RemoteSubAddOutcome.Added)]
    [InlineData(true, "offline", RemoteSubAddOutcome.Added)]   // Ok wins; a worded success is a success
    [InlineData(false, "invalid", RemoteSubAddOutcome.NotAName)]
    [InlineData(false, "", RemoteSubAddOutcome.NotCarried)]
    // Anything else the provider ever starts saying is something that happened to the REQUEST,
    // never a statement about the community, so it must not become a permanent-sounding refusal.
    [InlineData(false, "rate_limited", RemoteSubAddOutcome.Unreachable)]
    [InlineData(false, "http_500", RemoteSubAddOutcome.Unreachable)]
    internal void EveryProbeShapeLandsSomewhere(bool ok, string? error, RemoteSubAddOutcome expected) =>
        Assert.Equal(expected, RemoteSubAddMessages.Classify(ok, error));

    // ---- wording -----------------------------------------------------------------------

    [Fact]
    public void SuccessSaysNothing()
    {
        Assert.Null(RemoteSubAddMessages.KeyFor(RemoteSubAddOutcome.Added));
        Assert.Null(RemoteSubAddMessages.Describe(RemoteSubAddOutcome.Added, "EroticHypnosis", 20));
    }

    [Theory]
    [InlineData(RemoteSubAddOutcome.NotAName, "msg_remote_sub_invalid")]
    [InlineData(RemoteSubAddOutcome.NotCarried, "msg_remote_sub_not_found")]
    [InlineData(RemoteSubAddOutcome.Unreachable, "msg_remote_sub_offline")]
    [InlineData(RemoteSubAddOutcome.AlreadyAdded, "msg_remote_sub_duplicate")]
    [InlineData(RemoteSubAddOutcome.LibraryFull, "msg_remote_sub_library_cap")]
    internal void EachRefusalHasItsOwnKey(RemoteSubAddOutcome outcome, string key) =>
        Assert.Equal(key, RemoteSubAddMessages.KeyFor(outcome));

    /// <summary>
    /// The name goes into the sentence, the cap goes into the cap's sentence, and neither ends up
    /// showing a raw "{0}" - which is what an outcome wired to the wrong argument looks like.
    /// </summary>
    [Fact]
    public void ThePlaceholderIsAlwaysFilled()
    {
        foreach (var outcome in new[] { RemoteSubAddOutcome.NotAName, RemoteSubAddOutcome.NotCarried,
                                        RemoteSubAddOutcome.Unreachable, RemoteSubAddOutcome.AlreadyAdded,
                                        RemoteSubAddOutcome.LibraryFull })
        {
            var text = RemoteSubAddMessages.Describe(outcome, "Hypnohookup", 20);
            Assert.False(string.IsNullOrWhiteSpace(text), outcome + " has no words");
            Assert.DoesNotContain("{0}", text, StringComparison.Ordinal);
        }

        Assert.Contains("Hypnohookup",
            RemoteSubAddMessages.Describe(RemoteSubAddOutcome.NotCarried, "Hypnohookup", 20)!,
            StringComparison.Ordinal);
        Assert.Contains("20",
            RemoteSubAddMessages.Describe(RemoteSubAddOutcome.LibraryFull, "Hypnohookup", 20)!,
            StringComparison.Ordinal);
    }

    /// <summary>A missing name must not throw or print "r/".</summary>
    [Fact]
    public void ANullNameIsSurvivable()
    {
        var text = RemoteSubAddMessages.Describe(RemoteSubAddOutcome.NotCarried, null, 20);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    // ---- the three surfaces -------------------------------------------------------------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    /// <summary>
    /// Every language file carries a line for every refusal. A missing key is not a crash here -
    /// the helper falls back to English - but it is eight locales quietly reading the fallback.
    /// </summary>
    [Fact]
    public void AllNineLanguagesCarryAllFiveRefusals()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.True(files.Length == 9, "expected 9 language files, found " + files.Length);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var key in new[] { "msg_remote_sub_invalid", "msg_remote_sub_not_found",
                                        "msg_remote_sub_offline", "msg_remote_sub_duplicate",
                                        "msg_remote_sub_library_cap" })
                Assert.Contains("\"" + key + "\":", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The Assets tab must not grow a second wording. Its old inline English is gone; every
    /// refusal there goes through ShowRemoteSubOutcome.
    /// </summary>
    [Fact]
    public void TheAssetsTabHasNoWordingOfItsOwn()
    {
        var src = ReadSource("MainWindow", "MainWindow.Assets.cs");
        Assert.DoesNotContain("doesn't exist or has no media", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Couldn't reach the feed", src, StringComparison.Ordinal);
        Assert.Contains("ShowRemoteSubOutcome", src, StringComparison.Ordinal);
    }

    /// <summary>
    /// The For You page is JavaScript and cannot call the helper, so its four lines are checked
    /// against the same promise: name the provider, and never fuse "not carried" with "offline".
    /// </summary>
    [Fact]
    public void TheForYouPageSaysTheSameThing()
    {
        var js = ReadSource("Resources", "web", "fyp", "main.js");
        var at = js.IndexOf("function onSubProbe", StringComparison.Ordinal);
        Assert.True(at > 0, "onSubProbe is no longer in fyp/main.js");
        var body = js.Substring(at, Math.Min(2000, js.Length - at));

        Assert.Contains("Scrolller does not carry", body, StringComparison.Ordinal);
        Assert.Contains("Could not reach Scrolller", body, StringComparison.Ordinal);
        Assert.DoesNotContain("doesn't exist or has no media", body, StringComparison.Ordinal);
    }
}
