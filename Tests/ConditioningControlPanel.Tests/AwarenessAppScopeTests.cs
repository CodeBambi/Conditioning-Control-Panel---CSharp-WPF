using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The string rules behind keyword-trigger app scoping.
///
/// Only these two helpers are unit-testable: the gate itself reads the live foreground window and
/// App.Settings, so it needs a running app and a real desktop. That makes these tests worth more
/// than they look, because a scope list that silently fails to match is indistinguishable from a
/// scope list that is working - triggers just keep firing (block list) or never fire (allow list),
/// with nothing on screen to say which. The ".exe" rule is the specific trap: the setting is
/// described to users as an app name, Windows reports "chrome", and users type what they see in
/// Task Manager, which is "chrome.exe".
///
/// Pure static string logic. No App, no WPF, no Win32.
/// </summary>
public class AwarenessAppScopeTests
{
    // ---------------------------------------------------------------------------------------
    // MatchesAppList - the read side, consulted on every keystroke
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("chrome", "chrome")]
    [InlineData("Chrome", "chrome")]
    [InlineData("chrome", "CHROME")]
    [InlineData("chrome.exe", "chrome")]
    [InlineData("Chrome.EXE", "chrome")]
    [InlineData("  chrome  ", "chrome")]
    [InlineData("  chrome.exe  ", "Chrome")]
    public void ListEntryMatchesTheProcessRegardlessOfCaseExeOrPadding(string entry, string processName)
    {
        Assert.True(KeywordTriggerService.MatchesAppList(new[] { entry }, processName));
    }

    [Fact]
    public void NonMatchingProcessIsNotMatched()
    {
        var list = new[] { "chrome", "discord" };

        Assert.False(KeywordTriggerService.MatchesAppList(list, "firefox"));
        Assert.False(KeywordTriggerService.MatchesAppList(list, "ms-teams"));
    }

    [Fact]
    public void MatchIsWholeNameNotSubstring()
    {
        // A substring match would make "chrome" cover "chromedriver", and in a block list that
        // silently widens what the user muted. In an allow list it silently widens where triggers
        // fire, which is the direction that actually matters.
        var list = new[] { "chrome" };

        Assert.False(KeywordTriggerService.MatchesAppList(list, "chromedriver"));
        Assert.False(KeywordTriggerService.MatchesAppList(list, "googlechrome"));
        Assert.True(KeywordTriggerService.MatchesAppList(list, "chrome"));
    }

    [Fact]
    public void EmptyAndNullInputsNeverMatch()
    {
        // The gate calls this with whatever the OS handed back. A blank process name matching
        // anything would, in ExceptListed mode, suppress every trigger on the machine.
        Assert.False(KeywordTriggerService.MatchesAppList(null, "chrome"));
        Assert.False(KeywordTriggerService.MatchesAppList(new List<string>(), "chrome"));
        Assert.False(KeywordTriggerService.MatchesAppList(new[] { "chrome" }, ""));
        Assert.False(KeywordTriggerService.MatchesAppList(new[] { "chrome" }, "   "));
    }

    [Fact]
    public void BlankAndBareExeListEntriesAreIgnoredRatherThanMatchingEverything()
    {
        // "" and ".exe" both reduce to an empty entry. If an empty entry compared equal to a
        // process name, one stray comma in the text box would change the meaning of the whole list.
        var list = new[] { "", "   ", ".exe", "chrome" };

        Assert.False(KeywordTriggerService.MatchesAppList(list, "firefox"));
        Assert.True(KeywordTriggerService.MatchesAppList(list, "chrome"));
    }

    // ---------------------------------------------------------------------------------------
    // ParseAppList - the write side, fed by the Awareness tab text box
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ParseSplitsOnCommasSemicolonsAndNewlines()
    {
        var parsed = KeywordTriggerService.ParseAppList("chrome, discord; ms-teams\nzoom\r\nslack");

        Assert.Equal(new[] { "chrome", "discord", "ms-teams", "zoom", "slack" }, parsed);
    }

    [Fact]
    public void ParseStripsExeAndTrimsSoStoredEntriesAreCanonical()
    {
        // Stored canonical means MatchesAppList never has to do the work twice, and the box
        // rewrites itself on commit so the user can see what was actually understood.
        var parsed = KeywordTriggerService.ParseAppList("  Chrome.exe , DISCORD.EXE ,ms-teams  ");

        Assert.Equal(new[] { "Chrome", "DISCORD", "ms-teams" }, parsed);
    }

    [Fact]
    public void ParseDropsDuplicatesCaseInsensitivelyKeepingTheFirstSpelling()
    {
        var parsed = KeywordTriggerService.ParseAppList("chrome, Chrome, CHROME.exe, discord");

        Assert.Equal(new[] { "chrome", "discord" }, parsed);
    }

    [Fact]
    public void ParseOfEmptyOrSeparatorOnlyTextGivesAnEmptyList()
    {
        // An empty list must stay empty rather than becoming a list of one blank entry - in
        // OnlyListed mode a blank entry that matched nothing would mute the whole feature with no
        // explanation, and one that matched everything would defeat the mode entirely.
        Assert.Empty(KeywordTriggerService.ParseAppList(null));
        Assert.Empty(KeywordTriggerService.ParseAppList(""));
        Assert.Empty(KeywordTriggerService.ParseAppList("   "));
        Assert.Empty(KeywordTriggerService.ParseAppList(", ; ,"));
        Assert.Empty(KeywordTriggerService.ParseAppList(".exe"));
    }

    [Fact]
    public void ParseThenMatchRoundTripsEveryFormAUserMightType()
    {
        // The two helpers are used by different layers (UI writes, service reads) and drifting
        // apart would be silent, so pin them against each other.
        var parsed = KeywordTriggerService.ParseAppList("Chrome.exe, ms-teams, Discord");

        Assert.True(KeywordTriggerService.MatchesAppList(parsed, "chrome"));
        Assert.True(KeywordTriggerService.MatchesAppList(parsed, "ms-teams"));
        Assert.True(KeywordTriggerService.MatchesAppList(parsed, "discord"));
        Assert.False(KeywordTriggerService.MatchesAppList(parsed, "firefox"));
    }

    // ---------------------------------------------------------------------------------------
    // Defaults
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void DefaultsPreserveTheBehaviourThatShippedBeforeScopingExisted()
    {
        // Scoping is opt-in on purpose: an existing user who never opens this must not find their
        // triggers newly silent somewhere. AppSettings is a plain INotifyPropertyChanged object,
        // so constructing one touches no services.
        var settings = new AppSettings();

        Assert.Equal(AwarenessAppScope.Everywhere, settings.KeywordTriggerAppScope);
        Assert.Empty(settings.KeywordTriggerApps);
        Assert.False(settings.KeywordTriggerIgnoreOwnFocus);
    }

    [Fact]
    public void TheAppListSetterNeverLeavesTheListNull()
    {
        // Round-tripped settings JSON written before these fields existed deserialises them as
        // null, and the gate indexes the list on the per-keystroke path.
        var settings = new AppSettings { KeywordTriggerApps = null! };

        Assert.NotNull(settings.KeywordTriggerApps);
        Assert.Empty(settings.KeywordTriggerApps);
    }
}
