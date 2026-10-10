using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
/// Programs 3b, from the shell's buttons (P04): Pause/Resume (greyed with WPF's hint while our session runs),
/// Restart after a lapse, Dismiss after graduation, the mantra door, the ritual picker (hidden until
/// ProgramCapabilities.RitualsAvailable) and the note on a loaded run this head cannot finish.
/// </summary>
public sealed class ProgramsRunControlsTests
{
    [Fact]
    public Task PauseResumeRestartDismissAndRitualFromTheRunPanel() => Run(async (shell, tab, svc, path) =>
    {
        T F<T>(string n) where T : Control => tab.FindControl<T>(n)!;
        var enrollment = svc.Enroll(svc.Library.First(p => p.Id == "first_week"))!;
        tab.RefreshPrograms();
        Assert.True(F<Button>("BtnProgramPauseResume").IsEffectivelyVisible);
        Assert.Equal(Loc.Get("btn_program_pause"), F<TextBlock>("TxtProgramPauseResume").Text);
        Assert.False(F<Border>("RunUnavailableNote").IsVisible);

        // WPF UpdateProgramSessionRow :1476: our session in flight greys Pause, with the reason.
        await shell.StartProgramSessionAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.False(F<Button>("BtnProgramPauseResume").IsEnabled);
        Assert.Equal(Loc.Get("programs_pause_blocked_hint"), ToolTip.GetTip(F<Button>("BtnProgramPauseResume")));
        AvApp.Sessions!.Stop();
        Dispatcher.UIThread.RunJobs();
        Assert.True(F<Button>("BtnProgramPauseResume").IsEnabled);

        Click(F<Button>("BtnProgramPauseResume"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ProgramEnrollmentState.Paused, enrollment.State);
        Assert.True(F<Border>("RunPausedNote").IsVisible);
        Assert.Equal(Loc.Get("btn_program_resume"), F<TextBlock>("TxtProgramPauseResume").Text);
        Click(F<Button>("BtnProgramPauseResume"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ProgramEnrollmentState.Active, enrollment.State);

        // Ritual (WPF :2112): day 6's photo task; the picker is gated on the capability flag.
        enrollment.CurrentDay = 6;
        enrollment.GetOrCreateRecord(6, enrollment.CurrentDayDate);
        tab.RefreshPrograms();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(tab.GetVisualDescendants().OfType<Button>(), b => b.Tag is string); // the task list is realised
        Assert.Null(RitualButton(tab));
        var photo = Path.Combine(Path.GetDirectoryName(path)!, "photo.png");
        File.WriteAllBytes(photo, new byte[] { 1 });
        ProgramCapabilities.RitualsAvailable = true;
        // A portal pick with no local path never completes the ritual photo-less (oracle roadmap-seed test 7).
        Assert.False(MainShellWindow.TrySubmitRitual(svc, "d6_ritual_pink", null));
        Assert.DoesNotContain("d6_ritual_pink", svc.TodayRecord!.CompletedTaskIds);
        MainShellWindow.RitualPhotoPicker = _ => Task.FromResult<(bool, string?)>((true, photo));
        tab.RefreshPrograms();
        Dispatcher.UIThread.RunJobs();
        Click(RitualButton(tab)!);
        for (var i = 0; i < 50 && !svc.TodayRecord!.CompletedTaskIds.Contains("d6_ritual_pink"); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        Assert.Contains("d6_ritual_pink", svc.TodayRecord!.CompletedTaskIds);
        Assert.Null(RitualButton(tab));

        // Restart after a lapse (WPF :2086): attempt 2, back on the run panel.
        enrollment.State = ProgramEnrollmentState.Lapsed;
        tab.RefreshPrograms();
        Click(F<Button>("BtnProgramRestart"));
        for (var i = 0; i < 50 && enrollment.State != ProgramEnrollmentState.Active; i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
        Assert.Equal(2, enrollment.AttemptNumber);
        Assert.True(F<StackPanel>("ProgramsRunPanel").IsEffectivelyVisible);

        // Dismiss after graduation (WPF :2099): into History, browse again, written.
        enrollment.State = ProgramEnrollmentState.Graduated;
        tab.RefreshPrograms();
        Click(F<Button>("BtnProgramDismissGraduated"));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(svc.ActiveEnrollment);
        Assert.True(F<StackPanel>("ProgramsBrowsePanel").IsEffectivelyVisible);
        using var reread = new ProgramService(path, readOnly: true);
        Assert.Equal(ProgramEnrollmentState.Graduated, reread.State.History.Single().State);
    });

    [Fact]
    public Task LoadedRunThisHeadCannotFinishSaysSoAndOpensTheMantraDoor() => Run(async (shell, tab, svc, _) =>
    {
        T F<T>(string n) where T : Control => tab.FindControl<T>(n)!;
        // A run synced from Windows: enrolled where every feature exists, then opened here.
        CoreProgram.TaskAvailableProvider = null;
        CoreProgram.HasPremiumProvider = () => true;
        var kept = svc.Library.First(p => p.Id == "kept");
        var enrollment = svc.Enroll(kept)!;
        enrollment.CurrentDay = 5;
        enrollment.GetOrCreateRecord(5, enrollment.CurrentDayDate);
        CoreProgram.TaskAvailableProvider = ProgramCapabilities.IsAvailable;
        tab.RefreshPrograms();
        Dispatcher.UIThread.RunJobs();

        Assert.True(F<Border>("RunUnavailableNote").IsVisible);
        Assert.Equal(Loc.GetF("programs_run_unavailable_note", ProgramService.UnavailableReason(kept, ProgramCapabilities.IsAvailable)!),
            F<TextBlock>("TxtRunUnavailableNote").Text);
        Assert.True(F<Button>("BtnProgramWithdraw").IsEnabled);

        // WPF :1149 (#1230): the Mantra task's door, at the task's rep count.
        var target = kept.GetDay(5)!.Tasks.First(t => t.Verifier == ConditioningControlPanel.Models.QuestCategory.Mantra).TargetValue;
        var door = tab.GetVisualDescendants().OfType<Button>()
            .Single(b => b.IsEffectivelyVisible && b.Tag is int && b.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text == Loc.Get("btn_program_open_mantras")));
        Assert.Equal(Math.Max(1, target), door.Tag);

        // Restart is a new attempt: refused like CanEnroll for a program this head cannot finish.
        enrollment.State = ProgramEnrollmentState.Lapsed;
        tab.RefreshPrograms();
        var reason = ProgramService.UnavailableReason(kept, ProgramCapabilities.IsAvailable);
        Assert.False(F<Button>("BtnProgramRestart").IsEnabled);
        Assert.Equal(reason, ToolTip.GetTip(F<Button>("BtnProgramRestart")));
        var restart = shell.RestartProgramAsync();
        var dialog = await Owned<MessageDialog>(shell);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == reason);
        dialog.Close();
        await restart;
        Assert.Equal(ProgramEnrollmentState.Lapsed, enrollment.State);
        Assert.Equal(1, enrollment.AttemptNumber);
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

    private static Button? RitualButton(ProgramsTabView tab) => tab.GetVisualDescendants().OfType<Button>()
        .FirstOrDefault(b => b.IsEffectivelyVisible && b.Tag as string == "d6_ritual_pink");

    private static Task Run(Func<MainShellWindow, ProgramsTabView, ProgramService, string, Task> body) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var dir = Directory.CreateTempSubdirectory("ccp-programs-3b-").FullName;
            var path = Path.Combine(dir, "programs.json");
            var (oldPrograms, oldSessions, oldTask) = (AvApp.Programs, AvApp.Sessions, CoreProgram.TaskAvailableProvider);
            var (oldPremium, oldRituals) = (CoreProgram.HasPremiumProvider, ProgramCapabilities.RitualsAvailable);
            var settings = CoreSettings.Current;
            var (seenTab, seenIntro) = (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro);
            settings.HasSeenProgramsTab = settings.HasSeenProgramsIntro = true;
            MainShellWindow? shell = null;
            try
            {
                CoreProgram.TaskAvailableProvider = ProgramCapabilities.IsAvailable;
                ProgramCapabilities.RitualsAvailable = false;
                var svc = AvApp.Programs = new ProgramService(path, readOnly: false);
                AvApp.Sessions = new SessionRunner(new SessionLogService());
                shell = new MainShellWindow();
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                shell.Show();
                shell.ShowTab("programs");
                Dispatcher.UIThread.RunJobs();
                await body(shell, shell.Named<ProgramsTabView>("ProgramsTab")!, svc, path);
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                MainShellWindow.RitualPhotoPicker = null;
                if (shell != null) { foreach (var w in shell.OwnedWindows.ToList()) w.Close(); shell.Close(); }
                Dispatcher.UIThread.RunJobs();
                AvApp.Sessions?.Stop();
                AvApp.Programs?.Dispose();
                (AvApp.Programs, AvApp.Sessions, CoreProgram.TaskAvailableProvider) = (oldPrograms, oldSessions, oldTask);
                (CoreProgram.HasPremiumProvider, ProgramCapabilities.RitualsAvailable) = (oldPremium, oldRituals);
                (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro) = (seenTab, seenIntro);
                CoreSession.IsSessionRunningProvider = null;
                Directory.Delete(dir, true);
            }
        });

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
