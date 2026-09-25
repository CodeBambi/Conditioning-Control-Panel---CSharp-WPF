using System;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The casino needs an account. Every door into it (Play card, Exclusives, the Sparkle wallet, EMI,
/// the friends drawer) calls <see cref="BackRoomHostService.Launch"/>, so the gate there is the fence.
/// </summary>
[Collection("LauncherSignIn")]
public class BackRoomSignInGateTests : IDisposable
{
    private readonly Func<bool> _previousProbe = LauncherCatalogue.SignedIn;
    private readonly Action _previousPrompt = BackRoomHostService.RequestSignIn;

    public void Dispose()
    {
        LauncherCatalogue.SignedIn = _previousProbe;
        BackRoomHostService.RequestSignIn = _previousPrompt;
    }

    [Fact]
    public void Signed_out_Launch_asks_for_sign_in_and_opens_nothing()
    {
        LauncherCatalogue.SignedIn = () => false;
        int asked = 0;
        BackRoomHostService.RequestSignIn = () => asked++;

        BackRoomHostService.Launch();

        Assert.Equal(1, asked);
        Assert.False(BackRoomHostService.IsActive);
    }

    [Fact]
    public void A_prompt_that_throws_never_escapes_Launch()
    {
        LauncherCatalogue.SignedIn = () => false;
        BackRoomHostService.RequestSignIn = () => throw new InvalidOperationException("no window");

        BackRoomHostService.Launch();

        Assert.False(BackRoomHostService.IsActive);
    }
}
