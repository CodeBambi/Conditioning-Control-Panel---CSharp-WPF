using System;
using System.IO;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Ticket 1539282547484139682: a session's corner GIF ignored the user's switches and could stack a
/// SECOND spiral on top of a standalone Corner GIF overlay. Two services can raise a corner overlay
/// (SessionEngine and CornerGifService) and the admission rule now lives once, beside the shared
/// source handling in <see cref="CornerGifMedia"/>, so neither can drift.
///
/// <para>The invariant the tests defend: whatever the inputs, the two helpers never both say yes.</para>
/// </summary>
public class CornerGifAdmissionTests
{
    private static readonly JsonSerializerSettings LoaderSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Error = (_, args) => { args.ErrorContext.Handled = true; }
    };

    // ---- the session side ----

    [Fact]
    public void SessionCornerGif_ShowsWhenTemplateAndUserBothAgree()
        => Assert.True(CornerGifMedia.AllowSessionCornerGif(
            templateEnabled: true, userAllowed: true, standaloneOverlayActive: false));

    [Fact]
    public void SessionCornerGif_UserMasterOffBeatsTheTemplate()
        => Assert.False(CornerGifMedia.AllowSessionCornerGif(
            templateEnabled: true, userAllowed: false, standaloneOverlayActive: false));

    [Fact]
    public void SessionCornerGif_TemplateOffStaysOffEvenWhenAllowed()
        => Assert.False(CornerGifMedia.AllowSessionCornerGif(
            templateEnabled: false, userAllowed: true, standaloneOverlayActive: false));

    [Fact]
    public void SessionCornerGif_YieldsToAStandaloneOverlayAlreadyOnScreen()
        => Assert.False(CornerGifMedia.AllowSessionCornerGif(
            templateEnabled: true, userAllowed: true, standaloneOverlayActive: true));

    // ---- the standalone side ----

    [Fact]
    public void StandaloneSlot_ShowsWhenNoSessionOverlayIsUp()
        => Assert.True(CornerGifMedia.AllowStandaloneCornerGif(
            slotEnabled: true, sessionCornerGifActive: false));

    [Fact]
    public void StandaloneSlot_YieldsWhileASessionOverlayIsUp()
        => Assert.False(CornerGifMedia.AllowStandaloneCornerGif(
            slotEnabled: true, sessionCornerGifActive: true));

    [Fact]
    public void DisabledStandaloneSlot_StaysDown()
        => Assert.False(CornerGifMedia.AllowStandaloneCornerGif(
            slotEnabled: false, sessionCornerGifActive: false));

    // ---- the invariant ----

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void TwoSpiralsAreNeverBothAdmitted(bool templateEnabled, bool userAllowed)
    {
        // Whichever one is already up, the other is refused - so the reported "two spirals" state
        // cannot be reached from either direction.
        var sessionUp = CornerGifMedia.AllowSessionCornerGif(templateEnabled, userAllowed, standaloneOverlayActive: false);
        Assert.False(sessionUp && CornerGifMedia.AllowStandaloneCornerGif(slotEnabled: true, sessionCornerGifActive: sessionUp));

        var standaloneUp = CornerGifMedia.AllowStandaloneCornerGif(slotEnabled: true, sessionCornerGifActive: false);
        Assert.False(standaloneUp && CornerGifMedia.AllowSessionCornerGif(templateEnabled, userAllowed, standaloneOverlayActive: standaloneUp));
    }

    // ---- the setting itself ----

    [Fact]
    public void SessionCornerGifAllowed_DefaultsOn_SoNobodysProgramChangesUnderThem()
    {
        Assert.True(new AppSettings().SessionCornerGifAllowed);
        Assert.True(JsonConvert.DeserializeObject<AppSettings>("{}", LoaderSettings)!.SessionCornerGifAllowed);
    }

    [Fact]
    public void SessionCornerGifAllowed_ReadsTheSavedFalse()
        => Assert.False(JsonConvert.DeserializeObject<AppSettings>(
            "{\"SessionCornerGifAllowed\": false}", LoaderSettings)!.SessionCornerGifAllowed);

    // ---- both standalone entry points ask the same question ----

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    /// <summary>
    /// CornerGifService admits a standalone slot from TWO places: RefreshOverlays (every slot, after
    /// a config change) and RefreshSlot (ONE slot, the live size/opacity slider edit). The rule was
    /// first written into RefreshOverlays only, which let a user enable a slot while a session
    /// overlay was up (correctly suppressed) and then realise it anyway by nudging that slot's
    /// slider - two corner spirals at once, ticket 1539282547484139682 all over again. Both bodies
    /// must go through <see cref="CornerGifMedia.AllowStandaloneCornerGif"/>; neither may admit on a
    /// bare Enabled check.
    /// </summary>
    [Theory]
    [InlineData("RefreshOverlays")]
    [InlineData("RefreshSlot")]
    public void BothStandaloneEntryPoints_ShareTheAdmissionRule(string method)
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "ConditioningControlPanel", "Services", "CornerGifService.cs"));

        var start = source.IndexOf("public void " + method + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, method + " was renamed - update this test with it");

        // The body runs to the start of the next member's doc comment (every member in this file
        // carries one), or to the end of the file for the last one.
        var end = source.IndexOf("        /// <summary>", start, StringComparison.Ordinal);
        var body = end > start ? source[start..end] : source[start..];

        Assert.Contains("CornerGifMedia.AllowStandaloneCornerGif", body);
        // ...and never the bare check it replaced.
        Assert.DoesNotMatch(new Regex(@"!\s*setting\.Enabled|!\s*o\.Enabled"), body);
    }
}
