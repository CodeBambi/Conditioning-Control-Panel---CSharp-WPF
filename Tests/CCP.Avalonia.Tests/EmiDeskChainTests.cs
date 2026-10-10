using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Rows E1-E3: her body art loads, the face is the real renderer, chains play and settle,
/// and the idle beats run.</summary>
public sealed class EmiDeskChainTests
{
    [Fact]
    public void FaceRules_SideFacesRotate_KaomojiAreKao()
    {
        Assert.True(EmiFace.IsSide(":)"));
        Assert.False(EmiFace.IsSide("0_0"));
        Assert.True(EmiFace.IsKao("(◕‿◕)"));
        Assert.False(EmiFace.IsKao("^_^"));
        Assert.Equal(137, EmiFace.VirtualHeight);
    }

    [Fact]
    public void Chains_SayCadenceAndPoseMap()
    {
        var say = EmiChains.MakeSay("hi there", "^_^", EmiChains.SayHoldMs("hi there"));
        Assert.Equal("0_0", say.Seq[0].Text);
        Assert.Equal("^_^", say.Seq[3].Text);
        Assert.Equal("...", say.Seq[2].Bubble);
        Assert.Equal("pet", EmiChains.FrameForFace("*_*"));
        Assert.NotNull(EmiChains.Get("pet"));
    }

    [Fact]
    public Task BodyArtLoads_ChainPlaysAndSettles() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var w = new EmiDeskWindow();
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.SetPose("idle");
            var body = w.FindControl<Image>("BodyImage")!;
            Assert.NotNull(body.Source);
            Assert.False(w.FindControl<Border>("BodyPlaceholder")!.IsVisible);

            bool done = false;
            w.PlayChain("pet", () => done = true);
            Assert.True(w.ChainLive);
            Assert.Equal("pet", w.PoseKey);
            for (int t = 0; t < 6000 && !done; t += 20)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(done);
            Assert.False(w.ChainLive);
            Assert.Equal("0_0", w.FaceText);
            Assert.Equal("idle", w.PoseKey);
            Assert.True(w.IdleBeatsRunning);

            // E4: Say types . / .. / ... in the bubble, lands the line, then clears it.
            bool said = false;
            w.Say("hello you", "^_^", () => said = true);
            Assert.Equal(".", w.BubbleText);
            bool landed = false;
            for (int t = 0; t < 12000 && !said; t += 20)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
                if (w.BubbleText == "hello you") landed = true;
            }
            Assert.True(landed);
            Assert.True(said);
            Assert.Null(w.BubbleText);
        }
        finally
        {
            w.ShutDown();
            service.SaveImmediate(); CoreSettings.ServiceProvider = oldSettings;
        }
    });

    [Fact]
    public Task PokeLadder_ThirdGlee_FourthAnnoyed_FifthRage() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var w = new EmiDeskWindow();
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var seen = new System.Collections.Generic.List<string?>();
            for (int i = 0; i < 5; i++)
            {
                w.PetFromClick();
                seen.Add(w.ChainId);
                for (int t = 0; t < 8000 && w.ChainLive; t += 20)
                {
                    await Task.Delay(20);
                    Dispatcher.UIThread.RunJobs();
                }
            }
            Assert.Equal(new[] { "pet", "petFlick", "petStreak", "pokeAnnoy", "pokeRage" }, seen);
        }
        finally
        {
            w.ShutDown();
            service.SaveImmediate(); CoreSettings.ServiceProvider = oldSettings;
        }
    });
}
