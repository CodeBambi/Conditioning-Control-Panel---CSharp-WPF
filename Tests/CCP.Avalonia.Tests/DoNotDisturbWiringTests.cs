using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>platform#12: the DND guard is consulted by the scheduled flash (App's CoreFlash.ShowProvider)
/// and the scheduled mandatory video (the overlay's scheduler), as WPF FlashService.cs:689 and
/// VideoService.cs:3013 do.</summary>
public sealed class DoNotDisturbWiringTests
{
    [Fact]
    public void ScheduledFlashAndVideoHoldWhileAListedAppIsInFront()
    {
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var fg = "vlc";
        DoNotDisturbGuard.ForegroundForTest = () => fg;
        try
        {
            var s = CoreSettings.Current;
            s.DndProcessList = new List<string> { "vlc" };
            s.DndSuppressFlashes = true;
            s.DndSuppressVideos = true;
            Assert.True(DoNotDisturbGuard.HoldsScheduledFlash());
            Assert.True(DoNotDisturbGuard.HoldsScheduledVideo());

            // The real overlay's scheduler asks the guard before a scheduled tick.
            var defer = MandatoryVideoOverlay.Instance.Scheduler.ShouldDefer;
            Assert.NotNull(defer);
            Assert.True(defer!());
            fg = "notepad";
            Assert.False(defer());
            Assert.False(DoNotDisturbGuard.HoldsScheduledFlash());

            fg = "vlc";
            s.DndSuppressFlashes = false;   // flashes are opt-in: off holds nothing
            Assert.False(DoNotDisturbGuard.HoldsScheduledFlash());
        }
        finally
        {
            DoNotDisturbGuard.ForegroundForTest = null;
            CoreSettings.ServiceProvider = oldProvider;
            service.SealForReset();
        }
    }
}
