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

    // ---- handing the corner back ----

    private static string SessionEngineSource() => File.ReadAllText(Path.Combine(
        RepoRoot(), "ConditioningControlPanel", "Services", "Session", "SessionEngine.cs"));

    private static string MemberBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " was renamed - update this test with it");
        var end = source.IndexOf("        /// <summary>", start, StringComparison.Ordinal);
        return end > start ? source[start..end] : source[start..];
    }

    /// <summary>
    /// A standalone slot suppressed while the session owned the corner used to be handed back ONLY
    /// at session end: enable a Spiral-card slot at minute 2 of a program day and it stayed
    /// invisible for the rest of the session, and toggling it again just re-ran the same suppressed
    /// refresh. The handback belongs in the teardown itself, so EVERY close path gives the corner
    /// back - the mid-session master check, the end-minute timer, RefreshCornerGifPolicy, a pause,
    /// and the panic key.
    /// </summary>
    [Fact]
    public void ClosingTheSessionOverlay_HandsTheCornerBackToTheUsersOwnSlots()
        => Assert.Contains("App.CornerGif?.RefreshOverlays()",
            MemberBody(SessionEngineSource(), "private void CloseCornerGif(bool handBackCorner)"));

    /// <summary>
    /// The two live editors close and immediately re-Show the SAME overlay, so they must not hand
    /// the corner back in between: a queued standalone slot counts as StandaloneCornerGifActive, so
    /// the re-Show would refuse and a size-slider nudge would silently kill the session's overlay.
    /// They are the only opt-outs; a third one is almost certainly a mistake.
    /// </summary>
    [Fact]
    public void OnlyTheLiveEditorsSkipTheHandback()
    {
        var source = SessionEngineSource();
        var optOuts = Regex.Matches(source, @"CloseCornerGif\(handBackCorner:\s*false\)").Count;
        Assert.Equal(2, optOuts);
        foreach (var editor in new[] { "public void UpdateCornerGifSize(", "public void UpdateCornerGifPath(" })
            Assert.Contains("CloseCornerGif(handBackCorner: false)", MemberBody(source, editor));
    }

    /// <summary>
    /// A pause means "get this off my screen", and the panic key pauses the session. PauseSession
    /// stopped every other feature and left the corner overlay spinning.
    /// </summary>
    [Fact]
    public void PausingASession_TakesTheCornerOverlayDown()
        => Assert.Contains("CloseCornerGif()",
            MemberBody(SessionEngineSource(), "public void PauseSession()"));

    /// <summary>...and resuming puts it back. The per-second tick only re-raises corner GIFs whose
    /// start minute is greater than zero, so a minute-0 one (every stock program day) would
    /// otherwise never come back after a pause.</summary>
    [Fact]
    public void ResumingASession_PutsTheCornerOverlayBack()
    {
        // MemberBody runs to the next documented member, and the next member here is undocumented,
        // so anchor on a token only ResumeSession's re-raise uses rather than on ShowCornerGif.
        var body = MemberBody(SessionEngineSource(), "public void ResumeSession()");
        Assert.Contains("cornerMinutes", body);
        Assert.Contains("ShowCornerGif(settings)", body);
    }

    /// <summary>
    /// The panic key's own door to the session overlay exists and does not re-show it. Without it
    /// the stop-everything pass had no way to reach this window at all: App.CornerGif owns only the
    /// standalone slots.
    /// </summary>
    [Fact]
    public void ThePanicDoorClosesTheSessionOverlayAndDoesNotReShowIt()
    {
        var body = MemberBody(SessionEngineSource(), "public void PanicCloseCornerGif()");
        Assert.Contains("CloseCornerGif()", body);
        Assert.DoesNotContain("ShowCornerGif", body);
    }

    /// <summary>
    /// Realization is deferred (a dispatcher pass, plus display-change retries), so the admission
    /// rule has to be re-asked at realize time as well as at queue time: a session can raise its own
    /// corner overlay in that window, and a slot queued the instant before would then land behind
    /// it - two spirals in one corner.
    /// </summary>
    [Fact]
    public void DeferredRealization_ReAsksTheAdmissionRule()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "ConditioningControlPanel", "Services", "CornerGifService.cs"));
        var start = source.IndexOf("private void ScheduleRealize(", StringComparison.Ordinal);
        Assert.True(start >= 0, "ScheduleRealize was renamed - update this test with it");
        var end = source.IndexOf("        /// <summary>", start, StringComparison.Ordinal);
        var body = end > start ? source[start..end] : source[start..];
        Assert.Contains("CornerGifMedia.AllowStandaloneCornerGif", body);
    }
}
