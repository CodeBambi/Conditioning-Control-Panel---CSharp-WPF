using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane k26: the two WPF panic steps the port list lacked. Swaps process-wide seams, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class PanicCardsCloseTests
{
    /// <summary>WPF PanicStopEverySurface "help popover" + "settings palette" (:2019-2024): both close on a
    /// panic, riding the lock-cards line (no new surface), and one failing never skips the other.</summary>
    [Fact]
    public void APanicClosesTheHelpPopoverAndTheSettingsPalette()
    {
        var (helpWas, paletteWas) = (PanicSurfaces.CloseHelpPopover, PanicSurfaces.CloseSettingsPalette);
        int help = 0, palette = 0;
        try
        {
            PanicSurfaces.CloseHelpPopover = () => { help++; throw new InvalidOperationException("help"); };
            PanicSurfaces.CloseSettingsPalette = () => palette++;
            var cards = PanicSurfaces.All.Single(x => x.Id == "lock-cards");
            try { cards.Stop(null); } catch { /* StopAll's own guard; the two closes must have run either way */ }
            Assert.Equal((1, 1), (help, palette));
        }
        finally { (PanicSurfaces.CloseHelpPopover, PanicSurfaces.CloseSettingsPalette) = (helpWas, paletteWas); }
    }
}
