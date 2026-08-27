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
///   2. the override default is ON, including when settings failed to load
///   3. turning the override off restores the old ladder and the old grace pause, byte for byte
///   4. the optional pause key matches nothing until the user binds it, and loses to the panic key
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
        => Assert.Equal(Rung.StopEverything, Decide(lockCardOpen: false, overrideAll: true));

    [Fact]
    public void OverrideOff_NoLockCard_RunsTheOldLadder()
        => Assert.Equal(Rung.RunLadder, Decide(lockCardOpen: false, overrideAll: false));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpenLockCard_OutranksBothModes(bool overrideAll)
        => Assert.Equal(Rung.DismissLockCard, Decide(lockCardOpen: true, overrideAll: overrideAll));

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
    /// The Ctrl+K palette rung, which override mode has to answer too. Escape is the DEFAULT panic
    /// key and the global hook delivers it whatever has focus, so an Escape aimed at closing the
    /// palette lands in the panic handler - and the tail EXITS THE APP on press 2 while the engine
    /// is stopped. Closing a palette must never be press 1 of "quit".
    /// </summary>
    [Fact]
    public void PalettePress_NeverAdvancesTheExitLadder_EvenInOverrideMode()
        => Assert.False(AdvancesExitLadder(Rung.StopEverything, paletteClaimedPress: true));

    [Fact]
    public void PalettePress_StillDoesNotAdvanceOnTheLegacyLadder()
        => Assert.False(AdvancesExitLadder(Rung.RunLadder, paletteClaimedPress: true));

    [Fact]
    public void APressThePaletteDidNotClaim_StillAdvances()
        => Assert.True(AdvancesExitLadder(Rung.StopEverything, paletteClaimedPress: false));

    [Fact]
    public void LockCardPress_RefusesRegardlessOfThePalette()
    {
        Assert.False(AdvancesExitLadder(Rung.DismissLockCard, paletteClaimedPress: true));
        Assert.False(AdvancesExitLadder(Rung.DismissLockCard, paletteClaimedPress: false));
    }

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
}
