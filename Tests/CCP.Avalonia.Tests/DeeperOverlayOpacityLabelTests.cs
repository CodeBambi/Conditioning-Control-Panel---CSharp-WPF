using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF 04807471a (ccp-bugs #936): the Deeper editor's overlay opacity labels show the value.</summary>
public sealed class DeeperOverlayOpacityLabelTests
{
    [Fact]
    public Task OpacityLabelsCarryTheSliderValue() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var editor = new DeeperEditorWindow();
        try
        {
            editor.SliderOverlayOpacity.Value = 0.6;
            Assert.Equal("Opacity  60%", editor.LblOverlayOpacity.Text);
            editor.ChkOverlayRamp.IsChecked = true;   // no effect selected: the next value change relabels
            editor.SliderOverlayOpacityEnd.Value = 0.25;
            Assert.Equal("Start opacity  60%", editor.LblOverlayOpacity.Text);
            Assert.Equal("End opacity  25%", editor.LblOverlayOpacityEnd.Text);
        }
        finally { editor.Close(); }
        return Task.CompletedTask;
    });
}
