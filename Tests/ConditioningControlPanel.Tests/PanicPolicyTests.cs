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
