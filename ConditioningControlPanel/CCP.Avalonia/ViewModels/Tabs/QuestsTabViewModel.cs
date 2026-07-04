using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Roadmap;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Quests and MainWindow.QuestsTab partials.
/// Drives the Transformation Roadmap track selection and the Daily/Weekly quest panel.
/// </summary>
public partial class QuestsTabViewModel : TabItemViewModel, IDisposable
{
    private readonly IRoadmapService? _roadmap;
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<QuestsTabViewModel>? _logger;
    private readonly IQuestService? _questService;
    private readonly IQuestDefinitionService? _questDefinitions;
    private readonly ISkillTreeService? _skillTreeService;
    private readonly IModService? _modService;
    private QuestProgress _questProgress;
    private DispatcherTimer? _fixStreakStatusTimer;

    public QuestsTabViewModel() : base("quests", "Quests", "📜")
    {
        _roadmapTracks = new ObservableCollection<RoadmapTrackViewModel>();
        _roadmapNodes = new ObservableCollection<RoadmapNodeViewModel>();
        _calendarDays = new ObservableCollection<StreakDayViewModel>();
        _dailySegmentsCompleted = new ObservableCollection<bool>();
        _questProgress = new QuestProgress();
        InitializeTracks();
        RefreshRoadmap();
        RefreshQuestUI();
        PopulateDesignTimeData();
    }

    public QuestsTabViewModel(
        IRoadmapService roadmap,
        ISettingsService settingsService,
        IDialogService dialogService,
        ILogger<QuestsTabViewModel> logger,
        IQuestService questService,
        ISkillTreeService skillTreeService,
        IModService modService,
        IQuestDefinitionService? questDefinitions = null) : base("quests", "Quests", "📜")
    {
        _roadmap = roadmap;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _questService = questService;
        _questDefinitions = questDefinitions;
        _skillTreeService = skillTreeService;
        _roadmapTracks = new ObservableCollection<RoadmapTrackViewModel>();
        _roadmapNodes = new ObservableCollection<RoadmapNodeViewModel>();
        _calendarDays = new ObservableCollection<StreakDayViewModel>();
        _dailySegmentsCompleted = new ObservableCollection<bool>();
        _questProgress = questService.Progress;
        questService.QuestsChanged += OnQuestsChanged;
        _roadmap.StepCompleted += OnRoadmapStepCompleted;
        _roadmap.TrackUnlocked += OnRoadmapTrackUnlocked;
        InitializeTracks();
        RefreshRoadmap();
        RefreshQuestUI();

        if (_questDefinitions != null)
        {
            SeasonTitle = _questDefinitions.SeasonTitle;
            _questDefinitions.QuestDefinitionsUpdated += OnQuestDefinitionsUpdated;
            _ = Task.Run(async () => await _questDefinitions.InitializeAsync());
        }

        _modService = modService;
        modService.ActiveModChanged += OnActiveModChanged;
    }

    private void OnQuestsChanged(object? sender, EventArgs e)
    {
        RefreshQuestUI();
    }

    private void OnQuestDefinitionsUpdated()
    {
        if (_questDefinitions != null)
            SeasonTitle = _questDefinitions.SeasonTitle;
        RefreshQuestUI();
    }

    private void OnActiveModChanged(object? sender, ModPackage e)
    {
        RefreshQuestUI();
        OnPropertyChanged(nameof(DailyImageUri));
        OnPropertyChanged(nameof(WeeklyImageUri));
    }

    /// <summary>
    /// Detach all singleton-service event subscriptions so this view-model isn't leaked
    /// (the services outlive the tab). Called when the tab/window is torn down.
    /// </summary>
    public void Dispose()
    {
        if (_questService != null)
            _questService.QuestsChanged -= OnQuestsChanged;
        if (_roadmap != null)
        {
            _roadmap.StepCompleted -= OnRoadmapStepCompleted;
            _roadmap.TrackUnlocked -= OnRoadmapTrackUnlocked;
        }
        if (_questDefinitions != null)
            _questDefinitions.QuestDefinitionsUpdated -= OnQuestDefinitionsUpdated;
        if (_modService != null)
            _modService.ActiveModChanged -= OnActiveModChanged;

        _fixStreakStatusTimer?.Stop();
        _fixStreakStatusTimer = null;
    }

    #region Tab Switching

    [ObservableProperty]
    private bool _isRoadmapVisible;

    [ObservableProperty]
    private bool _dailyWeeklyPanelVisible = true;

    [ObservableProperty]
    private bool _roadmapPanelVisible;

    partial void OnIsRoadmapVisibleChanged(bool value)
    {
        DailyWeeklyPanelVisible = !value;
        RoadmapPanelVisible = value;
    }

