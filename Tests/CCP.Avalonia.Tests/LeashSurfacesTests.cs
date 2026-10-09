using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Wave 5 r11: LeashSurfaces (WPF 7.1.5): event notices in the corner while the panel is
/// up, held while it is away and said when it is back; a tug asks for the wobble; an accepted offer
/// of ours plays the snap.</summary>
[Collection(RunsAloneCollection.Name)]   // swaps LeashSurfaces / LeashOverlay seams
public sealed class LeashSurfacesTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static readonly LeashPerson Vex = new("h1", "Vex", null);

    [Fact]
    public async Task Notices_ToastWhileUp_HoldWhileAway_SnapOnAccept()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            LeashFx.ForceStill = true;
            var toasts = new List<string>();
            var (oldToast, oldPanel) = (LeashSurfaces.Toast, LeashSurfaces.PanelShowing);
            bool panel = true;
            LeashSurfaces.Toast = (t, _) => toasts.Add(t);
            LeashSurfaces.PanelShowing = () => panel;
            Control? up = null;
            LeashOverlay.ShowOverride = c => up = c;
            int tugs = 0;
            void OnTug() => tugs++;
            LeashSurfaces.TugArrived += OnTug;
            try
            {
                LeashSurfaces.OnEvent(new LeashEvent("e1", LeashEventKind.Reward, Vex, DateTimeOffset.UtcNow, Reward: RewardKind.Sticker, StickerOrPoke: "good"));
                Assert.Single(toasts);

                panel = false;
                LeashSurfaces.OnEvent(new LeashEvent("e2", LeashEventKind.Assign, Vex, DateTimeOffset.UtcNow));
                Assert.Single(toasts);
                Assert.Single(LeashSurfaces.Held);
                panel = true;
                LeashSurfaces.FlushHeldNotices();
                Assert.Equal(2, toasts.Count);
                Assert.Empty(LeashSurfaces.Held);

                LeashSurfaces.OnEvent(new LeashEvent("e3", LeashEventKind.Tug, Vex, DateTimeOffset.UtcNow));
                Assert.Equal(1, tugs);

                LeashSurfaces.OnEvent(new LeashEvent("e4", LeashEventKind.Answered, Vex, DateTimeOffset.UtcNow, Accepted: true));
                Assert.IsType<LeashSnapCard>(up);
            }
            finally
            {
                LeashSurfaces.TugArrived -= OnTug;
                (LeashSurfaces.Toast, LeashSurfaces.PanelShowing) = (oldToast, oldPanel);
                LeashOverlay.ShowOverride = null;
                LeashOverlay.Close();
            }
            return Task.CompletedTask;
        });
    }
}
