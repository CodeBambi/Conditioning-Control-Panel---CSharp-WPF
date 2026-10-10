using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The doors into the Goon window from other surfaces (WPF GoonHostService.Launch(joinCode) /
/// LaunchToHost / OpenRoomForInviteAsync): the Lobby row and host bar, the Tonight Board Live card, the
/// friends invite tile and the launcher badge. Swaps process-wide seams, so alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class GoonDoorsTests
{
    private static string Read(string rel, [CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..")), rel));

    [Fact]
    public void TheInviteTile_IsShutOnlyForAnAccountThatCannotHost()
    {
        var (code, can) = (FriendsInviteCodes.GoonCode, FriendsInviteCodes.CanHostGoon);
        try
        {
            FriendsInviteCodes.GoonCode = () => null;
            FriendsInviteCodes.CanHostGoon = () => false;
            Assert.Equal("friends_invite_goon_prime", FriendsInviteCodes.BlockedKey(InviteDestination.Goon));
            // No room yet but the account may host: the tile is open (it opens a room, then sends its code).
            FriendsInviteCodes.CanHostGoon = () => true;
            Assert.Null(FriendsInviteCodes.BlockedKey(InviteDestination.Goon));
            // A live room: anyone in it can send its code.
            FriendsInviteCodes.GoonCode = () => "ABCD";
            FriendsInviteCodes.CanHostGoon = () => false;
            Assert.Null(FriendsInviteCodes.BlockedKey(InviteDestination.Goon));
        }
        finally { (FriendsInviteCodes.GoonCode, FriendsInviteCodes.CanHostGoon) = (code, can); }
    }

    [Fact]
    public void TheInviteTile_OpensARoomThroughTheGoonWindow()
    {
        var src = Read("CCP.Avalonia/Views/Controls/FriendsDrawer.Pickers.cs");
        Assert.Contains("OpenGoonRoom { get; set; } = Games.GameWindow.OpenGoonRoomForInviteAsync", src);
        Assert.Contains("await InviteToGoonAsync(f.Id)", src);
        Assert.DoesNotContain("SEAM(g3)", src);
    }

    [Fact]
    public void TheLobbyAndTheLiveCard_OpenTheTable_NotTheGamesOwnLobby()
    {
        var lobby = Read("CCP.Avalonia/Views/Windows/MainShellWindow.RegisterSocialTabs.cs");
        Assert.Contains("LobbyJoinGoon { get; set; } = key => Games.GameWindow.LaunchGoon(key)", lobby);
        Assert.Contains("LobbyHostGoon { get; set; } = () => Games.GameWindow.LaunchGoonToHost()", lobby);
        Assert.Contains("if (TierGate.DemandPremium(\"Goon Game\")) LobbyHostGoon();", lobby);   // patrons host
        Assert.DoesNotContain("SEAM(g3)", lobby);
        var board = Read("CCP.Avalonia/Views/Windows/MainShellWindow.DashboardBillboard.cs");
        Assert.Contains("LobbyJoinGoon(key)", board);
        Assert.Contains("LobbyJoinChess(key)", board);
    }

    [Fact]
    public void TheLauncherGoonTile_WearsTheOpenTablesBadge()
    {
        var tile = Read("CCP.Avalonia/Views/Windows/LauncherWindow.axaml.cs");
        Assert.Contains("OpenTablesBadgeFor(card.Id, revealed && !needsAccount)", tile);
        Assert.Contains("HookOpenTables();", tile);
    }
}
