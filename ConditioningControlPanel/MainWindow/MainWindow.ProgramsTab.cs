using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Views.Tabs;

namespace ConditioningControlPanel
{
    // -------------------------------------------------------------------------------------------
    // Training Programs tab (Views/Tabs/ProgramsTabView.xaml) and the dashboard's Today card.
    //
    // The view is dumb: every handler forwards here, and every refresh is a full repaint from
    // App.Programs. There is no ViewModel layer in this app, so the item lists are rebuilt as
    // plain carriers (Views/Tabs/ProgramsTabItems.cs) and handed to ItemsSource.
    // -------------------------------------------------------------------------------------------
    public partial class MainWindow
    {
        #region Programs Tab

        /// <summary>One-shot guard: the service outlives the tab, so we subscribe exactly once.</summary>
        private bool _programsSubscribed;

        // -----------------------------------------------------------------------------------
        // Wiring
        // -----------------------------------------------------------------------------------

        private void EnsureProgramsSubscribed()
        {
            if (_programsSubscribed) return;
            var svc = App.Programs;
            if (svc == null) return;

            svc.TodayChanged += OnProgramTodayChanged;
            svc.ProgramLapsed += OnProgramLapsed;
            svc.ProgramGraduated += OnProgramGraduated;
            _programsSubscribed = true;
        }

        private void OnProgramTodayChanged(object? sender, EventArgs e) => MarshalProgramRefresh();

        private void OnProgramLapsed(object? sender, Services.Program.ProgramLapsedEventArgs e) => MarshalProgramRefresh();

        private void OnProgramGraduated(object? sender, Services.Program.ProgramDayEventArgs e) => MarshalProgramRefresh();

