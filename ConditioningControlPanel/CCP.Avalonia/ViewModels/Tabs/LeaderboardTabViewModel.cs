using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Leaderboard partial.
/// Drives leaderboard mode selection, sorting, and refresh via <see cref="ILeaderboardService"/>.
/// </summary>
public partial class LeaderboardTabViewModel : TabItemViewModel
{
    private readonly IDialogService? _dialogService;
    private readonly ILogger<LeaderboardTabViewModel>? _logger;
    private readonly ILeaderboardService? _leaderboardService;

    public LeaderboardTabViewModel() : base("leaderboard", "Leaderboard", "📊")
    {
        _entries = new ObservableCollection<LeaderboardEntryViewModel>();
        _modes = new ObservableCollection<LeaderboardModeViewModel>
        {
            new("monthly", Loc.Get("label_monthly"), "#FF69B4"),
            new("all-time", Loc.Get("label_all_time"), "#FFD700")
        };
    }

    public LeaderboardTabViewModel(
        IDialogService dialogService,
        ILogger<LeaderboardTabViewModel> logger,
        ILeaderboardService leaderboardService) : base("leaderboard", "Leaderboard", "📊")
    {
        _dialogService = dialogService;
        _logger = logger;
        _leaderboardService = leaderboardService;
        _entries = new ObservableCollection<LeaderboardEntryViewModel>();
        _modes = new ObservableCollection<LeaderboardModeViewModel>
        {
            new("monthly", Loc.Get("label_monthly"), "#FF69B4"),
            new("all-time", Loc.Get("label_all_time"), "#FFD700")
        };

        _leaderboardService.LeaderboardUpdated += OnLeaderboardUpdated;
    }

    /// <summary>
    /// Raised when the view should switch to another primary tab (e.g., Profile).
    /// </summary>
    public event Action<string>? RequestSelectTab;

    /// <summary>
    /// ProfileSync slice 7 (WPF MainWindow.Leaderboard.cs:303): opening the leaderboard pushes
    /// the local profile first so the board reflects fresh data. Fire-and-forget — the sync
    /// service's gate + 30s cooldown make repeat visits harmless.
    /// </summary>
    public override void OnSelected()
    {
        base.OnSelected();
        var profileSync = App.Services?.GetService<IProfileSyncService>();
        if (profileSync == null) return;
        _ = Task.Run(async () =>
        {
            try { await profileSync.SyncProfileAsync(); }
            catch (Exception ex) { _logger?.LogDebug("Leaderboard-open profile sync failed: {Error}", ex.Message); }
        });
    }

    [ObservableProperty]
    private ObservableCollection<LeaderboardModeViewModel> _modes;

    [ObservableProperty]
    private string _currentMode = "monthly";

    [ObservableProperty]
    private ObservableCollection<LeaderboardEntryViewModel> _entries;

    [ObservableProperty]
    private LeaderboardEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private string _statusText = Loc.Get("label_syncing");

    [ObservableProperty]
    private string _seasonText = "";

    [ObservableProperty]
    private string _subtitleText = "";

    [ObservableProperty]
    private bool _isBusy;

    partial void OnCurrentModeChanged(string value)
    {
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync(string? sortBy = null)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = Loc.Get("label_syncing");

        try
        {
            _logger?.LogInformation("Refreshing leaderboard (mode={Mode}, sort={Sort})", CurrentMode, sortBy ?? "default");

            if (_leaderboardService == null)
            {
                StatusText = Loc.Get("label_failed_to_load");
                return;
            }

            var ok = await _leaderboardService.RefreshAsync(sortBy, CurrentMode);

            SeasonText = CurrentMode == "all-time"
                ? Loc.Get("label_all_time_legends_never_die")
                : Loc.GetF("label_0_prove_your_devotion", "Current Season");
            SubtitleText = CurrentMode == "all-time"
                ? Loc.Get("label_cumulative_xp_across_all_seasons")
                : Loc.Get("label_resets_monthly_your_rank_is_everything");

            if (ok)
            {
                MapEntries();
                StatusText = Loc.GetF("label_0_online_1_users",
                    _leaderboardService.OnlineUsers,
                    _leaderboardService.TotalUsers);
            }
            else
            {
                StatusText = _leaderboardService.LastRefreshError ?? Loc.Get("label_failed_to_load");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error refreshing leaderboard");
            StatusText = Loc.Get("label_error_loading_leaderboard");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnLeaderboardUpdated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MapEntries();
            StatusText = _leaderboardService == null
                ? Loc.Get("label_failed_to_load")
                : Loc.GetF("label_0_online_1_users", _leaderboardService.OnlineUsers, _leaderboardService.TotalUsers);
        });
    }

