using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Programs 3a, the user path from the shell (P04): only first_week is enrollable; Enroll goes through
/// the ceremony and writes the temp profile's programs.json; today's session runs on App.Sessions
/// through the ProgramEngineBridge; a panic ends it uncredited (P06); a completed run credits the day;
/// Withdraw confirms and returns to browse.
/// </summary>
public sealed class ProgramsRunLifecycleTests
{
    [Fact]
    public Task EnrollStartPanicCompleteAndWithdrawFromTheShell() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var dir = Directory.CreateTempSubdirectory("ccp-programs-3a-").FullName;
        var path = Path.Combine(dir, "programs.json");
        var (oldPrograms, oldSessions, oldTask) = (AvApp.Programs, AvApp.Sessions, CoreProgram.TaskAvailableProvider);
        var settings = CoreSettings.Current;
        var (seenTab, seenIntro) = (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro);
        settings.HasSeenProgramsTab = settings.HasSeenProgramsIntro = true;
        MainShellWindow? shell = null;
        try
        {
            CoreProgram.TaskAvailableProvider = ProgramCapabilities.IsAvailable;
            var svc = AvApp.Programs = new ProgramService(path, readOnly: false);
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            shell = new MainShellWindow();
            runner.SessionLog.LogReady += shell.OnSessionLogReady;   // as App startup wires both
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            shell.Show();
            shell.ShowTab("programs");
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<ProgramsTabView>("ProgramsTab")!;
            T F<T>(string n) where T : Control => tab.FindControl<T>(n)!;

            var rows = F<ListBox>("ProgramLibraryList").Items.Cast<ProgramBrowseItem>().ToList();
            // A locked premium card stays pressable (it opens the plans page, as WPF); of the rest only first_week enrolls.
            Assert.Equal(new[] { "first_week" }, rows.Where(r => r.IsActionEnabled && !r.IsLocked).Select(r => r.ProgramId));

            // Enroll: the ceremony, then the run panel and a written ledger.
            var enroll = shell.EnrollProgramAsync("first_week");
            var dialog = await Owned<ProgramEnrollDialog>(shell);
            dialog.FindControl<TextBox>("TxtContractInput")!.Text = svc.GetProgram("first_week")!.ContractPhrase;
            Click(dialog.FindControl<Button>("BtnConfirmEnroll")!);
            await enroll;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ProgramEnrollmentState.Active, svc.ActiveEnrollment!.State);
            Assert.True(File.Exists(path));
            Assert.True(F<StackPanel>("ProgramsRunPanel").IsEffectivelyVisible);
            Assert.False(F<Border>("RunReadOnlyNote").IsVisible);
            Assert.True(F<Button>("BtnProgramWithdraw").IsEffectivelyEnabled);
            Assert.True(F<Button>("BtnStartTodaySession").IsEffectivelyEnabled);

            // Today's session through the bridge; the row says it is ours.
            await shell.StartProgramSessionAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(runner.IsRunning && svc.IsProgramSession(runner.CurrentSession));
            Assert.Equal(Loc.Get("programs_session_in_progress"), F<TextBlock>("TxtStartTodaySession").Text);
            Assert.False(F<Button>("BtnStartTodaySession").IsEnabled);

            // Panic ends it, uncredited, with no "ended early" recap on top; the row repaints.
            PanicSurfaces.StopAll("test", shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(runner.IsRunning);
            Assert.False(svc.TodayRecord!.SessionCompleted);
            Assert.Empty(shell.OwnedWindows.OfType<SessionCompleteWindow>());
            Assert.Equal(Loc.Get("btn_program_start_session"), F<TextBlock>("TxtStartTodaySession").Text);

            // A run to the end credits the day (SessionRunner.Stopped -> OnEngineSessionCompleted).
            await shell.StartProgramSessionAsync();
            runner.Stop(completed: true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(svc.TodayRecord!.SessionCompleted);
            Assert.Equal(Loc.Get("programs_session_done"), F<TextBlock>("TxtStartTodaySession").Text);
            Assert.Single(shell.OwnedWindows.OfType<SessionCompleteWindow>());   // a real end still recaps
            CloseOwned(shell);

            // Withdraw: confirm, then browse; the ledger keeps the run in History.
            var withdraw = shell.WithdrawProgramAsync();
            Click((await Owned<MessageDialog>(shell)).FindControl<Button>("BtnOk")!);
            await withdraw;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(svc.ActiveEnrollment);
            Assert.True(F<StackPanel>("ProgramsBrowsePanel").IsEffectivelyVisible);
            using var reread = new ProgramService(path, readOnly: true);
            Assert.Equal(ProgramEnrollmentState.Withdrawn, reread.State.History.Single().State);
        }
        finally
        {
            CoreEngine.StoppedHook = null;
            if (shell != null) { AvApp.Sessions!.SessionLog.LogReady -= shell.OnSessionLogReady; CloseOwned(shell); shell.Close(); }
            Dispatcher.UIThread.RunJobs();
            AvApp.Sessions?.Stop();
            AvApp.Programs?.Dispose();
            (AvApp.Programs, AvApp.Sessions, CoreProgram.TaskAvailableProvider) = (oldPrograms, oldSessions, oldTask);
            (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro) = (seenTab, seenIntro);
            CoreSession.IsSessionRunningProvider = null;
            Directory.Delete(dir, true);
        }
    });

    private static async Task<T> Owned<T>(Window owner) where T : Window
    {
        for (var i = 0; i < 250 && !owner.OwnedWindows.OfType<T>().Any(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        return Assert.Single(owner.OwnedWindows.OfType<T>());
    }

    private static void CloseOwned(Window owner)
    {
        foreach (var w in owner.OwnedWindows.ToList()) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