        /// <summary>
        /// The service raises from a dispatcher timer today, but it may raise from a background
        /// completion tomorrow - marshal unconditionally and bail if the app is shutting down.
        /// </summary>
        private void MarshalProgramRefresh()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                if (dispatcher.CheckAccess()) RefreshProgramsUI();
                else dispatcher.BeginInvoke(new Action(RefreshProgramsUI));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program refresh marshalling failed");
            }
        }

        /// <summary>Loaded hook on the dashboard card - the earliest point we can subscribe without touching App.</summary>
        internal void ProgramTodayCard_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureProgramsSubscribed();
            RefreshProgramTodayCard();
        }

        // -----------------------------------------------------------------------------------
        // Small helpers
        // -----------------------------------------------------------------------------------

        private static Brush ProgramThemeBrush(string key, Brush fallback)
        {
            try { return Application.Current?.TryFindResource(key) as Brush ?? fallback; }
            catch { return fallback; }
        }

        private static Brush ProgramAccentBrush(string? hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color color)
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    return brush;
                }
            }
            catch { /* a bad accent must never break the tab */ }

            return ProgramThemeBrush("PinkBrush", Brushes.HotPink);
        }

        private static bool ProgramHasPremium => App.Patreon?.HasPremiumAccess == true;

        // -----------------------------------------------------------------------------------
        // Refresh
        // -----------------------------------------------------------------------------------

        internal void RefreshProgramsUI()
        {
            try
            {
                EnsureProgramsSubscribed();

                var tab = ProgramsTab;
                if (tab == null) return;

                tab.ProgramsBrowsePanel.Visibility = Visibility.Collapsed;
                tab.ProgramsRunPanel.Visibility = Visibility.Collapsed;
                tab.ProgramsLapsedPanel.Visibility = Visibility.Collapsed;
                tab.ProgramsGraduatedPanel.Visibility = Visibility.Collapsed;

                var svc = App.Programs;
                var enrollment = svc?.ActiveEnrollment;
                var program = svc?.ActiveProgram;

                if (svc == null || enrollment == null || program == null ||
                    enrollment.State == ProgramEnrollmentState.Withdrawn)
                {
                    BuildProgramBrowseList();
                    tab.ProgramsBrowsePanel.Visibility = Visibility.Visible;
                }
                else
                {
                    switch (enrollment.State)
                    {
                        case ProgramEnrollmentState.Lapsed:
                            BuildProgramLapsedPanel(program, enrollment);
                            tab.ProgramsLapsedPanel.Visibility = Visibility.Visible;
                            break;

                        case ProgramEnrollmentState.Graduated:
                            BuildProgramGraduatedPanel(program, enrollment);
                            tab.ProgramsGraduatedPanel.Visibility = Visibility.Visible;
                            break;

                        default:
                            BuildProgramRunPanel(program, enrollment);
                            tab.ProgramsRunPanel.Visibility = Visibility.Visible;
                            break;
                    }
                }

                RefreshProgramTodayCard();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshProgramsUI failed");
            }
        }

        // -----------------------------------------------------------------------------------
        // Browse
        // -----------------------------------------------------------------------------------

        private void BuildProgramBrowseList()
        {
            var tab = ProgramsTab;
            var svc = App.Programs;
            var library = svc?.Library ?? (IReadOnlyList<ProgramDefinition>)Array.Empty<ProgramDefinition>();

            var mutedBrush = ProgramThemeBrush("TextMutedBrush", Brushes.Gray);
            var pinkBrush = ProgramThemeBrush("PinkBrush", Brushes.HotPink);
            var surfaceBrush = ProgramThemeBrush("SurfaceBgBrush", Brushes.Transparent);
            var tintBrush = ProgramThemeBrush("TransparentPinkBrush", Brushes.Transparent);

            var items = new List<ProgramBrowseItem>();

            foreach (var def in library)
            {
                var isPremium = def.Tier == ProgramTier.Premium;
                var locked = isPremium && !ProgramHasPremium;

                var canEnroll = true;
                if (svc != null)
                {
                    // The service's reason strings are diagnostics, not UI copy - log, show our own.
                    canEnroll = svc.CanEnroll(def, out var reason);
                    if (!canEnroll && !locked)
                        App.Logger?.Debug("Program {Program} not enrollable: {Reason}", def.Id, reason);
                }

                var item = new ProgramBrowseItem
                {
                    ProgramId = def.Id,
                    Icon = def.Icon,
                    Title = def.Title,
                    Subtitle = def.Subtitle,
                    Pitch = def.Pitch,
                    LengthLabel = Loc.GetF("programs_length_days", def.LengthDays),
                    TierLabel = isPremium ? Loc.Get("programs_tier_premium") : Loc.Get("programs_tier_free"),
                    TierBrush = isPremium ? pinkBrush : mutedBrush,
                    TierBackground = isPremium ? tintBrush : surfaceBrush,
                    AccentBrush = ProgramAccentBrush(def.AccentColor),
                    IsLocked = locked
                };

                if (locked)
                {
                    item.ActionText = Loc.Get("btn_program_locked");
                    item.IsActionEnabled = true; // routes to the App Info popup, never to enrollment
                    item.ReasonText = Loc.Get("programs_locked_hint");
                    item.ReasonVisibility = Visibility.Visible;
                    item.CardOpacity = 0.72;
                }
                else if (!canEnroll)
                {
                    item.ActionText = Loc.Get("btn_program_enroll");
                    item.IsActionEnabled = false;
                    item.ReasonText = Loc.Get("programs_unavailable");
                    item.ReasonVisibility = Visibility.Visible;
                    item.CardOpacity = 0.72;
                }
                else
                {
                    item.ActionText = Loc.Get("btn_program_enroll");
                }

                items.Add(item);
            }

            tab.ProgramLibraryList.ItemsSource = items;
            tab.TxtProgramsBrowseEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // -----------------------------------------------------------------------------------
        // Run view
        // -----------------------------------------------------------------------------------

        private void BuildProgramRunPanel(ProgramDefinition program, ProgramEnrollment enrollment)
        {
            var tab = ProgramsTab;
            var svc = App.Programs;
            var accent = ProgramAccentBrush(program.AccentColor);
            var chapter = svc?.TodayChapter;

            tab.RunAccentBar.Background = accent;
            tab.TxtRunProgramTitle.Text = program.Title;
            tab.TxtRunChapterName.Text = chapter?.Name ?? program.Subtitle;
            tab.TxtRunChapterName.Foreground = accent;
            tab.TxtRunDayCounter.Text = Loc.GetF("programs_day_counter", enrollment.CurrentDay, program.LengthDays);

            tab.RunStrictBadge.Visibility = enrollment.StrictMode ? Visibility.Visible : Visibility.Collapsed;

            if (enrollment.AttemptNumber > 1)
            {
                tab.TxtRunAttempt.Text = Loc.GetF("programs_attempt", enrollment.AttemptNumber);
                tab.RunAttemptBadge.Visibility = Visibility.Visible;
            }
            else
            {
                tab.RunAttemptBadge.Visibility = Visibility.Collapsed;
            }

            tab.TxtRunDaysOff.Text = enrollment.DaysOffRemaining > 0
                ? Loc.GetF("programs_days_off_left", enrollment.DaysOffRemaining)
                : Loc.Get("programs_days_off_none");

            var paused = enrollment.State == ProgramEnrollmentState.Paused;
            tab.RunPausedNote.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
            tab.BtnProgramPauseResume.Content = paused ? Loc.Get("btn_program_resume") : Loc.Get("btn_program_pause");

            BuildProgramDayStrip(program, enrollment, accent);
            BuildProgramTodayPanel(program, enrollment, accent);
        }

        private void BuildProgramDayStrip(ProgramDefinition program, ProgramEnrollment enrollment, Brush accent)
        {
            var mutedBrush = ProgramThemeBrush("TextMutedBrush", Brushes.Gray);
            var lightBrush = ProgramThemeBrush("TextLightBrush", Brushes.White);
            var surfaceBrush = ProgramThemeBrush("SurfaceBgBrush", Brushes.Transparent);
            var borderBrush = ProgramThemeBrush("GlassBorderBrush", Brushes.Gray);
            var dangerBrush = ProgramThemeBrush("DangerBrush", Brushes.IndianRed);

            var pips = new List<ProgramDayPip>(Math.Max(0, program.LengthDays));

            for (int i = 1; i <= program.LengthDays; i++)
            {
                var record = enrollment.GetRecord(i);
                var pip = new ProgramDayPip { DayIndex = i, Label = i.ToString() };

                if (i == enrollment.CurrentDay)
                {
                    pip.Fill = Brushes.Transparent;
                    pip.Stroke = accent;
                    pip.PipBorderThickness = new Thickness(2);
                    pip.LabelBrush = accent;
                    pip.LabelWeight = FontWeights.Bold;
                    pip.Tip = Loc.GetF("programs_pip_today", i);
                }
                else if (record?.DayCompleted == true)
                {
                    pip.Fill = accent;
                    pip.Stroke = accent;
                    pip.LabelBrush = lightBrush;
                    pip.LabelWeight = FontWeights.SemiBold;
                    pip.Tip = Loc.GetF("programs_pip_done", i);
                }
                else if (record?.Missed == true)
                {
                    pip.Fill = Brushes.Transparent;
                    pip.Stroke = dangerBrush;
                    pip.LabelBrush = dangerBrush;
                    pip.Tip = Loc.GetF("programs_pip_missed", i);
                }
                else
                {
                    pip.Fill = surfaceBrush;
                    pip.Stroke = borderBrush;
                    pip.LabelBrush = mutedBrush;
                    pip.PipOpacity = 0.65;
                    pip.Tip = Loc.GetF("programs_pip_locked", i);
                }

                pips.Add(pip);
            }

            ProgramsTab.ProgramDayStrip.ItemsSource = pips;
        }

        private void BuildProgramTodayPanel(ProgramDefinition program, ProgramEnrollment enrollment, Brush accent)
        {
            var tab = ProgramsTab;
            var svc = App.Programs;
            var day = svc?.Today;
            var record = svc?.TodayRecord;

            if (day == null || record == null)
            {
                tab.TodayPanel.Visibility = Visibility.Collapsed;
                return;
            }

            tab.TodayPanel.Visibility = Visibility.Visible;

            var mutedBrush = ProgramThemeBrush("TextMutedBrush", Brushes.Gray);
            var lightBrush = ProgramThemeBrush("TextLightBrush", Brushes.White);
            var paused = enrollment.State == ProgramEnrollmentState.Paused;

            // Boss days read differently: the accent border makes the whole panel escalate.
            tab.TodayPanel.BorderBrush = day.IsBoss ? accent : ProgramThemeBrush("GlassBorderBrush", Brushes.Gray);
            tab.TodayPanel.BorderThickness = new Thickness(day.IsBoss ? 2 : 1);
            tab.TodayBossBadge.Visibility = day.IsBoss ? Visibility.Visible : Visibility.Collapsed;
            tab.TodayReturnBadge.Visibility = record.IsReturnDay ? Visibility.Visible : Visibility.Collapsed;

            tab.TxtTodayTitle.Text = day.Title;
            tab.TxtTodayBlurb.Text = day.Blurb;
            tab.TodayCompleteBanner.Visibility = record.DayCompleted ? Visibility.Visible : Visibility.Collapsed;

            // --- Session slot ---
            // Glyph / button / progress strip all live in UpdateProgramSessionRow so the
            // full repaint and the once-a-second live tick can never disagree about them.
            tab.TxtTodaySessionMinutes.Text = Loc.GetF("programs_session_minutes", day.SessionMinutes);
            UpdateProgramSessionRow();

            // --- Ambient layer ---
            var ambient = day.Ambient;
            if (ambient != null && (!string.IsNullOrWhiteSpace(ambient.Description) || ambient.RequiredMinutes > 0))
            {
                tab.TodayAmbientRow.Visibility = Visibility.Visible;
                tab.TxtTodayAmbient.Text = ambient.Description;
                if (ambient.RequiredMinutes > 0)
                {
                    tab.TxtTodayAmbientProgress.Text = Loc.GetF("programs_ambient_progress",
                        Math.Min(record.AmbientMinutes, ambient.RequiredMinutes), ambient.RequiredMinutes);
                    tab.TxtTodayAmbientProgress.Visibility = Visibility.Visible;
                }
                else
                {
                    tab.TxtTodayAmbientProgress.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                tab.TodayAmbientRow.Visibility = Visibility.Collapsed;
            }

            // --- Tasks ---
            var items = new List<ProgramTaskItem>();
            var hasRitual = false;

            foreach (var task in day.Tasks)
            {
                var complete = svc != null && svc.IsTaskComplete(record, task);
                var blocked = svc != null && svc.IsTaskBlocked(task);
                var isRitual = task.Kind == ProgramTaskKind.Ritual;
                if (isRitual) hasRitual = true;

                var item = new ProgramTaskItem
                {
                    TaskId = task.Id,
                    Description = task.Description,
                    StatusGlyph = complete ? "✓" : "○",
                    StatusBrush = complete ? accent : mutedBrush,
                    TextBrush = complete ? mutedBrush : lightBrush,
                    RowOpacity = blocked ? 0.5 : 1.0
                };

                if (!complete && task.Kind == ProgramTaskKind.AutoVerified && task.TargetValue > 1)
                {
                    record.TaskProgress.TryGetValue(task.Id, out var current);
                    item.ProgressText = Loc.GetF("programs_task_progress",
                        Math.Min(current, task.TargetValue), task.TargetValue);
                    item.ProgressVisibility = Visibility.Visible;
                }

                if (blocked)
                {
                    item.BadgeText = Loc.Get("programs_task_locked");
                    item.BadgeVisibility = Visibility.Visible;
                }
                else if (task.Optional)
                {
                    item.BadgeText = Loc.Get("programs_task_optional");
                    item.BadgeVisibility = Visibility.Visible;
                }

                item.SubmitVisibility = isRitual && !complete && !blocked && !paused
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                items.Add(item);
            }

            tab.TodayTaskList.ItemsSource = items;
            tab.TxtTodayNoTasks.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            tab.TxtRitualPrivacyNote.Visibility = hasRitual ? Visibility.Visible : Visibility.Collapsed;
        }

        // -----------------------------------------------------------------------------------
        // Live session row
        // -----------------------------------------------------------------------------------

        /// <summary>mm:ss, or h:mm:ss past the hour. Never negative.</summary>
        private static string FormatProgramClock(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
                : $"{(int)span.TotalMinutes}:{span.Seconds:D2}";
        }

        /// <summary>
        /// Repaints the Session row's live state: the glyph, the button and the progress strip.
        ///
        /// Called once per engine tick from OnSessionProgressUpdated (MainWindow.Presets.cs), so it
        /// must stay cheap - no list rebuilds, no disk, no service walks. It does NOT spin a timer:
        /// SessionEngine's own 1-second DispatcherTimer already drives ProgressUpdated, and all three
        /// engine construction sites (Presets, RemoteControl, ProgramsTab) subscribe the same
        /// MainWindow handlers, so there is nothing extra to subscribe and nothing to unsubscribe.
        ///
        /// A session this tab did NOT start (Dashboard, preset, remote) is deliberately not claimed:
        /// no progress bar, no completion, just a disabled button saying something else is running.
        /// ProgramService.IsProgramSession is the discriminator, the same one that decides whether a
        /// completed session may tick the day.
        /// </summary>
        internal void UpdateProgramSessionRow()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                // No IsLoaded gate on purpose: the first full repaint can run before the window
                // finishes loading, and gating here would leave the row showing the XAML default
                // ("Start today's session") over a session that is already in flight. Only child
                // control properties are touched, so this is safe pre-load; teardown is covered
                // by the dispatcher check above.
                var tab = ProgramsTab;
                if (tab?.TodaySessionProgressRow == null) return;

                var svc = App.Programs;
                var enrollment = svc?.ActiveEnrollment;
                var record = svc?.TodayRecord;

                if (svc == null || enrollment == null || record == null)
                {
                    tab.TodaySessionProgressRow.Visibility = Visibility.Collapsed;
                    return;
                }

                var mutedBrush = ProgramThemeBrush("TextMutedBrush", Brushes.Gray);
                var accent = ProgramAccentBrush(svc.ActiveProgram?.AccentColor);
                var paused = enrollment.State == ProgramEnrollmentState.Paused;

                var engine = _sessionEngine;
                var running = engine?.IsRunning == true;
                var current = running ? engine!.CurrentSession : null;
                var isOurs = running && svc.IsProgramSession(current);

                if (isOurs && current != null)
                {
                    var total = TimeSpan.FromMinutes(Math.Max(1, current.DurationMinutes));
                    var elapsed = engine!.ElapsedTime;
                    if (elapsed > total) elapsed = total;

                    tab.TodaySessionProgressBar.Foreground = accent;
                    tab.TodaySessionProgressBar.Value = Math.Clamp(engine.ProgressPercent, 0, 100);

                    var clock = Loc.GetF("programs_session_progress",
                        FormatProgramClock(elapsed), FormatProgramClock(total));
                    tab.TxtTodaySessionProgress.Text = engine.IsPaused
                        ? Loc.GetF("programs_session_progress_paused", clock)
                        : clock;

                    tab.TodaySessionProgressRow.Visibility = Visibility.Visible;

                    tab.TxtTodaySessionGlyph.Text = "◉";
                    tab.TxtTodaySessionGlyph.Foreground = accent;

                    // Deliberately NOT a second stop button: the bottom bar's STOP is on every
                    // screen and owns ending a session. A stop here would sit exactly where
                    // "Start today's session" was a second ago - a misclick would abandon the day.
                    tab.BtnStartTodaySession.Content = Loc.Get("programs_session_in_progress");
                    tab.BtnStartTodaySession.IsEnabled = false;
                    tab.BtnStartTodaySession.ToolTip = Loc.Get("programs_session_stop_hint");
                    return;
                }

                // Nothing of ours in flight - hide the strip and fall back to the slot states.
                tab.TodaySessionProgressRow.Visibility = Visibility.Collapsed;
                tab.TodaySessionProgressBar.Value = 0;
                tab.BtnStartTodaySession.ToolTip = null;

                if (record.SessionCompleted)
                {
                    tab.TxtTodaySessionGlyph.Text = "✓";
                    tab.TxtTodaySessionGlyph.Foreground = accent;
                    tab.BtnStartTodaySession.Content = Loc.Get("programs_session_done");
                    tab.BtnStartTodaySession.IsEnabled = false;
                    return;
                }

                tab.TxtTodaySessionGlyph.Text = "○";
                tab.TxtTodaySessionGlyph.Foreground = mutedBrush;

                if (running)
                {
                    // Someone else's session. Say so plainly, and refuse to start on top of it -
                    // StartProgramSession would otherwise kill it without asking.
                    tab.BtnStartTodaySession.Content = Loc.Get("programs_session_other_running");
                    tab.BtnStartTodaySession.IsEnabled = false;
                    tab.BtnStartTodaySession.ToolTip = Loc.Get("programs_session_other_running_hint");
                    return;
                }

                tab.BtnStartTodaySession.Content = Loc.Get("btn_program_start_session");
                tab.BtnStartTodaySession.IsEnabled = !paused;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "UpdateProgramSessionRow failed");
            }
        }

        // -----------------------------------------------------------------------------------
        // Session start/end announcements
        //
        // Both fire from the shared MainWindow session handlers (OnSessionStarted /
        // OnSessionStopped in MainWindow.Presets.cs), which ALL THREE engine construction sites
        // subscribe - Presets.cs, RemoteControl.cs and ProgramsTab.cs. Hooking there rather than
        // at a construction site is what stops this silently working in one path only.
        //
        // Surface: App.Notifications (Services/Notifications/NotificationService.cs), the app's
        // standard non-modal toast host in MainWindow's top-right. No new window, no new service.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Announces the start of a session THIS program launched. A foreign session is silent -
        /// the Programs feature has nothing to say about it.
        /// </summary>
        internal void AnnounceProgramSessionStarted()
        {
            try
            {
                var svc = App.Programs;
                var program = svc?.ActiveProgram;
                var enrollment = svc?.ActiveEnrollment;
                if (svc == null || program == null || enrollment == null) return;
                if (!svc.IsProgramSession(_sessionEngine?.CurrentSession)) return;

                App.Notifications?.Show(
                    Loc.GetF("programs_toast_session_started", program.Title, enrollment.CurrentDay),
                    Services.NotificationType.Info,
                    TimeSpan.FromSeconds(6));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program session start toast failed");
            }
        }

        /// <summary>
        /// Announces the end of a session this program launched.
        ///
        /// SessionEngine raises SessionStopped BEFORE SessionCompleted (StopSession fires the
        /// former, then does the XP/achievement work, then the latter), and it is
        /// SessionCompleted that lets ProgramService tick today's slot. So the caller passes the
        /// program-session verdict captured at SessionStopped time and this runs deferred at
        /// Background priority - by then StopSession has fully unwound and TodayRecord tells us
        /// whether the session finished or was cut short.
        /// </summary>
        internal void AnnounceProgramSessionEnded(bool wasProgramSession)
        {
            if (!wasProgramSession) return;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    try
                    {
                        if (Application.Current?.Dispatcher == null) return;
                        if (Application.Current.Dispatcher.HasShutdownStarted) return;

                        var svc = App.Programs;
                        var program = svc?.ActiveProgram;
                        if (svc == null || program == null) return;

                        var completed = svc.TodayRecord?.SessionCompleted == true;

                        App.Notifications?.Show(
                            completed
                                ? Loc.GetF("programs_toast_session_completed", program.Title)
                                : Loc.GetF("programs_toast_session_ended_early", program.Title),
                            completed ? Services.NotificationType.Success : Services.NotificationType.Warning,
                            TimeSpan.FromSeconds(8),
                            Loc.Get("programs_toast_view_today"),
                            () => ShowTab("programs"));

                        // The slot may have flipped to done during that unwind.
                        UpdateProgramSessionRow();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Program session end toast failed");
                    }
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program session end toast could not be scheduled");
            }
        }

        // -----------------------------------------------------------------------------------
        // Lapsed / Graduated
        // -----------------------------------------------------------------------------------

        private void BuildProgramLapsedPanel(ProgramDefinition program, ProgramEnrollment enrollment)
        {
            ProgramsTab.TxtLapsedBody.Text = Loc.GetF("programs_lapsed_body",
                program.Title, enrollment.AttemptNumber + 1);
        }

        private void BuildProgramGraduatedPanel(ProgramDefinition program, ProgramEnrollment enrollment)
        {
            var tab = ProgramsTab;
            tab.TxtGraduatedSub.Text = Loc.GetF("programs_graduated_sub", program.Title);
            tab.TxtGraduatedStats.Text = Loc.GetF("programs_graduated_stats",
                enrollment.AttemptNumber, enrollment.PerfectDayCount, program.LengthDays);
        }

        // -----------------------------------------------------------------------------------
        // Handlers
        // -----------------------------------------------------------------------------------

        internal void BtnProgramEnroll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.Controls.Button btn) return;
                var programId = btn.Tag as string;
                if (string.IsNullOrWhiteSpace(programId)) return;

                var svc = App.Programs;
                var def = svc?.Library?.FirstOrDefault(p =>
                    string.Equals(p.Id, programId, StringComparison.OrdinalIgnoreCase));
                if (svc == null || def == null) return;

                // Premium the user can't take: the ✨ card is an ad, not an entry point.
                if (def.Tier == ProgramTier.Premium && !ProgramHasPremium)
                {
                    ShowAppInfoPopup();
                    return;
                }

                if (!svc.CanEnroll(def, out var reason))
                {
                    App.Logger?.Information("Program enrollment blocked for {Program}: {Reason}", def.Id, reason);
                    ShowStyledDialog(Loc.Get("programs_unavailable_title"), Loc.Get("programs_unavailable"),
                        Loc.Get("btn_ok"), "");
                    return;
                }

                var dialog = new ProgramEnrollDialog(def) { Owner = this };
                if (dialog.ShowDialog() != true) return;

                svc.Enroll(def, dialog.StrictMode, dialog.ShareLevel, dialog.DayBoundaryHour, dialog.NudgeHour);
                RefreshProgramsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Program enrollment failed");
            }
        }

        internal void BtnProgramPauseResume_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var svc = App.Programs;
                if (svc?.ActiveEnrollment == null) return;

                if (svc.ActiveEnrollment.State == ProgramEnrollmentState.Paused) svc.Resume();
                else svc.Pause();

                RefreshProgramsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program pause/resume failed");
            }
        }

        /// <summary>
        /// Always available, on every screen. The confirm exists so a misclick doesn't end a run -
        /// it does not argue, guilt or bargain.
        /// </summary>
        internal void BtnProgramWithdraw_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.Programs?.ActiveEnrollment == null) return;

                var confirmed = ShowStyledDialog(
                    Loc.Get("programs_withdraw_confirm_title"),
                    Loc.Get("programs_withdraw_confirm_body"),
                    Loc.Get("btn_program_withdraw_confirm"),
                    Loc.Get("btn_program_withdraw_keep"));

                if (!confirmed) return;

                App.Programs?.Withdraw();
                RefreshProgramsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program withdraw failed");
            }
        }

        internal void BtnProgramRestart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.Programs?.RestartAfterLapse();
                RefreshProgramsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program restart failed");
            }
        }

        internal void BtnProgramDismissGraduated_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.Programs?.DismissGraduated();
                RefreshProgramsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program graduation dismiss failed");
            }
        }

        /// <summary>Ritual task: pick a photo, hand it to the service. The file never leaves this machine.</summary>
        internal void BtnProgramSubmitRitual_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.Controls.Button btn) return;
                var taskId = btn.Tag as string;
                if (string.IsNullOrWhiteSpace(taskId)) return;

                var picker = new Microsoft.Win32.OpenFileDialog
                {
                    Title = Loc.Get("programs_photo_dialog_title"),
                    Filter = Loc.Get("programs_photo_filter"),
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (picker.ShowDialog(this) != true) return;

                App.Programs?.SubmitRitualTask(taskId!, picker.FileName, null);
                RefreshProgramsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program ritual submission failed");
            }
        }

        internal void BtnStartTodaySession_Click(object sender, RoutedEventArgs e) => StartProgramSession();

        // -----------------------------------------------------------------------------------
        // Starting today's session
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Mirrors StartSessionFromRemote (MainWindow.RemoteControl.cs). The session engine is a
        /// private MainWindow field created lazily, so this is the only place the Programs feature
        /// can reach it. StartSessionAsync THROWS when a session is already running - it does not
        /// return false - so a session already in flight is refused here, before anything is built.
        /// </summary>
        internal async void StartProgramSession()
        {
            try
            {
                // Never start on top of a running session. This used to stop whatever was running
                // and take over, which silently killed a session started from the Dashboard, a
                // preset or the remote. The button is disabled in that state too
                // (UpdateProgramSessionRow); this is the guard behind it.
                if (_sessionEngine?.IsRunning == true)
                {
                    App.Logger?.Information("[Programs] Start refused - a session is already running");
                    UpdateProgramSessionRow();
                    ShowStyledDialog(Loc.Get("programs_session_busy_title"),
                        Loc.Get("programs_session_busy_body"), Loc.Get("btn_ok"), "");
                    return;
                }

                var session = App.Programs?.BuildTodaySession();
                if (session == null)
                {
                    App.Logger?.Warning("[Programs] BuildTodaySession returned null - nothing to start");
                    ShowStyledDialog(Loc.Get("title_error"), Loc.Get("programs_session_start_failed"),
                        Loc.Get("btn_ok"), "");
                    return;
                }

                if (_sessionEngine == null)
                {
                    _sessionEngine = new Services.SessionEngine(this);
                    _sessionEngine.SessionCompleted += OnSessionCompleted;
                    _sessionEngine.ProgressUpdated += OnSessionProgressUpdated;
                    _sessionEngine.PhaseChanged += OnSessionPhaseChanged;
                    _sessionEngine.SessionStarted += OnSessionStarted;
                    _sessionEngine.SessionStopped += OnSessionStopped;
                }

                // Both attaches detach any previous engine first, so re-attaching is safe.
                App.Bark?.AttachSessionEngine(_sessionEngine);
                App.Programs?.AttachSessionEngine(_sessionEngine);

                if (!_isRunning)
                {
                    StartEngine();

                    // StartEngine turns on whatever the saved settings had; the session engine
                    // owns the overlays from here.
                    App.Overlay?.StopPinkFilter();
                    App.Overlay?.StopSpiral();
                }

                App.IsSessionRunning = true;
                await _sessionEngine.StartSessionAsync(session);

                App.Logger?.Information("[Programs] Started program session: {Name}", session.Name);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[Programs] Failed to start today's session");
                try
                {
                    ShowStyledDialog(Loc.Get("title_error"), Loc.Get("programs_session_start_failed"),
                        Loc.Get("btn_ok"), "");
                }
                catch { /* the dialog failing must not mask the original error */ }
            }
        }

        // -----------------------------------------------------------------------------------
        // Dashboard "Today" card
        // -----------------------------------------------------------------------------------

        /// <summary>Click target on the dashboard card.</summary>
        internal void ProgramTodayCard_Click(object sender, RoutedEventArgs e) => ShowTab("programs");

        /// <summary>
        /// Repaints the dashboard's Today card. Collapsed entirely when no program is running -
        /// the dashboard is crowded enough without an empty placeholder.
        /// </summary>
        internal void RefreshProgramTodayCard()
        {
            try
            {
                var dash = SettingsTab;
                if (dash?.ProgramTodayCard == null) return;

                var svc = App.Programs;
                var enrollment = svc?.ActiveEnrollment;
                var program = svc?.ActiveProgram;
                var day = svc?.Today;
                var record = svc?.TodayRecord;

                if (svc == null || enrollment == null || program == null || day == null || record == null ||
                    enrollment.State is ProgramEnrollmentState.Withdrawn or ProgramEnrollmentState.Graduated)
                {
                    dash.ProgramTodayCard.Visibility = Visibility.Collapsed;
                    return;
                }

                var accent = ProgramAccentBrush(program.AccentColor);
                dash.ProgramTodayCard.BorderBrush = accent;
                dash.ProgramTodayAccent.Background = accent;
                dash.TxtProgramTodayTitle.Foreground = accent;
                dash.TxtProgramTodayTitle.Text = program.Title;

                string remainder;
                if (enrollment.State == ProgramEnrollmentState.Paused)
                {
                    remainder = Loc.Get("programs_card_paused");
                }
                else if (enrollment.State == ProgramEnrollmentState.Lapsed)
                {
                    remainder = Loc.Get("programs_card_lapsed");
                }
                else
                {
                    var parts = new List<string>();
                    if (!record.SessionCompleted) parts.Add(Loc.Get("programs_card_session_left"));

                    var tasksLeft = svc.RequiredTasks(day).Count(t => !svc.IsTaskComplete(record, t));
                    if (tasksLeft == 1) parts.Add(Loc.Get("programs_card_task_left_one"));
                    else if (tasksLeft > 1) parts.Add(Loc.GetF("programs_card_task_left_many", tasksLeft));

                    remainder = parts.Count == 0
                        ? Loc.Get("programs_card_all_done")
                        : Loc.GetF("programs_card_remaining", string.Join(", ", parts));
                }

                dash.TxtProgramTodayLine.Text = string.Join("  ·  ", new[]
                {
                    Loc.GetF("programs_card_day", enrollment.CurrentDay),
                    day.Title,
                    remainder
                });

                dash.ProgramTodayCard.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshProgramTodayCard failed");
            }
        }

        #endregion
    }
}