    private void MapEntries()
    {
        if (_leaderboardService == null) return;

        Entries.Clear();
        foreach (var entry in _leaderboardService.Entries)
        {
            Entries.Add(new LeaderboardEntryViewModel
            {
                Rank = entry.Rank,
                DisplayName = entry.DisplayName,
                Level = entry.Level,
                Xp = entry.Xp,
                TotalXpEarned = entry.TotalXpEarned,
                HighestLevelEver = entry.HighestLevelEver,
                PatreonTier = entry.PatreonTier,
                IsOnline = entry.IsOnline,
                AchievementsCount = entry.AchievementsCount,
                DiscordId = entry.DiscordId,
                IsSeason0Og = entry.IsSeason0Og
            });
        }
    }

    [RelayCommand]
    private async Task SetModeAsync(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode) || mode == CurrentMode) return;
        CurrentMode = mode;
        await RefreshAsync(mode == "all-time" ? "xp" : "level");
    }

    [RelayCommand]
    private async Task SortAsync(string? sortBy)
    {
        if (string.IsNullOrWhiteSpace(sortBy)) return;
        _logger?.LogInformation("Leaderboard sort requested: {Sort}", sortBy);

        var sorted = sortBy.ToLowerInvariant() switch
        {
            "name" => Entries.OrderBy(x => x.DisplayName).ToList(),
            "online" => Entries.OrderByDescending(x => x.IsOnline).ThenByDescending(x => x.Level).ToList(),
            "achievements" => Entries.OrderByDescending(x => x.AchievementsCount).ToList(),
            _ => Entries.ToList()
        };

        Entries.Clear();
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].Rank = i + 1;
            Entries.Add(sorted[i]);
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenDiscordProfileAsync(string? discordId)
    {
        if (string.IsNullOrWhiteSpace(discordId)) return;
        try
        {
            var url = $"https://discord.com/users/{discordId}";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            _logger?.LogInformation("Opened Discord profile for {DiscordId}", discordId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open Discord profile for {DiscordId}", discordId);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task ViewSelectedProfileAsync()
    {
        if (SelectedEntry == null) return;
        _logger?.LogInformation("Leaderboard: view profile for {Name}", SelectedEntry.DisplayName);

        // Switch to the Profile tab. L4-03: the ProfileTab is registered under the key
        // "discord" (ProfileTabViewModel ctor) and MainWindowViewModel.SelectTab matches on
        // Key, so we must request "discord" — "profile" matches no tab and silently no-ops.
        var handlers = RequestSelectTab;
        if (handlers != null)
        {
            handlers("discord");
            return;
        }

        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("leaderboard_profile_search_not_ported")) ?? Task.CompletedTask);
    }
}

/// <summary>
/// Single leaderboard row view model.
/// </summary>
public partial class LeaderboardEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private int _rank;

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private int _level;

    [ObservableProperty]
    private double _xp;

    [ObservableProperty]
    private double _totalXpEarned;

    [ObservableProperty]
    private int _highestLevelEver;

    [ObservableProperty]
    private int _patreonTier;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private int _achievementsCount;

    [ObservableProperty]
    private string? _discordId;

    [ObservableProperty]
    private bool _isSeason0Og;
}

/// <summary>
/// Leaderboard mode toggle view model.
/// </summary>
public partial class LeaderboardModeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key = "";

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _accentColor = "#FF69B4";

    public LeaderboardModeViewModel(string key, string label, string accentColor)
    {
        _key = key;
        _label = label;
        _accentColor = accentColor;
    }
}
