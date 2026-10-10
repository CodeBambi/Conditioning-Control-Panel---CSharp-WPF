using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>HC8, the moment bus on the head: a drawn line reaches her bubble, a hold is a face with no
/// bubble, a fire while she is away only arms holds, and every panic route arms her silence.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class EmiDeskMomentTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static async Task Pump(int ms)
    {
        for (int t = 0; t < ms; t += 20)
        {
            await Task.Delay(20);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public Task ALineReachesHerBubble_AHoldIsAFaceWithNoBubble() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var w = new EmiDeskWindow();
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();

            w.SpeakLine(new LineDraw("t.line", "t", "hello there", "^_^", null, 2, false, 0));
            Assert.Equal("t.line", w.LastSpokenLineId);
            string? seen = null;
            for (int i = 0; i < 200 && seen != "hello there"; i++) { await Pump(40); seen = w.BubbleText ?? seen; }
            Assert.Equal("hello there", seen);

            w.CancelChain();
            w.HoldFace(new LineDraw("t.hold", "t", string.Empty, "-_-", null, 3, true, 60));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("-_-", w.LastHoldFace);
            Assert.Equal("t.line", w.LastSpokenLineId);   // a hold is not a spoken line
            await Pump(200);   // the hold lets go on its own: every effect has an out
        }
        finally
        {
            w.ShutDown();
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    [Fact]
    public Task WhileSheIsAway_AFireOnlyArmsHolds_AndNeverThrows() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        await Task.CompletedTask;
        var svc = EmiDeskService.Instance;
        var engine = EmiLineEngine.Instance;
        Assert.False(svc.IsOut);
        var heard = new List<string>();
        EventHandler<EmiMoment> onMoment = (_, m) => heard.Add(m.Id);
        svc.MomentFired += onMoment;
        try
        {
            engine.ResetForTests();
            Assert.True(engine.Ready);                    // the lines file ships beside the head
            Assert.Equal((Action<string, object?>)svc.Fire, EmiDeskBus.Sink);

            EmiDeskBus.Fire("levelUp", new { level = 3 });
            EmiDeskBus.Fire("not a moment in the file");
            Assert.False(engine.HoldActive);              // chatter is skipped while she is away

            EmiDeskBus.Fire("attentionCheckShown");
            Assert.True(engine.HoldActive);               // a hold is safety: it arms even while away
            EmiDeskBus.ReleaseHold("attentionCheckShown");
            Assert.False(engine.HoldActive);

            Assert.Equal(new[] { "levelUp", "not a moment in the file", "attentionCheckShown" }, heard);
            Assert.Equal("firstContact", EmiDeskService.ChooseGreetMoment(EmiLineEngine.FromJson("{\"version\":1,\"moments\":{},\"pools\":{},\"asks\":[]}")));
        }
        finally
        {
            svc.MomentFired -= onMoment;
            engine.ResetForTests();
        }
    });

    [Fact]
    public Task EveryPanicRouteArmsHerFiveMinuteSilence() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        await Task.CompletedTask;
        _ = EmiDeskService.Instance;                      // the sink is wired on first touch
        var engine = EmiLineEngine.Instance;
        var prevHold = PanicSurfaces.SafetyHold;
        PanicSurfaces.SafetyHold = () => { };             // Circe's hold is not this test's business
        try
        {
            engine.ResetForTests();
            Assert.False(engine.HoldActive);

            PanicSurfaces.ArmSafetyHold();

            Assert.True(engine.HoldActive);
            Assert.Null(engine.Draw("levelUp"));          // nothing speaks inside the tail
        }
        finally
        {
            PanicSurfaces.SafetyHold = prevHold;
            engine.ResetForTests();
        }
    });
}
