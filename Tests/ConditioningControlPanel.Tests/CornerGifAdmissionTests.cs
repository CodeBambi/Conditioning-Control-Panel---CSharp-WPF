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
    /// refresh. The handback belongs in the teardown itself, so every TERMINAL close path gives the
    /// corner back - the mid-session master check, the end-minute timer, RefreshCornerGifPolicy and
    /// session end.
    /// </summary>
    [Fact]
    public void ClosingTheSessionOverlay_HandsTheCornerBackToTheUsersOwnSlots()
        => Assert.Contains("App.CornerGif?.RefreshOverlays()",
            MemberBody(SessionEngineSource(), "private void CloseCornerGif(bool handBackCorner)"));

    /// <summary>
    /// ...but only when this session actually TOOK the corner. An unconditional handback turned
    /// every close-that-closed-nothing into StopAll + a re-Show of every standalone slot, i.e. the
    /// Close,Close,Show,Show burst on AllowsTransparency windows that CornerGifService.QueueShow's
    /// own doc comment names as the #494 freeze, the #709 crash and the #958 hang - fired from the
    /// panic path and the pause button, for no reason.
    ///
    /// <para>The gate is the DEBT, not "is there a window": a pause and a panic press hide the
    /// overlay without ending the session's claim, so a session paused by a panic and then stopped
    /// has no window left and must still hand the corner back.</para>
    /// </summary>
    [Fact]
    public void TheHandbackOnlyRunsWhenTheSessionActuallyTookTheCorner()
    {
        var body = MemberBody(SessionEngineSource(), "private void CloseCornerGif(bool handBackCorner)");
        Assert.Contains("if (handBackCorner && _cornerHandbackOwed)", body);
        Assert.DoesNotContain("if (handBackCorner)", body);
    }

    /// <summary>The debt is taken on when the overlay goes up, and nowhere else.</summary>
    [Fact]
    public void ShowingTheSessionOverlay_TakesOnTheHandbackDebt()
    {
        var source = SessionEngineSource();
        Assert.Equal(1, Regex.Matches(source, @"_cornerHandbackOwed = true;").Count);
        Assert.Contains("_cornerHandbackOwed = true;", source[source.IndexOf(
            "_sessionCornerGifActive = true;", StringComparison.Ordinal)..]);
    }

    /// <summary>
    /// Every HIDE-only close opts out of the handback. The two live editors close and immediately
    /// re-Show the SAME overlay (a queued standalone slot counts as StandaloneCornerGifActive, so
    /// the re-Show would refuse and a size-slider nudge would silently kill the session's overlay);
    /// PauseSession must leave the corner claimed so ResumeSession can take it back; and the panic
    /// door must never put a spiral back on screen. Anything else handing the corner back is almost
    /// certainly a mistake.
    /// </summary>
    [Fact]
    public void OnlyTheHideOnlyClosesSkipTheHandback()
    {
        var source = SessionEngineSource();
        var optOuts = Regex.Matches(source, @"CloseCornerGif\(handBackCorner:\s*false\)").Count;
        Assert.Equal(4, optOuts);
        foreach (var member in new[]
                 {
                     "public void UpdateCornerGifSize(",
                     "public void UpdateCornerGifPath(",
                     "public void PauseSession()",
                     "public void PanicCloseCornerGif()"
                 })
            Assert.Contains("CloseCornerGif(handBackCorner: false)", MemberBody(source, member));
    }

    /// <summary>
    /// A pause means "get this off my screen", and the panic key pauses the session. PauseSession
    /// stopped every other feature and left the corner overlay spinning.
    ///
    /// <para>It is a HIDE, not a terminal close: handing the corner back here let the user's own
    /// slot realize during the pause, and ResumeSession's re-raise then refused it
    /// (StandaloneCornerGifActive), so one press of the pause button killed a stock minute-0
    /// program-day corner GIF for the rest of the day - the per-second tick only ever re-raises
    /// overlays with CornerGifStartMinute greater than zero. Pause and resume have to be
    /// symmetric.</para>
    /// </summary>
    [Fact]
    public void PausingASession_HidesTheCornerOverlayWithoutGivingTheCornerAway()
        => Assert.Contains("CloseCornerGif(handBackCorner: false)",
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
        // handBackCorner: FALSE. The handback is App.CornerGif.RefreshOverlays(), which re-queues
        // every enabled standalone slot - so a handing-back panic door re-realized the user's OWN
        // corner spiral one dispatcher pass after the stop-all sweep closed it, and the one thing
        // the whole change promises ("one press takes every surface down") failed on the very
        // spiral the ticket is about.
        Assert.Contains("CloseCornerGif(handBackCorner: false)", body);
        Assert.DoesNotContain("ShowCornerGif", body);
        Assert.DoesNotContain("RefreshOverlays", body);
    }

    /// <summary>
    /// The stop-all pass sweeps the standalone slots LAST, after the session overlay is down, so
    /// whatever the session door did the final word on the corner is StopAll (which also cancels
    /// queued realizations, so a slot mid-stagger cannot land after the pass).
    /// </summary>
    [Fact]
    public void ThePanicPass_SweepsTheStandaloneSlotsAfterTheSessionOverlay()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs"));
        var start = source.IndexOf("private void PanicStopEverySurface()", StringComparison.Ordinal);
        Assert.True(start >= 0, "PanicStopEverySurface was renamed - update this test with it");
        var end = source.IndexOf("        /// <summary>", start, StringComparison.Ordinal);
        var body = end > start ? source[start..end] : source[start..];

        var session = body.IndexOf("PanicCloseCornerGif()", StringComparison.Ordinal);
        var standalone = body.IndexOf("App.CornerGif?.StopAll()", StringComparison.Ordinal);
        Assert.True(session >= 0 && standalone >= 0, "both corner steps must be in the pass");
        Assert.True(session < standalone,
            "the standalone StopAll sweep must come after the session overlay's own door");
    }

    /// <summary>
    /// The live dedupe has to resolve in BOTH directions. RefreshCornerGifPolicy was wired only to
    /// the session-side master checkbox, so a session whose corner GIF was refused at start because
    /// a standalone slot was up stayed refused for the whole run even after the user switched that
    /// slot off - the per-second tick only re-raises overlays with CornerGifStartMinute above zero.
    /// The user turned their own corner GIF off expecting the program's to appear and nothing
    /// happened.
    /// </summary>
    [Theory]
    [InlineData("RefreshOverlays")]
    [InlineData("RefreshSlot")]
    public void TheStandaloneSideAlsoReAsksTheSessionAdmission(string method)
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "ConditioningControlPanel", "Services", "CornerGifService.cs"));
        var start = source.IndexOf("public void " + method + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, method + " was renamed - update this test with it");
        var end = source.IndexOf("        /// <summary>", start, StringComparison.Ordinal);
        var body = end > start ? source[start..end] : source[start..];
        Assert.Contains("NotifySessionAdmissionChanged()", body);
    }

    /// <summary>
    /// The live re-resolve may CLOSE the overlay while a session is paused, but it must never OPEN
    /// one: a pause (the pause button, or the one the panic key triggers) means "nothing on my
    /// screen", and now that the standalone side reaches this too, switching a corner slot off
    /// after a panic press would otherwise put the session's spiral straight back up. ResumeSession
    /// owns the re-raise.
    /// </summary>
    [Fact]
    public void TheLiveReResolveNeverRaisesTheOverlayOnAPausedSession()
    {
        var body = MemberBody(SessionEngineSource(), "public void RefreshCornerGifPolicy()");
        var show = body.IndexOf("ShowCornerGif(settings)", StringComparison.Ordinal);
        Assert.True(show >= 0, "the re-raise branch is gone - update this test with it");
        var guard = body.IndexOf("IsRunning && !IsPaused", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < show, "the re-raise must be gated on the session not being paused");
        // ...and it must respect the program's end minute, exactly like ResumeSession's re-raise.
        Assert.Contains("settings.CornerGifEndMinute <= 0", body);
    }

    /// <summary>
    /// ...and the bounce that creates (standalone refresh -> session policy -> a close that hands
    /// the corner back -> standalone refresh) is stopped by RefreshCornerGifPolicy's own
    /// re-entrancy guard rather than by luck.
    /// </summary>
    [Fact]
    public void TheTwoSidedRefreshCannotPingPong()
    {
        var body = MemberBody(SessionEngineSource(), "public void RefreshCornerGifPolicy()");
        Assert.Contains("if (_refreshingCornerGifPolicy) return;", body);
        Assert.Contains("_refreshingCornerGifPolicy = true;", body);
        Assert.Contains("_refreshingCornerGifPolicy = false;", body);
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
