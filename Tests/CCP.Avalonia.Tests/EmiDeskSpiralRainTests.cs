using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>EMI's spiral and rain offers on this head (WPF EmiOffers.FireSpiral / FireRain, ShowOverlayTimed,
/// EmiGifRain): the head offers both, the spiral is a timed hold that says overlaySpiralUp, the rain runs
/// on the cascade's own dials and skips when it cannot, no setting moves, and panic takes her rain down.</summary>
[Collection(RunsAloneCollection.Name)]   // swaps EmiOffers.Head, EmiDeskBus.Sink and the service's overlay seams
public sealed class EmiDeskSpiralRainTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private sealed class Rig
    {
        public readonly List<(SpiralHold Hold, int Ms)> Spirals = new();
        public readonly List<double> Rains = new();
        public readonly List<(string Id, object? Ctx)> Moments = new();
        public bool Busy, Images = true;
    }

    private static Task With(Action<Rig, EmiDeskService> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        await Task.CompletedTask;
        var svc = EmiDeskService.Instance;
        var rig = new Rig();
        var head = EmiOffers.Head; var sink = EmiDeskBus.Sink;
        var host = EmiDeskService.EffectHost; var spiral = EmiDeskService.SpiralShow; var rain = EmiDeskService.RainShow;
        var busy = EmiDeskService.RainBusy; var images = EmiDeskService.HasLocalImages;
        var s = CoreSettings.Current;
        var spiralOn = s.SpiralEnabled; var spiralOpacity = s.SpiralOpacity;
        try
        {
            EmiDeskService.EffectHost = () => new Border();
            EmiDeskService.SpiralShow = (_, hold, ms) => rig.Spirals.Add((hold, ms));
            EmiDeskService.RainShow = (_, seconds) => rig.Rains.Add(seconds);
            EmiDeskService.RainBusy = () => rig.Busy;
            EmiDeskService.HasLocalImages = () => rig.Images;
            EmiOffers.Head = svc.BuildOfferSeams();
            EmiDeskBus.Sink = (id, ctx) => rig.Moments.Add((id, ctx));
            body(rig, svc);
            Assert.Equal(spiralOn, s.SpiralEnabled);        // an offer never changes a setting
            Assert.Equal(spiralOpacity, s.SpiralOpacity);
        }
        finally
        {
            EmiOffers.Head = head; EmiDeskBus.Sink = sink;
            EmiDeskService.EffectHost = host; EmiDeskService.SpiralShow = spiral; EmiDeskService.RainShow = rain;
            EmiDeskService.RainBusy = busy; EmiDeskService.HasLocalImages = images;
        }
    });

    [Fact]
    public Task TheHeadOffersBothEffects() => With((_, svc) =>
    {
        var seams = svc.BuildOfferSeams();
        Assert.NotNull(seams.Spiral);
        Assert.NotNull(seams.Rain);
        Assert.True(EmiOffers.EffectFeasible("spiral"));
    });

    [Fact]
    public Task TheSpiralIsATimedHoldAtTheUsersOpacity_AndSaysEffectFired() => With((rig, _) =>
    {
        EmiOffers.FireSpiral(fromAsk: true);
        var (hold, ms) = Assert.Single(rig.Spirals);
        Assert.Equal(EmiOffers.SpiralMs, ms);
        Assert.InRange(hold.Opacity, 0.05, 1.0);
        Assert.Null(hold.Path);                              // the app's own spiral, never a file of hers
        Assert.Contains(rig.Moments, m => m.Id == "effectFired");
    });

    [Fact]
    public Task TheSpiralClampsItsSpan_AndWithNoWindowItDoesNothing() => With((rig, svc) =>
    {
        svc.SpiralFor(10, 5.0);
        Assert.Equal(500, rig.Spirals[0].Ms);
        Assert.Equal(1.0, rig.Spirals[0].Hold.Opacity);
        EmiDeskService.EffectHost = () => null;
        svc.SpiralFor(6000, 0.5);
        Assert.Single(rig.Spirals);
    });

    [Fact]
    public Task TheRainRunsTenSeconds_SkipsWhenRainingOrEmpty_AndClampsToThirty() => With((rig, svc) =>
    {
        svc.RainFor(EmiOffers.RainDuration);
        Assert.Equal(10.0, Assert.Single(rig.Rains));

        rig.Busy = true;
        svc.RainFor(EmiOffers.RainDuration);
        Assert.Single(rig.Rains);                            // a second cascade on top is noise
        rig.Busy = false; rig.Images = false;
        svc.RainFor(EmiOffers.RainDuration);
        Assert.Single(rig.Rains);                            // nothing local to rain
        rig.Images = true;
        svc.RainFor(TimeSpan.FromMinutes(5));
        Assert.Equal(30.0, rig.Rains[1]);
        svc.RainFor(TimeSpan.Zero);
        Assert.Equal(6.0, rig.Rains[2]);
    });

    [Fact]
    public Task TheRealSpiralHoldIsHers_AndTheStopPassReleasesIt() => With((_, svc) =>
    {
        EmiDeskService.SpiralShow = (host, hold, ms) => SpiralOverlay.Hold(null, EmiDeskService.EmiOwner, hold, ms);
        try
        {
            svc.SpiralFor(6000, 0.5);
            Assert.True(SpiralOverlay.IsHeldBy(EmiDeskService.EmiOwner));
            SpiralOverlay.ReleaseAllHolds();                 // what the panic stop pass runs
            Assert.False(SpiralOverlay.IsHeldBy(EmiDeskService.EmiOwner));
        }
        finally { SpiralOverlay.ReleaseAllHolds(); }
    });

    [Fact]
    public Task EveryPanicRouteTakesHerRainDown() => With((_, _) =>
    {
        var prevHold = PanicSurfaces.SafetyHold; var prevStop = PanicSurfaces.StopEmiRain;
        int stops = 0;
        PanicSurfaces.SafetyHold = () => { };
        PanicSurfaces.StopEmiRain = () => stops++;
        try
        {
            PanicSurfaces.ArmSafetyHold();
            Assert.Equal(1, stops);
            PanicSurfaces.StopEmiRain = () => throw new InvalidOperationException("a failing stop");
            PanicSurfaces.ArmSafetyHold();                   // never throws, the rest of the press still runs
        }
        finally { PanicSurfaces.SafetyHold = prevHold; PanicSurfaces.StopEmiRain = prevStop; }
        EmiDeskService.StopRain();                           // safe when nothing is raining
        Assert.NotEqual(EmiDeskService.EmiOwner, GifCascadeOverlay.Owner);
    });
}
