using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The media-source picker (WPF MainWindow.Assets.cs:2208-3230, main 3e225c963..686f8c960),
/// driven from the Assets tab ctor through its chips, cards, Fine-tune row and Add button.
/// No network: the consent modal and the Scrolller probe are faked.</summary>
public sealed class AssetsMediaPickerTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static async Task WithView(Func<AssetsTabView, AppSettings, Task> body)
    {
        Setup();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldProbe = AssetsTabView.ProbeSub;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        Window? w = null;
        try
        {
            var s = CoreSettings.Current;
            s.MediaSource = "local";
            s.RemoteMediaConsented = false;
            s.FypOnlineConsented = false;
            s.FypOnlineNiches = new() { FypOnlineCoordinator.Catalog[0].Id };
            s.FypOnlineCustomSubs = new();
            foreach (var row in s.BuildRemoteSubLibraryView()) s.RemoveLibrarySub(row.Name);
            var view = new AssetsTabView();
            w = new Window { Width = 1200, Height = 900, Content = view };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            await body(view, s);
        }
        finally
        {
            w?.Close();
            AssetsTabView.ConsentOverride = null;
            AssetsTabView.ProbeSub = oldProbe;
            CoreSettings.ServiceProvider = oldProvider;
        }
    }

    private static ToggleButton Chip(AssetsTabView v, string key) =>
        v.FindControl<WrapPanel>("RemoteSourceChips")!.Children.OfType<ToggleButton>().Single(c => (string?)c.Tag == key);

    [Fact]
    public Task SourceSwitchWritesOnlyAfterTheAwaitedConsentSaysYes() => AvaloniaTestDispatcher.RunAsync(() => WithView(async (view, s) =>
    {
        var details = view.FindControl<StackPanel>("RemoteMediaDetails")!;
        Assert.False(details.IsVisible);

        // No: nothing changes, the chips go back to "local".
        var ask = new TaskCompletionSource<bool>();
        AssetsTabView.ConsentOverride = (_, _) => ask.Task;
        Chip(view, "online").IsChecked = true;
        Assert.Equal("local", s.MediaSource);           // not written while the ask is open
        Assert.True(Chip(view, "local").IsChecked);
        Assert.False(Chip(view, "online").IsChecked);
        ask.SetResult(false);
        await view.LastPickerTask;
        Assert.Equal("local", s.MediaSource);
        Assert.False(s.RemoteMediaConsented);
        Assert.False(details.IsVisible);

        // Yes: consent stored, source switched, the details unfold; Both shows the share slider.
        AssetsTabView.ConsentOverride = (_, _) => Task.FromResult(true);
        Chip(view, "online").IsChecked = true;
        await view.LastPickerTask;
        Assert.Equal("online", s.MediaSource);
        Assert.True(s.RemoteMediaConsented);
        Assert.True(details.IsVisible);
        Assert.False(view.FindControl<StackPanel>("RemoteRatioRow")!.IsVisible);
        Chip(view, "mixed").IsChecked = true;            // already consented: no second ask
        await view.LastPickerTask;
        Assert.Equal("mixed", s.MediaSource);
        Assert.True(view.FindControl<StackPanel>("RemoteRatioRow")!.IsVisible);
    }));

    [Fact]
    public Task FlavourCardSetsThePoolAndMineBringsTheOldSelectionBack() => AvaloniaTestDispatcher.RunAsync(() => WithView((view, s) =>
    {
        // A selection no flavour equals (Catalog[0] alone is Trance's preset), so it is "Mine".
        s.FypOnlineCustomSubs = new() { "myownsub" };
        var before = s.FypOnlineNiches.ToList();
        var tiles = view.FindControl<UniformGrid>("RemoteFlavourTiles")!.Children.OfType<Button>().ToList();
        Assert.Equal(6, tiles.Count);
        var pink = FlavourPresets.ById("pink")!;
        var tile = tiles.Single(t => (string?)t.Tag == "pink");
        tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var sel = FlavourPresets.Resolve(pink, FypOnlineCoordinator.Catalog);
        Assert.Equal(sel.NicheIds, s.FypOnlineNiches);
        Assert.Equal(sel.CustomSubs, s.FypOnlineCustomSubs);
        Assert.True(Check(tile).IsVisible);
        Assert.False(Check(tiles.Single(t => (string?)t.Tag == "mine")).IsVisible);
        Assert.Equal(AssetsTabView.SummaryText(s.FypOnlineNiches, s.FypOnlineCustomSubs),
            view.FindControl<TextBlock>("TxtRemoteSummary")!.Text);

        // Fine-tune opens the list and the browser steps aside.
        view.FindControl<Button>("BtnRemoteFineTune")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(view.FindControl<ScrollViewer>("RemoteFineTuneScroll")!.IsVisible);
        Assert.False(view.FindControl<Border>("AssetBrowserSection")!.IsVisible);

        tiles.Single(t => (string?)t.Tag == "mine").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(before, s.FypOnlineNiches);
        Assert.Equal(new[] { "myownsub" }, s.FypOnlineCustomSubs);
        return Task.CompletedTask;
    }));

    private static TextBlock Check(Button tile) =>
        ((Grid)tile.Content!).Children.OfType<TextBlock>().Single(t => (string?)t.Tag == "check");

    [Fact]
    public Task AddButtonProbesThenKeepsOrExplains() => AvaloniaTestDispatcher.RunAsync(() => WithView(async (view, s) =>
    {
        AssetsTabView.ProbeSub = (name, _) => Task.FromResult(name == "goodsub"
            ? new SubProbe { Ok = true, VideoCount = 7 }
            : new SubProbe { Ok = false, Error = null });
        var box = view.FindControl<TextBox>("TxtRemoteCustomSub")!;
        var add = view.FindControl<Button>("BtnRemoteAddSub")!;

        box.Text = "goodsub";
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await view.LastPickerTask;
        Assert.True(s.LibraryHasSub("goodsub"));
        Assert.Contains("goodsub", s.FypOnlineCustomSubs);
        Assert.True(add.IsEnabled);

        box.Text = "nosuchsub";
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await view.LastPickerTask;
        Assert.False(s.LibraryHasSub("nosuchsub"));
        Assert.False(s.FypOnlineSubVerdicts["nosuchsub"].Ok);   // a real verdict is remembered
        var error = view.FindControl<TextBlock>("TxtRemoteSubError")!;
        Assert.True(error.IsVisible);
        Assert.Equal(RemoteSubAddMessages.Describe(RemoteSubAddOutcome.NotCarried, "nosuchsub", AppSettings.RemoteSubLibraryCap), error.Text);
        Assert.True(view.FindControl<ScrollViewer>("RemoteFineTuneScroll")!.IsVisible);
    }));
}
