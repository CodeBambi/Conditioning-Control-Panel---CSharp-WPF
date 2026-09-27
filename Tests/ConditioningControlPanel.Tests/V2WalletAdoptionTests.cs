using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// When a counter reply is allowed to move the Sparkle Points wallet. The two rules exist because
/// both ways of getting this wrong lose the player real points and MergeMax cannot get them back.
/// </summary>
public class V2WalletAdoptionTests
{
    [Fact]
    public void ABuyReceiptIsSettledMoney_SoItLowersTheWallet()
    {
        Assert.True(V2WalletAdoption.Decide(sameAccount: true, fromBuy: true, serverSp: 70, localSp: 100, out var next));
        Assert.Equal(70, next);
    }

    [Fact]
    public void ABuyReceiptRaisesToo_WhenAnotherDeviceHasBeenEarning()
    {
        Assert.True(V2WalletAdoption.Decide(true, true, 900, 100, out var next));
        Assert.Equal(900, next);
    }

    [Fact]
    public void APlainReadNEVERLowersTheWallet()
    {
        // The client credits level-up and bubble points locally before any sync pushes them, so a
        // counter read taken right after a level-up is simply older than the wallet.
        Assert.False(V2WalletAdoption.Decide(true, false, serverSp: 100, localSp: 130, out var next));
        Assert.Equal(130, next);
    }

    [Fact]
    public void APlainReadMayRaiseTheWallet_ThatIsWhatItIsFor()
    {
        Assert.True(V2WalletAdoption.Decide(true, false, 500, 130, out var next));
        Assert.Equal(500, next);
    }

    [Fact]
    public void AnAccountThatChangedUnderTheReplyIsDropped()
    {
        // The write lands on the UI thread after an await: by then the player may be someone else,
        // and account A's balance must not be saved into account B's settings.
        Assert.False(V2WalletAdoption.Decide(sameAccount: false, fromBuy: true, serverSp: 70, localSp: 100, out var next));
        Assert.Equal(100, next);
        Assert.False(V2WalletAdoption.Decide(false, false, 500, 100, out next));
        Assert.Equal(100, next);
    }

    [Fact]
    public void NothingIsWrittenWhenNothingChanges()
    {
        Assert.False(V2WalletAdoption.Decide(true, true, 100, 100, out _));
        Assert.False(V2WalletAdoption.Decide(true, false, 100, 100, out _));
    }

    [Fact]
    public void ANegativeBalanceIsNonsenseAndIsIgnored()
        => Assert.False(V2WalletAdoption.Decide(true, true, -1, 100, out _));
}
