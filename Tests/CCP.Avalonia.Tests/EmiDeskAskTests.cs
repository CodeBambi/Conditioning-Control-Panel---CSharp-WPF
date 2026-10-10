using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>EMI's offers on the head: the two chips go up and wait, yes runs the effect once it is
/// re-checked, a line behind the question parks, an ignored question is a face and no effect, and
/// the game and Brain Drain moments fire once on each edge.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class EmiDeskAskTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static LineDraw Line(string id, string text) => new(id, "t", text, "^_^", null, 2, false, 0);

    private static AskDraw Ask(string effect) => new("ask.k13", "summoned", "want the packs?", "^_^",
        new[] { "sure", "nah" }, Line("ask.k13.yes", "on it"), Line("ask.k13.no", "ok"), effect, null);

    private static async Task Run(Func<EmiDeskWindow, List<string>, Task> body)
    {
        EnsureApp();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldHead = EmiOffers.Head;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var svc = EmiDeskService.Instance;
        var heard = new List<string>();
        EventHandler<EmiMoment> onMoment = (_, m) => heard.Add(m.Id);
        svc.MomentFired += onMoment;
        var w = new EmiDeskWindow();
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            await body(w, heard);
        }
        finally
        {
            svc.MomentFired -= onMoment;
            w.ShutDown();
            EmiOffers.Head = oldHead;
            EmiLineEngine.Instance.ResetForTests();
            CoreSettings.ServiceProvider = oldSettings;
        }
    }

    [Fact]
    public Task TheChipsWait_ALineParks_AndYesRunsTheEffect() => AvaloniaTestDispatcher.RunAsync(() => Run((w, heard) =>
    {
        var urls = new List<string>();
        EmiOffers.Head = new EmiOffers.HeadSeams { OpenUrl = urls.Add };

        w.ShowAsk(Ask("packs"));
        Assert.True(w.AskLive);
        Assert.Empty(w.ChipLabels);                      // the dots come first
        w.FinishAskCadence();
        Assert.Equal(new[] { "sure", "nah" }, w.ChipLabels);
        Assert.Equal("want the packs?", w.BubbleText);
        Assert.True(w.BubbleHost.IsHitTestVisible);      // open only while there is something to click

        w.SpeakLine(Line("t.other", "something else"));  // she does not talk over her own offer
        Assert.NotEqual("t.other", w.LastSpokenLineId);

        w.AnswerAsk(0);
        Assert.False(w.AskLive);
        Assert.Empty(w.ChipLabels);
        Assert.False(w.BubbleHost.IsHitTestVisible);
        Assert.Contains("askAnswered", heard);
        Assert.Equal("ask.k13.yes", w.LastSpokenLineId);
        Assert.Equal(new[] { ConditioningControlPanel.Services.DiscordLinks.PackCatalogue }, urls);

        w.AnswerAsk(0);                                   // a second click on nothing is nothing
        Assert.Single(urls);
        return Task.CompletedTask;
    }));

    [Fact]
    public Task AccessIsReCheckedOnTheClick_AndNoRunsNothing() => AvaloniaTestDispatcher.RunAsync(() => Run((w, heard) =>
    {
        var urls = new List<string>();
        EmiOffers.Head = new EmiOffers.HeadSeams { OpenUrl = urls.Add };
        w.ShowAsk(Ask("packs"));
        w.FinishAskCadence();
        EmiOffers.Head = new EmiOffers.HeadSeams();      // the world moved while she waited
        w.AnswerAsk(0);
        Assert.Empty(urls);
        Assert.Contains("askAnswered", heard);

        EmiOffers.Head = new EmiOffers.HeadSeams { OpenUrl = urls.Add };
        w.ShowAsk(Ask("packs"));
        w.FinishAskCadence();
        w.AnswerAsk(1);                                   // no: her reply, and no effect
        Assert.Empty(urls);
        Assert.Equal("ask.k13.no", w.LastSpokenLineId);
        return Task.CompletedTask;
    }));

    [Fact]
    public Task AnIgnoredQuestionIsAFace_AndAFullScreenFeatureTakesItDown() => AvaloniaTestDispatcher.RunAsync(() => Run((w, heard) =>
    {
        var urls = new List<string>();
        EmiOffers.Head = new EmiOffers.HeadSeams { OpenUrl = urls.Add };
        w.ShowAsk(Ask("packs"));
        w.FinishAskCadence();
        w.CancelAsk("escape");
        Assert.False(w.AskLive);
        Assert.Empty(w.ChipLabels);
        Assert.Equal("-_-", w.Face);
        Assert.Contains("askIgnored", heard);
        Assert.DoesNotContain("askAnswered", heard);
        Assert.Empty(urls);

        heard.Clear();
        w.ShowAsk(Ask("packs"));
        w.FinishAskCadence();
        EmiDeskService.Instance.Fire("intakeOpened");     // a kill moment (WPF AskKillMoments)
        Assert.False(w.AskLive);
        Assert.Contains("askIgnored", heard);
        return Task.CompletedTask;
    }));

    [Fact]
    public Task GamesAndBrainDrainFireOncePerEdge() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        Assert.Equal("arcademy", GameWindow.EmiMomentStem("arcademy"));
        Assert.Equal("dtrh", GameWindow.EmiMomentStem("dtrh"));
        Assert.Equal("fyp", GameWindow.EmiMomentStem("fyp"));
        Assert.Null(GameWindow.EmiMomentStem("goon"));

        var svc = EmiDeskService.Instance;
        var heard = new List<string>();
        EventHandler<EmiMoment> onMoment = (_, m) => heard.Add(m.Id);
        var (oldShowing, oldDefer) = (EmiDrainWatch.Showing, EmiDrainWatch.Defer);
        bool showing = false;
        svc.MomentFired += onMoment;
        try
        {
            EmiDrainWatch.ResetForTests();
            EmiDrainWatch.Showing = () => showing;
            EmiDrainWatch.Defer = a => a();

            EmiDrainWatch.Down();                         // a stop that never had a start says nothing
            Assert.Empty(heard);
            showing = true;
            EmiDrainWatch.Up(40, false);
            EmiDrainWatch.Up(55, true);                   // the dial moved, or the windows were rebuilt
            Assert.Equal(new[] { "brainDrainOn" }, heard);
            showing = false;
            EmiDrainWatch.Down();
            EmiDrainWatch.Down();
            Assert.Equal(new[] { "brainDrainOn", "brainDrainOff" }, heard);
        }
        finally
        {
            svc.MomentFired -= onMoment;
            (EmiDrainWatch.Showing, EmiDrainWatch.Defer) = (oldShowing, oldDefer);
            EmiDrainWatch.ResetForTests();
            EmiLineEngine.Instance.ResetForTests();
        }
        return Task.CompletedTask;
    });
}