    [RelayCommand]
    private void ShowDailyWeekly()
    {
        IsRoadmapVisible = false;
    }

    [RelayCommand]
    private void ShowRoadmap()
    {
        IsRoadmapVisible = true;
        RefreshRoadmap();
    }

    #endregion

    #region Roadmap

    [ObservableProperty]
    private ObservableCollection<RoadmapTrackViewModel> _roadmapTracks;

    [ObservableProperty]
    private ObservableCollection<RoadmapNodeViewModel> _roadmapNodes;

    [ObservableProperty]
    private RoadmapTrack _currentTrack = RoadmapTrack.EmptyDoll;

    [ObservableProperty]
    private string _trackName = "";

    [ObservableProperty]
    private string _trackSubtitle = "";

    [ObservableProperty]
    private string _trackProgressText = "";

    [ObservableProperty]
    private bool _isTrackLocked;

    [ObservableProperty]
    private string _lockReason = "";

    [ObservableProperty]
    private bool _showBadgeIndicator;

    [ObservableProperty]
    private string _totalStepsText = "";

    [ObservableProperty]
    private string _totalPhotosText = "";

    [ObservableProperty]
    private string _journeyDaysText = "--";

    partial void OnCurrentTrackChanged(RoadmapTrack value)
    {
        RefreshRoadmap();
    }

    private void InitializeTracks()
    {
        RoadmapTracks.Clear();
        foreach (RoadmapTrack track in Enum.GetValues(typeof(RoadmapTrack)))
        {
            var def = RoadmapTrackDefinition.GetByTrack(track);
            RoadmapTracks.Add(new RoadmapTrackViewModel(track, def?.Name ?? track.ToString(), def?.AccentColor ?? "#FF69B4"));
        }
    }

    [RelayCommand]
    private void SelectTrack(RoadmapTrack track)
    {
        CurrentTrack = track;
    }

