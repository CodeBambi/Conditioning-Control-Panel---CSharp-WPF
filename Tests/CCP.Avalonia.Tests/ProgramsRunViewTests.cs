using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Programs slice 2: an existing enrollment, loaded read-only from a temp profile's programs.json,
/// reaches the run view through the shell's Programs door. CHECKPOINT A: a day the program clock
/// has moved past is labelled "last saved", not "today"; no lifecycle/session control is shown;
/// nothing is written and the state does not move.
/// </summary>
public sealed class ProgramsRunViewTests
{
    [Fact]
    public void ShellShowsTheSavedRunReadOnlyAndNeverCallsAStaleDayToday() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var program = BuiltInPrograms.All().First(p => p.Id == "first_week");
        var savedDay = new DateTime(2026, 3, 11);
        var state = new ProgramState
        {
            Active = new ProgramEnrollment
            {
                ProgramId = program.Id, StartedAt = savedDay.AddDays(-1).AddHours(21), CurrentDay = 2,
                CurrentDayDate = savedDay, State = ProgramEnrollmentState.Active, DaysOffRemaining = 1,
            }
        };
        state.Active.GetOrCreateRecord(1, savedDay.AddDays(-1)).DayCompleted = true;
        state.Active.GetOrCreateRecord(2, savedDay).TaskProgress["d2_lockcards"] = 1;

        var dir = Directory.CreateTempSubdirectory("ccp-programs-run-").FullName;
        var path = Path.Combine(dir, "programs.json");
        File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        var bytes = File.ReadAllBytes(path);

        var oldPrograms = AvApp.Programs;
        var oldClock = ProgramsTabView.Clock;
        var settings = CoreSettings.Current;
        var (seenTab, seenIntro) = (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro);
        settings.HasSeenProgramsTab = settings.HasSeenProgramsIntro = true;   // no first-visit save/popup
        MainShellWindow? shell = null;
        try
        {
            AvApp.Programs = new ProgramService(path, readOnly: true);
            // Five days past the saved day: Linux runs no rollover, so this is a stale snapshot.
            ProgramsTabView.Clock = new FixedClock(savedDay.AddDays(5).AddHours(12));

            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("programs");
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<ProgramsTabView>("ProgramsTab")!;
            T F<T>(string n) where T : Control => tab.FindControl<T>(n)!;

            Assert.True(F<StackPanel>("ProgramsRunPanel").IsEffectivelyVisible);
            Assert.False(F<StackPanel>("ProgramsBrowsePanel").IsVisible);
            Assert.True(F<Border>("RunReadOnlyNote").IsEffectivelyVisible);
            Assert.Equal(program.Title, F<TextBlock>("TxtRunProgramTitle").Text);
            Assert.Equal(Loc.GetF("programs_day_counter", 2, program.LengthDays), F<TextBlock>("TxtRunDayCounter").Text);
            Assert.Equal($"1 / {program.LengthDays}", F<TextBlock>("TxtRunStatDone").Text);
            var pips = F<ItemsControl>("ProgramDayStrip").ItemsSource!.Cast<ProgramDayPip>().ToList();
            Assert.Equal(program.LengthDays, pips.Count);
            Assert.Equal("✓", pips[0].Label);
            Assert.StartsWith(Loc.GetF("programs_pip_last_saved", 2), pips[1].Tip);

            Assert.Equal(program.GetDay(2)!.Title, F<TextBlock>("TxtTodayTitle").Text);
            Assert.Equal(Loc.GetF("programs_last_saved_header", savedDay.ToShortDateString()),
                F<TextBlock>("TxtTodayHeader").Text);
            Assert.False(F<TextBlock>("TxtTodayCloses").IsVisible);   // a closed day has no deadline
            var tasks = F<ItemsControl>("TodayTaskList").ItemsSource!.Cast<ProgramTaskItem>().ToList();
            Assert.Contains(tasks, t => t.TaskId == "d2_lockcards" && t.ProgressText == Loc.GetF("programs_task_progress", 1, 2));
            Assert.All(tasks, t => Assert.False(t.SubmitVisible || t.OpenVisible));

            // No lifecycle or session control on the read-only head.
            foreach (var name in new[] { "BtnProgramPauseResume", "BtnProgramWithdraw", "BtnStartTodaySession",
                                         "BtnProgramRestart", "BtnProgramDismissGraduated" })
                Assert.False(F<Button>(name).IsEffectivelyVisible, name);

            // Same program day as the save: then, and only then, it is "today".
            ProgramsTabView.Clock = new FixedClock(savedDay.AddHours(12));
            tab.RefreshPrograms();
            Assert.Equal(Loc.Get("programs_today_header"), F<TextBlock>("TxtTodayHeader").Text);
            Assert.True(F<TextBlock>("TxtTodayCloses").IsVisible);

            // The app stays open past the day boundary: a read-only service raises no TodayChanged,
            // so the next reveal of the tab must notice and relabel the day.
            ProgramsTabView.Clock = new FixedClock(savedDay.AddDays(1).AddHours(12));
            tab.IsVisible = false;
            tab.IsVisible = true;
            Assert.Equal(Loc.GetF("programs_last_saved_header", savedDay.ToShortDateString()),
                F<TextBlock>("TxtTodayHeader").Text);
            Assert.False(F<TextBlock>("TxtTodayCloses").IsVisible);

            // Nothing moved, nothing written.
            Assert.Equal(2, AvApp.Programs.ActiveEnrollment!.CurrentDay);
            Assert.Equal(ProgramEnrollmentState.Active, AvApp.Programs.ActiveEnrollment.State);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(dir));
        }
        finally
        {
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            AvApp.Programs?.Dispose();
            AvApp.Programs = oldPrograms;
            ProgramsTabView.Clock = oldClock;
            (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro) = (seenTab, seenIntro);
            Directory.Delete(dir, true);
        }
    });

    private sealed class FixedClock(DateTime local) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(local, DateTimeKind.Utc));
    }
}
