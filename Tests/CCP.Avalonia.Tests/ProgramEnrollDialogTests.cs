using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Program;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>dialogs-program-enroll: the enrollment ceremony collects exactly what WPF
/// ProgramEnrollDialog.xaml.cs collects (contract phrase gate, Strict only when offered, boundary and
/// nudge hours, Escape/Cancel refuse). The dialog persists nothing; no shell caller exists yet.</summary>
public sealed class ProgramEnrollDialogTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void Pump() { for (int i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs(); }

    private static T C<T>(Window w, string name) where T : Control => w.FindControl<T>(name)!;

    private static void Click(Window w, string name) =>
        C<Button>(w, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task ContractPhraseGatesConfirmAndConfirmReturnsTheChosenClock() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var program = BuiltInPrograms.All()[0];
        Assert.False(string.IsNullOrWhiteSpace(program.ContractPhrase));
        var owner = new Window();
        owner.Show();
        var dlg = new ProgramEnrollDialog(program);
        try
        {
            var result = dlg.ShowDialogSafe<bool?>(owner);
            Pump();

            Assert.False(C<Button>(dlg, "BtnConfirmEnroll").IsEnabled);
            Assert.Equal(Loc.Get("program_enroll_contract_hint"), C<TextBlock>(dlg, "TxtBlockedHint").Text);
            Assert.Equal(program.Rules.DefaultDayBoundaryHour, C<ComboBox>(dlg, "CmbBoundaryHour").SelectedIndex);
            Assert.Equal(21, C<ComboBox>(dlg, "CmbNudgeHour").SelectedIndex);

            // WPF normalizes case and whitespace: a sloppy but complete phrase unlocks Confirm.
            C<TextBox>(dlg, "TxtContractInput").Text = "  " + program.ContractPhrase.ToLowerInvariant().Replace(" ", "   ") + " ";
            Pump();
            Assert.True(C<Button>(dlg, "BtnConfirmEnroll").IsEnabled);
            Assert.Equal("", C<TextBlock>(dlg, "TxtBlockedHint").Text);

            C<RadioButton>(dlg, "RadioModeStrict").IsChecked = true;
            C<ComboBox>(dlg, "CmbBoundaryHour").SelectedIndex = 6;
            C<ComboBox>(dlg, "CmbNudgeHour").SelectedIndex = 0; // "Off"
            Click(dlg, "BtnConfirmEnroll");
            Pump();

            Assert.True(await result);
            Assert.True(dlg.StrictMode);
            Assert.Equal(6, dlg.DayBoundaryHour);
            Assert.Equal(-1, dlg.NudgeHour);
        }
        finally { dlg.Close(); owner.Close(); }
    });

    [Fact]
    public Task StrictUnavailableAndEscapeRefuse() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var program = BuiltInPrograms.All()[0];
        program.Rules.StrictAvailable = false;
        var owner = new Window();
        owner.Show();
        var dlg = new ProgramEnrollDialog(program);
        try
        {
            var result = dlg.ShowDialogSafe<bool?>(owner);
            Pump();

            Assert.False(C<RadioButton>(dlg, "RadioModeStrict").IsEnabled);
            Assert.True(C<TextBlock>(dlg, "TxtStrictUnavailable").IsVisible);

            var esc = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            dlg.RaiseEvent(esc);
            Pump();

            Assert.True(esc.Handled);
            Assert.False(await result);
            Assert.False(dlg.StrictMode);
        }
        finally { dlg.Close(); owner.Close(); }
    });
}
