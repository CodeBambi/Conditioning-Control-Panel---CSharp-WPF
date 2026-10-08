using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF LockCardFeatureControl.xaml.cs:134-207 + LockCardService.ShowLockCard: the repeat-shape
/// switches write settings, hide the rows they make dead, and the next card's count follows them.</summary>
public sealed class LockCardRepeatShapePanelTests
{
    [Fact]
    public Task MatchByLengthHidesRepeatsAndSizesTheTestCard() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var s = CoreSettings.Current;
        var phrases = s.LockCardPhrases;
        var (random, min, byLen, len, vary, strict) = (s.LockCardRandomRepeats, s.LockCardRepeatsMin,
            s.LockCardTargetLengthEnabled, s.LockCardTargetLength, s.LockCardTargetLengthVariance, s.LockCardStrict);
        var handler = CoreLockCard.ShowHandler;
        Window? host = null;
        try
        {
            s.LockCardPhrases = new Dictionary<string, bool> { ["GOOD GIRL"] = true };
            s.LockCardRandomRepeats = false;
            s.LockCardTargetLengthEnabled = false;
            s.LockCardStrict = false;
            CoreLockCard.ShowHandler = LockCardWindow.ShowNext;   // what App seeds, minus the UI hop

            var feature = new LockCardFeatureControl();
            host = new Window { Content = feature };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            T F<T>(string n) where T : Control => feature.FindControl<T>(n)!;

            // Random: reveals "fewest repeats" and writes the floor.
            Assert.False(F<Grid>("RowRepeatsMin").IsVisible);
            F<CheckBox>("ChkRandomRepeats").IsChecked = true;
            F<Slider>("SliderRepeatsMin").Value = 2;
            Assert.True(F<Grid>("RowRepeatsMin").IsVisible);
            Assert.True(s.LockCardRandomRepeats);
            Assert.Equal(2, s.LockCardRepeatsMin);
            Assert.Equal("2x", F<TextBlock>("TxtRepeatsMin").Text);

            // Match by length: hides Repeats + random, shows the length dials.
            F<CheckBox>("ChkTargetLength").IsChecked = true;
            F<Slider>("SliderTargetLength").Value = 120;
            F<Slider>("SliderTargetVariance").Value = 0;
            Assert.True(s.LockCardTargetLengthEnabled);
            Assert.Equal(0, s.LockCardTargetLengthVariance);
            Assert.False(F<Grid>("RowRepeats").IsVisible);
            Assert.False(F<Grid>("RowRandomRepeats").IsVisible);
            Assert.False(F<Grid>("RowRepeatsMin").IsVisible);
            Assert.True(F<Grid>("RowTargetLength").IsVisible);
            Assert.Equal("\u00B10", F<TextBlock>("TxtTargetVariance").Text);

            // The Test button's card owes ceil(120 / 9) = 14 repeats, not the flat slider.
            F<Button>("BtnTest").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(14, LockCardWindow.Primary!.RequiredRepeats);
        }
        finally
        {
            LockCardWindow.ForceCloseAll();
            host?.Close();
            CoreLockCard.ShowHandler = handler;
            s.LockCardPhrases = phrases;
            (s.LockCardRandomRepeats, s.LockCardRepeatsMin, s.LockCardTargetLengthEnabled,
             s.LockCardTargetLength, s.LockCardTargetLengthVariance, s.LockCardStrict) = (random, min, byLen, len, vary, strict);
        }
        return Task.CompletedTask;
    });
}
