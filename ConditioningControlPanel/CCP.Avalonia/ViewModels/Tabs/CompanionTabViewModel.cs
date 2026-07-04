



using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Companion;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.CompanionTab partial.
/// Companion selection cards, prompts, and avatar UI settings.
/// </summary>
public partial class CompanionTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<CompanionTabViewModel>? _logger;
    private readonly IModService? _modService;
    private readonly ICompanionService? _companionService;
    private readonly ICommunityPromptService? _promptService;
    private readonly IAvatarWindowService? _avatarWindowService;

    public CompanionTabViewModel() : base("companion", "Companion", "🤖")
    {
        _companions = new ObservableCollection<CompanionCardViewModel>();
        _installedPrompts = new ObservableCollection<CommunityPromptRowViewModel>();
        InitializeDesignData();
    }

    public CompanionTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ILogger<CompanionTabViewModel> logger,
        IModService modService,
        ICompanionService companionService,
        ICommunityPromptService promptService,
        IAvatarWindowService avatarWindowService) : base("companion", "Companion", "🤖")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _modService = modService;
        _companionService = companionService;
        _promptService = promptService;
        _avatarWindowService = avatarWindowService;
        _companions = new ObservableCollection<CompanionCardViewModel>();
        _installedPrompts = new ObservableCollection<CommunityPromptRowViewModel>();
        SyncUi();
    }

    public override void OnSelected()
    {
        base.OnSelected();
        AttachEvents();
    }

    public override void OnDeselected()
    {
        base.OnDeselected();
        DetachEvents();
    }

    private void AttachEvents()
    {
        if (_companionService == null) return;
        _companionService.CompanionSwitched += OnCompanionEvent;
        _companionService.XPAwarded += OnCompanionXpEvent;
        _companionService.LevelUp += OnCompanionLevelEvent;
        _companionService.XPDrained += OnCompanionXpDrainEvent;
    }

    private void DetachEvents()
    {
        if (_companionService == null) return;
        _companionService.CompanionSwitched -= OnCompanionEvent;
        _companionService.XPAwarded -= OnCompanionXpEvent;
        _companionService.LevelUp -= OnCompanionLevelEvent;
        _companionService.XPDrained -= OnCompanionXpDrainEvent;
    }

    private void OnCompanionEvent(object? sender, CompanionId e) => SyncUi();
    private void OnCompanionXpEvent(object? sender, (CompanionId Companion, double Amount, double Modifier) e) => SyncUi();
    private void OnCompanionLevelEvent(object? sender, (CompanionId Companion, int NewLevel) e) => SyncUi();
    private void OnCompanionXpDrainEvent(object? sender, double e) => SyncUi();

    [ObservableProperty]
    private ObservableCollection<CompanionCardViewModel> _companions;

    [ObservableProperty]
    private ObservableCollection<CommunityPromptRowViewModel> _installedPrompts;

    [ObservableProperty]
    private CompanionCardViewModel? _activeCompanion;

    [ObservableProperty]
    private string _activeCompanionName = "";

    [ObservableProperty]
    private string _activeCompanionLevelText = "";

    [ObservableProperty]
    private string _activeCompanionDescription = "";

    [ObservableProperty]
    private string _activeCompanionXpText = "";

    [ObservableProperty]
    private double _activeCompanionProgress;

    [ObservableProperty]
    private bool _avatarEnabled;

    [ObservableProperty]
    private bool _triggerModeEnabled;

    [ObservableProperty]
    private int _triggerIntervalSeconds = 60;

    [ObservableProperty]
    private int _idleIntervalSeconds = 120;

    [ObservableProperty]
    private int _bubbleDurationSeconds = 2;

    [ObservableProperty]
    private bool _isDetached;

    [ObservableProperty]
    private string _activePromptName = Loc.Get("label_default_built_in");

    [ObservableProperty]
    private string _customizePromptName = "";

    partial void OnAvatarEnabledChanged(bool value) => Save();
    partial void OnTriggerModeEnabledChanged(bool value) => Save();
    partial void OnTriggerIntervalSecondsChanged(int value) => Save();
    partial void OnIdleIntervalSecondsChanged(int value) => Save();
    partial void OnBubbleDurationSecondsChanged(int value) => Save();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _logger?.LogInformation("Refreshing Companion tab");
        SyncUi();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SwitchCompanionAsync(int companionIndex)
    {
        _logger?.LogInformation("Switch companion requested: {Index}", companionIndex);

        if (companionIndex < 0 || companionIndex >= CompanionDefinition.AllCompanions.Length)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_invalid_companion_selection"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        _companionService?.SwitchCompanion((CompanionId)companionIndex);
        SyncUi();

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task AssignPersonalityAsync(int companionIndex)
    {
        _logger?.LogInformation("Assign personality requested for companion {Index}", companionIndex);

        var filters = new[] { new FileFilter("JSON files", new[] { "json" }) };
        var files = await (_dialogService?.ShowOpenFileDialogAsync(
            Loc.Get("title_select_ai_personality"),
            filters) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));

        if (files.Count == 0) return;

        var imported = await (_promptService?.ImportFromFileAsync(files[0]) ?? Task.FromResult<CommunityPrompt?>(null));
        if (imported == null)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_import_failed"),
                Loc.Get("msg_prompt_import_failed"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        _settingsService?.Current?.SetCompanionPromptId(companionIndex, imported.Id);
        _settingsService?.Save();
        _companionService?.SwitchCompanion((CompanionId)companionIndex);
        SyncUi();

        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_prompt_assigned"),
            string.Format(Loc.Get("msg_prompt_assigned_to_companion_fmt"), imported.Name, CompanionDefinition.GetById(companionIndex).Name)) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ActivatePromptAsync(string? promptId)
    {
        if (string.IsNullOrWhiteSpace(promptId)) return;
        _logger?.LogInformation("Activate community prompt: {PromptId}", promptId);

        if (_promptService?.ActivatePrompt(promptId) == true)
        {
            SyncUi();
            return;
        }

        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_error"),
            Loc.Get("msg_prompt_activate_failed"),
            DialogSeverity.Warning) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task RemovePromptAsync(string? promptId)
    {
        if (string.IsNullOrWhiteSpace(promptId)) return;
        _logger?.LogInformation("Remove community prompt: {PromptId}", promptId);

        var prompt = _promptService?.GetInstalledPrompt(promptId);
        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_remove_prompt"),
            string.Format(Loc.Get("msg_remove_prompt_confirm_0"), prompt?.Name ?? promptId)) ?? Task.FromResult(false));
        if (!confirm) return;

        _promptService?.RemovePrompt(promptId);
        SyncUi();
    }

    [RelayCommand]
    private async Task DeactivatePromptAsync()
    {
        _logger?.LogInformation("Deactivate community prompt requested");
        _promptService?.DeactivatePrompt();
        SyncUi();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CustomizePromptAsync()
    {
        _logger?.LogInformation("Customize companion prompt requested");
        var dialog = new ConditioningControlPanel.Avalonia.Dialogs.CompanionPromptEditorDialog();
        // ShowDialog throws ArgumentNullException on a null owner (L3-04): resolve the main window.
        var owner = (global::Avalonia.Application.Current?.ApplicationLifetime
            as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is not null)
        {
            await dialog.ShowDialog<bool?>(owner);
        }
        SyncUi();
    }

    [RelayCommand]
    private async Task ToggleDetachAsync()
    {
        IsDetached = !IsDetached;
        _logger?.LogInformation("Companion tab detach toggled: {Detached}", IsDetached);
        _avatarWindowService?.SetDetached(IsDetached);
        await Task.CompletedTask;
    }

    private void SyncUi()
    {
        try
        {
            var settings = _settingsService?.Current;
            if (settings == null)
            {
                InitializeDesignData();
                return;
            }

            AvatarEnabled = settings.AvatarEnabled;
            TriggerModeEnabled = settings.TriggerModeEnabled;
            TriggerIntervalSeconds = settings.TriggerIntervalSeconds;
            IdleIntervalSeconds = settings.IdleGiggleIntervalSeconds;
            BubbleDurationSeconds = (int)settings.BubbleDurationSeconds;

            RefreshCompanionCards();
            RefreshPrompts();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SyncCompanionTabUI failed");
        }
    }

    private void RefreshCompanionCards()
    {
        Companions.Clear();
        var colors = new[] { "#FF69B4", "#9370DB", "#50C878", "#FF6B6B", "#F5DEB3" };
        var activeId = (int?)_companionService?.ActiveCompanion ?? 0;

        for (int i = 0; i < CompanionDefinition.AllCompanions.Length; i++)
        {
            var def = CompanionDefinition.GetById(i);
            var progress = _companionService?.GetProgress((CompanionId)i);
            var isMax = progress?.IsMaxLevel ?? false;
            var level = progress?.Level ?? 1;
            var promptId = _settingsService?.Current?.GetCompanionPromptId(i);
            var assignedName = promptId != null
                ? _promptService?.GetInstalledPrompt(promptId)?.Name
                : null;

            Companions.Add(new CompanionCardViewModel
            {
                Index = i,
                Name = _modService?.MakeModAware(def.GetDisplayName(false)) ?? def.Name,
                LevelText = isMax ? "MAX" : $"Lv.{level}",
                ColorHex = colors[i % colors.Length],
                IsActive = i == activeId,
                IsSupported = true,
                AssignedPromptName = assignedName ?? ""
            });
        }

        ActiveCompanion = Companions.FirstOrDefault(c => c.IsActive);
        if (ActiveCompanion != null) UpdateActiveCompanionDetails(ActiveCompanion);
    }

    private void UpdateActiveCompanionDetails(CompanionCardViewModel card)
    {
        var def = CompanionDefinition.GetById(card.Index);
        var progress = _companionService?.GetProgress((CompanionId)card.Index);
        var isMax = progress?.IsMaxLevel ?? false;

        ActiveCompanionName = card.Name;
        ActiveCompanionLevelText = isMax
            ? " · MAX LEVEL"
            : $" · Level {progress?.Level ?? 1}";
        ActiveCompanionDescription = def.Description;
        ActiveCompanionXpText = isMax
            ? "Complete!"
            : $"{(progress?.CurrentXP ?? 0):F0} / {(progress?.XPForNextLevel ?? 0):F0} XP";
        ActiveCompanionProgress = isMax ? 100 : (progress?.LevelProgress ?? 0) * 100;
    }

    private void RefreshPrompts()
    {
        InstalledPrompts.Clear();
        var activePromptId = _settingsService?.Current?.ActiveCommunityPromptId;
        var installed = _promptService?.GetInstalledPrompts() ?? new List<CommunityPrompt>();

        CustomizePromptName = GetActivePromptDisplayName();
        ActivePromptName = GetActivePromptDisplayName();

        if (installed.Count == 0)
        {
            InstalledPrompts.Add(new CommunityPromptRowViewModel
            {
                Name = Loc.Get("label_no_prompts_installed"),
                IsPlaceholder = true
            });
            return;
        }

        foreach (var prompt in installed)
        {
            InstalledPrompts.Add(new CommunityPromptRowViewModel
            {
                Id = prompt.Id,
                Name = prompt.Name,
                Author = prompt.Author,
                IsActive = prompt.Id == activePromptId
            });
        }
    }

    private string GetActivePromptDisplayName()
    {
        var activePromptId = _settingsService?.Current?.ActiveCommunityPromptId;
        if (!string.IsNullOrEmpty(activePromptId))
        {
            return _promptService?.GetInstalledPrompt(activePromptId)?.Name
                ?? $"Prompt {activePromptId}";
        }

        if (_settingsService?.Current?.CompanionPrompt?.UseCustomPrompt == true)
        {
            return Loc.Get("label_custom_edited");
        }

        return Loc.Get("label_default_built_in");
    }

    private void Save()
    {
        try
        {
            var settings = _settingsService?.Current;
            if (settings == null) return;
            settings.AvatarEnabled = AvatarEnabled;
            settings.TriggerModeEnabled = TriggerModeEnabled;
            settings.TriggerIntervalSeconds = TriggerIntervalSeconds;
            settings.IdleGiggleIntervalSeconds = IdleIntervalSeconds;
            settings.BubbleDurationSeconds = BubbleDurationSeconds;
            _settingsService?.Save();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save companion settings");
        }
    }

    private void InitializeDesignData()
    {
        AvatarEnabled = true;
        TriggerModeEnabled = false;
        TriggerIntervalSeconds = 60;
        IdleIntervalSeconds = 120;
        BubbleDurationSeconds = 2;
        Companions.Clear();
        InstalledPrompts.Clear();
        ActivePromptName = Loc.Get("label_default_built_in");
    }
}

public partial class CompanionCardViewModel : ObservableObject
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _levelText = "";

    [ObservableProperty]
    private string _colorHex = "#FF69B4";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isSupported = true;

    [ObservableProperty]
    private string _assignedPromptName = "";
}

public partial class CommunityPromptRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _author = "";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isPlaceholder;
}
