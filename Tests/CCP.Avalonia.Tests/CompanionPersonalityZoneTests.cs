using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Z4 "Make her yours" reads the live personality and writes through the shell's
/// explicit-content gate (WPF MakeHerYoursRuntimeVm + MainWindow.CompanionRoom.cs:121-178).</summary>
public sealed class CompanionPersonalityZoneTests
{
    private static void Click(Window shell, Control c)
    {
        c.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var at = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), shell)!.Value;
        shell.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
        shell.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static ToggleButton Chip(MakeHerYoursView view, string id) =>
        view.GetLogicalDescendants().OfType<ToggleButton>().First(t => (t.DataContext as PresetChip)?.Id == id);

    private static ExplicitContentAcknowledgementDialog? Gate(Window shell) =>
        shell.OwnedWindows.OfType<ExplicitContentAcknowledgementDialog>().SingleOrDefault();

    [Fact]
    public Task PresetChipsSpiceAndResetGoThroughTheGate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
        s.ActiveCommunityPromptId = null;
        s.CompanionPrompt.UseCustomPrompt = false;
        s.CompanionPrompt.ExplicitContentAcknowledged = false;
        s.SlutModeEnabled = false;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            var view = shell.GetLogicalDescendants().OfType<MakeHerYoursView>().Single();
            var vm = view.ViewModel;
            Assert.Same(shell, vm.Shell);

            // Chips are the service's presets, the active one selected; the readout names it.
            var all = PersonalityService.Shared.GetAllPresets();
            Assert.Equal(all.Select(p => p.Id), vm.Presets.Select(p => p.Id));
            Assert.True(Chip(view, PersonalityPresets.NeutralDefaultId).IsChecked);
            var neutral = PersonalityService.Shared.GetPresetById(PersonalityPresets.NeutralDefaultId)!;
            Assert.Equal(Loc.GetF("companion_personality_active_preset_fmt", neutral.Name), vm.ActivePersonalityLine);

            // A plain preset switches (spice off: nothing on the picker asks).
            var plain = all.First(p => p.Id != PersonalityPresets.NeutralDefaultId && !ExplicitContentGate.RequiresAcknowledgement(p, false));
            Click(shell, Chip(view, plain.Id));
            Assert.Equal(plain.Id, s.ActivePersonalityPresetId);
            Assert.False(Chip(view, PersonalityPresets.NeutralDefaultId).IsChecked);

            // With spice on, a preset with an explicit variant asks first; Cancel leaves the old
            // preset active and the chip off.
            s.SlutModeEnabled = true;
            var spicy = all.First(p => p.Id != plain.Id && ExplicitContentGate.RequiresAcknowledgement(p, true));
            Click(shell, Chip(view, spicy.Id));
            var gate = Gate(shell);
            Assert.NotNull(gate);
            gate!.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(plain.Id, s.ActivePersonalityPresetId);
            Assert.False(Chip(view, spicy.Id).IsChecked);
            Assert.True(Chip(view, plain.Id).IsChecked);

            // Spice on over that preset: refused on Cancel.
            s.SlutModeEnabled = false;
            s.ActivePersonalityPresetId = spicy.Id;
            vm.Sync();
            vm.IsSpiceOn = true;
            Dispatcher.UIThread.RunJobs();
            Gate(shell)!.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(s.SlutModeEnabled);
            Assert.False(vm.IsSpiceOn);

            // ...and written once acknowledged.
            ExplicitContentGate.MarkAcknowledged(s.CompanionPrompt);
            vm.IsSpiceOn = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(Gate(shell));
            Assert.True(s.SlutModeEnabled);
            Assert.True(vm.IsSpiceOn);

            // A community prompt shows Reset, and Reset clears the override.
            s.ActiveCommunityPromptId = "community-x";
            shell.ShowTab("achievements");
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("companion");   // re-read on return (CompanionRoomView.ResumeClocks)
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.CanResetPersonality);
            Assert.Equal(Loc.GetF("companion_personality_active_custom_fmt", "community-x"), vm.ActivePersonalityLine);
            vm.ResetPersonalityCommand.Execute(null);
            Assert.Null(s.ActiveCommunityPromptId);
            Assert.False(vm.CanResetPersonality);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