    [RelayCommand]
    private async Task SelectNodeAsync(RoadmapNodeViewModel? node)
    {
        if (node == null || _roadmap == null) return;

        var stepDef = RoadmapStepDefinition.GetById(node.StepId);
        if (stepDef == null) return;

        var progress = _roadmap.GetStepProgress(node.StepId);
        var owner = GetMainWindow();

        // Completed step -> themed diary dialog (full photo, stats, editable note).
        // WPF parity: MainWindow.Roadmap.cs RoadmapNode_Click completed branch (replaces the
        // former msg_roadmap_diary_not_yet_ported placeholder).
        if (progress?.IsCompleted == true)
        {
            if (owner is not null)
            {
                var diary = new ConditioningControlPanel.Avalonia.Dialogs.RoadmapDiaryDialog(
                    node.StepId, stepDef, progress);
                await diary.ShowDialog(owner);
            }
            return;
        }

        // Locked step -> inform the user (unchanged; reuses existing loc keys).
        if (!_roadmap.IsStepActive(node.StepId))
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_step_locked"),
                Loc.Get("msg_complete_the_previous_steps_first")) ?? Task.CompletedTask);
            return;
        }

        // Active step -> themed start dialog. The ported RoadmapStartDialog merges WPF's confirm
        // dialog and the file picker into one step, returning the chosen PhotoPath directly.
        // WPF parity: MainWindow.Roadmap.cs RoadmapNode_Click active branch. Timing nuance: the
        // combined dialog means StartStep now runs after the photo is picked (WPF called StartStep
        // between its confirm dialog and a separate file picker).
        if (owner is null) return;

        var startDialog = new ConditioningControlPanel.Avalonia.Dialogs.RoadmapStartDialog(stepDef);
        var started = await startDialog.ShowDialog<bool>(owner);
        if (!started || !startDialog.Confirmed || string.IsNullOrEmpty(startDialog.PhotoPath)) return;

        _roadmap.StartStep(node.StepId);
        await SubmitPhotoAsync(node.StepId, startDialog.PhotoPath);
    }

    private async Task SubmitPhotoAsync(string stepId, string photoPath)
    {
        if (_roadmap == null) return;

        var stepDef = RoadmapStepDefinition.GetById(stepId);
        if (stepDef == null) return;

        var owner = GetMainWindow();
        if (owner is null) return;

        // Themed photo-submit confirmation. WPF parity: MainWindow.Roadmap.cs ShowPhotoConfirmation
        // -> RoadmapConfirmDialog (shows the step title + photo requirement).
        var confirmDialog = new ConditioningControlPanel.Avalonia.Dialogs.RoadmapConfirmDialog(
            stepDef.Title, stepDef.PhotoRequirement);
        var confirmed = await confirmDialog.ShowDialog<bool>(owner);
        if (!confirmed || !confirmDialog.Confirmed) return;

        // L4-09: collect the optional note via the ported InputDialog (owner-resolved, modal).
        // Empty text or cancel leaves the note null, preserving the prior behavior. Mirrors the
        // WPF flow in MainWindow.Roadmap.cs (ShowPhotoConfirmation), reusing the same loc keys.
        string? note = null;
        var noteDialog = new ConditioningControlPanel.Avalonia.Dialogs.InputDialog(
            Loc.Get("title_add_note"),
            Loc.Get("msg_add_note_prompt"),
            "");
        var accepted = await noteDialog.ShowDialog<bool>(owner);
        if (accepted && !string.IsNullOrEmpty(noteDialog.ResultText))
            note = noteDialog.ResultText;

        _roadmap.SubmitPhoto(stepId, photoPath, note);
        RefreshRoadmap();
    }

    private static global::Avalonia.Controls.Window? GetMainWindow()
        => (global::Avalonia.Application.Current?.ApplicationLifetime
            as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private void RefreshRoadmap()
    {
        if (_roadmap == null)
        {
            // Designer/runtime without roadmap service keeps manually populated values.
            foreach (var track in RoadmapTracks)
                track.IsSelected = track.Track == CurrentTrack;
            return;
        }

        var trackDef = RoadmapTrackDefinition.GetByTrack(CurrentTrack);
        TrackName = trackDef?.Name ?? CurrentTrack.ToString();
        TrackSubtitle = trackDef?.Subtitle ?? "";

        var (completed, total) = _roadmap.GetTrackProgress(CurrentTrack);
        TrackProgressText = Loc.GetF("label_0_7_steps_completed", completed, total);

        IsTrackLocked = !_roadmap.IsTrackUnlocked(CurrentTrack);
        LockReason = IsTrackLocked
            ? Loc.Get("label_complete_the_previous_track_to_unlock")
            : "";

        ShowBadgeIndicator = CurrentTrack == RoadmapTrack.SluttyBlowdoll && _roadmap.Progress.HasCertifiedBlowdollBadge;

        foreach (var track in RoadmapTracks)
            track.IsSelected = track.Track == CurrentTrack;

        GenerateNodes();
        RefreshStats();
    }

    private void GenerateNodes()
    {
        RoadmapNodes.Clear();
        if (_roadmap == null) return;

        var steps = RoadmapStepDefinition.GetStepsForTrack(CurrentTrack);
        var trackDef = RoadmapTrackDefinition.GetByTrack(CurrentTrack);
        var accent = trackDef?.AccentColor ?? "#FF69B4";

        foreach (var step in steps)
        {
            var progress = _roadmap.GetStepProgress(step.Id);
            var isCompleted = _roadmap.IsStepCompleted(step.Id);
            var isActive = _roadmap.IsStepActive(step.Id);
            var isLocked = !isCompleted && !isActive;

            RoadmapNodes.Add(new RoadmapNodeViewModel
            {
                StepId = step.Id,
                StepNumber = step.StepType == RoadmapStepType.Boss ? "BOSS" : $"Step {step.StepNumber}",
                Title = step.Title,
                Requirement = step.PhotoRequirement,
                IsCompleted = isCompleted,
                IsActive = isActive,
                IsLocked = isLocked,
                AccentColor = accent,
                PhotoPath = progress?.PhotoPath,
                UserNote = progress?.UserNote
            });
        }
    }

    private void RefreshStats()
    {
        if (_roadmap == null) return;

        var progress = _roadmap.Progress;
        TotalStepsText = Loc.GetF("label_0_7_steps_completed", progress.TotalStepsCompleted, 21);
        TotalPhotosText = progress.TotalPhotosSubmitted.ToString();

        if (progress.JourneyStartedAt.HasValue)
        {
            var days = (int)(DateTime.Now - progress.JourneyStartedAt.Value).TotalDays;
            JourneyDaysText = days.ToString();
        }
        else
        {
            JourneyDaysText = "--";
        }
    }

    private void OnRoadmapStepCompleted(object? sender, RoadmapStepCompletedEventArgs e)
    {
        _logger?.LogInformation("Roadmap step completed: {Step}", e.StepDefinition.Title);
        RefreshRoadmap();

        // WPF parity (MainWindow.Roadmap.cs OnRoadmapStepCompleted): celebrate the completion with
        // the themed, self-closing achievement popup, shown non-modally in the bottom-right corner.
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var popup = new ConditioningControlPanel.Avalonia.Dialogs.RoadmapStepPopup(
                    e.StepDefinition, e.StepProgress);
                popup.Show();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to show roadmap step completion popup");
            }
        });
    }

    private void OnRoadmapTrackUnlocked(object? sender, RoadmapTrack track)
    {
        _logger?.LogInformation("Roadmap track unlocked: {Track}", track);
        RefreshRoadmap();
    }

    #endregion

    #region Daily / Weekly Quests

    [ObservableProperty]
    private string _seasonTitle = "";

    [ObservableProperty]
    private string _dailyCounterText = "0/3";

    [ObservableProperty]
    private bool _allDailyDone;

    [ObservableProperty]
    private string _dailyQuestName = "";

    [ObservableProperty]
    private string _dailyQuestDescription = "";

    [ObservableProperty]
    private string _dailyQuestIcon = "";

    [ObservableProperty]
    private string _dailyProgressText = "0 / 0";

    [ObservableProperty]
    private double _dailyProgressFraction;

    [ObservableProperty]
    private string _dailyImageUri = "";

    [ObservableProperty]
    private string _dailyXpText = "";

    [ObservableProperty]
    private string _dailyStreakBonusText = "";

    [ObservableProperty]
    private string _dailyRerollBonusText = "";

    [ObservableProperty]
    private bool _dailyCompletedVisible;

    [ObservableProperty]
    private string _dailyRerollText = Loc.Get("btn_reroll");

    [ObservableProperty]
    private bool _dailyRerollEnabled;

    [ObservableProperty]
    private ObservableCollection<bool> _dailySegmentsCompleted;

    [ObservableProperty]
    private bool _dailyCardVisible = true;

    [ObservableProperty]
    private bool _dailyAllCompletedMessageVisible;

    [ObservableProperty]
    private string _weeklyQuestName = "";

    [ObservableProperty]
    private string _weeklyQuestDescription = "";

    [ObservableProperty]
    private string _weeklyQuestIcon = "";

    [ObservableProperty]
    private string _weeklyProgressText = "0 / 0";

    [ObservableProperty]
    private double _weeklyProgressFraction;

    [ObservableProperty]
    private string _weeklyImageUri = "";

    [ObservableProperty]
    private string _weeklyXpText = "";

    [ObservableProperty]
    private string _weeklyStreakBonusText = "";

    [ObservableProperty]
    private string _weeklyRerollBonusText = "";

    [ObservableProperty]
    private bool _weeklyCompletedVisible;

    [ObservableProperty]
    private string _weeklyRerollText = Loc.Get("btn_reroll");

    [ObservableProperty]
    private bool _weeklyRerollEnabled;

    [ObservableProperty]
    private string _totalDailyCompletedText = "0";

    [ObservableProperty]
    private string _totalWeeklyCompletedText = "0";

    [ObservableProperty]
    private string _totalQuestXpText = "0";

    [ObservableProperty]
    private string _questStatsText = "0 completed today";

    [ObservableProperty]
    private string _streakText = "";

    [ObservableProperty]
    private bool _fixStreakVisible;

    [ObservableProperty]
    private string _fixStreakStatusText = "";

    [ObservableProperty]
    private bool _fixStreakStatusVisible;

    [ObservableProperty]
    private bool _isStreakFixMode;

    [ObservableProperty]
    private bool _questCompleteBannerVisible;

    [ObservableProperty]
    private bool _isLoginOverlayVisible;

    [ObservableProperty]
    private ObservableCollection<StreakDayViewModel> _calendarDays;

    [RelayCommand]
    private async Task RerollDailyAsync()
    {
        if (_questService == null) return;

        _logger?.LogInformation("Daily quest reroll requested");
        var success = _questService.RerollDaily();
        if (!success)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_no_rerolls"),
                Loc.Get("msg_no_daily_rerolls_remaining")) ?? Task.CompletedTask);
        }
        RefreshQuestUI();
    }

    [RelayCommand]
    private async Task RerollWeeklyAsync()
    {
        if (_questService == null) return;

        _logger?.LogInformation("Weekly quest reroll requested");
        var success = _questService.RerollWeekly();
        if (!success)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_no_rerolls"),
                Loc.Get("msg_no_weekly_rerolls_remaining")) ?? Task.CompletedTask);
        }
        RefreshQuestUI();
    }

    [RelayCommand]
    private async Task FixStreakAsync()
    {
        if (IsStreakFixMode)
        {
            ExitStreakFixMode();
            return;
        }

        var settings = _settingsService?.Current;
        if (settings == null) return;

        var hasSkill = _skillTreeService?.HasSkill("oopsie_insurance") ?? false;
        if (!hasSkill)
        {
            FixStreakStatusText = Loc.Get("label_oopsie_insurance_skill_required");
            FixStreakStatusVisible = true;
            return;
        }

        if (settings.SeasonalStreakRecoveryUsed)
        {
            FixStreakStatusText = Loc.Get("label_already_used_oopsie_insurance_this_season");
            FixStreakStatusVisible = true;
            return;
        }

        var progress = _questProgress;
        var today = DateTime.Today;
        var completedDates = new HashSet<DateTime>(
            progress.DailyQuestCompletionDates?.Select(d => d.Date) ?? Enumerable.Empty<DateTime>());
        bool hasMissedDays = Enumerable.Range(1, today.Day - 1)
            .Select(d => new DateTime(today.Year, today.Month, d))
            .Any(d => !completedDates.Contains(d.Date));

        if (!hasMissedDays)
        {
            FixStreakStatusText = Loc.Get("label_no_broken_streak_you_re_doing_great_sweetie");
            FixStreakStatusVisible = true;
            return;
        }

        if (settings.PlayerXP < 500)
        {
            FixStreakStatusText = Loc.Get("label_not_enough_xp_you_need_500_xp_to_fix_a_day");
            FixStreakStatusVisible = true;
            return;
        }

        IsStreakFixMode = true;
        FixStreakStatusText = Loc.Get("label_click_a_missed_day_to_fix_it_costs_500_xp_onc");
        FixStreakStatusVisible = true;
        RefreshCalendar();
    }

    [RelayCommand]
    private async Task FixStreakDayAsync(StreakDayViewModel? day)
    {
        if (day == null || !IsStreakFixMode) return;
        if (!day.IsMissed) return;

        var settings = _settingsService?.Current;
        if (settings == null) return;

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_oopsie_insurance"),
            Loc.GetF("msg_fix_streak_day_confirm", day.Date.ToString("MMMM d"))) ?? Task.FromResult(false));
        if (!confirm) return;

        _logger?.LogInformation("Oopsie Insurance used to fix {Date} for 500 XP (server-validated)", day.Date);

        // Persist the streak fix FIRST so a crash can't spend XP without recording the fix.
        _questService?.FixStreakDay(day.Date);

        settings.PlayerXP = Math.Max(0, settings.PlayerXP - 500);
        settings.SeasonalStreakRecoveryUsed = true;
        _settingsService?.Save();

        IsStreakFixMode = false;
        FixStreakStatusText = Loc.GetF("label_fixed_0_streak_updated", day.Date.ToString("MMMM d"));
        FixStreakStatusVisible = true;
        RefreshCalendar();

        // Auto-hide the status after 3s on the UI thread. A one-shot DispatcherTimer keeps the
        // ObservableProperty mutation on the dispatcher thread (Task.Run would mutate off-thread).
        _fixStreakStatusTimer?.Stop();
        _fixStreakStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _fixStreakStatusTimer.Tick += (_, _) =>
        {
            _fixStreakStatusTimer?.Stop();
            _fixStreakStatusTimer = null;
            if (!IsStreakFixMode)
            {
                FixStreakStatusVisible = false;
            }
        };
        _fixStreakStatusTimer.Start();
    }

    private void ExitStreakFixMode()
    {
        IsStreakFixMode = false;
        FixStreakStatusVisible = false;
        FixStreakStatusText = "";
        RefreshCalendar();
    }

    private void RefreshQuestUI()
    {
        var settings = _settingsService?.Current;
        var progress = _questProgress;
        if (progress == null) return;

        _questService?.EnsureGenerated();

        var dailyCompleted = progress.GetDailyQuestsCompletedToday();
        DailyCounterText = $"{dailyCompleted}/3";
        AllDailyDone = progress.AreAllDailyQuestsCompleted();

        var dailyDef = GetDailyDefinition();
        DailyQuestName = dailyDef?.LocalizedName ?? dailyDef?.Name ?? Loc.Get("label_daily_quest");
        DailyQuestDescription = dailyDef?.LocalizedDescription ?? dailyDef?.Description ?? "";
        DailyQuestIcon = dailyDef?.Icon ?? "⚡";

        var dailyCurrent = progress.DailyQuest?.CurrentProgress ?? 0;
        var dailyTarget = dailyDef?.TargetValue ?? 1;
        DailyProgressText = $"{dailyCurrent} / {dailyTarget}";
        DailyProgressFraction = dailyTarget > 0 ? (double)dailyCurrent / dailyTarget : 0.0;
        DailyImageUri = dailyDef?.EffectiveImagePath ?? "";

        var playerLevel = settings?.PlayerLevel ?? 1;
        var questStreak = settings?.DailyQuestStreak ?? 0;
        var streakMult = 1.0 + (questStreak * 0.03);
        var scaledDailyXP = _questService?.GetScaledDailyXp() ?? (int)Math.Round(150 * (1 + playerLevel * 0.04) * streakMult);
        DailyXpText = $"🎁 {scaledDailyXP} XP";
        DailyStreakBonusText = questStreak > 0 ? $"(+{questStreak * 3}%🔥)" : "";
        DailyRerollBonusText = "";
        DailyCompletedVisible = progress.DailyQuest?.IsCompleted == true;
        var dailyRerollsRemaining = _questService?.GetRemainingDailyRerolls() ?? progress.GetRemainingDailyRerolls(settings?.HasCachedPremiumAccess ?? false);
        DailyRerollEnabled = dailyRerollsRemaining > 0;
        DailyRerollText = DailyRerollEnabled
            ? Loc.GetF("label_reroll_left_fmt", dailyRerollsRemaining)
            : Loc.Get("label_no_rerolls_left");

        var weeklyDef = GetWeeklyDefinition();
        WeeklyQuestName = weeklyDef?.LocalizedName ?? weeklyDef?.Name ?? Loc.Get("label_weekly_quest");
        WeeklyQuestDescription = weeklyDef?.LocalizedDescription ?? weeklyDef?.Description ?? "";
        WeeklyQuestIcon = weeklyDef?.Icon ?? "🔥";

        var weeklyCurrent = progress.WeeklyQuest?.CurrentProgress ?? 0;
        var weeklyTarget = weeklyDef?.TargetValue ?? 1;
        WeeklyProgressText = $"{weeklyCurrent} / {weeklyTarget}";
        WeeklyProgressFraction = weeklyTarget > 0 ? (double)weeklyCurrent / weeklyTarget : 0.0;
        WeeklyImageUri = weeklyDef?.EffectiveImagePath ?? "";

        var scaledWeeklyXP = _questService?.GetScaledWeeklyXp() ?? (int)Math.Round(600 * (1 + playerLevel * 0.04) * streakMult);
        WeeklyXpText = $"🎁 {scaledWeeklyXP} XP";
        WeeklyStreakBonusText = questStreak > 0 ? $"(+{questStreak * 3}%🔥)" : "";
        WeeklyRerollBonusText = "";
        WeeklyCompletedVisible = progress.WeeklyQuest?.IsCompleted == true;
        var weeklyRerollsRemaining = _questService?.GetRemainingWeeklyRerolls() ?? progress.GetRemainingWeeklyRerolls(settings?.HasCachedPremiumAccess ?? false);
        WeeklyRerollEnabled = weeklyRerollsRemaining > 0;
        WeeklyRerollText = WeeklyRerollEnabled
            ? Loc.GetF("label_reroll_left_fmt", weeklyRerollsRemaining)
            : Loc.Get("label_no_rerolls_left");

        TotalDailyCompletedText = progress.TotalDailyQuestsCompleted.ToString();
        TotalWeeklyCompletedText = progress.TotalWeeklyQuestsCompleted.ToString();
        TotalQuestXpText = progress.TotalXPFromQuests.ToString();

        int completedToday = dailyCompleted + (progress.WeeklyQuest?.IsCompleted == true ? 1 : 0);
        QuestStatsText = Loc.GetF("label_completed_today_fmt", completedToday);

        StreakText = questStreak > 0 ? $"🔥 {questStreak} day streak (+{questStreak * 3}% XP)" : "";

        FixStreakVisible = _skillTreeService?.HasSkill("oopsie_insurance") ?? false;

        DailySegmentsCompleted.Clear();
        for (int i = 0; i < 3; i++)
            DailySegmentsCompleted.Add(i < dailyCompleted);

        DailyCardVisible = !AllDailyDone;
        DailyAllCompletedMessageVisible = AllDailyDone;
        QuestCompleteBannerVisible = false;

        RefreshCalendar();
    }

    private QuestDefinition? GetDailyDefinition()
    {
        if (_questProgress?.DailyQuest == null) return null;

        var remote = _questDefinitions?.GetDailyQuests();
        if (remote != null)
        {
            var remoteQuest = remote.FirstOrDefault(q => q.Id == _questProgress.DailyQuest.DefinitionId);
            if (remoteQuest != null) return remoteQuest;
        }

        return QuestDefinition.DailyQuests.FirstOrDefault(q => q.Id == _questProgress.DailyQuest.DefinitionId);
    }

    private QuestDefinition? GetWeeklyDefinition()
    {
        if (_questProgress?.WeeklyQuest == null) return null;

        var remote = _questDefinitions?.GetWeeklyQuests();
        if (remote != null)
        {
            var remoteQuest = remote.FirstOrDefault(q => q.Id == _questProgress.WeeklyQuest.DefinitionId);
            if (remoteQuest != null) return remoteQuest;
        }

        return QuestDefinition.WeeklyQuests.FirstOrDefault(q => q.Id == _questProgress.WeeklyQuest.DefinitionId);
    }

    private void RefreshCalendar()
    {
        CalendarDays.Clear();

        var settings = _settingsService?.Current;
        var progress = _questProgress;
        var completedDates = new HashSet<DateTime>(
            progress.DailyQuestCompletionDates?.Select(d => d.Date) ?? Enumerable.Empty<DateTime>());
        var shieldedDates = new HashSet<DateTime>(
            settings?.StreakShieldUsedDates?.Select(d => d.Date) ?? Enumerable.Empty<DateTime>());

        var today = DateTime.Today;
        int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(today.Year, today.Month, day);
            bool isCompleted = completedDates.Contains(date);
            bool isFuture = date > today;
            bool isMissed = !isCompleted && !isFuture && date < today;
            bool isToday = date == today;
            bool isShielded = shieldedDates.Contains(date);

            CalendarDays.Add(new StreakDayViewModel
            {
                Date = date,
                DayNumber = day,
                IsCompleted = isCompleted,
                IsToday = isToday,
                IsFuture = isFuture,
                IsMissed = isMissed,
                IsShielded = isShielded,
                IsHighlighted = IsStreakFixMode && isMissed,
                BackgroundBrush = GetDayBrush(isCompleted, isToday, isFuture, isMissed, IsStreakFixMode && isMissed),
                ForegroundBrush = GetDayForegroundBrush(isCompleted, isToday, isHighlighted: IsStreakFixMode && isMissed)
            });
        }
    }

    #endregion

    private static IBrush GetDayBrush(bool isCompleted, bool isToday, bool isFuture, bool isMissed, bool isHighlighted)
    {
        if (isHighlighted)
            return new SolidColorBrush(Color.Parse("#FFFF69B4"));
        if (isCompleted)
            return new SolidColorBrush(Color.Parse("#FFFFD700"));
        if (isToday)
            return new SolidColorBrush(Color.Parse("#FF3D3D60"));
        if (isMissed)
            return new SolidColorBrush(Color.Parse("#FF4A2D2D"));
        if (isFuture)
            return new SolidColorBrush(Color.Parse("#FF1A1A2E"));
        return new SolidColorBrush(Color.Parse("#FF2A2A40"));
    }

    private static IBrush GetDayForegroundBrush(bool isCompleted, bool isToday, bool isHighlighted)
    {
        if (isCompleted || isHighlighted)
            return new SolidColorBrush(Colors.Black);
        return new SolidColorBrush(Colors.White);
    }

    #region Design-Time Data

    private void PopulateDesignTimeData()
    {
        if (_roadmap != null) return; // Only when running without services (designer).

        SeasonTitle = Loc.Get("label_airhead_april");
        DailyCounterText = "1/3";
        AllDailyDone = false;
        DailyQuestName = "Flash Flood";
        DailyQuestDescription = "View 50 flash images";
        DailyQuestIcon = "⚡";
        DailyProgressText = "12 / 50";
        DailyProgressFraction = 0.24;
        DailyImageUri = "pack://application:,,,/Resources/features/flash.png";
        DailyXpText = "🎁 150 XP";
        DailyStreakBonusText = "(+15%🔥)";
        DailyCompletedVisible = false;
        DailyRerollEnabled = true;
        DailyRerollText = Loc.GetF("label_reroll_left_fmt", 1);
        DailySegmentsCompleted.Clear();
        DailySegmentsCompleted.Add(true);
        DailySegmentsCompleted.Add(false);
        DailySegmentsCompleted.Add(false);
        DailyCardVisible = true;
        DailyAllCompletedMessageVisible = false;

        WeeklyQuestName = "Flash Monsoon";
        WeeklyQuestDescription = "View 500 flash images";
        WeeklyQuestIcon = "⚡";
        WeeklyProgressText = "120 / 500";
        WeeklyProgressFraction = 0.24;
        WeeklyImageUri = "pack://application:,,,/Resources/features/flash.png";
        WeeklyXpText = "🎁 600 XP";
        WeeklyStreakBonusText = "(+15%🔥)";
        WeeklyCompletedVisible = false;
        WeeklyRerollEnabled = true;
        WeeklyRerollText = Loc.GetF("label_reroll_left_fmt", 1);

        TotalDailyCompletedText = "12";
        TotalWeeklyCompletedText = "3";
        TotalQuestXpText = "4,200";
        QuestStatsText = Loc.GetF("label_completed_today_fmt", 2);
        StreakText = "🔥 5 day streak (+15% XP)";
        FixStreakVisible = true;

        var sampleToday = DateTime.Today;
        CalendarDays.Add(new StreakDayViewModel
        {
            Date = sampleToday.AddDays(-2),
            DayNumber = sampleToday.AddDays(-2).Day,
            IsCompleted = true,
            IsToday = false,
            IsFuture = false,
            IsMissed = false,
            BackgroundBrush = GetDayBrush(true, false, false, false, false),
            ForegroundBrush = GetDayForegroundBrush(true, false, false)
        });
        CalendarDays.Add(new StreakDayViewModel
        {
            Date = sampleToday.AddDays(-1),
            DayNumber = sampleToday.AddDays(-1).Day,
            IsCompleted = false,
            IsToday = false,
            IsFuture = false,
            IsMissed = true,
            BackgroundBrush = GetDayBrush(false, false, false, true, false),
            ForegroundBrush = GetDayForegroundBrush(false, false, false)
        });
        CalendarDays.Add(new StreakDayViewModel
        {
            Date = sampleToday,
            DayNumber = sampleToday.Day,
            IsCompleted = false,
            IsToday = true,
            IsFuture = false,
            IsMissed = false,
            BackgroundBrush = GetDayBrush(false, true, false, false, false),
            ForegroundBrush = GetDayForegroundBrush(false, true, false)
        });
        CalendarDays.Add(new StreakDayViewModel
        {
            Date = sampleToday.AddDays(1),
            DayNumber = sampleToday.AddDays(1).Day,
            IsCompleted = false,
            IsToday = false,
            IsFuture = true,
            IsMissed = false,
            BackgroundBrush = GetDayBrush(false, false, true, false, false),
            ForegroundBrush = GetDayForegroundBrush(false, false, false)
        });

        TrackName = Loc.Get("btn_the_empty_doll");
        TrackSubtitle = Loc.Get("label_gateway_phase");
        TrackProgressText = Loc.GetF("label_0_7_steps_completed", 2, 7);
        IsTrackLocked = false;
        LockReason = "";
        ShowBadgeIndicator = false;
        TotalStepsText = Loc.GetF("label_0_7_steps_completed", 5, 21);
        TotalPhotosText = "4";
        JourneyDaysText = "12";

        RoadmapNodes.Add(new RoadmapNodeViewModel
        {
            StepId = "empty_doll_1",
            StepNumber = "Step 1",
            Title = "Blank Slate",
            Requirement = "Take a photo of yourself ready to begin",
            IsCompleted = true,
            IsActive = false,
            IsLocked = false,
            AccentColor = "#FF69B4"
        });
        RoadmapNodes.Add(new RoadmapNodeViewModel
        {
            StepId = "empty_doll_2",
            StepNumber = "Step 2",
            Title = "First Trigger",
            Requirement = "Take a photo while listening to a trigger file",
            IsCompleted = false,
            IsActive = true,
            IsLocked = false,
            AccentColor = "#FF69B4"
        });
        RoadmapNodes.Add(new RoadmapNodeViewModel
        {
            StepId = "empty_doll_3",
            StepNumber = "Step 3",
            Title = "Deep Drop",
            Requirement = "Take a photo in your uniform",
            IsCompleted = false,
            IsActive = false,
            IsLocked = true,
            AccentColor = "#FF69B4"
        });

        QuestCompleteBannerVisible = true;
        IsLoginOverlayVisible = false;
    }

    #endregion
}

