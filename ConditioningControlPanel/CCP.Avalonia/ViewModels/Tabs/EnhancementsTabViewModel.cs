using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Enhancements partial.
/// Skill tree state, tier-grouped layout, purchase flow, and active bonuses.
/// Wired to the cross-platform <see cref="ISkillTreeService"/>.
/// </summary>
public partial class EnhancementsTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<EnhancementsTabViewModel>? _logger;
    private readonly IModService? _modService;
    private readonly ISkillTreeService? _skillTreeService;
    private readonly IAchievementService? _achievementService;

    public EnhancementsTabViewModel() : base("enhancements", "Enhancements", "✨")
    {
        _skillTiers = new ObservableCollection<SkillTierGroupViewModel>();
        _skills = new ObservableCollection<SkillNodeViewModel>();
        _connections = new ObservableCollection<SkillConnectionViewModel>();
        _activeBonuses = new ObservableCollection<ActiveBonusViewModel>();
        _stats = new ObservableCollection<StatRowViewModel>();
        _ditzyStats = new ObservableCollection<StatRowViewModel>();
        InitializeDesignData();
    }

    public EnhancementsTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ILogger<EnhancementsTabViewModel> logger,
        IModService modService,
        ISkillTreeService skillTreeService,
        IAchievementService achievementService) : base("enhancements", "Enhancements", "✨")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _modService = modService;
        _skillTreeService = skillTreeService;
        _achievementService = achievementService;
        _skillTiers = new ObservableCollection<SkillTierGroupViewModel>();
        _skills = new ObservableCollection<SkillNodeViewModel>();
        _connections = new ObservableCollection<SkillConnectionViewModel>();
        _activeBonuses = new ObservableCollection<ActiveBonusViewModel>();
        _stats = new ObservableCollection<StatRowViewModel>();
        _ditzyStats = new ObservableCollection<StatRowViewModel>();
        RefreshUi();

        if (_modService != null)
            _modService.ActiveModChanged += (_, _) => RefreshUi();
    }

    [ObservableProperty]
    private int _skillPoints;

    [ObservableProperty]
    private int _totalPointsSpent;

    [ObservableProperty]
    private string _xpMultiplierText = "1.00x";

    [ObservableProperty]
    private string _conditioningTimeText = "0h 0m";

    [ObservableProperty]
    private bool _pinkRushActive;

    [ObservableProperty]
    private bool _statsExpanded;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isLoginOverlayVisible;

    [ObservableProperty]
    private string _sparklePointsText = "0";

    [ObservableProperty]
    private string _statsToggleText = "";

    [ObservableProperty]
    private ObservableCollection<SkillTierGroupViewModel> _skillTiers;

    [ObservableProperty]
    private ObservableCollection<SkillNodeViewModel> _skills;

    [ObservableProperty]
    private ObservableCollection<SkillConnectionViewModel> _connections;

    [ObservableProperty]
    private ObservableCollection<ActiveBonusViewModel> _activeBonuses;

    [ObservableProperty]
    private ObservableCollection<StatRowViewModel> _stats;

    [ObservableProperty]
    private ObservableCollection<StatRowViewModel> _ditzyStats;

    public string HeaderTitle => Loc.Get("label_enhancement_tree_title");
    public string HeaderSubtitle => Loc.Get("label_enhancement_tree_subtitle");
    public string HeaderWarning => Loc.Get("label_enhancement_tree_warning");

    [RelayCommand]
    private void ToggleStatsExpanded() => StatsExpanded = !StatsExpanded;

    partial void OnStatsExpandedChanged(bool value)
    {
        StatsToggleText = value
            ? Loc.Get("label_stats_collapse")
            : Loc.Get("label_stats_expand");
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _logger?.LogInformation("Refreshing Enhancements UI");
        RefreshUi();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task PurchaseSkillAsync(SkillNodeViewModel? node)
    {
        if (node == null) return;

        if (_dialogService == null)
        {
            return;
        }

        if (_skillTreeService == null)
        {
            await _dialogService.ShowMessageAsync(
                Loc.Get("title_not_implemented"),
                Loc.Get("msg_feature_not_implemented"));
            return;
        }

        if (!node.CanPurchase)
        {
            await _dialogService.ShowMessageAsync(
                Loc.Get("dialog_purchase_failed"),
                Loc.Get("msg_skill_not_available"),
                DialogSeverity.Warning);
            return;
        }

        var confirm = await _dialogService.ShowConfirmationAsync(
            Loc.Get("dialog_purchase_enhancement"),
            Loc.GetF("msg_purchase_skill", node.Name, node.Cost, Loc.Get("label_sparkle_points").ToLower(), node.FlavorText, node.Description));
        if (!confirm) return;

        try
        {
            _logger?.LogInformation("Purchase skill requested: {SkillId}", node.SkillId);
            var (success, error) = await _skillTreeService.PurchaseSkillAsync(node.SkillId);
            if (success)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_success"),
                    Loc.GetF("msg_skill_purchased_fmt", node.Name));
            }
            else
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("dialog_purchase_failed"),
                    error ?? Loc.Get("msg_purchase_failed"),
                    DialogSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Purchase skill failed");
            await _dialogService.ShowMessageAsync(
                Loc.Get("dialog_purchase_failed"),
                ex.Message,
                DialogSeverity.Warning);
        }
        finally
        {
            RefreshUi();
        }
    }

    [RelayCommand]
    private async Task OpenSkillDetailsAsync(SkillNodeViewModel? node)
    {
        if (node == null) return;

        if (_dialogService == null)
        {
            return;
        }

        if (_skillTreeService == null)
        {
            await _dialogService.ShowMessageAsync(
                Loc.Get("title_not_implemented"),
                Loc.Get("msg_feature_not_implemented"));
            return;
        }

        await _dialogService.ShowMessageAsync(
            node.Name,
            $"{node.FlavorText}\n\n{node.Description}");
    }

    private void RefreshUi()
    {
        try
        {
            var settings = _settingsService?.Current;
            if (settings == null || _skillTreeService == null)
            {
                InitializeDesignData();
                return;
            }

            IsLoggedIn = !string.IsNullOrEmpty(settings.UnifiedId);
            IsLoginOverlayVisible = !IsLoggedIn;

            SkillPoints = settings.SkillPoints;
            SparklePointsText = Loc.GetF("label_sparkle_points_fmt", SkillPoints);
            PinkRushActive = settings.PinkRushActive;
            TotalPointsSpent = _skillTreeService.TotalPointsSpent;

            XpMultiplierText = $"{_skillTreeService.GetTotalXpMultiplier():F2}x";
            ConditioningTimeText = FormatConditioningTime(settings.TotalConditioningMinutes);

            RefreshSkills();
            RefreshActiveBonuses();
            RefreshStats();
            RefreshDitzyStats(settings);

            StatsToggleText = StatsExpanded
                ? Loc.Get("label_stats_collapse")
                : Loc.Get("label_stats_expand");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "RefreshEnhancementsUI failed");
        }
    }

    private void RefreshSkills()
    {
        SkillTiers.Clear();
        Skills.Clear();
        Connections.Clear();
        var settings = _settingsService?.Current;

        var nodesById = new Dictionary<string, SkillNodeViewModel>();

        foreach (var skill in SkillDefinition.All)
        {
            // Mirror the WPF head: secret skills are never drawn in the tree
            // (MainWindow.Enhancements.cs draws `.Where(s => !s.IsSecret)`).
            if (skill.IsSecret)
                continue;

            var tierGroup = SkillTiers.FirstOrDefault(g => g.Tier == skill.Tier);
            if (tierGroup == null)
            {
                tierGroup = new SkillTierGroupViewModel
                {
                    Tier = skill.Tier,
                    TierName = GetTierName(skill.Tier)
                };
                SkillTiers.Add(tierGroup);
            }

            var isUnlocked = _skillTreeService?.HasSkill(skill.Id) == true;
            var canPurchase = CanPurchaseSkill(skill);

            var node = new SkillNodeViewModel
            {
                SkillId = skill.Id,
                Name = _modService?.MakeModAware(skill.Name) ?? skill.LocalizedName,
                Cost = skill.Cost,
                Tier = skill.Tier,
                PrerequisiteId = skill.PrerequisiteId,
                PrerequisiteName = string.IsNullOrEmpty(skill.PrerequisiteId)
                    ? null
                    : SkillDefinition.All.FirstOrDefault(s => s.Id == skill.PrerequisiteId)?.LocalizedName,
                IsUnlocked = isUnlocked,
                CanPurchase = canPurchase,
                IsLocked = !isUnlocked && !canPurchase,
                Description = skill.LocalizedDescription,
                FlavorText = skill.LocalizedFlavorText,
                IconUri = $"pack://application:,,,/Resources/skills/{skill.Id}.png"
            };

            tierGroup.Skills.Add(node);
            nodesById[skill.Id] = node;
        }

        // Keep tiers in ascending order.
        var sortedTiers = SkillTiers.OrderBy(g => g.Tier).ToList();
        SkillTiers.Clear();
        foreach (var group in sortedTiers)
            SkillTiers.Add(group);

        // Assign canvas positions based on tier row layout.
        AssignSkillPositions();

        // Flatten skills for the canvas ItemsControl.
        foreach (var group in sortedTiers)
        {
            foreach (var skill in group.Skills)
                Skills.Add(skill);
        }

        // Build connection lines from each skill to its prerequisite.
        foreach (var node in Skills)
        {
            if (!string.IsNullOrEmpty(node.PrerequisiteId) && nodesById.TryGetValue(node.PrerequisiteId, out var source))
            {
                Connections.Add(new SkillConnectionViewModel
                {
                    // Horizontal tree: prerequisite's right-center -> skill's left-center
                    // (cards are 160x200). WPF lays skills out in 3 left-to-right paths.
                    X1 = source.PositionX + 160,
                    Y1 = source.PositionY + 100,
                    X2 = node.PositionX,
                    Y2 = node.PositionY + 100,
                    IsUnlocked = source.IsUnlocked,
                    IsAvailable = node.CanPurchase || node.IsUnlocked
                });
            }
        }
    }

    // Skill-tree layout ported from the WPF head (MainWindow.Enhancements.cs:91-137): three linear
    // horizontal paths (top/mid/bottom rows) + a root node + the tier-6 analytics chain, keyed by
    // skill id. WPF's 500px header lived on the canvas (its paths started at x=570); our redesign
    // moved the header out of the canvas, so paths start at x=40. colSpacing matches WPF (270);
    // rowSpacing is 220 to clear our 200px-tall cards (WPF's 160 fit its shorter nodes). Rows at
    // y=0/220/440 -> content box ~3200x640, which drives the canvas size and the height-scale.
    private static readonly IReadOnlyDictionary<string, (double X, double Y)> SkillLayout = BuildSkillLayout();

    private static IReadOnlyDictionary<string, (double X, double Y)> BuildSkillLayout()
    {
        const double startX = 40;
        const double colSpacing = 270;
        const double rowSpacing = 220;
        double top = 0, mid = rowSpacing, bot = rowSpacing * 2;
        double Col(int c) => startX + colSpacing * c;
        return new Dictionary<string, (double X, double Y)>
        {
            ["pink_hours"] = (Col(0), mid),
            // Path 1 (top row)
            ["ditzy_data"] = (Col(1), top),
            ["hive_mind"] = (Col(2), top),
            ["trophy_case"] = (Col(3), top),
            ["popular_girl"] = (Col(4), top),
            ["quest_refresh"] = (Col(5), top),
            ["better_quests"] = (Col(6), top),
            // Path 2 (mid row)
            ["sparkle_boost_1"] = (Col(1), mid),
            ["sparkle_boost_2"] = (Col(2), mid),
            ["lucky_bimbo"] = (Col(3), mid),
            ["sparkle_boost_3"] = (Col(4), mid),
            ["lucky_bubbles"] = (Col(5), mid),
            ["pink_rush"] = (Col(6), mid),
            // Path 3 (bottom row)
            ["good_girl_streak"] = (Col(1), bot),
            ["milestone_rewards"] = (Col(2), bot),
            ["oopsie_insurance"] = (Col(3), bot),
            ["streak_power"] = (Col(4), bot),
            ["reroll_addict"] = (Col(5), bot),
            ["perfect_bimbo_week"] = (Col(6), bot),
            // Tier-6 analytics chain (continues the mid row)
            ["ditzy_data_pro"] = (Col(7), mid),
            ["season_rewind"] = (Col(8), mid),
            ["bestie_records"] = (Col(9), mid),
            ["brain_drain_report"] = (Col(10), mid),
            ["certified_data_bimbo"] = (Col(11), mid),
        };
    }

    private void AssignSkillPositions()
    {
        // Position mapped skills per the WPF layout; any unmapped (future) skill is appended to the
        // end of the mid row so it still renders instead of stacking at the origin.
        double nextUnmappedX = 40 + 270 * 12;
        foreach (var group in SkillTiers)
        {
            foreach (var skill in group.Skills)
            {
                if (SkillLayout.TryGetValue(skill.SkillId, out var pos))
                {
                    skill.PositionX = pos.X;
                    skill.PositionY = pos.Y;
                }
                else
                {
                    skill.PositionX = nextUnmappedX;
                    skill.PositionY = 220;
                    nextUnmappedX += 270;
                }
            }
        }
    }

    private bool CanPurchaseSkill(SkillDefinition skill)
    {
        var settings = _settingsService?.Current;
        if (settings == null || _skillTreeService == null)
            return false;

        if (_skillTreeService.HasSkill(skill.Id))
            return false;

        if (!string.IsNullOrEmpty(skill.PrerequisiteId) && !_skillTreeService.HasSkill(skill.PrerequisiteId))
            return false;

        if (settings.SkillPoints < skill.Cost)
            return false;

        if (skill.IsSecret && !IsSecretRequirementMet(skill.Id, settings))
            return false;

        return true;
    }

    private static bool IsSecretRequirementMet(string skillId, AppSettings settings)
    {
        return skillId switch
        {
            "night_shift" => settings.NightTimeUsageCount >= 10,
            "early_bird_bimbo" => settings.EarlyMorningUsageCount >= 10,
            "eternal_doll" => settings.HighestLevelEver >= 50,
            _ => false,
        };
    }

    private static string GetTierName(int tier) => tier switch
    {
        1 => "Foundation",
        2 => "Core Branches",
        3 => "Specialization",
        4 => "Mastery",
        5 => "Secret",
        _ => $"Tier {tier}"
    };

    private void RefreshActiveBonuses()
    {
        ActiveBonuses.Clear();
        var multiplier = _skillTreeService?.GetTotalXpMultiplier() ?? 1.0;
        var breakdown = ComputeMultiplierBreakdown();
        foreach (var (source, value) in breakdown)
        {
            if (source == "Base") continue;
            ActiveBonuses.Add(new ActiveBonusViewModel { Source = source, Value = value });
        }

        if (ActiveBonuses.Count == 0 && multiplier > 1.0)
        {
            ActiveBonuses.Add(new ActiveBonusViewModel { Source = "Skills", Value = multiplier - 1.0 });
        }
    }

    private void RefreshStats()
    {
        Stats.Clear();
        var achievements = _achievementService?.Progress;
        Stats.Add(new StatRowViewModel { Label = Loc.Get("label_sessions_started"), Value = (achievements?.TotalSessionsStarted ?? 0).ToString("N0") });
        Stats.Add(new StatRowViewModel { Label = Loc.Get("label_sessions_completed"), Value = (achievements?.CompletedSessions.Count ?? 0).ToString("N0") });
        Stats.Add(new StatRowViewModel { Label = Loc.Get("label_sessions_abandoned"), Value = (achievements?.TotalSessionsAbandoned ?? 0).ToString("N0") });
    }

    private static string FormatMinutes(double minutes)
    {
        return minutes >= 60
            ? $"{minutes / 60:F1} {Loc.Get("label_hrs")}"
            : $"{minutes:F1} {Loc.Get("label_min_abbrev")}";
    }

    private void RefreshDitzyStats(AppSettings settings)
    {
        DitzyStats.Clear();
        var achievements = _achievementService?.Progress;
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_total_xp_earned_stat"), Value = ((int)(achievements?.TotalXPEarned ?? settings.PlayerXP)).ToString("N0") });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_skill_points_earned"), Value = (achievements?.TotalSkillPointsEarned ?? settings.SkillPoints).ToString("N0") });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_longest_session"), Value = FormatMinutes(achievements?.LongestSessionMinutes ?? 0) });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_attention_passes"), Value = (achievements?.TotalAttentionChecksPassed ?? 0).ToString("N0") });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_video_att_passed"), Value = (achievements?.VideoAttentionChecksPassed ?? 0).ToString("N0") });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_video_att_failed"), Value = (achievements?.VideoAttentionChecksFailed ?? 0).ToString("N0") });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_bubble_count_games"), Value = (achievements?.TotalBubbleCountGames ?? 0).ToString("N0") });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_bc_correct"), Value = (achievements?.TotalBubbleCountCorrect ?? 0).ToString("N0") });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_bc_best_streak"), Value = (achievements?.BubbleCountBestStreak ?? 0).ToString("N0") });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_total_flashes_stat"), Value = (achievements?.TotalFlashImages ?? 0).ToString("N0") });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_bubbles_popped_stat"), Value = (achievements?.TotalBubblesPopped ?? 0).ToString("N0") });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_lock_cards_done"), Value = (achievements?.TotalLockCardsCompleted ?? 0).ToString("N0") });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_video_time"), Value = FormatMinutes(achievements?.TotalVideoMinutes ?? 0) });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_pink_filter_time"), Value = FormatMinutes(achievements?.TotalPinkFilterMinutes ?? 0) });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_spiral_time"), Value = FormatMinutes(achievements?.TotalSpiralMinutes ?? 0) });
        DitzyStats.Add(new StatRowViewModel { Label = Loc.Get("label_consecutive_days"), Value = (achievements?.ConsecutiveDays ?? 0).ToString("N0") });
    }

    private List<(string Source, double Value)> ComputeMultiplierBreakdown()
    {
        var list = new List<(string, double)> { ("Base", 1.0) };
        foreach (var skill in SkillDefinition.All)
        {
            if (_skillTreeService?.HasSkill(skill.Id) != true) continue;
            if (skill.EffectType == SkillEffectType.XpMultiplier && skill.EffectValue > 0)
            {
                list.Add((skill.Name, skill.EffectValue));
            }
        }
        return list;
    }

    private static string FormatConditioningTime(double totalMinutes)
    {
        var hours = (int)(totalMinutes / 60);
        var minutes = (int)(totalMinutes % 60);
        return $"{hours}h {minutes}m";
    }

    private void InitializeDesignData()
    {
        IsLoggedIn = true;
        IsLoginOverlayVisible = false;
        SkillPoints = 24;
        SparklePointsText = Loc.GetF("label_sparkle_points_fmt", SkillPoints);
        TotalPointsSpent = 0;
        XpMultiplierText = "1.00x";
        ConditioningTimeText = "0h 0m";
        PinkRushActive = false;
        StatsToggleText = Loc.Get("label_stats_expand");
        StatsExpanded = false;
        SkillTiers.Clear();
        Skills.Clear();
        Connections.Clear();
        ActiveBonuses.Clear();
        Stats.Clear();
        DitzyStats.Clear();

        var sampleSkills = new[]
        {
            new SkillNodeViewModel
            {
                SkillId = "pink_hours",
                Name = "Pink Hours",
                Cost = 2,
                Tier = 1,
                IsUnlocked = true,
                IconUri = "pack://application:,,,/Resources/skills/pink_hours.png",
                Description = "Shows total conditioning time across all sessions",
                FlavorText = Loc.Get("enh_flavor_pink_hours"),
                PositionX = 40,
                PositionY = 130
            },
            new SkillNodeViewModel
            {
                SkillId = "ditzy_data",
                Name = "Ditzy Data",
                Cost = 5,
                Tier = 2,
                PrerequisiteId = "pink_hours",
                PrerequisiteName = "Pink Hours",
                CanPurchase = true,
                IconUri = "pack://application:,,,/Resources/skills/ditzy_data.png",
                Description = "Unlocks statistics panel with session data",
                FlavorText = Loc.Get("enh_flavor_ditzy_data"),
                PositionX = 320,
                PositionY = 60
            },
            new SkillNodeViewModel
            {
                SkillId = "sparkle_boost_1",
                Name = "Sparkle Boost",
                Cost = 8,
                Tier = 2,
                PrerequisiteId = "pink_hours",
                PrerequisiteName = "Pink Hours",
                CanPurchase = true,
                IconUri = "pack://application:,,,/Resources/skills/sparkle_boost_1.png",
                Description = "+10% XP from all sources",
                FlavorText = Loc.Get("enh_flavor_sparkle_boost"),
                PositionX = 320,
                PositionY = 280
            },
            new SkillNodeViewModel
            {
                SkillId = "hive_mind",
                Name = "Hive Mind",
                Cost = 10,
                Tier = 3,
                PrerequisiteId = "ditzy_data",
                PrerequisiteName = "Ditzy Data",
                IsLocked = true,
                IconUri = "pack://application:,,,/Resources/skills/hive_mind.png",
                Description = "Shows live online user count",
                FlavorText = Loc.Get("enh_flavor_hive_mind"),
                PositionX = 600,
                PositionY = 60
            }
        };

        var designTiers = sampleSkills.GroupBy(s => s.Tier).Select(g => new SkillTierGroupViewModel
        {
            Tier = g.Key,
            TierName = GetTierName(g.Key),
            Skills = new ObservableCollection<SkillNodeViewModel>(g)
        });

        foreach (var tier in designTiers.OrderBy(t => t.Tier))
            SkillTiers.Add(tier);

        foreach (var skill in sampleSkills)
            Skills.Add(skill);

        Connections.Add(new SkillConnectionViewModel
        {
            X1 = 120,
            Y1 = 330,
            X2 = 400,
            Y2 = 60,
            IsUnlocked = true,
            IsAvailable = true
        });
        Connections.Add(new SkillConnectionViewModel
        {
            X1 = 120,
            Y1 = 330,
            X2 = 400,
            Y2 = 280,
            IsUnlocked = true,
            IsAvailable = true
        });
        Connections.Add(new SkillConnectionViewModel
        {
            X1 = 400,
            Y1 = 260,
            X2 = 680,
            Y2 = 60,
            IsUnlocked = false,
            IsAvailable = false
        });

        ActiveBonuses.Add(new ActiveBonusViewModel { Source = "Pink Hours", Value = 0 });
        ActiveBonuses.Add(new ActiveBonusViewModel { Source = "Base", Value = 1.0 });

        Stats.Add(new StatRowViewModel { Label = Loc.Get("label_sessions_started"), Value = "12" });
        Stats.Add(new StatRowViewModel { Label = Loc.Get("label_sessions_completed"), Value = "10" });
        Stats.Add(new StatRowViewModel { Label = Loc.Get("label_sessions_abandoned"), Value = "2" });

        RefreshDitzyStats(new AppSettings());
    }
}

