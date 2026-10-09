using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Owner report 2026-10-09: "Mute avatar" in the tube's right-click menu did nothing. Most of
/// that menu had no Click handler. Each test raises the item's own Click.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class AvatarTubeMenuTests
{
    private static MenuItem Item(AvatarTubeWindow tube, string name)
    {
        var item = tube.FindControl<MenuItem>(name);
        Assert.NotNull(item);
        return item!;
    }

    private static void Click(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task MuteAvatar_FlipsTheSetting_AndTheHeaderFollows() => Run(tube =>
    {
        var item = Item(tube, "MenuItemMute");
        Assert.Equal(Loc.Get("menu_mute_avatar_off"), item.Header as string);
        Assert.False(tube.IsMuted);

        Click(item);
        Assert.True(CoreSettings.Current.AvatarMuted);
        Assert.True(tube.IsMuted);   // the tube obeys at once
        Assert.Equal(Loc.Get("menu_mute_avatar_on"), item.Header as string);
        Assert.Equal(Color.FromRgb(255, 99, 71), (item.Foreground as ISolidColorBrush)?.Color);

        Click(item);
        Assert.False(CoreSettings.Current.AvatarMuted);
        Assert.Equal(Loc.Get("menu_mute_avatar_off"), item.Header as string);
        Assert.Equal(Colors.White, (item.Foreground as ISolidColorBrush)?.Color);
    });

    [Fact]
    public Task TriggerMode_FlipsTheSetting_AndTheHeaderFollows() => Run(tube =>
    {
        var item = Item(tube, "MenuItemTriggerMode");
        Assert.Equal(Loc.Get("menu_trigger_mode_off"), item.Header as string);

        Click(item);
        Assert.True(CoreSettings.Current.TriggerModeEnabled);
        Assert.Equal(Loc.Get("menu_trigger_mode_on"), item.Header as string);

        Click(item);
        Assert.False(CoreSettings.Current.TriggerModeEnabled);
        Assert.Equal(Loc.Get("menu_trigger_mode_off"), item.Header as string);
        Assert.NotNull(item.Foreground);
    });

    [Fact]
    public Task MuteWhispers_FlipsTheMute_NeverTheMasterEnable() => Run(tube =>
    {
        var item = Item(tube, "MenuItemMuteWhispers");
        var enabledBefore = CoreSettings.Current.SubAudioEnabled;
        Assert.Equal(Loc.Get("menu_mute_whispers_off"), item.Header as string);

        Click(item);
        Assert.True(CoreSettings.Current.SubAudioMuted);
        Assert.Equal(enabledBefore, CoreSettings.Current.SubAudioEnabled);
        Assert.Equal(Loc.Get("menu_mute_whispers_on"), item.Header as string);

        Click(item);
        Assert.False(CoreSettings.Current.SubAudioMuted);
        Assert.Equal(Loc.Get("menu_mute_whispers_off"), item.Header as string);
    });

    [Fact]
    public Task EveryVisibleItem_NamesItsColour_AndPauseBrowserIsHidden() => Run(tube =>
    {
        tube.UpdateQuickMenuState();
        foreach (var name in new[] { "MenuItemTalkToBambi", "MenuItemDetach", "MenuItemEngine", "MenuItemTriggerMode",
                     "MenuItemBambiTakeover", "MenuItemMute", "MenuItemShowChatHistory", "MenuItemMuteWhispers" })
            Assert.True(Item(tube, name).Foreground != null, name + " has no Foreground");
        // No browser seam can mute or pause media on this head, so the item does not promise it.
        Assert.False(Item(tube, "MenuItemPauseBrowser").IsVisible);
        // Stopped engine: the item offers Start, in green, and is clickable.
        var engine = Item(tube, "MenuItemEngine");
        Assert.Equal(Loc.Get("menu_start_engine"), engine.Header as string);
        Assert.True(engine.IsEnabled);
    });

    [Fact]
    public Task DoubleClickWhileMuted_ShowsTheMutedIndicator() => Run(tube =>
    {
        CoreSettings.Current.AvatarMuted = true;
        var bubble = tube.FindControl<Border>("SpeechBubble")!;
        bubble.IsVisible = false;
        tube.ShowMutedIndicator();
        Dispatcher.UIThread.RunJobs();
        Assert.True(bubble.IsVisible);
        Assert.StartsWith("MUTED", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
    });

    private static Task Run(Action<AvatarTubeWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        bool muted = s.AvatarMuted, trigger = s.TriggerModeEnabled, whispers = s.SubAudioMuted, detached = s.AvatarTubeDetached;
        s.AvatarTubeDetached = false;
        s.AvatarMuted = false;
        s.TriggerModeEnabled = false;
        s.SubAudioMuted = false;
        var main = new Window { Width = 1000, Height = 700, Position = new PixelPoint(700, 200) };
        AvatarTubeWindow? tube = null;
        try
        {
            main.Show();
            tube = new AvatarTubeWindow(main);
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            body(tube);
        }
        finally
        {
            tube?.Close();
            main.Close();
            Dispatcher.UIThread.RunJobs();
            s.AvatarMuted = muted;
            s.TriggerModeEnabled = trigger;
            s.SubAudioMuted = whispers;
            s.AvatarTubeDetached = detached;
            CoreSettings.Save();
            service.SealForReset();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
