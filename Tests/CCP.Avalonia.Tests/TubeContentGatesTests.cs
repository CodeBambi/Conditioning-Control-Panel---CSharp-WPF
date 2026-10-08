using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Moderation;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The tube's two CCBill gates (WPF AvatarTubeWindow.ChatInput.cs:1284, AvatarTubeWindow.xaml.cs:396):
/// an explicit preset picked from the avatar's Personality submenu waits on the acknowledgement
/// dialog, which Esc does not dismiss and whose Cancel/close box switch nothing; and the third
/// moderation hit opens the content-policy warning.
/// </summary>
public sealed class TubeContentGatesTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static T Dialog<T>(Window owner) where T : Window
    {
        Dispatcher.UIThread.RunJobs();
        return owner.OwnedWindows.OfType<T>().Single();
    }

    [Fact]
    public Task ExplicitPresetFromTheMenuWaitsOnTheAcknowledgement() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.CompanionPrompt ??= new CompanionPromptSettings();
        s.CompanionPrompt.UseCustomPrompt = false;
        s.CompanionPrompt.ExplicitContentAcknowledged = false;
        s.SlutModeEnabled = true;
        var tube = new AvatarTubeWindow(null);
        try
        {
            var explicitPreset = PersonalityService.Shared.GetAllPresets()
                .First(p => p.Id != PersonalityPresets.SlutModeId && ExplicitContentGate.RequiresAcknowledgement(p, true)
                            && p.Id != s.ActivePersonalityPresetId);
            var before = s.ActivePersonalityPresetId;
            tube.Show();

            // The user path: right-click opens the menu, which fills the Personality submenu.
            tube.FindControl<ContextMenu>("AvatarContextMenu")!.Open(tube.FindControl<Border>("AvatarBorder") ?? (Control)tube);
            Dispatcher.UIThread.RunJobs();
            MenuItem Item() => tube.FindControl<MenuItem>("MenuItemPersonality")!.Items.OfType<MenuItem>()
                .Single(i => (string?)i.Tag == explicitPreset.Id);

            // Cancel: nothing switches, nothing is acknowledged.
            Item().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var dlg = Dialog<ExplicitContentAcknowledgementDialog>(tube);
            dlg.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            Dispatcher.UIThread.RunJobs();
            Assert.True(dlg.IsVisible);   // WPF has no IsCancel button: Esc does not answer the gate
            dlg.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
            Dispatcher.UIThread.RunJobs();
            Assert.True(dlg.IsVisible);   // ...and no IsDefault one: Enter does not either
            dlg.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, s.ActivePersonalityPresetId);
            Assert.False(ExplicitContentGate.IsAlreadyAcknowledged(s.CompanionPrompt));

            // Close box: the same refusal.
            Item().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dialog<ExplicitContentAcknowledgementDialog>(tube).Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, s.ActivePersonalityPresetId);
            Assert.False(ExplicitContentGate.IsAlreadyAcknowledged(s.CompanionPrompt));

            // Accept is dead until the 18+ box is ticked; then the acknowledgement is recorded and the preset switches.
            Item().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            dlg = Dialog<ExplicitContentAcknowledgementDialog>(tube);
            var accept = dlg.FindControl<Button>("BtnAccept")!;
            Assert.False(accept.IsEnabled);
            dlg.FindControl<CheckBox>("ChkAgeConfirm")!.IsChecked = true;
            Assert.True(accept.IsEnabled);
            accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(explicitPreset.Id, s.ActivePersonalityPresetId);
            Assert.True(ExplicitContentGate.IsAlreadyAcknowledged(s.CompanionPrompt));
            Assert.False(string.IsNullOrEmpty(s.CompanionPrompt.ExplicitAcknowledgedAt));

            // Acknowledged once (with the version, in the settings the debounced Save writes): picking it again asks nothing.
            Item().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(tube.OwnedWindows.OfType<ExplicitContentAcknowledgementDialog>());
        }
        finally
        {
            foreach (var w in tube.OwnedWindows.ToArray()) w.Close();
            tube.Close();
            CoreSettings.ServiceProvider = previous;
        }
        await Task.CompletedTask;
    });

    [Fact]
    public Task ThirdModerationHitOpensTheContentPolicyWarning() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var previous = CoreModerationLog.CounterProvider;
        var dir = Directory.CreateTempSubdirectory("ccp-mod-").FullName;
        var counter = new ModerationCounter(Path.Combine(dir, "moderation-counter.json"));
        CoreModerationLog.CounterProvider = () => counter;
        ContentPolicyWarningDialog? opened = null;
        using var hook = Window.WindowOpenedEvent.AddClassHandler<ContentPolicyWarningDialog>((w, _) => opened = w);
        var shell = new Window();
        var tube = new AvatarTubeWindow(shell);
        try
        {
            // Visible shell: the warning is owned by it, as WPF's Owner = _parentWindow.
            shell.Show();
            tube.Show();
            counter.RecordHit(ProhibitedCategory.Illegal, "input");
            counter.RecordHit(ProhibitedCategory.Illegal, "input");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(opened);
            counter.RecordHit(ProhibitedCategory.Illegal, "input");
            var dlg = Dialog<ContentPolicyWarningDialog>(shell);
            Assert.Contains("3", dlg.FindControl<TextBlock>("TxtBodyCount")!.Text);
            dlg.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(shell.OwnedWindows.OfType<ContentPolicyWarningDialog>());

            // Shell and tube hidden in the tray: WPF still shows the warning, so must this head.
            opened = null;
            shell.Hide();
            tube.Hide();
            OnWarning(tube, counter);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(opened);
            Assert.True(opened!.IsVisible);
            opened.Close();
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            tube.Close();
            shell.Close();
            CoreModerationLog.CounterProvider = previous;
            Directory.Delete(dir, true);
        }
        await Task.CompletedTask;
    });

    // The counter raises WarningTriggered once per threshold-cross; re-raise it for the tray case.
    private static void OnWarning(AvatarTubeWindow tube, ModerationCounter counter) =>
        typeof(AvatarTubeWindow).GetMethod("OnWarningTriggered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(tube, new object[] { counter.GetState() });
}