public partial class SkillTierGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private int _tier;

    [ObservableProperty]
    private string _tierName = "";

    [ObservableProperty]
    private ObservableCollection<SkillNodeViewModel> _skills = new();
}

public partial class SkillNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _skillId = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private int _cost;

    [ObservableProperty]
    private int _tier;

    [ObservableProperty]
    private string? _prerequisiteId;

    [ObservableProperty]
    private string? _prerequisiteName;

    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private bool _canPurchase;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private double _positionX;

    [ObservableProperty]
    private double _positionY;

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _flavorText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string? _iconUri;

    partial void OnIsUnlockedChanged(bool value) => UpdateStatusText();
    partial void OnCanPurchaseChanged(bool value) => UpdateStatusText();
    partial void OnCostChanged(int value) => UpdateStatusText();

    private void UpdateStatusText()
    {
        StatusText = IsUnlocked
            ? $"💎{Cost} {Loc.Get("label_skill_owned")}"
            : CanPurchase
                ? $"💎 {Cost}"
                : $"🔒 {Cost}";
    }
}

public partial class SkillConnectionViewModel : ObservableObject
{
    [ObservableProperty]
    private double _x1;

    [ObservableProperty]
    private double _y1;

    [ObservableProperty]
    private double _x2;

    [ObservableProperty]
    private double _y2;

    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private bool _isAvailable;

    public string Stroke => IsUnlocked ? "#90EE90" : IsAvailable ? "#FF69B4" : "#555566";

    public global::Avalonia.Point StartPoint => new(X1, Y1);

    public global::Avalonia.Point EndPoint => new(X2, Y2);
}

public partial class ActiveBonusViewModel : ObservableObject
{
    [ObservableProperty]
    private string _source = "";

    [ObservableProperty]
    private double _value;

    public string DisplayText => Value == 0
        ? Source
        : $"{Source}: +{Value:P0}";
}

public partial class StatRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _value = "";
}
