using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF 7.1.5 Settings rows the port had dropped: Notifications' banner pool switch and
/// Account's friends presence switch (same switch as the drawer header).</summary>
public sealed class SettingsRowsParityTests
{
    [Fact]
    public Task BannerPoolAndFriendsPresenceWriteTheirSettings() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldMark = PresenceAsk.MarkAsked;
        var oldFriends = global::ConditioningControlPanel.Avalonia.Platform.FriendsHead.Service;
        var service = new SettingsService();
        service.Current.BannerPoolEnabled = true;
        service.Current.FriendsPresenceShared = false;
        CoreSettings.ServiceProvider = () => service;
        global::ConditioningControlPanel.Avalonia.Platform.FriendsHead.Service = null;
        bool asked = false;
        PresenceAsk.MarkAsked = () => asked = true;
        try
        {
            var notes = new NotificationsSettingsSection();
            var pool = notes.FindControl<CheckBox>("ChkBannerPool")!;
            Assert.True(pool.IsChecked);
            pool.IsChecked = false;
            Assert.False(service.Current.BannerPoolEnabled);

            var account = new AccountSettingsSection();
            var presence = account.FindControl<CheckBox>("ChkFriendsPresence")!;
            Assert.False(presence.IsChecked);
            presence.IsChecked = true;
            Assert.True(service.Current.FriendsPresenceShared);
            Assert.True(asked);
        }
        finally
        {
            service.SealForReset(); // a throwaway service: its debounced save must never land on disk
            CoreSettings.ServiceProvider = oldProvider;
            PresenceAsk.MarkAsked = oldMark;
            global::ConditioningControlPanel.Avalonia.Platform.FriendsHead.Service = oldFriends;
        }
        return Task.CompletedTask;
    });
}
