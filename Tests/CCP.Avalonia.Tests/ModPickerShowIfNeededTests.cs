using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// ModPickerDialog.ShowIfNeeded's guards, in WPF's order
/// (ConditioningControlPanel/Dialogs/ModPickerDialog.xaml.cs:631-647).
/// </summary>
public sealed class ModPickerShowIfNeededTests
{
    [Fact]
    public void ShowsOnlyForAnUnshownOnlineModularInstallWithAPackService()
    {
        Assert.True(ModPickerDialog.ShouldShow(new AppSettings(), hasPackService: true, isFullInstall: false, manifestUnavailable: false));

        Assert.False(ModPickerDialog.ShouldShow(new AppSettings { ModPickerShown = true }, true, false, false));
        Assert.False(ModPickerDialog.ShouldShow(new AppSettings(), hasPackService: false, false, false));
        Assert.False(ModPickerDialog.ShouldShow(new AppSettings(), true, isFullInstall: true, false));
        Assert.False(ModPickerDialog.ShouldShow(new AppSettings(), true, false, manifestUnavailable: true));
        Assert.False(ModPickerDialog.ShouldShow(new AppSettings { OfflineMode = true }, true, false, false));

        // Offline allowance spent: WPF shows it anyway (and latches).
        var spent = new AppSettings { OfflineMode = true, ModPickerOfflineOffers = ModPickerDialog.MaxOfflineOffers };
        Assert.True(ModPickerDialog.ShouldShow(spent, true, false, false));
    }
}
