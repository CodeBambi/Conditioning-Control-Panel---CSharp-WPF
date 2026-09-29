using System;
using System.Reflection;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// DESK-RUN 34: "a session or a lockdown starting: the card goes to the Inbox instead". The landing
/// reads the world from the app, so this runs its own <c>ReadWorld</c> with a session running and
/// nothing else on: no Strict Lock, no program, no lockdown. An invite must wait, not knock (sound,
/// Emi line, topmost card) over the session.
/// </summary>
[Collection(AppSessionFlagCollection.Name)]
public class FriendsLandingWorldTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_invite_during_an_ordinary_session_waits()
    {
        var before = App.IsSessionRunning;
        try
        {
            App.IsSessionRunning = true;
            var read = typeof(FriendsLanding).GetMethod("ReadWorld", BindingFlags.NonPublic | BindingFlags.Static)!;
            var world = (LandingWorld)read.Invoke(null, null)!;
            var invite = new InboxItem("0123456789abcdef", SendKind.Invite, "u_ann", "Ann", null, null,
                InviteDestination.BackRoom, null, null, T0, T0.AddMinutes(5));
            var route = LandingRules.Decide(invite, world with { PanelVisible = true }, T0.AddSeconds(10));

            Assert.True(world.Holding, $"a plain session reads {world}: nothing holds, so a knock card goes up over it");
            Assert.Equal(LandingRoute.Hold, route);
        }
        finally { App.IsSessionRunning = before; }
    }

    [Fact]
    public void No_session_holds_nothing()
    {
        var before = App.IsSessionRunning;
        try
        {
            App.IsSessionRunning = false;
            var read = typeof(FriendsLanding).GetMethod("ReadWorld", BindingFlags.NonPublic | BindingFlags.Static)!;
            var world = (LandingWorld)read.Invoke(null, null)!;
            Assert.False(world.Holding);
        }
        finally { App.IsSessionRunning = before; }
    }
}

/// <summary><c>App.IsSessionRunning</c> is process-wide; the suite that sets it runs alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class AppSessionFlagCollection
{
    public const string Name = "App.IsSessionRunning static";
}
