using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The READ-ONLY run view: an existing enrollment from <see cref="AvApp.Programs"/> (built with
    /// ProgramService.CreateReadOnly, CHECKPOINT A) drawn the way WPF MainWindow.ProgramsTab.cs:539-1450
    /// and :1978-1998 draw it. Nothing here writes, rolls over, credits a task or starts a session:
    /// the lifecycle buttons are hidden in the XAML, the ritual/mantra doors stay closed, and because
    /// no rollover runs on this head the day is labelled "last saved" whenever the program clock has
    /// moved past it (<see cref="IsSnapshotStale"/>), computed for display only.
    /// ponytail: ignition FX, day/task pops, node breathe and the live session row are programs
    /// slices 3 and 5 (~/ccp-port/briefs/programs-run-plan.md).
    /// </summary>
    public partial class ProgramsTabView
    {
        /// <summary>The wall clock the "last saved" label is computed from; tests step it (P08).</summary>
        internal static TimeProvider Clock { get; set; } = TimeProvider.System;

        private ProgramService? _subscribed;
        private bool _refreshPending;

        /// <summary>The stale verdict the shown run panel was built with; null when no run panel is up.
        /// A read-only service raises no TodayChanged, so crossing the day boundary is noticed here.</summary>
        private bool? _builtStale;

        /// <summary>
        /// True when the program clock has moved past the saved day: the state is then a snapshot,
        /// not today. Display only - ProgramClock.ProgramDate is pure and nothing is written back.
        /// </summary>
        internal static bool IsSnapshotStale(ProgramEnrollment enrollment, DateTime localNow) =>
            ProgramClock.ProgramDate(localNow, enrollment.DayBoundaryHour) != enrollment.CurrentDayDate.Date;

        // ---- wiring (WPF :113-190): TodayChanged / Lapsed / Graduated -> one marshalled refresh ----

        private void SubscribePrograms()
        {
            var svc = AvApp.Programs;
            if (ReferenceEquals(svc, _subscribed)) return;
            UnsubscribePrograms();
            if (svc == null) return;
            svc.TodayChanged += OnProgramChanged;
            svc.ProgramLapsed += OnProgramChanged;
            svc.ProgramGraduated += OnProgramChanged;
            _subscribed = svc;
        }

        private void UnsubscribePrograms()
        {
            if (_subscribed == null) return;
            _subscribed.TodayChanged -= OnProgramChanged;
            _subscribed.ProgramLapsed -= OnProgramChanged;
            _subscribed.ProgramGraduated -= OnProgramChanged;
            _subscribed = null;
        }

        private void OnProgramChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
        {
            if (VisualRoot is not null) RefreshPrograms();
        });

        /// <summary>WPF :509-516: a hidden tab only remembers that it is stale; showing it flushes.</summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != IsVisibleProperty || !IsVisible || VisualRoot is null) return;
            var enrollment = AvApp.Programs?.ActiveEnrollment;
            var boundaryCrossed = _builtStale is { } built && enrollment != null &&
                                  built != IsSnapshotStale(enrollment, Clock.GetLocalNow().DateTime);
            if (_refreshPending || boundaryCrossed) RefreshPrograms();
        }

        /// <summary>WPF RefreshProgramsUI + RebuildProgramsTab (:490-584): pick the panel, build it, reveal it.</summary>
        internal void RefreshPrograms()
        {
            if (!IsVisible && VisualRoot is not null)
            {
                _refreshPending = true;
                return;
            }
            _refreshPending = false;
            SubscribePrograms();

            var svc = AvApp.Programs;
            var enrollment = svc?.ActiveEnrollment;
            var program = svc?.ActiveProgram;
            var browse = svc == null || enrollment == null || program == null ||
                         enrollment.State == ProgramEnrollmentState.Withdrawn;
            var lapsed = !browse && enrollment!.State == ProgramEnrollmentState.Lapsed;
            var graduated = !browse && enrollment!.State == ProgramEnrollmentState.Graduated;
            var run = !browse && !lapsed && !graduated;
            _builtStale = null;

            try
            {
                if (browse) RefreshBrowse();
                else if (lapsed)
                    Find<TextBlock>("TxtLapsedBody").Text =
                        Loc.GetF("programs_lapsed_body", program!.Title, enrollment!.AttemptNumber + 1);
                else if (graduated)
                {
                    Find<TextBlock>("TxtGraduatedSub").Text = Loc.GetF("programs_graduated_sub", program!.Title);
                    Find<TextBlock>("TxtGraduatedStats").Text = Loc.GetF("programs_graduated_stats",
                        enrollment!.AttemptNumber, enrollment.PerfectDayCount, program.LengthDays);
                }
                else BuildRunPanel(svc!, program!, enrollment!);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Programs tab build failed for {Program}", program?.Id ?? "(browse)");
            }
            finally
            {
                Find<StackPanel>("ProgramsBrowsePanel").IsVisible = browse;
                Find<StackPanel>("ProgramsLapsedPanel").IsVisible = lapsed;
                Find<StackPanel>("ProgramsGraduatedPanel").IsVisible = graduated;
                Find<StackPanel>("ProgramsRunPanel").IsVisible = run;
                Find<Border>("RunReadOnlyNote").IsVisible = !browse;
            }
        }

        // ---- run panel (WPF BuildProgramRunPanel :693-792) ----

        private void BuildRunPanel(ProgramService svc, ProgramDefinition program, ProgramEnrollment enrollment)
        {
            var chapter = svc.TodayChapter;
            var accent = MainShellWindow.AccentBrush(
                !string.IsNullOrWhiteSpace(chapter?.AccentColor) ? chapter!.AccentColor : program.AccentColor);
            var stale = IsSnapshotStale(enrollment, Clock.GetLocalNow().DateTime);
            _builtStale = stale;

            Find<Border>("RunAccentBar").Background = accent;
            var sigilPath = ProgramArtPaths.Sigil(program);
            var sigil = sigilPath == null ? null : ModArt.FirstOf(new[] { sigilPath }, 256);
            Find<Grid>("RunSigilBox").IsVisible = sigil != null;
            Find<Border>("RunSigilHost").IsVisible = sigil != null;
            Find<Ellipse>("RunSigilGlow").IsVisible = sigil != null;
            Find<Border>("RunAccentBar").IsVisible = sigil == null;
            if (sigil != null)
            {
                var mark = Find<Rectangle>("RunSigil");
                mark.OpacityMask = new ImageBrush(sigil) { Stretch = Stretch.Uniform };
                mark.Fill = accent;
                Find<Ellipse>("RunSigilGlow").Fill = MainShellWindow.ProgramRadialGlowBrush(accent, 150);
            }

            Find<TextBlock>("TxtRunProgramTitle").Text = program.Title;
            var chapterName = Find<TextBlock>("TxtRunChapterName");
            chapterName.Text = chapter?.Name ?? program.Subtitle;
            chapterName.Foreground = accent;
            Find<TextBlock>("TxtRunDayCounter").Text =
                Loc.GetF("programs_day_counter", enrollment.CurrentDay, program.LengthDays);
            Find<Border>("RunStrictBadge").IsVisible = enrollment.StrictMode;
            Find<Border>("RunAttemptBadge").IsVisible = enrollment.AttemptNumber > 1;
            if (enrollment.AttemptNumber > 1)
                Find<TextBlock>("TxtRunAttempt").Text = Loc.GetF("programs_attempt", enrollment.AttemptNumber);

            Find<TextBlock>("TxtRunStatDone").Text = $"{enrollment.CompletedDayCount} / {program.LengthDays}";
            Find<TextBlock>("TxtRunStatPerfect").Text = enrollment.PerfectDayCount.ToString();
            Find<TextBlock>("TxtRunStatDaysOff").Text = enrollment.DaysOffRemaining.ToString();

            var chapterReward = chapter?.RewardDescription;
            var chip = Find<Border>("RunChapterRewardChip");
            chip.IsVisible = !string.IsNullOrWhiteSpace(chapterReward);
            Find<TextBlock>("TxtRunChapterReward").Text = chapterReward ?? "";
            ToolTip.SetTip(chip, chapterReward);

            Find<Border>("RunPausedNote").IsVisible = enrollment.State == ProgramEnrollmentState.Paused;

            BuildDayStrip(program, enrollment, accent, stale);
            BuildTodayPanel(svc, program, enrollment, accent, stale);
        }

        // ---- reward track (WPF BuildProgramDayStrip :794-939) ----

        private void BuildDayStrip(ProgramDefinition program, ProgramEnrollment enrollment, IBrush accent, bool stale)
        {
            var muted = Theme("TextMutedBrush", Brushes.Gray);
            var light = Theme("TextLightBrush", Brushes.White);
            var surface = Theme("SurfaceBgBrush", Brushes.Transparent);
            var border = Theme("GlassBorderBrush", Brushes.Gray);
            var danger = Theme("DangerBrush", Brushes.IndianRed);
            var days = program.AllDays.GroupBy(d => d.DayIndex).ToDictionary(g => g.Key, g => g.First());
            var glow = MainShellWindow.ProgramRadialGlowBrush(accent, 170);
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
                    pip.RewardTip = !string.IsNullOrWhiteSpace(day.RewardDescription)
                        ? day.RewardDescription! : Loc.Get("programs_boss_badge");
                }

                string tip;
                if (i == enrollment.CurrentDay)
                {
                    pip.Stroke = accent;
                    pip.PipBorderThickness = new Thickness(2.5);
                    pip.LabelBrush = accent;
                    pip.LabelWeight = FontWeight.Bold;
                    pip.NodeSize = 42;
                    pip.LabelSize = 15;
                    pip.IsCurrent = true;
                    pip.GlowBrush = glow;
                    pip.GlowVisible = true;
                    tip = Loc.GetF(stale ? "programs_pip_last_saved" : "programs_pip_today", i);
                }
                else if (record?.DayCompleted == true)
                {
                    pip.Fill = accent;
                    pip.Stroke = accent;
                    pip.Label = "✓";
                    pip.LabelBrush = light;
                    pip.LabelWeight = FontWeight.Bold;
                    pip.NodeSize = 32;
                    pip.LabelSize = 13;
                    tip = Loc.GetF("programs_pip_done", i);
                }
                else if (record?.Missed == true)
                {
                    pip.Stroke = danger;
                    pip.Label = "✕";
                    pip.LabelBrush = danger;
                    pip.NodeSize = 32;
                    tip = Loc.GetF("programs_pip_missed", i);
                }
                else
                {
                    pip.Fill = surface;
                    pip.Stroke = border;
                    pip.LabelBrush = muted;
                    pip.PipOpacity = 0.65;
                    tip = Loc.GetF("programs_pip_locked", i);
                }

                if (milestone) pip.NodeSize += 6;
                pip.Tip = day != null && !string.IsNullOrWhiteSpace(day.Title) ? $"{tip} · {day.Title}" : tip;
                pips.Add(pip);
            }

            var rail = Find<Grid>("ProgramDayRail");
            Find<ItemsControl>("ProgramDayStrip").ItemsSource = pips;
            var railColumns = ((Grid)rail.Parent!).ColumnDefinitions;
            railColumns[0].MaxWidth = program.LengthDays <= 14 ? 840 : double.PositiveInfinity;
            railColumns[1].Width = program.LengthDays <= 14 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

            var total = Math.Max(1, program.LengthDays);
            var done = Math.Max(0, Math.Clamp(enrollment.CurrentDay, 0, total) - 0.5);
            var fill = Find<Border>("RailProgressFill");
            var fillColumns = ((Grid)fill.Parent!).ColumnDefinitions;
            fillColumns[0].Width = new GridLength(done, GridUnitType.Star);
            fillColumns[1].Width = new GridLength(Math.Max(0.0001, total - done), GridUnitType.Star);
            fill.Background = RailFill(accent);
        }

        // ---- the day (WPF BuildProgramTodayPanel :941-1248) ----

        private void BuildTodayPanel(ProgramService svc, ProgramDefinition program, ProgramEnrollment enrollment,
                                     IBrush accent, bool stale)
        {
            var day = svc.Today;
            var record = svc.TodayRecord;
            var panel = Find<Border>("TodayPanel");
            panel.IsVisible = day != null && record != null;
            if (day == null || record == null) return;

            var muted = Theme("TextMutedBrush", Brushes.Gray);
            var light = Theme("TextLightBrush", Brushes.White);
            var glass = Theme("GlassBorderBrush", Brushes.Gray);

            // CHECKPOINT A: never call a snapshot "today". Set from code at Template priority (P09)
            // and rebuilt on every language change by RefreshPrograms.
            Find<TextBlock>("TxtTodayHeader").Text = stale
                ? Loc.GetF("programs_last_saved_header", enrollment.CurrentDayDate.ToShortDateString())
                : Loc.Get("programs_today_header");

            panel.BorderBrush = day.IsBoss ? accent : glass;
            panel.BorderThickness = new Thickness(day.IsBoss ? 2 : 1);
            Find<Border>("TodayBossBadge").IsVisible = day.IsBoss;
            Find<Border>("TodayReturnBadge").IsVisible = record.IsReturnDay;

            var hero = ModArt.FirstOf(ProgramArtPaths.DayHero(program, day));
            Find<Border>("RunHeroHost").IsVisible = hero != null;
            Find<Grid>("TodayHeroGrid").ColumnDefinitions[1].Width =
                hero != null ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
            if (hero != null)
            {
                var plate = Find<Rectangle>("RunHeroPlate");
                plate.OpacityMask = new ImageBrush(hero) { Stretch = Stretch.UniformToFill };
                plate.Fill = accent;
            }
            Find<Rectangle>("TodayHeroGlow").Fill = MainShellWindow.ProgramRadialGlowBrush(accent, 70, 0.78, 0.2, 0.9);

            Find<TextBlock>("TxtTodayTitle").Text = day.Title;
            Find<TextBlock>("TxtTodayBlurb").Text = day.Blurb;
            BuildTodayLayers(program, day, accent);

            Find<Border>("TodayRewardChip").IsVisible = !string.IsNullOrWhiteSpace(day.RewardDescription);
            Find<TextBlock>("TxtTodayReward").Text = day.RewardDescription ?? "";
            ToolTip.SetTip(Find<Border>("TodayRewardChip"), day.RewardDescription);
            Find<Border>("TodayCompleteBanner").IsVisible = record.DayCompleted;

            // Session slot, idle states only (WPF UpdateProgramSessionRow :1555-1568): nothing runs here.
            var minutes = record.IsReturnDay ? ProgramService.ReturnDayMinutes(day.SessionMinutes) : day.SessionMinutes;
            Find<TextBlock>("TxtTodaySessionMinutes").Text = Loc.GetF("programs_session_minutes", minutes);
            var glyph = Find<TextBlock>("TxtTodaySessionGlyph");
            glyph.Text = record.SessionCompleted ? "✓" : "○";
            glyph.Foreground = record.SessionCompleted ? accent : muted;

            var ambient = day.Ambient;
            var showAmbient = ambient != null &&
                              (!string.IsNullOrWhiteSpace(ambient.Description) || ambient.RequiredMinutes > 0);
            Find<Border>("TodayAmbientRow").IsVisible = showAmbient;
            if (showAmbient)
            {
                Find<TextBlock>("TxtTodayAmbient").Text = ambient!.Description;
                var progress = Find<TextBlock>("TxtTodayAmbientProgress");
                progress.IsVisible = ambient.RequiredMinutes > 0;
                progress.Text = ambient.RequiredMinutes > 0
                    ? Loc.GetF("programs_ambient_progress",
                        Math.Min(record.AmbientMinutes, ambient.RequiredMinutes), ambient.RequiredMinutes)
                    : "";
            }

            var items = new List<ProgramTaskItem>();
            int required = 0, completedRequired = 0, optional = 0, blocked = 0;
            var hasRitual = false;
            var doneInk = ContrastForeground(accent, light);
            foreach (var task in day.Tasks)
            {
                var complete = svc.IsTaskComplete(record, task);
                var isBlocked = svc.IsTaskBlocked(task);
                if (isBlocked) blocked++;
                else if (task.Optional) optional++;
                else { required++; if (complete) completedRequired++; }
                hasRitual |= task.Kind == ProgramTaskKind.Ritual;

                var howTo = complete ? null : TaskHowTo(task);
                var item = new ProgramTaskItem
                {
                    TaskId = task.Id,
                    Description = task.Description,
                    HowTo = howTo ?? "",
                    HowToVisible = !string.IsNullOrWhiteSpace(howTo),
                    StatusGlyph = complete ? "✓" : "○",
                    StatusBrush = complete ? accent : muted,
                    TextBrush = complete ? muted : light,
                    RowOpacity = isBlocked ? 0.5 : 1.0,
                    AccentBrush = accent,
                    CardBorderBrush = complete ? accent : glass,
                    DoneChipVisible = complete,
                    DoneChipForeground = doneInk,
                    // Read-only head: the ritual picker and the mantra door would record progress.
                    SubmitVisible = false,
                    OpenVisible = false,
                };

                var icon = ModArt.TryLoad(TaskIconPath(task));
                if (icon != null)
                {
                    item.Icon = icon;
                    item.IconVisible = true;
                    item.GlyphVisible = false;
                }

                if (task.Kind == ProgramTaskKind.AutoVerified && task.TargetValue > 1)
                {
                    record.TaskProgress.TryGetValue(task.Id, out var current);
                    var shown = complete ? task.TargetValue : Math.Min(current, task.TargetValue);
                    item.ProgressText = Loc.GetF("programs_task_progress", shown, task.TargetValue);
                    item.ProgressStar = new GridLength(Math.Max(0, shown), GridUnitType.Star);
                    item.RemainderStar = new GridLength(Math.Max(0.0001, task.TargetValue - shown), GridUnitType.Star);
                    item.BarVisible = true;
                }

                var badge = isBlocked ? "programs_task_locked"
                    : task.OutsideSession ? "programs_task_outside_session"
                    : task.Optional ? "programs_task_optional" : null;
                item.BadgeText = badge == null ? "" : Loc.Get(badge);
                item.BadgeVisible = badge != null;
                items.Add(item);
            }

            Find<ItemsControl>("TodayTaskList").ItemsSource = items;
            Find<TextBlock>("TxtTodayNoTasks").IsVisible = items.Count == 0;
            Find<TextBlock>("TxtRitualPrivacyNote").IsVisible = hasRitual;
            Find<Grid>("TaskArcGrid").ColumnDefinitions[0].MaxWidth = Math.Clamp(items.Count, 1, 3) * 370;

            var pill = Find<Border>("TodayTasksDonePill");
            pill.IsVisible = required > 0;
            if (required > 0)
            {
                var text = Loc.GetF("programs_tasks_done_count", completedRequired, required);
                if (optional > 0) text += $"  ·  {optional} {Loc.Get("programs_task_optional")}";
                if (blocked > 0) text += $"  ·  {blocked} {Loc.Get("btn_program_locked")}";
                Find<TextBlock>("TxtTodayTasksDone").Text = text;
            }
            ToolTip.SetTip(pill, required > 0 && blocked > 0 ? Loc.Get("programs_locked_hint") : null);

            BuildUpNext(program, enrollment, day, record, accent, stale);
        }

        // ---- WPF BuildProgramTodayLayers (:1250-1318) ----

        private static readonly (Func<SessionSettings, bool> IsOn, string LabelKey, string IconPath)[] LayerCatalog =
        {
            (s => s.FlashEnabled,            "programs_layer_flash",       "features/flash.png"),
            (s => s.SubliminalEnabled,       "programs_layer_subliminal",  "features/subliminal.png"),
            (s => s.AudioWhispersEnabled,    "programs_layer_whispers",    "features/audio_whispers.png"),
            (s => s.BouncingTextEnabled,     "programs_layer_bouncing",    "features/bouncing_text.png"),
            (s => s.BubblesEnabled,          "programs_layer_bubbles",     "features/Bubble_pop.png"),
            (s => s.PinkFilterEnabled,       "programs_layer_pink",        "features/Pink_filter.png"),
            (s => s.SpiralEnabled,           "programs_layer_spiral",      "features/spiral_overlay.png"),
            (s => s.MandatoryVideosEnabled,  "programs_layer_video",       "features/mandatory_videos.png"),
            (s => s.LockCardEnabled,         "programs_layer_lockcard",    "features/Phrase_Lock.png"),
            (s => s.BubbleCountEnabled,      "programs_layer_bubblecount", "features/Bubble_count.png"),
            (s => s.MindWipeEnabled,         "programs_layer_mindwipe",    "features/Mind_Wipers.png"),
            (s => s.CornerGifEnabled,        "programs_layer_cornergif",   "features/corner_gif.png")
        };

        private void BuildTodayLayers(ProgramDefinition program, ProgramDay day, IBrush accent)
        {
            var template = program.GetTemplate(day.SessionTemplateId);
            var settings = DaySettings(program, day);
            var panel = Find<StackPanel>("TodayLayersPanel");
            if (template == null || settings == null)
            {
                panel.IsVisible = false;
                return;
            }

            var previous = day.DayIndex > 1 ? DaySettings(program, program.GetDay(day.DayIndex - 1)) : null;
            var muted = Theme("TextMutedBrush", Brushes.Gray);
            var light = Theme("TextLightBrush", Brushes.White);
            var glass = Theme("GlassBorderBrush", Brushes.Gray);
            var newTip = Loc.Get("programs_layer_new_tip");
            var newInk = ContrastForeground(accent, light);
            var chips = new List<ProgramLayerChip>();
            foreach (var (isOn, labelKey, iconPath) in LayerCatalog)
            {
                if (!isOn(settings)) continue;
                var label = Loc.Get(labelKey);
                var isNew = previous != null && !isOn(previous);
                var icon = ModArt.TryLoad(iconPath);
                chips.Add(new ProgramLayerChip
                {
                    Label = label,
                    AccentBrush = accent,
                    LabelBrush = isNew ? light : muted,
                    BorderBrush = isNew ? accent : glass,
                    NewForeground = newInk,
                    NewVisible = isNew,
                    Tip = isNew ? $"{label} - {newTip}" : label,
                    Icon = icon,
                    IconVisible = icon != null,
                });
            }

            var name = Find<TextBlock>("TxtTodayTemplateName");
            name.Text = template.Name;
            name.Foreground = accent;
            var blurb = Find<TextBlock>("TxtTodayTemplateBlurb");
            blurb.Text = template.Description;
            blurb.IsVisible = !string.IsNullOrWhiteSpace(template.Description);
            Find<ItemsControl>("TodayLayerList").ItemsSource = chips;
            panel.IsVisible = chips.Count > 0;
        }

        /// <summary>WPF ProgramDaySettings (:464): the template floor, cloned only when overrides apply.</summary>
        private static SessionSettings? DaySettings(ProgramDefinition program, ProgramDay? day)
        {
            if (day == null) return null;
            var template = program.GetTemplate(day.SessionTemplateId);
            if (template?.Floor == null) return null;
            if (day.Overrides is not { Count: > 0 }) return template.Floor;
            try
            {
                var copy = ProgramSessionBuilder.Clone(template.Floor);
                ProgramSessionBuilder.ApplyOverrides(copy, day.Overrides);
                return copy;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Program day {Day} overrides could not be previewed", day.DayIndex);
                return template.Floor;
            }
        }

        // ---- WPF BuildProgramUpNext (:1333-1427) ----

        private void BuildUpNext(ProgramDefinition program, ProgramEnrollment enrollment, ProgramDay day,
                                 ProgramDayRecord record, IBrush accent, bool stale)
        {
            var muted = Theme("TextMutedBrush", Brushes.Gray);
            var upcoming = program.AllDays.Where(d => d.DayIndex > day.DayIndex).Take(3).ToList();
            var items = new List<ProgramUpNextItem>(upcoming.Count);
            for (int i = 0; i < upcoming.Count; i++)
            {
                var next = upcoming[i];
                var parts = new List<string> { Loc.GetF("programs_session_minutes", next.SessionMinutes) };
                var first = next.Tasks.FirstOrDefault();
                if (first != null && !string.IsNullOrWhiteSpace(first.Description))
                {
                    parts.Add(first.Description);
                    if (next.Tasks.Count > 1) parts.Add(Loc.GetF("programs_next_more_tasks", next.Tasks.Count - 1));
                }
                items.Add(new ProgramUpNextItem
                {
                    DayLabel = Loc.GetF("programs_card_day", next.DayIndex),
                    Title = next.Title,
                    Meta = string.Join("  ·  ", parts),
                    DayBrush = next.IsBoss ? accent : muted,
                    Glyph = next.IsBoss ? "👑" : "",
                    GlyphTip = next.IsBoss ? Loc.Get("programs_boss_badge") : "",
                    GlyphVisible = next.IsBoss,
                    RowOpacity = 1.0 - (i * 0.18)
                });
            }
            Find<ItemsControl>("TodayUpNextList").ItemsSource = items;
            Find<TextBlock>("TxtTodayUpNextFinal").IsVisible = items.Count == 0;

            // The "closes at" deadline is about a live day; a stale snapshot's day has already
            // closed, so no deadline is shown for it (CHECKPOINT A).
            var boundary = DateTime.Today.AddHours(Math.Clamp(enrollment.DayBoundaryHour, 0, 23)).ToShortTimeString();
            var closes = Find<TextBlock>("TxtTodayCloses");
            var note = Find<TextBlock>("TxtTodayClosesNote");
            closes.IsVisible = !stale && !(record.DayCompleted && items.Count == 0);
            note.IsVisible = false;
            if (enrollment.State == ProgramEnrollmentState.Paused) closes.Text = Loc.Get("programs_closes_paused");
            else if (record.DayCompleted && items.Count > 0)
                closes.Text = Loc.GetF("programs_closes_done", upcoming[0].DayIndex, boundary);
            else if (!record.DayCompleted)
            {
                closes.Text = Loc.GetF("programs_closes_at", boundary);
                note.Text = Loc.Get("programs_closes_note");
                note.IsVisible = !stale;
            }

            var streak = 0;
            for (int i = record.DayCompleted ? enrollment.CurrentDay : enrollment.CurrentDay - 1; i >= 1; i--)
            {
                if (enrollment.GetRecord(i)?.DayCompleted != true) break;
                streak++;
            }
            Find<TextBlock>("TxtTodayStreak").Text = streak switch
            {
                0 => Loc.Get("programs_streak_none"),
                1 => Loc.Get("programs_streak_one"),
                _ => Loc.GetF("programs_streak_many", streak)
            };
        }

        // ---- small helpers (WPF :201-435) ----

        private IBrush Theme(string key, IBrush fallback) =>
            this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;

        /// <summary>WPF ProgramContrastForeground (:242): dark ink only on a pale (luminance &gt; 0.6) accent.</summary>
        private IBrush ContrastForeground(IBrush background, IBrush light)
        {
            if (background is not ISolidColorBrush solid) return light;
            static double Lin(byte c) { var v = c / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            var c = solid.Color;
            var luminance = 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
            return luminance <= 0.6 ? light : Theme("DarkerBgBrush", Brushes.Black);
        }

        /// <summary>WPF ProgramRailFillBrush (:285): accent at 40% fading to full, left to right.</summary>
        private static IBrush RailFill(IBrush accent)
        {
            if (accent is not ISolidColorBrush solid) return accent;
            var c = solid.Color;
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(c.A * 0.40), c.R, c.G, c.B), 0),
                    new GradientStop(c, 1),
                },
            };
        }

        /// <summary>WPF ProgramTaskIconPath (:370).</summary>
        private static string? TaskIconPath(ProgramTask task) =>
            task.Kind != ProgramTaskKind.AutoVerified ? null : task.Verifier switch
            {
                QuestCategory.Bubbles => "features/Bubble_pop.png",
                QuestCategory.LockCard => "features/Phrase_Lock.png",
                QuestCategory.Video => "features/mandatory_videos.png",
                QuestCategory.BubbleCount => "features/Bubble_count.png",
                QuestCategory.PinkFilter => "features/Pink_filter.png",
                QuestCategory.Flash => "features/flash.png",
                QuestCategory.Spiral => "features/spiral_overlay.png",
                _ => null
            };

        /// <summary>WPF ProgramTaskHowTo (:395).</summary>
        private static string? TaskHowTo(ProgramTask task)
        {
            if (task.Kind == ProgramTaskKind.Ritual) return Loc.Get("programs_howto_ritual");
            if (task.Kind != ProgramTaskKind.AutoVerified || task.Verifier == null) return null;
            var key = task.Verifier switch
            {
                QuestCategory.Flash => "programs_howto_flash",
                QuestCategory.Bubbles => "programs_howto_bubbles",
                QuestCategory.BubbleCount => "programs_howto_bubblecount",
                QuestCategory.LockCard => "programs_howto_lockcard",
                QuestCategory.Video => "programs_howto_video",
                QuestCategory.PinkFilter => "programs_howto_pinkfilter",
                QuestCategory.Spiral => "programs_howto_spiral",
                QuestCategory.Mantra => "programs_howto_mantra",
                QuestCategory.Autonomy => "programs_howto_autonomy",
                QuestCategory.KeywordTrigger => "programs_howto_keyword",
                QuestCategory.Lockdown => "programs_howto_lockdown",
                QuestCategory.Remote => "programs_howto_remote",
                QuestCategory.BlinkTrainer => "programs_howto_blink",
                _ => null
            };
            if (key == null) return null;
            var text = Loc.GetF(key, Math.Max(1, task.TargetValue));
            return task.OutsideSession ? text + " " + Loc.Get("programs_howto_outside_suffix") : text;
        }
    }
}
