using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Lane E1, Companion: Chat is the v2 conversation page and its header carries the on
/// switch (7.1.1, WPF CompanionOnSwitchTests: the switch must be reachable without any sheet);
/// Personality, Permissions, Links and AI are registered pages that adopt the room's LIVE zones.</summary>
public sealed class CompanionPagesShellTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void Pump()
    {
        for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task ChatHeaderCarriesShowWhileTheCompanionIsOff() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarEnabled = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("companion");
            Pump();
            var tab = shell.GetLogicalDescendants().OfType<CompanionTabView>().Single();
            var page = tab.Conversation;
            Assert.NotNull(page);
            Assert.False(tab.RoomView.IsVisible, "the old room stays collapsed under the conversation");
            var hero = tab.RoomView.HeroZone.ViewModel!;
            hero.Sync();
            Pump();

            var show = page!.FindControl<Button>("BtnShow")!;
            var hide = page.FindControl<Button>("BtnHide")!;
            Assert.True(show.IsVisible, "off: Show leads in the page header");
            Assert.False(hide.IsVisible);
            Assert.Same(hero.ToggleShownCommand, show.Command);
            Assert.Same(hero.ToggleShownCommand, hide.Command);
            Assert.Same(hero.DetachCommand, page.FindControl<Button>("BtnPopOut")!.Command);
            Assert.Same(hero.ToggleMuteCommand, page.FindControl<Button>("BtnVoice")!.Command);

            CoreSettings.Current.AvatarEnabled = true;
            hero.Sync();
            Pump();
            Assert.False(show.IsVisible);
            Assert.True(hide.IsVisible, "on: Hide takes its place");
        }
        finally
        {
            shell.Close();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void OldSheetsOpenTheirPages()
    {
        Assert.Equal("personality", ConversationPage.PageFor("who"));
        Assert.Equal("permissions", ConversationPage.PageFor("permissions"));
        Assert.Equal("companionlinks", ConversationPage.PageFor("videos"));
        Assert.Equal("companionai", ConversationPage.PageFor("memory"));
        Assert.Equal("companionai", ConversationPage.PageFor("connection"));
        Assert.Null(ConversationPage.PageFor("nothing"));
    }

    [Fact]
    public Task CompanionPagesAdoptTheRoomsLiveZones() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Pump();
            foreach (var key in new[] { "personality", "permissions", "companionlinks", "companionai" })
                Assert.True(shell.IsRegisteredNavTab(key), key + " is a registered page, not a stand-in");

            var room = shell.Named<CompanionTabView>("CompanionTab")!.RoomView;
            var host = shell.Named<Grid>("LaneTabHost")!;

            shell.ShowTab("personality");
            Pump();
            var personality = host.Children.OfType<PersonalityPage>().Single();
            Assert.Same(room.FindControl<MakeHerYoursView>("PersonalityZone"), personality.PresetsHost.Content);
            Assert.NotNull(personality.PickerHost.Content);
            Assert.NotNull(personality.CommunityHost.Content);

            shell.ShowTab("permissions");
            Pump();
            var permissions = host.Children.OfType<PermissionsPage>().Single();
            Assert.Same(room.FindControl<AiPermissionsGrid>("PermissionsZone"), permissions.PermissionsHost.Content);

            shell.ShowTab("companionlinks");
            Pump();
            var links = host.Children.OfType<LinksPage>().Single();
            Assert.NotNull(links.VideosHost.Content);
            Assert.True(links.KnowledgeLinks.SaveImmediately);

            shell.ShowTab("companionai");
            Pump();
            var ai = host.Children.OfType<AiPage>().Single();
            Assert.Same(room.FindControl<EngineRoomDrawer>("EngineZone"), ai.EngineHost.Content);
            Assert.Same(room.FindControl<MemoryDiaryView>("MemoryZone"), ai.MemoryHost.Content);
            Assert.NotNull(ai.BehaviorHost.Content);   // how often it talks: an open card (7.1.3)
            Assert.NotNull(ai.TriggersHost.Content);
            Assert.IsType<PreferredNameEditor>(ai.NameHost.Content);

            // Back to Personality: the same live zone, adopted again, never a copy.
            shell.ShowTab("personality");
            Pump();
            Assert.Same(room.FindControl<MakeHerYoursView>("PersonalityZone"), personality.PresetsHost.Content);
        }
        finally
        {
            shell.Close();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
