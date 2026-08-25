using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The do-not-disturb process list ("designate media players as do-not-disturb").
///
/// <para>Normalisation is the whole feature's hinge: the user types whatever they think their
/// player is called - "VLC.exe", " vlc ", a comma-separated line pasted from somewhere - and the
/// guard compares it against <c>Process.ProcessName</c>, which is lower-case and never carries the
/// extension. If those two ever disagree the feature fails SILENTLY: the list looks right in the
/// settings box and videos keep opening over the film. That is exactly the kind of bug a test is
/// for, since nothing about it is visible at a glance.</para>
/// </summary>
public class DoNotDisturbListTests
{
    // ---------- normalisation ----------

    [Fact]
    public void ParseProcessList_StripsExeTrimsAndLowercases()
    {
        // The case from the feature request, verbatim.
        Assert.Equal(new[] { "vlc", "mpv" }, DoNotDisturbGuard.ParseProcessList("VLC.exe, vlc , mpv"));
    }

    [Theory]
    [InlineData("VLC.exe", "vlc")]
    [InlineData("  mpv  ", "mpv")]
    [InlineData("PotPlayerMini64.EXE", "potplayermini64")]
    [InlineData("\"vlc.exe\"", "vlc")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalize_HandlesTheShapesUsersType(string? raw, string expected)
        => Assert.Equal(expected, DoNotDisturbGuard.Normalize(raw));

    [Fact]
    public void ParseProcessList_AcceptsNewlinesCommasAndSemicolons()
    {
        // The box says "one per line", the tooltip says "or commas", and someone will paste
        // semicolons. All three, mixed, in one value.
        var parsed = DoNotDisturbGuard.ParseProcessList("vlc\r\nmpv.exe, potplayermini64; MPC-HC64");
        Assert.Equal(new[] { "vlc", "mpv", "potplayermini64", "mpc-hc64" }, parsed);
    }

    [Fact]
    public void ParseProcessList_CollapsesDuplicatesKeepingFirstOrder()
    {
        // "vlc" three ways is still one app, and the order the user typed survives.
        var parsed = DoNotDisturbGuard.ParseProcessList("mpv\nVLC.exe\nvlc\n VLC ");
        Assert.Equal(new[] { "mpv", "vlc" }, parsed);
    }

    [Fact]
    public void ParseProcessList_EmptyInputIsEmptyListNotNull()
    {
        Assert.Empty(DoNotDisturbGuard.ParseProcessList(""));
        Assert.Empty(DoNotDisturbGuard.ParseProcessList(null));
        Assert.Empty(DoNotDisturbGuard.ParseProcessList(" , ;\n\n"));
    }

    [Fact]
    public void FormatProcessList_RoundTripsThroughTheTextBox()
    {
        // What SyncFromSettings paints must parse back to the same list, or every visit to the
        // settings page would quietly rewrite the user's list.
        var original = DoNotDisturbGuard.ParseProcessList("VLC.exe, mpv , PotPlayerMini64");
        var text = DoNotDisturbGuard.FormatProcessList(original);
        Assert.Equal(original, DoNotDisturbGuard.ParseProcessList(text));
    }

    [Fact]
    public void FormatProcessList_NullIsEmptyString() => Assert.Equal("", DoNotDisturbGuard.FormatProcessList(null));

    // ---------- settings defaults ----------

    private static readonly JsonSerializerSettings LoaderSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Error = (_, args) => { args.ErrorContext.Handled = true; }
    };

    [Fact]
    public void FreshInstall_ListIsEmptyAndNeverAutoPopulated()
    {
        // Guessing someone's media player would turn features off for a user who never asked.
        Assert.Empty(new AppSettings().DndProcessList);
    }

    [Fact]
    public void FreshInstall_VideosHeldFlashesNot()
    {
        var s = new AppSettings();
        Assert.True(s.DndSuppressVideos);
        Assert.False(s.DndSuppressFlashes);
    }

    [Fact]
    public void SettingsWithoutTheKeys_LandOnTheSameDefaults()
    {
        // An upgrading install has no dnd_* keys in its settings.json.
        var s = JsonConvert.DeserializeObject<AppSettings>("{}", LoaderSettings)!;
        Assert.Empty(s.DndProcessList);
        Assert.True(s.DndSuppressVideos);
        Assert.False(s.DndSuppressFlashes);
    }

    [Fact]
    public void ListSurvivesAJsonRoundTrip()
    {
        var s = new AppSettings { DndProcessList = new List<string> { "vlc", "mpv" } };
        var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(s), LoaderSettings)!;
        Assert.Equal(new[] { "vlc", "mpv" }, back.DndProcessList);
    }

    [Fact]
    public void NullListAssignmentBecomesEmptyNotNull()
    {
        // A hand-edited settings.json with "dnd_process_list": null must not null-ref the guard.
        var s = new AppSettings { DndProcessList = null! };
        Assert.NotNull(s.DndProcessList);
        Assert.Empty(s.DndProcessList);
    }

    // ---------- the guard with no settings behind it ----------

    [Fact]
    public void GuardIsInertWhenNothingIsConfigured()
    {
        // Headless: App.Settings is null. Every predicate must read "do not suppress" - a missing
        // settings object can never be an excuse to stop showing the user's content.
        DoNotDisturbGuard.ResetCacheForTests();
        Assert.False(DoNotDisturbGuard.IsPrivilegedAppForeground());
        Assert.False(DoNotDisturbGuard.ShouldSuppressVideos());
        Assert.False(DoNotDisturbGuard.ShouldSuppressFlashes());
    }
}
