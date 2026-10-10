using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>HB22: the subliminal card reads the "steals focus" switch (WPF SubliminalService.cs:730,
/// :764-765). Swaps the process-wide activate seam, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalStealsFocusTests
{
    [Fact]
    public Task The_card_takes_the_foreground_only_with_the_switch_on() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var before = SubliminalOverlay.ActivateCard;
        var activated = 0;
        SubliminalOverlay.ActivateCard = _ => activated++;
        var off = new SubliminalOverlayWindow("OBEY");
        var on = new SubliminalOverlayWindow("OBEY");
        try
        {
            SubliminalOverlay.Present(off, stealsFocus: false);
            Assert.True(off.IsVisible);
            Assert.Equal(0, activated);

            SubliminalOverlay.Present(on, stealsFocus: true);
            Assert.True(on.IsVisible);
            Assert.Equal(1, activated);
            Assert.False(on.ShowActivated);   // shown passive first, as WPF's SWP_NOACTIVATE show, then activated

            // A platform that refuses the focus never breaks the card.
            SubliminalOverlay.ActivateCard = _ => throw new InvalidOperationException("refused");
            var refused = new SubliminalOverlayWindow("OBEY");
            SubliminalOverlay.Present(refused, stealsFocus: true);
            Assert.True(refused.IsVisible);
            refused.Close();
        }
        finally
        {
            SubliminalOverlay.ActivateCard = before;
            off.Close(); on.Close();
        }
        return Task.CompletedTask;
    });
}