public partial class RoadmapTrackViewModel : ObservableObject
{
    [ObservableProperty]
    private RoadmapTrack _track;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _accentColor;

    [ObservableProperty]
    private bool _isSelected;

    public RoadmapTrackViewModel(RoadmapTrack track, string name, string accentColor)
    {
        _track = track;
        _name = name;
        _accentColor = accentColor;
    }
}

public partial class RoadmapNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _stepId = "";

    [ObservableProperty]
    private string _stepNumber = "";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _requirement = "";

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private string _accentColor = "#FF69B4";

    [ObservableProperty]
    private string? _photoPath;

    [ObservableProperty]
    private string? _userNote;

    public string StatusText => IsCompleted ? Loc.Get("btn_view") : IsLocked ? Loc.Get("label_locked") : Loc.Get("btn_start");
}

/// <summary>
/// View-model for a single day in the streak calendar.
/// </summary>
public partial class StreakDayViewModel : ObservableObject
{
    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
    private int _dayNumber;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isToday;

    [ObservableProperty]
    private bool _isFuture;

    [ObservableProperty]
    private bool _isMissed;

    [ObservableProperty]
    private bool _isShielded;

    [ObservableProperty]
    private bool _isHighlighted;

    [ObservableProperty]
    private IBrush _backgroundBrush = Brushes.Transparent;

    [ObservableProperty]
    private IBrush _foregroundBrush = Brushes.White;

    public string DayLabel => $"{Date:ddd} {DayNumber}";
}
