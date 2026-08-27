using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json;
using Xunit;
using static ConditioningControlPanel.Services.Safety.PanicPolicy;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// v6.8.5 "panic button is panic button" (suggestion thread 1541736938703167550, ccp-bugs #1054 /
/// #1066). One press used to be handed down a six rung ladder and then spent as the #735 video
/// grace pause, so the engine only stopped on press two or three. <see cref="PanicPolicy"/> is the
/// pure decision that collapses that, and every sharp edge of it lives here:
///
///   1. the Lock Card still outranks everything, in BOTH modes, and never advances the exit ladder
///   2. the Ctrl+K palette is rung 2 in BOTH modes and its press stops NOTHING (Escape is both the
///      default panic key and the universal "close this popup" key, so dismissing the palette
///      mid-session must not pause the session, cost 100 XP or track a Relapse panic)
///   3. the override default is ON, including when settings failed to load
///   4. turning the override off restores the old ladder and the old grace pause, byte for byte
///   5. the optional pause key matches nothing until the user binds it, and loses to the panic key
/// </summary>
public class PanicPolicyTests
{
    private static readonly JsonSerializerSettings LoaderSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Error = (_, args) => { args.ErrorContext.Handled = true; }
    };

    private static AppSettings Load(string json)
        => JsonConvert.DeserializeObject<AppSettings>(json, LoaderSettings)!;

    // ---- 1. the rung decision ----

    [Fact]
    public void OverrideOn_NoLockCard_StopsEverything()
        => Assert.Equal(Rung.StopEverything,
            Decide(lockCardOpen: false, paletteClaimedPress: false, overrideAll: true));

    [Fact]
    public void OverrideOff_NoLockCard_RunsTheOldLadder()
        => Assert.Equal(Rung.RunLadder,
            Decide(lockCardOpen: false, paletteClaimedPress: false, overrideAll: false));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpenLockCard_OutranksBothModes(bool overrideAll)
        => Assert.Equal(Rung.DismissLockCard,
            Decide(lockCardOpen: true, paletteClaimedPress: false, overrideAll: overrideAll));

    /// <summary>
    /// The Lock Card is asked BEFORE the palette, and the caller must not even put the palette
    /// question to the palette while a card is open (asking closes it). Pinned here so the
    /// ordering cannot be flipped by a later edit.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpenLockCard_OutranksThePaletteToo(bool overrideAll)
        => Assert.Equal(Rung.DismissLockCard,
            Decide(lockCardOpen: true, paletteClaimedPress: true, overrideAll: overrideAll));

    /// <summary>
    /// The bug this rung exists for: with Escape as the default panic key, dismissing the Ctrl+K
    /// quick-settings palette mid-session used to run the whole stop-everything pass - engine
    /// stopped, session paused with its 100 XP penalty, a Relapse panic tracked - on a default
    /// install, for a press the user meant as "close this popup".
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PaletteClaimedPress_IsItsOwnRungInBothModes(bool overrideAll)
        => Assert.Equal(Rung.DismissSettingsPalette,
            Decide(lockCardOpen: false, paletteClaimedPress: true, overrideAll: overrideAll));

    // ---- 2. the exit ladder ----

    [Fact]
    public void LockCardPress_NeverAdvancesTheExitLadder()
        => Assert.False(AdvancesExitLadder(Rung.DismissLockCard));

    [Fact]
    public void StopEverythingPress_AdvancesTheExitLadder()
        => Assert.True(AdvancesExitLadder(Rung.StopEverything));

    [Fact]
    public void LadderPress_AdvancesTheExitLadder()
        => Assert.True(AdvancesExitLadder(Rung.RunLadder));

    /// <summary>
    /// The Ctrl+K palette rung. Escape is the DEFAULT panic key and the global hook delivers it
    /// whatever has focus, so an Escape aimed at closing the palette lands in the panic handler -
    /// and the tail EXITS THE APP on press 2 while the engine is stopped. Closing a palette must
    /// never be press 1 of "quit".
    /// </summary>
    [Fact]
    public void PalettePress_NeverAdvancesTheExitLadder()
        => Assert.False(AdvancesExitLadder(Rung.DismissSettingsPalette));

    /// <summary>
    /// Override mode closes a mini-game or the feed itself instead of handing the press to it - so
    /// that press must NOT also arm the double-press "quit the app" tap. The legacy ladder gave the
    /// press to the game and returned, so the counter never moved and the app could not be exited
    /// from inside the Arcademy / DtRH / a descent / the feed; the For You rung's own comment
    /// records that a reflexive Esc-Esc double-tap is real, play-tested behaviour, and the second
    /// tap lands with the engine stopped, i.e. straight on Application.Shutdown().
    /// </summary>
    [Fact]
    public void StopEverythingPress_DoesNotArmTheExitTapWhenItClosedAGame()
        => Assert.False(AdvancesExitLadder(Rung.StopEverything, aGameSurfaceOwnedTheScreen: true));

    /// <summary>With no game on screen the press counts exactly as before: double-press-to-exit is
    /// an escape hatch of its own and must survive this fix.</summary>
    [Fact]
    public void StopEverythingPress_StillArmsTheExitTapWithNoGameOnScreen()
        => Assert.True(AdvancesExitLadder(Rung.StopEverything, aGameSurfaceOwnedTheScreen: false));

    /// <summary>The two dismiss rungs never count, game or no game - a press spent on a Lock Card
    /// or the Ctrl+K palette can never be the tap that quits the app.</summary>
    [Theory]
    [InlineData("DismissLockCard", true)]
    [InlineData("DismissLockCard", false)]
    [InlineData("DismissSettingsPalette", true)]
    [InlineData("DismissSettingsPalette", false)]
    public void DismissRungs_NeverCountEitherWay(string name, bool gameOnScreen)
        => Assert.False(AdvancesExitLadder(ByName(name), gameOnScreen));

    /// <summary>Legacy mode is untouched: there, a game on screen is answered by its own hand-off
    /// rung long before the tail is reached, so the flag has nothing to say about it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LegacyLadderPress_IsUnaffectedByTheGameProbe(bool gameOnScreen)
        => Assert.True(AdvancesExitLadder(Rung.RunLadder, gameOnScreen));

    // ---- 2b. which rungs are allowed to tear surfaces down ----

    /// <summary>
    /// The two dismiss rungs answer the surface that owns the press and stop there. Nothing else is
    /// touched: no stop pass, no engine stop, no session pause, no XP penalty. This is the whole
    /// point of the palette rung, and it is also the Lock Card's long-standing contract.
    /// </summary>
    // Rung is internal, and xUnit only discovers PUBLIC test methods, so the theories name their
    // rung as text and map it here rather than taking an internal parameter type.
    private static Rung ByName(string name) => name switch
    {
        "DismissLockCard" => Rung.DismissLockCard,
        "DismissSettingsPalette" => Rung.DismissSettingsPalette,
        "StopEverything" => Rung.StopEverything,
        "RunLadder" => Rung.RunLadder,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown rung")
    };

    [Theory]
    [InlineData("DismissLockCard")]
    [InlineData("DismissSettingsPalette")]
    public void DismissRungs_StopNothing(string rung)
        => Assert.False(StopsSurfaces(ByName(rung)));

    [Theory]
    [InlineData("StopEverything")]
    [InlineData("RunLadder")]
    public void RealPanicRungs_StopSurfaces(string rung)
        => Assert.True(StopsSurfaces(ByName(rung)));

    /// <summary>Every rung that stops surfaces also advances the exit ladder, and vice versa: the
    /// two properties must not drift apart into a rung that stops the world without counting, or
    /// counts toward "quit the app" without stopping anything.</summary>
    [Theory]
    [InlineData("DismissLockCard")]
    [InlineData("DismissSettingsPalette")]
    [InlineData("StopEverything")]
    [InlineData("RunLadder")]
    public void StoppingAndCountingAgreeOnEveryRung(string name)
    {
        var rung = ByName(name);
        Assert.Equal(StopsSurfaces(rung), AdvancesExitLadder(rung));
    }

    /// <summary>Every value of the enum is covered by the theories above - a new rung must decide
    /// both questions on purpose, not inherit an untested default.</summary>
    [Fact]
    public void EveryRungIsCoveredByThoseTheories()
        => Assert.Equal(4, Enum.GetValues(typeof(Rung)).Length);

    // ---- 3. the master switch ----

    [Fact]
    public void OverrideDefaultsOn_ForAFreshInstall()
        => Assert.True(new AppSettings().PanicOverridesAll);

    [Fact]
    public void OverrideDefaultsOn_ForASettingsFileWithoutTheKey()
        => Assert.True(Load("{}").PanicOverridesAll);

    [Fact]
    public void OverrideDefaultsOn_WhenSettingsAreMissingEntirely()
        => Assert.True(OverrideEnabled(null));

    [Fact]
    public void OverrideReadsTheSavedFalse()
    {
        var s = Load("{\"PanicOverridesAll\": false}");
        Assert.False(s.PanicOverridesAll);
        Assert.False(OverrideEnabled(s));
    }

    // ---- 4. the #735 grace pause moved to the pause key ----

    [Fact]
    public void OverrideOn_PanicKeyNoLongerSpendsThePressOnTheGracePause()
        => Assert.False(AllowGracePauseFromPanicKey(overrideAll: true));

    [Fact]
    public void OverrideOff_PanicKeyStillPausesTheVideoFirst()
        => Assert.True(AllowGracePauseFromPanicKey(overrideAll: false));

    /// <summary>
    /// The override owns the PANIC key's presses and nothing else. The door that matters most here
    /// is the strict-lock video window's Escape handler: it only exists when Escape is NOT the panic
    /// key, and it is that user's only "someone walked in" out (the Closing veto still refuses
    /// Alt+F4). If PanicOverridesAll reached it, Escape in a strict-locked video would do nothing at
    /// all, and the Pause key ships unbound so there would be no replacement.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonPanicKeyDoors_AreUntouchedByTheOverride(bool overrideAll)
        => Assert.True(AllowGracePause(fromPanicKey: false, overrideAll: overrideAll));

    [Fact]
    public void PanicKeyDoor_ClosesUnderTheOverride()
        => Assert.False(AllowGracePause(fromPanicKey: true, overrideAll: true));

    [Fact]
    public void PanicKeyDoor_StaysOpenWithTheOverrideOff()
        => Assert.True(AllowGracePause(fromPanicKey: true, overrideAll: false));

    // Which video-window presses count as panic presses at all.

    [Fact]
    public void EscapeIsAPanicPress_OnlyWhenItIsTheBoundPanicKey()
    {
        Assert.True(EscapeIsThePanicKey(panicKeyEnabled: true, panicKey: "Escape"));
        Assert.True(EscapeIsThePanicKey(panicKeyEnabled: true, panicKey: " escape "));
        Assert.False(EscapeIsThePanicKey(panicKeyEnabled: true, panicKey: "F8"));
        Assert.False(EscapeIsThePanicKey(panicKeyEnabled: false, panicKey: "Escape"));
        Assert.False(EscapeIsThePanicKey(panicKeyEnabled: true, panicKey: null));
    }

    /// <summary>The default install: Escape IS the panic key, so an Escape in a non-strict video is
    /// a panic press and the override takes it; the strict door (Escape not the panic key) is not.</summary>
    [Fact]
    public void DefaultInstall_PanicKeyIsEscape_AndTheOverrideOwnsThatPress()
    {
        var fresh = new AppSettings();
        Assert.True(EscapeIsThePanicKey(fresh.PanicKeyEnabled, fresh.PanicKey));
        Assert.False(AllowGracePause(
            fromPanicKey: EscapeIsThePanicKey(fresh.PanicKeyEnabled, fresh.PanicKey),
            overrideAll: OverrideEnabled(fresh)));
    }

    // ---- 5. the optional pause key ----

    [Fact]
    public void PauseKeyIsUnboundByDefault()
    {
        Assert.Equal("", new AppSettings().PauseKey);
        Assert.Equal("", Load("{}").PauseKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnboundPauseKey_MatchesNothing(string? bound)
    {
        Assert.False(IsPauseKeyPress(bound, "F9"));
        Assert.False(IsPauseKeyPress(bound, "Escape"));
    }

    [Fact]
    public void BoundPauseKey_MatchesItsOwnKeyOnly()
    {
        Assert.True(IsPauseKeyPress("F9", "F9"));
        Assert.False(IsPauseKeyPress("F9", "F10"));
    }

    [Fact]
    public void PauseKeyMatch_IgnoresCaseAndStrayWhitespace()
    {
        Assert.True(IsPauseKeyPress(" f9 ", "F9"));
        Assert.True(IsPauseKeyPress("F9", "f9"));
    }

    [Fact]
    public void PauseKeyPress_IsNeverAnEmptyKeystroke()
        => Assert.False(IsPauseKeyPress("F9", null));

    [Fact]
    public void PanicKeyWinsACollision()
        => Assert.True(PauseKeyIsShadowedByPanicKey("F9", panicKeyEnabled: true, pauseKey: "f9"));

    [Fact]
    public void DisabledPanicKey_ShadowsNothing()
        => Assert.False(PauseKeyIsShadowedByPanicKey("F9", panicKeyEnabled: false, pauseKey: "F9"));

    [Fact]
    public void DifferentKeys_DoNotShadow()
        => Assert.False(PauseKeyIsShadowedByPanicKey("F9", panicKeyEnabled: true, pauseKey: "F10"));

    [Fact]
    public void UnboundPauseKey_IsNotShadowed()
        => Assert.False(PauseKeyIsShadowedByPanicKey("F9", panicKeyEnabled: true, pauseKey: ""));

    // ---- 6. the stop-everything surface list ----
    //
    // PanicStopEverySurface is UI-thread WPF code, so it cannot be exercised from a unit test; what
    // CAN be pinned is that the surfaces the brief lists are all named in it, and that the two
    // things it must NOT do stay out. Both regressions below were shipped once and caught in review.

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string PanicStopEverySurfaceBody()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs"));
        var start = source.IndexOf("private void PanicStopEverySurface()", StringComparison.Ordinal);
        Assert.True(start >= 0, "PanicStopEverySurface was renamed - update this test with it");
        var end = source.IndexOf("        /// <summary>", start, StringComparison.Ordinal);
        return end > start ? source[start..end] : source[start..];
    }

    /// <summary>
    /// Every surface the "panic button is panic button" brief lists has to be in the one pass.
    /// The session-scoped corner GIF is the one that was missed: CornerGifService.StopAll() owns
    /// only the standalone Spiral-card slots, so on a program day with Corner GIF at minute 0 one
    /// press stopped everything else and left the session spiral spinning - which is the exact
    /// surface support ticket 1539282547484139682 is about.
    /// </summary>
    [Theory]
    [InlineData("App.Video?.ForceCleanup")]
    [InlineData("App.Flash?.Stop()")]
    [InlineData("App.Bubbles?.Stop()")]
    [InlineData("App.Subliminal?.Stop()")]
    [InlineData("App.Overlay?.Stop()")]
    [InlineData("App.Overlay?.StopSpiral()")]
    [InlineData("App.Overlay?.StopPinkFilter()")]
    [InlineData("App.CornerGif?.StopAll()")]
    [InlineData("SessionEngine.Active?.PanicCloseCornerGif()")]
    [InlineData("PanicSilence()")]
    [InlineData("App.Chaos?.ForceShutdown()")]
    [InlineData("DtrhHostService.CloseActive()")]
    [InlineData("ArcademyHostService.CloseActive()")]
    [InlineData("FypHostService.Close()")]
    [InlineData("JustDropHostService.CloseActive()")]
    [InlineData("App.KillAllAudio")]
    public void StopEverything_CoversEverySurface(string call)
        => Assert.Contains(call, PanicStopEverySurfaceBody());

    /// <summary>
    /// A panic stops what is on screen; it must never reconfigure the app. EnablePinkFilter(false)
    /// / EnableSpiral(false) write the user's PERSISTENT feature switches, so every mid-session
    /// panic press left Spiral and Pink Filter switched off for all their later manual runs. The
    /// windows are already torn down by OverlayService.Stop(), which also clears its _isRunning so
    /// no reconcile tick can repaint them.
    /// </summary>
    [Theory]
    [InlineData("EnablePinkFilter(false)")]
    [InlineData("EnableSpiral(false)")]
    public void StopEverything_NeverWritesTheUsersPersistentFeatureSwitches(string forbidden)
        => Assert.DoesNotContain(forbidden, CodeOnly(PanicStopEverySurfaceBody()));

    /// <summary>Comment lines stripped: the body explains in prose WHY the two settings writes are
    /// gone, and naming them there must not read as calling them.</summary>
    private static string CodeOnly(string body)
    {
        const char lf = (char)10;
        return string.Join(lf.ToString(), body
            .Split(lf)
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The pass must not re-ask the palette whether it claims this press: the claim (and the short
    /// Escape grace window behind it) is settled once, in the rung decision, before anything is
    /// torn down. Asking twice would burn the grace window that decision depends on.
    /// </summary>
    [Fact]
    public void StopEverything_DoesNotReConsumeTheEscapeGraceWindow()
        => Assert.DoesNotContain("SettingsPaletteWindow.TryConsumeEscape", PanicStopEverySurfaceBody());

    /// <summary>
    /// The game probe has to be READ BEFORE the stop pass closes those windows - sampled after, it
    /// would always be false and the exit-tap guard above would never fire.
    /// </summary>
    [Fact]
    public void TheGameProbe_IsSampledBeforeTheStopPass()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs"));
        var probe = source.IndexOf("bool gameOwnedTheScreen = AnyGameSurfaceOwnsTheScreen();", StringComparison.Ordinal);
        Assert.True(probe >= 0, "the pre-stop game probe is gone - the exit-tap guard cannot work without it");
        var stop = source.IndexOf("PanicStopEverySurface();", probe, StringComparison.Ordinal);
        Assert.True(stop > probe, "the probe must be read before PanicStopEverySurface() closes those windows");
    }

    /// <summary>
    /// ...and it must cover every surface that used to consume the press on its own rung, or that
    /// surface's users get the reflexive-double-tap app exit back.
    /// </summary>
    [Theory]
    [InlineData("App.Chaos?.IsDescending")]
    [InlineData("DtrhHostService.IsActive")]
    [InlineData("ArcademyHostService.IsActive")]
    [InlineData("FypHostService.IsActive")]
    [InlineData("JustDropHostService.IsActive")]
    public void TheGameProbe_CoversEveryHandOffSurface(string call)
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs"));
        var start = source.IndexOf("private static bool AnyGameSurfaceOwnsTheScreen()", StringComparison.Ordinal);
        Assert.True(start >= 0, "AnyGameSurfaceOwnsTheScreen was renamed - update this test with it");
        var end = source.IndexOf("        /// <summary>", start, StringComparison.Ordinal);
        var body = end > start ? source[start..end] : source[start..];
        Assert.Contains(call, body);
    }
}
