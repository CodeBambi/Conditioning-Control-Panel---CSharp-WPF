// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProgramsTab.cs (WPF 7.1.5) - progression#1:
// RebuildProgramsTab (:498), BuildProgramRunPanel (:652), BuildProgramDayStrip (:753),
// BuildProgramTodayPanel (:900), UpdateProgramSessionRow (:1410), the lapsed/graduated panels
// (:1937/:1943) and the handlers (:1958-:2230). Same rules, numbers and loc keys.
// ponytail: not yet carried - the ignition rig / heat tiers / seals (MainWindow.ProgramsFx.cs), the
// per-task how-to lines and icons (ProgramTaskHowTo / ProgramTaskIconPath), the Today layer chips
// (BuildProgramTodayLayers), Up Next (BuildProgramUpNext), the hero plate and sigil art (ProgramArt,
// progression#2/#3) and the session sheen. Each needs its WPF block ported onto the names that are
// already in ProgramsTabView.axaml.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ProgramsTabView
    {
        private ProgramService? _hooked;

        private MainShellWindow? Shell => TopLevel.GetTopLevel(this) as MainShellWindow;

        private void HookPrograms()
        {
            var svc = App.Programs;
            if (svc == null || ReferenceEquals(svc, _hooked)) return;
            UnhookPrograms();
            _hooked = svc;
            svc.TodayChanged += OnProgramChanged;
            svc.ProgramLapsed += OnProgramChanged;
            svc.ProgramGraduated += OnProgramChanged;
            svc.DayCompleted += OnProgramChanged;
            if (App.Sessions is { } runner) runner.Ticked += OnSessionTicked;
        }

        private void UnhookPrograms()
        {
            if (_hooked is { } svc)
            {
                svc.TodayChanged -= OnProgramChanged;
                svc.ProgramLapsed -= OnProgramChanged;
                svc.ProgramGraduated -= OnProgramChanged;
                svc.DayCompleted -= OnProgramChanged;
            }
            _hooked = null;
            if (App.Sessions is { } runner) runner.Ticked -= OnSessionTicked;
        }

        private void OnProgramChanged(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() => { if (VisualRoot is not null) RefreshBrowse(); });

        /// <summary>WPF UpdateProgramSessionRow runs on every progress tick: no list rebuilds.</summary>
        private void OnSessionTicked() => Dispatcher.UIThread.Post(() =>
        {
            if (VisualRoot is not null && Find<StackPanel>("ProgramsRunPanel").IsVisible) UpdateProgramSessionRow();
        });

        /// <summary>Public refresh for the shell (WPF RefreshProgramsUI).</summary>
        internal void RefreshProgramsUI() => RefreshBrowse();

        /// <summary>
        /// WPF RebuildProgramsTab: picks the state panel, builds it, reveals it. Returns false for the
        /// browse state (the caller builds the list). Visibility is applied in a finally so a throw
        /// inside a build leaves a half-dressed panel, never a blank tab.
        /// </summary>
        private bool RefreshRunState()
        {
            var svc = App.Programs;
            var enrollment = svc?.ActiveEnrollment;
            var program = svc?.ActiveProgram;
            var browse = svc == null || enrollment == null || program == null ||
                         enrollment.State == ProgramEnrollmentState.Withdrawn;
            if (browse) return false;

            var lapsed = enrollment!.State == ProgramEnrollmentState.Lapsed;
            var graduated = enrollment.State == ProgramEnrollmentState.Graduated;
            var run = !lapsed && !graduated;
            try
            {
                if (lapsed)
                    Find<TextBlock>("TxtLapsedBody").Text = Loc.GetF("programs_lapsed_body", program!.Title, enrollment.AttemptNumber + 1);
                else if (graduated)
                {
                    Find<TextBlock>("TxtGraduatedSub").Text = Loc.GetF("programs_graduated_sub", program!.Title);
                    Find<TextBlock>("TxtGraduatedStats").Text = Loc.GetF("programs_graduated_stats",
                        enrollment.AttemptNumber, enrollment.PerfectDayCount, program.LengthDays);
                }
                else BuildRunPanel(program!, enrollment);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Programs tab build failed for {Program}", program?.Id);
            }
            finally
            {
                Find<StackPanel>("ProgramsBrowsePanel").IsVisible = false;
                Find<StackPanel>("ProgramsLapsedPanel").IsVisible = lapsed;
                Find<StackPanel>("ProgramsGraduatedPanel").IsVisible = graduated;
                Find<StackPanel>("ProgramsRunPanel").IsVisible = run;
            }
            return true;
        }

        private IBrush ThemeBrush(string key, IBrush fallback) =>
            this.TryFindResource(key, ActualThemeVariant, out var o) && o is IBrush b ? b : fallback;

        private void BuildRunPanel(ProgramDefinition program, ProgramEnrollment enrollment)
        {
            var svc = App.Programs!;
            var chapter = svc.TodayChapter;
            var accent = MainShellWindow.AccentBrush(chapter?.AccentColor ?? program.AccentColor);

            Find<Border>("RunAccentBar").Background = accent;
            // ponytail: ProgramArt.Sigil (progression#2) - no art on this head yet, so the plain bar shows.
            Find<Grid>("RunSigilBox").IsVisible = false;
            Find<Border>("RunAccentBar").IsVisible = true;

            Find<TextBlock>("TxtRunProgramTitle").Text = program.Title;
            var chapterName = Find<TextBlock>("TxtRunChapterName");
            chapterName.Text = chapter?.Name ?? program.Subtitle;
            chapterName.Foreground = accent;
            Find<TextBlock>("TxtRunDayCounter").Text = Loc.GetF("programs_day_counter", enrollment.CurrentDay, program.LengthDays);
            Find<Border>("RunStrictBadge").IsVisible = enrollment.StrictMode;
            var attempt = enrollment.AttemptNumber > 1;
            Find<Border>("RunAttemptBadge").IsVisible = attempt;
            if (attempt) Find<TextBlock>("TxtRunAttempt").Text = Loc.GetF("programs_attempt", enrollment.AttemptNumber);

            Find<TextBlock>("TxtRunStatDone").Text = $"{enrollment.CompletedDayCount} / {program.LengthDays}";
            Find<TextBlock>("TxtRunStatPerfect").Text = enrollment.PerfectDayCount.ToString();
            Find<TextBlock>("TxtRunStatDaysOff").Text = enrollment.DaysOffRemaining.ToString();

            var reward = chapter?.RewardDescription;
            var chip = Find<Border>("RunChapterRewardChip");
            chip.IsVisible = !string.IsNullOrWhiteSpace(reward);
            if (chip.IsVisible)
            {
                Find<TextBlock>("TxtRunChapterReward").Text = reward!;
                ToolTip.SetTip(chip, reward);
            }

            var paused = enrollment.State == ProgramEnrollmentState.Paused;
            Find<Border>("RunPausedNote").IsVisible = paused;
            Find<TextBlock>("TxtProgramPauseResume").Text = Loc.Get(paused ? "btn_program_resume" : "btn_program_pause");

            BuildDayStrip(program, enrollment, accent);
            BuildTodayPanel(program, enrollment, accent);
        }

        /// <summary>WPF BuildProgramDayStrip: one node per day, today ringed, done filled, missed crossed.</summary>
        private void BuildDayStrip(ProgramDefinition program, ProgramEnrollment enrollment, IBrush accent)
        {
            var muted = ThemeBrush("TextMutedBrush", Brushes.Gray);
            var light = ThemeBrush("TextLightBrush", Brushes.White);
            var surface = ThemeBrush("SurfaceBgBrush", Brushes.Transparent);
            var border = ThemeBrush("GlassBorderBrush", Brushes.Gray);
            var danger = ThemeBrush("DangerBrush", Brushes.IndianRed);
            var days = program.AllDays.GroupBy(d => d.DayIndex).ToDictionary(g => g.Key, g => g.First());
            var pips = new List<ProgramDayPip>(Math.Max(0, program.LengthDays));
            for (int i = 1; i <= program.LengthDays; i++)
            {
                var record = enrollment.GetRecord(i);
                var day = days.GetValueOrDefault(i);
                var pip = new ProgramDayPip { DayIndex = i, Label = i.ToString() };
                var milestone = day != null && (day.IsBoss || !string.IsNullOrWhiteSpace(day.RewardDescription));
                if (milestone)
                {
                    pip.RewardGlyph = day!.IsBoss ? "👑" : "🎁";
                    pip.RewardVisible = true;
                    pip.RewardTip = !string.IsNullOrWhiteSpace(day.RewardDescription) ? day.RewardDescription! : Loc.Get("programs_boss_badge");
                }
                string tip;
                if (i == enrollment.CurrentDay)
                {
                    pip.Fill = Brushes.Transparent; pip.Stroke = accent; pip.PipBorderThickness = new Thickness(2.5);
                    pip.LabelBrush = accent; pip.LabelWeight = FontWeight.Bold; pip.NodeSize = 42; pip.LabelSize = 15;
                    pip.IsCurrent = true; pip.GlowVisible = true; pip.GlowBrush = accent;
                    tip = Loc.GetF("programs_pip_today", i);
                }
                else if (record?.DayCompleted == true)
                {
                    pip.Fill = accent; pip.Stroke = accent; pip.Label = "✓"; pip.LabelBrush = light;
                    pip.LabelWeight = FontWeight.Bold; pip.NodeSize = 32; pip.LabelSize = 13;
                    tip = Loc.GetF("programs_pip_done", i);
                }
                else if (record?.Missed == true)
                {
                    pip.Fill = Brushes.Transparent; pip.Stroke = danger; pip.Label = "✕"; pip.LabelBrush = danger; pip.NodeSize = 32;
                    tip = Loc.GetF("programs_pip_missed", i);
                }
                else
                {
                    pip.Fill = surface; pip.Stroke = border; pip.LabelBrush = muted; pip.PipOpacity = 0.65;
                    tip = Loc.GetF("programs_pip_locked", i);
                }
                if (milestone) pip.NodeSize += 6;
                pip.Tip = day != null && !string.IsNullOrWhiteSpace(day.Title) ? $"{tip} · {day.Title}" : tip;
                pips.Add(pip);
            }
            Find<ItemsControl>("ProgramDayStrip").ItemsSource = pips;

            // The done segment ends on today's node centre (star units, WPF RailDoneColumn/RailRestColumn).
            var fill = Find<Border>("RailProgressFill");
            fill.Background = accent;
            if (fill.Parent is Grid g && g.ColumnDefinitions.Count >= 2)
            {
                var total = Math.Max(1, program.LengthDays);
                var done = Math.Max(0, Math.Clamp(enrollment.CurrentDay, 0, total) - 0.5);
                g.ColumnDefinitions[0].Width = new GridLength(done, GridUnitType.Star);
                g.ColumnDefinitions[1].Width = new GridLength(Math.Max(0.0001, total - done), GridUnitType.Star);
            }
        }

        /// <summary>WPF BuildProgramTodayPanel: today's title, session row, ambient row and tasks.</summary>
        private void BuildTodayPanel(ProgramDefinition program, ProgramEnrollment enrollment, IBrush accent)
        {
            var svc = App.Programs!;
            var day = svc.Today;
            var record = svc.TodayRecord;
            var panel = Find<Border>("TodayPanel");
            if (day == null || record == null) { panel.IsVisible = false; return; }
            panel.IsVisible = true;

            var muted = ThemeBrush("TextMutedBrush", Brushes.Gray);
            var light = ThemeBrush("TextLightBrush", Brushes.White);
            var glass = ThemeBrush("GlassBorderBrush", Brushes.Gray);
            var paused = enrollment.State == ProgramEnrollmentState.Paused;

            panel.BorderBrush = day.IsBoss ? accent : glass;
            panel.BorderThickness = new Thickness(day.IsBoss ? 2 : 1);
            Find<Border>("TodayBossBadge").IsVisible = day.IsBoss;
            Find<Border>("TodayReturnBadge").IsVisible = record.IsReturnDay;
            Find<TextBlock>("TxtTodayTitle").Text = day.Title;
            Find<TextBlock>("TxtTodayBlurb").Text = day.Blurb;

            var rewardChip = Find<Border>("TodayRewardChip");
            rewardChip.IsVisible = !string.IsNullOrWhiteSpace(day.RewardDescription);
            if (rewardChip.IsVisible)
            {
                Find<TextBlock>("TxtTodayReward").Text = day.RewardDescription!;
                ToolTip.SetTip(rewardChip, day.RewardDescription);
            }
            Find<Border>("TodayCompleteBanner").IsVisible = record.DayCompleted;

            var minutes = record.IsReturnDay ? ProgramService.ReturnDayMinutes(day.SessionMinutes) : day.SessionMinutes;
            Find<TextBlock>("TxtTodaySessionMinutes").Text = Loc.GetF("programs_session_minutes", minutes);
            UpdateProgramSessionRow();

            var ambient = day.Ambient;
            var ambientRow = Find<Border>("TodayAmbientRow");
            ambientRow.IsVisible = ambient != null && (!string.IsNullOrWhiteSpace(ambient.Description) || ambient.RequiredMinutes > 0);
            if (ambientRow.IsVisible)
            {
                Find<TextBlock>("TxtTodayAmbient").Text = ambient!.Description;
                var progress = Find<TextBlock>("TxtTodayAmbientProgress");
                progress.IsVisible = ambient.RequiredMinutes > 0;
                if (progress.IsVisible)
                    progress.Text = Loc.GetF("programs_ambient_progress",
                        Math.Min(record.AmbientMinutes, ambient.RequiredMinutes), ambient.RequiredMinutes);
            }

            var items = new List<ProgramTaskItem>();
            var hasRitual = false;
            int required = 0, completedRequired = 0, optional = 0, blockedCount = 0;
            foreach (var task in day.Tasks)
            {
                var complete = svc.IsTaskComplete(record, task);
                var blocked = svc.IsTaskBlocked(task);
                if (blocked) blockedCount++;
                else if (task.Optional) optional++;
                else { required++; if (complete) completedRequired++; }
                var ritual = task.Kind == ProgramTaskKind.Ritual;
                if (ritual) hasRitual = true;

                var item = new ProgramTaskItem
                {
                    TaskId = task.Id,
                    Description = task.Description,
                    StatusGlyph = complete ? "✓" : "○",
                    StatusBrush = complete ? accent : muted,
                    TextBrush = complete ? muted : light,
                    RowOpacity = blocked ? 0.5 : 1.0,
                    AccentBrush = accent,
                    CardBorderBrush = complete ? accent : glass,
                    DoneChipVisible = complete,
                };
                if (task.Kind == ProgramTaskKind.AutoVerified && task.TargetValue > 1)
                {
                    record.TaskProgress.TryGetValue(task.Id, out var current);
                    var shown = complete ? task.TargetValue : Math.Min(current, task.TargetValue);
                    item.ProgressText = Loc.GetF("programs_task_progress", shown, task.TargetValue);
                    item.ProgressStar = new GridLength(Math.Max(0, shown), GridUnitType.Star);
                    item.RemainderStar = new GridLength(Math.Max(0.0001, task.TargetValue - shown), GridUnitType.Star);
                    item.BarVisible = true;
                }
                if (blocked) { item.BadgeText = Loc.Get("programs_task_locked"); item.BadgeVisible = true; }
                else if (task.OutsideSession) { item.BadgeText = Loc.Get("programs_task_outside_session"); item.BadgeVisible = true; }
                else if (task.Optional) { item.BadgeText = Loc.Get("programs_task_optional"); item.BadgeVisible = true; }
                item.SubmitVisible = ritual && !complete && !blocked && !paused;
                item.OpenReps = Math.Max(1, task.TargetValue);
                item.OpenVisible = task.Kind == ProgramTaskKind.AutoVerified && task.Verifier == Models.QuestCategory.Mantra
                                   && !complete && !blocked && !paused;
                items.Add(item);
            }
            Find<ItemsControl>("TodayTaskList").ItemsSource = items;
            Find<TextBlock>("TxtTodayNoTasks").IsVisible = items.Count == 0;
            Find<TextBlock>("TxtRitualPrivacyNote").IsVisible = hasRitual;

            var pill = Find<Border>("TodayTasksDonePill");
            pill.IsVisible = required > 0;
            if (required > 0)
            {
                var text = Loc.GetF("programs_tasks_done_count", completedRequired, required);
                if (optional > 0) text += $"  ·  {optional} {Loc.Get("programs_task_optional")}";
                if (blockedCount > 0) text += $"  ·  {blockedCount} {Loc.Get("btn_program_locked")}";
                Find<TextBlock>("TxtTodayTasksDone").Text = text;
                ToolTip.SetTip(pill, blockedCount > 0 ? Loc.Get("programs_locked_hint") : null);
            }
        }

        private static string Clock(TimeSpan span) =>
            span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}" : $"{(int)span.TotalMinutes}:{span.Seconds:D2}";

        /// <summary>WPF UpdateProgramSessionRow: the start button, the live progress row and the
        /// pause lock while the program's own session runs.</summary>
        internal void UpdateProgramSessionRow()
        {
            try
            {
                var svc = App.Programs;
                var enrollment = svc?.ActiveEnrollment;
                var record = svc?.TodayRecord;
                var row = Find<Grid>("TodaySessionProgressRow");
                var progressText = Find<TextBlock>("TxtTodaySessionProgress");
                var pauseBtn = Find<Button>("BtnProgramPauseResume");
                var start = Find<Button>("BtnStartTodaySession");
                var glyph = Find<TextBlock>("TxtTodaySessionGlyph");
                if (svc == null || enrollment == null || record == null)
                {
                    row.IsVisible = progressText.IsVisible = false;
                    pauseBtn.IsEnabled = true;
                    ToolTip.SetTip(pauseBtn, null);
                    return;
                }
                var muted = ThemeBrush("TextMutedBrush", Brushes.Gray);
                var accent = MainShellWindow.AccentBrush(svc.ActiveProgram?.AccentColor);
                var paused = enrollment.State == ProgramEnrollmentState.Paused;
                var runner = App.Sessions;
                var running = runner?.IsRunning == true;
                var current = running ? runner!.CurrentSession : null;
                var ours = running && svc.IsProgramSession(current);

                pauseBtn.IsEnabled = !(ours && !paused);
                ToolTip.SetTip(pauseBtn, ours && !paused ? Loc.Get("programs_pause_blocked_hint") : null);

                if (ours && current != null)
                {
                    var total = TimeSpan.FromMinutes(Math.Max(1, current.DurationMinutes));
                    var elapsed = runner!.Elapsed;
                    if (elapsed > total) elapsed = total;
                    var bar = Find<ProgressBar>("TodaySessionProgressBar");
                    bar.Foreground = accent;
                    bar.Value = Math.Clamp(elapsed.TotalSeconds / total.TotalSeconds * 100, 0, 100);
                    var clock = Loc.GetF("programs_session_progress", Clock(elapsed), Clock(total));
                    progressText.Text = runner.IsPaused ? Loc.GetF("programs_session_progress_paused", clock) : clock;
                    progressText.Foreground = accent;
                    progressText.IsVisible = row.IsVisible = true;
                    glyph.Text = "◉";
                    glyph.Foreground = accent;
                    start.Content = Loc.Get("programs_session_in_progress");
                    start.IsEnabled = false;
                    ToolTip.SetTip(start, Loc.Get("programs_session_stop_hint"));
                    return;
                }

                row.IsVisible = progressText.IsVisible = false;
                Find<ProgressBar>("TodaySessionProgressBar").Value = 0;
                ToolTip.SetTip(start, null);
                if (record.SessionCompleted)
                {
                    glyph.Text = "✓";
                    glyph.Foreground = accent;
                    start.Content = Loc.Get("programs_session_done");
                    start.IsEnabled = false;
                    return;
                }
                glyph.Text = "○";
                glyph.Foreground = muted;
                if (running)
                {
                    start.Content = Loc.Get("programs_session_other_running");
                    start.IsEnabled = false;
                    ToolTip.SetTip(start, Loc.Get("programs_session_other_running_hint"));
                    return;
                }
                start.Content = Loc.Get("btn_program_start_session");
                start.IsEnabled = !paused;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "UpdateProgramSessionRow failed");
            }
        }

        // ---- HANDLERS (WPF MainWindow.ProgramsTab.cs:1958-2230) ------------------------------------

        private async void BtnProgramEnroll_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button { Tag: string programId } || string.IsNullOrWhiteSpace(programId)) return;
                var svc = App.Programs;
                var def = svc?.Library.FirstOrDefault(p => string.Equals(p.Id, programId, StringComparison.OrdinalIgnoreCase));
                if (svc == null || def == null || Shell is not { } shell) return;

                if (def.Tier == ProgramTier.Premium && !CoreEntitlement.HasPremium)
                {
                    shell.OpenAppSettingsSection("account");   // WPF ShowAppInfoPopup
                    return;
                }
                if (!svc.CanEnroll(def, out var reason))
                {
                    Serilog.Log.Information("Program enrollment blocked for {Program}: {Reason}", def.Id, reason);
                    await MessageDialog.ShowAsync(shell, Loc.Get("programs_unavailable_title"), Loc.Get("programs_unavailable"));
                    return;
                }
                var dialog = new ProgramEnrollDialog(def);
                if (await dialog.ShowDialogSafe<bool?>(shell) != true) return;
                svc.Enroll(def, dialog.StrictMode, ProgramShareLevel.Private, dialog.DayBoundaryHour, dialog.NudgeHour);
                RefreshBrowse();
                shell.RefreshProgramTodayCard();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Program enrollment failed");
            }
        }

        private async void BtnProgramPauseResume_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var svc = App.Programs;
                if (svc?.ActiveEnrollment == null) return;
                if (svc.ActiveEnrollment.State == ProgramEnrollmentState.Paused) svc.Resume();
                else if (!svc.Pause())
                {
                    if (Shell is { } shell)
                        await MessageDialog.ShowAsync(shell, Loc.Get("programs_pause_blocked_title"), Loc.Get("programs_pause_blocked_body"));
                    return;
                }
                RefreshBrowse();
                Shell?.RefreshProgramTodayCard();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Program pause/resume failed"); }
        }

        private async void BtnProgramWithdraw_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var svc = App.Programs;
                if (svc?.ActiveEnrollment == null || Shell is not { } shell) return;
                var runner = App.Sessions;
                var sessionLive = runner?.IsRunning == true && svc.IsProgramSession(runner.CurrentSession);
                var confirmed = await MessageDialog.ConfirmAsync(shell,
                    Loc.Get("programs_withdraw_confirm_title"),
                    Loc.Get(sessionLive ? "programs_withdraw_confirm_body_session" : "programs_withdraw_confirm_body"),
                    defaultToCancel: true,
                    okText: Loc.Get("btn_program_withdraw_confirm"),
                    cancelText: Loc.Get("btn_program_withdraw_keep"));
                if (!confirmed) return;
                // ponytail: WPF SuppressNextSessionSummary("program withdraw") - the recap still opens here.
                svc.Withdraw();
                RefreshBrowse();
                shell.RefreshProgramTodayCard();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Program withdraw failed"); }
        }

        private void BtnProgramRestart_Click(object? sender, RoutedEventArgs e)
        {
            try { App.Programs?.RestartAfterLapse(); RefreshBrowse(); Shell?.RefreshProgramTodayCard(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Program restart failed"); }
        }

        private void BtnProgramDismissGraduated_Click(object? sender, RoutedEventArgs e)
        {
            try { App.Programs?.DismissGraduated(); RefreshBrowse(); Shell?.RefreshProgramTodayCard(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Program graduation dismiss failed"); }
        }

        private async void BtnProgramSubmitRitual_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button { Tag: string taskId } || string.IsNullOrWhiteSpace(taskId)) return;
                if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get("programs_photo_dialog_title"),
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp" } } },
                });
                var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
                if (path == null) return;
                App.Programs?.SubmitRitualTask(taskId, path, null);
                RefreshBrowse();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Program ritual submission failed"); }
        }

        /// <summary>WPF StartProgramSession: today's session straight into the engine (no confirm).</summary>
        private async void BtnStartTodaySession_Click(object? sender, RoutedEventArgs e)
        {
            if (Shell is not { } shell) return;
            try
            {
                if (App.Sessions?.IsRunning == true)
                {
                    UpdateProgramSessionRow();
                    await MessageDialog.ShowAsync(shell, Loc.Get("programs_session_busy_title"), Loc.Get("programs_session_busy_body"));
                    return;
                }
                var session = App.Programs?.BuildTodaySession();
                if (session == null)
                {
                    await MessageDialog.ShowAsync(shell, Loc.Get("title_error"), Loc.Get("programs_session_start_failed"));
                    return;
                }
                shell.StartSession(session);
                Dispatcher.UIThread.Post(UpdateProgramSessionRow, DispatcherPriority.Background);
                Serilog.Log.Information("[Programs] Started program session: {Name}", session.Name);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[Programs] Failed to start today's session");
                try { await MessageDialog.ShowAsync(shell, Loc.Get("title_error"), Loc.Get("programs_session_start_failed")); } catch { }
            }
        }
    }
}
